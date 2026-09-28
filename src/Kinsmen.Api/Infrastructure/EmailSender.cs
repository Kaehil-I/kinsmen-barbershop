using System.Net.Http.Json;
using Kinsmen.Api.Domain;
using Microsoft.AspNetCore.Identity;


namespace Kinsmen.Api.Infrastructure;

/// <summary>Sends through Brevo's transactional API (free tier, 300 emails/day). It uses their HTTPS API
/// rather than SMTP because free hosting tiers such as Render block outbound SMTP ports.
/// Delivery problems are logged and swallowed: a booking must never fail because of an email.</summary>
public sealed class BrevoEmailSender(HttpClient http, string apiKey, string fromEmail, string fromName) : IEmailSender
{
    public async Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        try
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
            using var response = await http.SendAsync(request, timeout.Token);
            if (!response.IsSuccessStatusCode)
                Console.Error.WriteLine($"Email send failed: {(int)response.StatusCode} {await response.Content.ReadAsStringAsync(timeout.Token)}");
        }
        catch (Exception e)
        {
            Console.Error.WriteLine($"Email send threw: {e.GetType().Name}: {e.Message}");
        }
    }
}

/// <summary>Used whenever no Brevo key is configured, so the app runs without an email account.</summary>
public sealed class NullEmailSender : IEmailSender
{
    public Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default) => Task.CompletedTask;
}