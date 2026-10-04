using System.Security.Claims;
using Kinsmen.Web.ApiClient;
using Kinsmen.Web.Controllers;
using Kinsmen.Web.Models.Api;
using Kinsmen.Web.Models.ViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Kinsmen.Web.Tests;

public sealed class StaffControllerTests
{
    private sealed class FakeStaffApi : IStaffApiClient
    {
        public bool Enabled { get; set; } = true;
        public KinsmenApiException? Failure { get; set; }
        public List<(string UserId, string Role)> Changes { get; } = [];
        public Task<StaffStatus> GetStatusAsync(CancellationToken ct = default) => Task.FromResult(new StaffStatus { Enabled = Enabled });
        public Task<List<StaffMember>> GetStaffAsync(CancellationToken ct = default)
            => Enabled ? Task.FromResult(new List<StaffMember> { new() { UserId = "auth0|b", Email = "b@x.test", Role = "Barber" } })
                       : throw new InvalidOperationException("Must not be called while disabled.");
        public Task<List<StaffMember>> LookupAsync(string email, CancellationToken ct = default) => Task.FromResult(new List<StaffMember>());
        public Task<StaffMember> SetRoleAsync(string userId, string role, CancellationToken ct = default)
        {
            if (Failure is not null) throw Failure;
            Changes.Add((userId, role));
            return Task.FromResult(new StaffMember { UserId = userId, Role = role });
        }
        public Task<List<StaffAuditEntry>> GetAuditAsync(CancellationToken ct = default) => Task.FromResult(new List<StaffAuditEntry>());
    }

    private static string? MessageOf(ObjectResult result)
        => result.Value!.GetType().GetProperty("message")!.GetValue(result.Value) as string;

    private static StaffController Create(FakeStaffApi api)
    {
        var user = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "auth0|me")], "test"));
        return new StaffController(api) { ControllerContext = new() { HttpContext = new DefaultHttpContext { User = user } } };
    }

    [Fact]
    public async Task DisabledStaffManagementShowsTheExplanationWithoutListingStaff()
    {
        var model = (StaffPageViewModel)((ViewResult)await Create(new FakeStaffApi { Enabled = false }).Index(default)).Model!;
        Assert.False(model.Enabled);
        Assert.Empty(model.Staff);
        Assert.Null(model.ErrorMessage);
    }

    [Fact]
    public async Task EnabledPageListsStaffAndKnowsWhoYouAre()
    {
        var model = (StaffPageViewModel)((ViewResult)await Create(new FakeStaffApi()).Index(default)).Model!;
        Assert.True(model.Enabled);
        Assert.Single(model.Staff);
        Assert.Equal("auth0|me", model.CurrentUserId);
    }

    [Fact]
    public async Task SetRolePassesTheChangeThrough()
    {
        var api = new FakeStaffApi();
        var result = await Create(api).SetRole(new SetRoleAjaxRequest { UserId = "auth0|c", Role = "Barber" }, default);
        Assert.IsType<JsonResult>(result);
        Assert.Equal([("auth0|c", "Barber")], api.Changes);
    }

    [Fact]
    public async Task RefusalsShowTheApisOwnMessage()
    {
        var api = new FakeStaffApi
        {
            Failure = new KinsmenApiException(409, new ApiProblem { Title = "admin_demotion_not_allowed", Detail = "Admins can't be demoted here." },
                "Admins can't be demoted here.")
        };
        var result = (ObjectResult)await Create(api).SetRole(new SetRoleAjaxRequest { UserId = "auth0|a", Role = "Customer" }, default);
        Assert.Equal(409, result.StatusCode);
        Assert.Equal("Admins can't be demoted here.", MessageOf(result));
    }

    [Fact]
    public async Task Auth0OutagesAreNotBlamedOnTheBookingService()
    {
        var api = new FakeStaffApi
        {
            Failure = new KinsmenApiException(503, new ApiProblem { Title = "identity_provider_unavailable", Detail = "Couldn't reach Auth0. Try again shortly." },
                "Couldn't reach Auth0. Try again shortly.")
        };
        var result = (ObjectResult)await Create(api).SetRole(new SetRoleAjaxRequest { UserId = "auth0|a", Role = "Barber" }, default);
        Assert.Equal("Couldn't reach Auth0. Try again shortly.", MessageOf(result));
    }
}
