namespace Kinsmen.Web.ApiClient;

/// <summary>Starts waking the API as soon as someone is on the site, rather than when they first open a page
/// that needs it. Someone landing on the home page gives the API a head start of however long they spend
/// there, so Services or Book a Chair is usually ready by the time they click it.
/// Pings /health/live in the background at most once per <see cref="Interval"/> while the site has visitors;
/// that also keeps the API from sleeping while people are using the site.</summary>
public sealed class ApiWarmer(IHttpClientFactory httpClientFactory, TimeProvider timeProvider, ILogger<ApiWarmer> logger)
{
    public const string ClientName = "kinsmen-api-warmup";

    /// <summary>Well under Render's 15-minute idle limit, so an active site never lets the API fall asleep.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);

    private static readonly TimeSpan PingTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(8);

    private long _lastPing = long.MinValue;

    /// <summary>Fire-and-forget: never delays the visitor's own request. Returns the ping (or null when one
    /// wasn't due) so tests can wait for it.</summary>
    public Task? PingIfDue()
    {
        var now = timeProvider.GetTimestamp();
        var last = Interlocked.Read(ref _lastPing);
        if (last != long.MinValue && timeProvider.GetElapsedTime(last, now) < Interval) return null;

        // Only one request wins the race to send the ping.
        if (Interlocked.CompareExchange(ref _lastPing, now, last) != last) return null;

        return Task.Run(PingAsync);
    }

    /// <summary>Quick yes/no for api-wake.js: does the API answer right now? Deliberately not retried, so the
    /// browser's polling decides how long to keep waiting.</summary>
    public async Task<bool> IsAwakeAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var client = httpClientFactory.CreateClient(ClientName);
            if (client.BaseAddress is null) return false;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(StatusTimeout);
            using var response = await client.GetAsync("/health/live", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            return false;
        }
    }

    private async Task PingAsync()
    {
        try
        {
            using var client = httpClientFactory.CreateClient(ClientName);
            if (client.BaseAddress is null) return; // Api:BaseUrl not configured (tests, some local setups).

            using var timeout = new CancellationTokenSource(PingTimeout);
            using var response = await client.GetAsync("/health/live", HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            logger.LogDebug("API warm-up ping answered {StatusCode}", (int)response.StatusCode);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            // Best effort: the visitor's real API calls have their own retry and error handling.
            logger.LogDebug(ex, "API warm-up ping failed");
        }
    }
}

/// <summary>Pings once when this web app starts. On Render the web app wakes because someone just visited,
/// so this is the earliest moment to start the API waking too.</summary>
public sealed class ApiWarmupOnStartup(ApiWarmer warmer) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        warmer.PingIfDue();
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
