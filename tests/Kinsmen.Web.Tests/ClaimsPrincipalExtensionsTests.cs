using System.Security.Claims;
using Kinsmen.Web.Helpers;

namespace Kinsmen.Web.Tests;

public sealed class ClaimsPrincipalExtensionsTests
{
    private static ClaimsPrincipal WithRole(string role)
        => new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Role, role)], "test"));

    [Theory]
    [InlineData("Barber")]
    [InlineData("Admin")]
    public void BarbersAndAdminsAreStaff(string role)
        => Assert.True(WithRole(role).IsStaff());

    [Fact]
    public void CustomersAreNotStaff()
        => Assert.False(WithRole("Customer").IsStaff());

    [Fact]
    public void AVisitorWhoIsNotSignedInIsNotStaff()
        => Assert.False(new ClaimsPrincipal(new ClaimsIdentity()).IsStaff());
}
