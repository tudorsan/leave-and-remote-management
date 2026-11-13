using LeaveRemote.Api.Models;

namespace LeaveRemote.Api.Services;

/// <summary>
/// Cine poate aproba sau respinge cererea cui:
/// <list type="bullet">
/// <item>Admin: orice cerere, în afară de propriile cereri.</item>
/// <item>Manager: cererile angajaților din echipa sa și ale subordonaților direcți care sunt Manageri sau Admini.</item>
/// <item>Nimeni nu își poate analiza propria cerere (regula 7).</item>
/// </list>
/// Aceeași regulă filtrează și lista de cereri în așteptare, ca lista să conțină doar ce poate fi aprobat.
/// </summary>
public static class ReviewPolicy
{
    public static bool IsReviewer(User user) => user.Role is UserRole.Manager or UserRole.Admin;

    public static ServiceError? Check(User reviewer, User? requester, bool isApproval)
    {
        if (!IsReviewer(reviewer)) return ServiceError.Forbidden();

        if (requester is null || (!requester.IsActive && reviewer.Role != UserRole.Admin))
        {
            return ServiceError.Conflict("REQUESTER_INACTIVE", "Solicitantul nu există sau este inactiv.");
        }

        if (reviewer.Id == requester.Id)
        {
            return isApproval
                ? ServiceError.Conflict("SELF_APPROVAL_FORBIDDEN", "Nu îți poți aproba propria cerere.")
                : ServiceError.Conflict("SELF_REJECTION_FORBIDDEN", "Nu îți poți respinge propria cerere.");
        }

        if (reviewer.Role == UserRole.Admin) return null;

        if (requester.Role != UserRole.Employee)
        {
            return requester.ManagerId == reviewer.Id
                ? null
                : ServiceError.Forbidden("NOT_DIRECT_MANAGER",
                    "Doar superiorul direct sau un Admin poate analiza cererea unui Manager.");
        }

        return reviewer.TeamId.HasValue && requester.TeamId == reviewer.TeamId
            ? null
            : ServiceError.Forbidden("NOT_YOUR_TEAM", "Poți analiza doar cererile angajaților din echipa ta.");
    }

    /// <summary>Utilizatorii ale căror cereri pot fi analizate de <paramref name="reviewer"/> (traductibil în SQL).</summary>
    public static IQueryable<User> ReviewableBy(this IQueryable<User> users, User reviewer)
    {
        var reviewerId = reviewer.Id;
        var teamId = reviewer.TeamId;
        return reviewer.Role switch
        {
            UserRole.Admin => users.Where(u => u.Id != reviewerId),
            UserRole.Manager => users.Where(u => u.Id != reviewerId && u.IsActive &&
                (u.Role == UserRole.Employee ? teamId != null && u.TeamId == teamId : u.ManagerId == reviewerId)),
            _ => users.Where(_ => false)
        };
    }
}
