using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using LeaveRemote.Api.Services.Notifications;
using Microsoft.EntityFrameworkCore;

namespace LeaveRemote.Api.Data;

[AttributeUsage(AttributeTargets.Method)]
public sealed class RequestWriteTransactionAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var db = context.HttpContext.RequestServices.GetRequiredService<AppDbContext>();
        var cancellationToken = context.HttpContext.RequestAborted;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // Scrierile de cereri sunt serializate: regulile de suprapunere, sold și limite citesc și scriu aceleași date.
        var lockResult = await db.Database.SqlQueryRaw<int>("""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = N'LeaveRemote.RequestWrites', @LockMode = 'Exclusive',
                @LockOwner = 'Transaction', @LockTimeout = 5000;
            SELECT @result AS [Value];
            """).ToListAsync(cancellationToken);

        if (lockResult.Single() < 0)
        {
            context.Result = new ConflictObjectResult(new
            {
                code = "REQUEST_WRITE_BUSY",
                message = "O altă operație este în curs. Încearcă din nou."
            });
            return;
        }

        var outbox = context.HttpContext.RequestServices.GetRequiredService<NotificationOutbox>();
        var result = await next();
        if (result.Exception == null &&
            ((result.Result as IStatusCodeActionResult)?.StatusCode ?? 200) < 400)
        {
            await transaction.CommitAsync(cancellationToken);
            // Emailurile pleacă doar după ce modificările sunt salvate definitiv.
            // (fără tokenul request-ului: dacă clientul închide conexiunea după salvare, emailurile pleacă oricum).
            await outbox.FlushAsync(CancellationToken.None);
        }
        else
        {
            // Operația a eșuat: tranzacția se anulează, iar emailurile pregătite se aruncă.
            outbox.Discard();
        }
    }
}
