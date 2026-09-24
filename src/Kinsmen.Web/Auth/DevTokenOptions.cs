namespace Kinsmen.Web.Auth;

/// <summary>Bound from the "DevTokens" configuration section. Values are pasted in locally
/// after generating them with Zario's CLI command (see docs/backend/GETTING-STARTED.md) —
/// never commit real tokens; they expire after 30 minutes anyway.</summary>
public sealed class DevTokenOptions
{
    /// <summary>Which of the three tokens below GetAccessTokenAsync returns. Change this to
    /// switch which role you're testing as, e.g. when moving from customer screens to
    /// barber schedule screens.</summary>
    public string ActiveRole { get; set; } = "Customer";

    public string? Customer { get; set; }
    public string? Barber { get; set; }
    public string? Admin { get; set; }
}
