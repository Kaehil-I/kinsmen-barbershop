using Kinsmen.Web.ApiClient;
using Kinsmen.Web.Helpers;

namespace Kinsmen.Web.Tests;

public sealed class ApiErrorMessagesTests
{
    private const string RawMessage = "raw api message";

    private static KinsmenApiException Error(int status)
        => new KinsmenApiException(status, null, RawMessage);

    // 400s and 409s carry specific, actionable detail from the API itself (exact
    // validation problems, the conflict codes the pages give bespoke UI treatment to),
    // so they're passed straight through rather than replaced by generic wording.
    [Theory]
    [InlineData(400)]
    [InlineData(409)]
    public void ValidationAndConflictMessagesPassThroughUnchanged(int status)
        => Assert.Equal(RawMessage, ApiErrorMessages.For(Error(status)));

    [Fact]
    public void AnExpiredSessionTellsThePersonToLogInAgain()
    {
        var message = ApiErrorMessages.For(Error(401));

        Assert.Contains("log in", message, StringComparison.OrdinalIgnoreCase);

        // Dev tokens were replaced by Auth0 - advice about regenerating one would send
        // someone hunting for config that no longer exists.
        Assert.DoesNotContain("DevTokens", message);
        Assert.DoesNotContain("appsettings", message);
    }

    [Theory]
    [InlineData(403, "permission")]
    [InlineData(404, "found")]
    [InlineData(429, "many requests")]
    public void KnownFailureCodesGetTheirOwnExplanation(int status, string expectedWord)
    {
        var message = ApiErrorMessages.For(Error(status));

        Assert.NotEqual(RawMessage, message);
        Assert.Contains(expectedWord, message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AnInternalServerErrorSaysTheServiceHitAProblem()
    {
        var message = ApiErrorMessages.For(Error(500));

        Assert.NotEqual(RawMessage, message);
        Assert.Contains("problem", message, StringComparison.OrdinalIgnoreCase);
    }

    // 502/503/504 are what Render returns while the API wakes from its free-tier sleep: telling people to
    // refresh in a minute is accurate, where "hit a problem" made it look broken.
    [Theory]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public void GatewayFailuresSayTheServiceIsStartingUp(int status)
    {
        var message = ApiErrorMessages.For(Error(status));

        Assert.Contains("minute", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("refresh", message, StringComparison.OrdinalIgnoreCase);
    }

    // These are the messages the view marks with data-api-waking, so the page reloads itself once the API is up.
    [Fact]
    public void StartingUpMessagesAreRecognisedAndOthersAreNot()
    {
        Assert.True(ApiErrorMessages.IsWakingUp(ApiErrorMessages.For(Error(503))));
        Assert.True(ApiErrorMessages.IsWakingUp(ApiErrorMessages.ForConnectionFailure()));

        Assert.False(ApiErrorMessages.IsWakingUp(ApiErrorMessages.For(Error(500))));
        Assert.False(ApiErrorMessages.IsWakingUp(ApiErrorMessages.For(Error(401))));
        Assert.False(ApiErrorMessages.IsWakingUp(null));
    }

    [Fact]
    public void ConnectionFailureMessagePointsAtTheApiNotBeingReachable()
    {
        var message = ApiErrorMessages.ForConnectionFailure();

        Assert.Contains("reach", message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("refresh", message, StringComparison.OrdinalIgnoreCase);
        // Customers see this on the live site, so no developer instructions.
        Assert.DoesNotContain("docs/", message);
    }
}
