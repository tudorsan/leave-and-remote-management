using Microsoft.EntityFrameworkCore;

namespace LeaveRemote.Api.Data;

/// <summary>
/// Serializează scrierile asupra cererilor: regulile de suprapunere, sold și limite citesc și scriu aceleași date,
/// deci două operații simultane (ex. o aprobare și o expirare automată) nu trebuie să se întrepătrundă.
/// Lock-ul aparține tranzacției curente și se eliberează la commit sau rollback.
/// </summary>
public static class RequestWriteLock
{
    public static async Task<bool> AcquireAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var result = await db.Database.SqlQueryRaw<int>("""
            DECLARE @result int;
            EXEC @result = sys.sp_getapplock
                @Resource = N'LeaveRemote.RequestWrites', @LockMode = 'Exclusive',
                @LockOwner = 'Transaction', @LockTimeout = 5000;
            SELECT @result AS [Value];
            """).ToListAsync(cancellationToken);
        return result.Single() >= 0;
    }
}
