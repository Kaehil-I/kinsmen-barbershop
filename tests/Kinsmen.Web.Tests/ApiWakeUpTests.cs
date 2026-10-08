using System.Net;
using Kinsmen.Web.ApiClient;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Kinsmen.Web.Tests;

/// <summary>The API sleeps on Render's free tier. These cover waiting for it to wake (ColdStartRetryHandler)
/// and waking it early (ApiWarmer).</summary>
public sealed class ApiWakeUpTests
{
    // --- ColdStartRetryHandler --------------------------------------------------

    [Fact]
    public async Task AReadIsRetriedUntilTheApiHasWokenUp()
    {
        var api = new ScriptedApi(HttpStatusCode.ServiceUnavailable, HttpStatusCode.BadGateway, HttpStatusCode.OK);

        using var response = await Send(api, HttpMethod.Get);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(3, api.Calls);
    }

    [Fact]
    public async Task AConnectionFailureWhileStartingIsRetriedToo()
    {
        var api = new ScriptedApi(null, HttpStatusCode.OK); // null = connection refused

        using var response = await Send(api, HttpMethod.Get);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, api.Calls);
    }

    [Fact]
    public async Task WritesAreNeverRetriedSoNothingIsSubmittedTwice()
    {
        var api = new ScriptedApi(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);

        using var response = await Send(api, HttpMethod.Post);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal(1, api.Calls);
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.NotFound)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task RealErrorsFromTheApiAreNotRetried(HttpStatusCode status)
    {
        var api = new ScriptedApi(status, HttpStatusCode.OK);

        using var response = await Send(api, HttpMethod.Get);

        Assert.Equal(status, response.StatusCode);
        Assert.Equal(1, api.Calls);
    }

    [Fact]
    public async Task GivesUpWhenTheWakeBudgetRunsOut()
    {
        var api = new ScriptedApi(Enumerable.Repeat<HttpStatusCode?>(HttpStatusCode.ServiceUnavailable, 1000).ToArray());

        using var response = await Send(api, HttpMethod.Get, budget: TimeSpan.FromMilliseconds(100));

        // The page then shows its "still starting, refresh shortly" message.
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.InRange(api.Calls, 2, 50);
    }

    private static async Task<HttpResponseMessage> Send(ScriptedApi api, HttpMethod method, TimeSpan? budget = null)
    {
        var options = Options.Create(new ColdStartRetryOptions
        {
            WakeBudget = budget ?? TimeSpan.FromSeconds(10),
            RetryDelay = TimeSpan.FromMilliseconds(5)
        });
        var handler = new ColdStartRetryHandler(options, TimeProvider.System) { InnerHandler = api };
        using var invoker = new HttpMessageInvoker(handler);
        using var request = new HttpRequestMessage(method, "http://api.test/api/services");
        return await invoker.SendAsync(request, CancellationToken.None);
    }

    /// <summary>Answers each call with the next scripted status; null throws like a refused connection.</summary>
    private sealed class ScriptedApi(params HttpStatusCode?[] script) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var status = script[Math.Min(Calls, script.Length - 1)];
            Calls++;
            if (status is null) throw new HttpRequestException("Connection refused");
            return Task.FromResult(new HttpResponseMessage(status.Value));
        }
    }

    // --- ApiWarmer --------------------------------------------------------------

    [Fact]
    public async Task TheFirstVisitorWakesTheApi()
    {
        var (warmer, api, _) = Warmer();

        await (warmer.PingIfDue() ?? Task.CompletedTask);

        Assert.Equal(1, api.Calls);
        Assert.Equal("/health/live", api.LastPath);
    }

    [Fact]
    public async Task VisitorsWithinTheIntervalDoNotPingAgain()
    {
        var (warmer, api, clock) = Warmer();
        await (warmer.PingIfDue() ?? Task.CompletedTask);

        clock.Advance(ApiWarmer.Interval - TimeSpan.FromSeconds(1));

        Assert.Null(warmer.PingIfDue());
        Assert.Equal(1, api.Calls);
    }

    [Fact]
    public async Task OnceTheIntervalPassesTheNextVisitorPingsAgain()
    {
        var (warmer, api, clock) = Warmer();
        await (warmer.PingIfDue() ?? Task.CompletedTask);

        clock.Advance(ApiWarmer.Interval);
        await (warmer.PingIfDue() ?? Task.CompletedTask);

        Assert.Equal(2, api.Calls);
    }

    [Fact]
    public async Task AFailedPingNeverBreaksTheVisitorsRequest()
    {
        var (warmer, _, _) = Warmer(new ScriptedApi((HttpStatusCode?)null));

        // Would throw here if the failure escaped.
        await (warmer.PingIfDue() ?? Task.CompletedTask);
    }

    private static (ApiWarmer Warmer, RecordingApi Api, ManualClock Clock) Warmer(HttpMessageHandler? api = null)
    {
        var recording = new RecordingApi(api ?? new ScriptedApi(HttpStatusCode.OK));
        var clock = new ManualClock();
        var warmer = new ApiWarmer(new SingleClientFactory(recording), clock, NullLogger<ApiWarmer>.Instance);
        return (warmer, recording, clock);
    }

    private sealed class RecordingApi(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        public int Calls { get; private set; }
        public string? LastPath { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastPath = request.RequestUri?.AbsolutePath;
            return base.SendAsync(request, cancellationToken);
        }
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
            => new(handler, disposeHandler: false) { BaseAddress = new Uri("http://api.test") };
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks = 1;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public void Advance(TimeSpan by) => _ticks += by.Ticks;
    }
}
