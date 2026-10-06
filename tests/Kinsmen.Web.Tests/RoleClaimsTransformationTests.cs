using System.Security.Claims;
using Kinsmen.Web.Auth;

namespace Kinsmen.Web.Tests;

public sealed class RoleClaimsTransformationTests
{
    private static ClaimsPrincipal PrincipalWith(params Claim[] claims)
        => new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));

    [Theory]
    [InlineData("Customer")]
    [InlineData("Barber")]
    [InlineData("Admin")]
    public async Task PlainRoleClaimBecomesTheStandardRoleClaim(string role)
    {
        var principal = PrincipalWith(new Claim("role", role));

        var result = await new RoleClaimsTransformation().TransformAsync(principal);

        Assert.True(result.IsInRole(role));
        Assert.Single(result.FindAll(ClaimTypes.Role));
    }

    [Fact]
    public async Task ABarberIsNotAnAdmin()
    {
        var principal = PrincipalWith(new Claim("role", "Barber"));

        var result = await new RoleClaimsTransformation().TransformAsync(principal);

        Assert.True(result.IsInRole("Barber"));
        Assert.False(result.IsInRole("Admin"));
    }

    // ASP.NET Core can run a claims transformation more than once for the same
    // principal within a request, so it has to be safe to repeat - otherwise the role
    // claim would pile up.
    [Fact]
    public async Task RunningTwiceDoesNotDuplicateTheRoleClaim()
    {
        var principal = PrincipalWith(new Claim("role", "Barber"));
        var transformation = new RoleClaimsTransformation();

        await transformation.TransformAsync(principal);
        await transformation.TransformAsync(principal);

        Assert.Single(principal.FindAll(ClaimTypes.Role));
    }

    [Fact]
    public async Task ARoleAlreadyPresentAsTheStandardClaimIsNotDuplicated()
    {
        var principal = PrincipalWith(new Claim("role", "Barber"), new Claim(ClaimTypes.Role, "Barber"));

        await new RoleClaimsTransformation().TransformAsync(principal);

        Assert.Single(principal.FindAll(ClaimTypes.Role));
    }

    [Fact]
    public async Task NoRoleClaimMeansNoRoleIsAdded()
    {
        var principal = PrincipalWith(new Claim("sub", "auth0|123"));

        var result = await new RoleClaimsTransformation().TransformAsync(principal);

        Assert.Empty(result.FindAll(ClaimTypes.Role));
        Assert.False(result.IsInRole("Barber"));
    }

    [Fact]
    public async Task APrincipalWithNoIdentityIsLeftAlone()
    {
        var principal = new ClaimsPrincipal();

        var result = await new RoleClaimsTransformation().TransformAsync(principal);

        Assert.Same(principal, result);
        Assert.Empty(result.Claims);
    }
}
