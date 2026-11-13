using LeaveRemote.Api.Models;
using LeaveRemote.Api.Services;

namespace LeaveRemote.Api.Tests.Unit;

/// <summary>Matricea de aprobare din PDF și regula 7 (nimeni nu își aprobă propria cerere).</summary>
public class ReviewPolicyTests
{
    private static readonly User Admin = new() { Id = 1, Role = UserRole.Admin };
    private static readonly User Manager = new() { Id = 2, Role = UserRole.Manager, TeamId = 10, ManagerId = 1 };
    private static readonly User OtherManager = new() { Id = 3, Role = UserRole.Manager, TeamId = 20, ManagerId = 1 };
    private static readonly User Employee = new() { Id = 4, Role = UserRole.Employee, TeamId = 10, ManagerId = 2 };
    private static readonly User OtherTeamEmployee = new() { Id = 5, Role = UserRole.Employee, TeamId = 20, ManagerId = 3 };

    [Fact]
    public void Manager_approves_requests_from_own_team() =>
        Assert.Null(ReviewPolicy.Check(Manager, Employee, isApproval: true));

    [Fact]
    public void Manager_cannot_review_requests_from_another_team() =>
        Assert.Equal("NOT_YOUR_TEAM", ReviewPolicy.Check(Manager, OtherTeamEmployee, isApproval: true)?.Code);

    [Fact]
    public void Admin_approves_any_request() =>
        Assert.Null(ReviewPolicy.Check(Admin, OtherTeamEmployee, isApproval: true));

    [Fact]
    public void Employee_cannot_review_requests() =>
        Assert.Equal(403, ReviewPolicy.Check(Employee, new User { Id = 9, Role = UserRole.Employee, TeamId = 10 }, isApproval: true)?.StatusCode ?? 0);

    [Fact]
    public void A_managers_request_is_reviewed_by_the_direct_superior()
    {
        var subordinateManager = new User { Id = 6, Role = UserRole.Manager, TeamId = 30, ManagerId = Manager.Id };

        Assert.Null(ReviewPolicy.Check(Manager, subordinateManager, isApproval: true));
        Assert.Equal("NOT_DIRECT_MANAGER", ReviewPolicy.Check(OtherManager, subordinateManager, isApproval: true)?.Code);
    }

    [Fact]
    public void A_manager_in_the_same_team_cannot_review_another_manager_who_is_not_a_direct_report()
    {
        var peer = new User { Id = 7, Role = UserRole.Manager, TeamId = 10, ManagerId = 1 };

        Assert.Equal("NOT_DIRECT_MANAGER", ReviewPolicy.Check(Manager, peer, isApproval: true)?.Code);
    }

    [Theory]
    [InlineData(true, "SELF_APPROVAL_FORBIDDEN")]
    [InlineData(false, "SELF_REJECTION_FORBIDDEN")]
    public void Manager_cannot_review_own_request(bool isApproval, string code) =>
        Assert.Equal(code, ReviewPolicy.Check(Manager, Manager, isApproval)?.Code);

    [Fact]
    public void Admin_cannot_approve_own_request() =>
        Assert.Equal("SELF_APPROVAL_FORBIDDEN", ReviewPolicy.Check(Admin, Admin, isApproval: true)?.Code);

    [Fact]
    public void Only_an_admin_reviews_requests_of_deactivated_users()
    {
        var inactive = new User { Id = 8, Role = UserRole.Employee, TeamId = 10, IsActive = false };

        Assert.Equal("REQUESTER_INACTIVE", ReviewPolicy.Check(Manager, inactive, isApproval: true)?.Code);
        Assert.Null(ReviewPolicy.Check(Admin, inactive, isApproval: true));
    }
}
