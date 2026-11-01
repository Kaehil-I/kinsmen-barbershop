namespace Kinsmen.Web.Helpers;

/// <summary>Shows a stored phone number (+27821234567) the way people write it locally (082 123 4567).</summary>
public static class PhoneFormatter
{
    public static string Local(string? stored)
    {
        if (string.IsNullOrEmpty(stored)) return string.Empty;
        if (!stored.StartsWith("+27") || stored.Length != 12) return stored;
        var national = "0" + stored[3..];
        return $"{national[..3]} {national[3..6]} {national[6..]}";
    }
}
