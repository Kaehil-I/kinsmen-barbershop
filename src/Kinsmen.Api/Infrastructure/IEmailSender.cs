namespace Kinsmen.Api.Domain;

/// <summary>Sends one transactional email. Lives in Domain so BookingService doesn't depend on
/// Infrastructure; the real (Brevo) and no-op implementations live in Infrastructure.</summary>
public interface IEmailSender
{
    Task SendAsync(string toEmail, string subject, string htmlBody, CancellationToken ct = default);
}