namespace Kinsmen.Web.Helpers;

/// <summary>Browser security headers sent on every response.
///
/// The Content-Security-Policy only allows what the site actually loads: its own scripts (there are no
/// inline scripts; the booking page passes its data in a JSON block instead), Google Fonts, the Google Maps
/// embed on the Location page, and form posts to Auth0 (logout redirects there). Inline style attributes are
/// still allowed because the views and scripts use them widely; script injection is the bigger risk and stays
/// blocked. frame-ancestors/X-Frame-Options stop other sites framing pages (e.g. clickjacking the Admin screen).</summary>
public static class SecurityHeaders
{
    public static IReadOnlyList<(string Name, string Value)> For(string? auth0Domain)
    {
        var auth0 = string.IsNullOrWhiteSpace(auth0Domain) ? "" : " https://" + auth0Domain.Trim().TrimEnd('/');
        var csp = string.Join("; ",
            "default-src 'self'",
            "script-src 'self'",
            "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com",
            "font-src 'self' https://fonts.gstatic.com",
            "img-src 'self' data:",
            "frame-src https://maps.google.com https://www.google.com",
            "connect-src 'self'",
            "form-action 'self'" + auth0,
            "base-uri 'self'",
            "object-src 'none'",
            "frame-ancestors 'none'");
        return
        [
            ("Content-Security-Policy", csp),
            ("X-Content-Type-Options", "nosniff"),
            ("X-Frame-Options", "DENY"),
            ("Referrer-Policy", "strict-origin-when-cross-origin"),
            ("Permissions-Policy", "camera=(), microphone=(), geolocation=(), payment=()")
        ];
    }
}
