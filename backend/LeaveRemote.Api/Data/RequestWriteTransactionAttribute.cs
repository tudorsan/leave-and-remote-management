using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.Infrastructure;
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

        // Both request types share the same overlap and capacity rules.
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

        var result = await next();
        if (result.Exception == null &&
            ((result.Result as IStatusCodeActionResult)?.StatusCode ?? 200) < 400)
        {
            await transaction.CommitAsync(cancellationToken);
        }
    }
}
