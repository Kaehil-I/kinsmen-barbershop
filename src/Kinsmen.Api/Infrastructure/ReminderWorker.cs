using Kinsmen.Api.Domain;

namespace Kinsmen.Api.Infrastructure;

/// <summary>Checks every 10 minutes for appointments that are due a reminder.</summary>
public sealed class ReminderWorker(BookingService bookings, ILogger<ReminderWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var sent = await bookings.SendDueReminders(stoppingToken);
                if (sent > 0) logger.LogInformation("Sent {Count} appointment reminder(s).", sent);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception e) { logger.LogWarning("Reminder sweep failed: {ErrorType}", e.GetType().Name); }

            try { await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
