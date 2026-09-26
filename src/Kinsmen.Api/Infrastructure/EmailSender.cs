using System.Net.Http.Json;

namespace Kinsmen.Api.Infrastructure;

public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default);
}

/// <summary>Brevo's free tier (300 emails/day) is used over SMTP because Render's free
/// web service tier blocks outbound SMTP ports; Brevo's transactional API runs over
/// plain HTTPS, which is never blocked.</summary>
public sealed class BrevoEmailSender(HttpClient http, string apiKey, string fromEmail, string fromName) : IEmailSender
{
    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
        request.Headers.Add("api-key", apiKey);
        request.Content = JsonContent.Create(new
        {
            sender = new { email = fromEmail, name = fromName },
            to = new[] { new { email = toEmail } },
            subject,
            htmlContent = htmlBody
        });
        try
        {
            using var response = await http.SendAsync(request, ct);
            // A failed email should never break the booking action the customer is waiting on.
            if (!response.IsSuccessStatusCode)
                Console.Error.WriteLine($"Email send failed: {response.StatusCode} {await response.Content.ReadAsStringAsync(ct)}");
        }
        catch (Exception e) { Console.Error.WriteLine($"Email send threw: {e.GetType().Name}: {e.Message}"); }
    }
}

/// <summary>Used whenever no Brevo API key is configured (Development/Testing by default),
/// so the app and every existing test keep running without needing a real email account.</summary>
public sealed class NullEmailSender : IEmailSender
{
    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default) => Task.CompletedTask;
}