using System.Net;
using Microsoft.Extensions.Options;

namespace Kinsmen.Web.ApiClient;

/// <summary>On Render's free tier the API sleeps after 15 minutes without traffic. The first call after that
/// wakes it, but while it starts (roughly 25-60 seconds) Render answers with a 502/503/504 or the connection
/// fails, and without this the first visitor got an error page even though the API was already waking up.
/// Read-only requests (GET/HEAD) are retried every few seconds until the API answers or the time budget runs
/// out. Anything that changes data is never retried here, so a booking can't be submitted twice.</summary>
public sealed class ColdStartRetryHandler(IOptions<ColdStartRetryOptions> options, TimeProvider timeProvider) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get && request.Method != HttpMethod.Head)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        var settings = options.Value;
        var started = timeProvider.GetTimestamp();

        while (true)
        {
            HttpResponseMessage? response = null;
            try
            {
                response = await base.SendAsync(request, cancellationToken);
                if (!IsStillStarting(response.StatusCode)) return response;
            }
            catch (HttpRequestException) when (!cancellationToken.IsCancellationRequested)
            {
                // Connection refused/reset while the service is coming up. Retried below like a 503.
            }

            if (timeProvider.GetElapsedTime(started) + settings.RetryDelay > settings.WakeBudget)
            {
                // Out of time: hand back the last answer (or let the connection failure surface) so the
                // page shows its normal "still waking up" message instead of hanging.
                if (response is not null) return response;
                return await base.SendAsync(request, cancellationToken);
            }

            response?.Dispose();
            await Task.Delay(settings.RetryDelay, timeProvider, cancellationToken);
        }
    }

    /// <summary>The statuses Render's proxy returns while a sleeping service starts.</summary>
    public static bool IsStillStarting(HttpStatusCode status)
        => status is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout;
}

/// <summary>Bound from "Api:ColdStart". The defaults cover a Render free-tier cold start.</summary>
public sealed class ColdStartRetryOptions
{
    /// <summary>How long a read-only request keeps trying while the API wakes up.</summary>
    public TimeSpan WakeBudget { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>Pause between attempts.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(3);
}
