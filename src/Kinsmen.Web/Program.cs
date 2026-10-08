using Auth0.AspNetCore.Authentication;
using Kinsmen.Web.ApiClient;
using Kinsmen.Web.Auth;
using Kinsmen.Web.Helpers;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews()
    // Lets the browser's own JS (e.g. barber-schedule.js posting {"status":"Confirmed"})
    // bind straight to the BookingStatus enum on incoming requests to this app's own
    // controllers. Deliberately separate from KinsmenApiClient's JsonOptions, which
    // convert to/from Zario's API and use camelCase to match its contract — these two
    // JSON boundaries don't need to agree on casing, since nothing outside this app
    // ever sees the incoming side.
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddHttpContextAccessor();

builder.Services.Configure<ApiClientOptions>(builder.Configuration.GetSection("Api"));
// Proxy:Key is shared with the API (one Render environment group), so it lives outside the "Api" section.
builder.Services.PostConfigure<ApiClientOptions>(options => options.ProxyKey ??= builder.Configuration["Proxy:Key"]);

// Per-person limits (signed-in account, else IP). Static files are served before the limiter and don't count.
// "writes" covers anything that changes data; "login" covers starting an Auth0 sign-in or sign-up.
var limits = builder.Configuration.GetSection("RateLimit");
var perMinute = limits.GetValue("PerMinute", 120);
var writesPerMinute = limits.GetValue("WritesPerMinute", 20);
var loginPerMinute = limits.GetValue("LoginPerMinute", 10);
static FixedWindowRateLimiterOptions PerMinute(int permits) => new() { PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 };
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var response = context.HttpContext.Response;
        response.Headers.RetryAfter = "60";
        const string message = "Too many requests - wait a minute and try again.";
        if (context.HttpContext.Request.Headers.XRequestedWith == "XMLHttpRequest")
            await response.WriteAsJsonAsync(new { message }, cancellationToken);
        else
            await response.WriteAsync(message, cancellationToken);
    };
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(RateLimitPartitions.ClientKey(context), _ => PerMinute(perMinute)));
    options.AddPolicy(RateLimitPolicies.Writes, context =>
        RateLimitPartition.GetFixedWindowLimiter(RateLimitPartitions.ClientKey(context), _ => PerMinute(writesPerMinute)));
    options.AddPolicy(RateLimitPolicies.Login, context =>
        RateLimitPartition.GetFixedWindowLimiter("ip:" + context.Connection.RemoteIpAddress, _ => PerMinute(loginPerMinute)));
});

builder.Services
    .AddAuth0WebAppAuthentication(options =>
    {
        options.Domain = builder.Configuration["Auth0:Domain"]!;
        options.ClientId = builder.Configuration["Auth0:ClientId"]!;
        options.ClientSecret = builder.Configuration["Auth0:ClientSecret"];
    })
    .WithAccessToken(options =>
    {
        options.Audience = builder.Configuration["Auth0:Audience"];
        options.UseRefreshTokens = true;
    });

// Auth0TokenProvider is the seam DevelopmentTokenProvider used to fill — nothing else
// here needs to change.
builder.Services.AddScoped<ITokenProvider, Auth0TokenProvider>();
builder.Services.AddTransient<BearerTokenHandler>();
builder.Services.AddTransient<ProxyHeadersHandler>();

// Render's free tier puts the API to sleep when idle: read-only calls wait for it to wake instead of failing,
// and the API is woken as soon as someone is on the site (see ColdStartRetryHandler and ApiWarmer).
builder.Services.TryAddSingleton(TimeProvider.System);
builder.Services.Configure<ColdStartRetryOptions>(builder.Configuration.GetSection("Api:ColdStart"));
builder.Services.AddTransient<ColdStartRetryHandler>();
builder.Services.AddSingleton<ApiWarmer>();
builder.Services.AddHostedService<ApiWarmupOnStartup>();

// Maps Auth0's plain "role" claim to the standard role claim type, so
// [Authorize(Roles = "...")] and User.IsInRole(...) work normally — see
// RoleClaimsTransformation for why this is needed at all.
builder.Services.AddTransient<IClaimsTransformation, RoleClaimsTransformation>();

// fetch() calls from our own JS silently follow an auth redirect and hand back the
// login page's HTML as if it were the JSON response they expected. Requests carrying
// the X-Requested-With header (every protected fetch() call sends it — see
// wwwroot/js/auth-fetch.js) get a plain 401/403 instead, so the JS can redirect the
// browser itself rather than mishandling HTML as data.
builder.Services.ConfigureApplicationCookie(options =>
{
    var redirectToLogin = options.Events.OnRedirectToLogin;
    options.Events.OnRedirectToLogin = context =>
    {
        if (context.Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        }
        return redirectToLogin(context);
    };

    var redirectToAccessDenied = options.Events.OnRedirectToAccessDenied;
    options.Events.OnRedirectToAccessDenied = context =>
    {
        if (context.Request.Headers["X-Requested-With"] == "XMLHttpRequest")
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        }
        return redirectToAccessDenied(context);
    };
});

builder.Services.AddHttpClient<IKinsmenApiClient, KinsmenApiClient>((serviceProvider, client) =>
{
    var apiOptions = serviceProvider.GetRequiredService<IOptions<ApiClientOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(apiOptions.BaseUrl))
    {
        client.BaseAddress = new Uri(apiOptions.BaseUrl);
    }
})
    .AddHttpMessageHandler<BearerTokenHandler>()
    .AddHttpMessageHandler<ProxyHeadersHandler>()
    .AddHttpMessageHandler<ColdStartRetryHandler>();

builder.Services.AddHttpClient<IStaffApiClient, StaffApiClient>((serviceProvider, client) =>
{
    var apiOptions = serviceProvider.GetRequiredService<IOptions<ApiClientOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(apiOptions.BaseUrl)) client.BaseAddress = new Uri(apiOptions.BaseUrl);
})
    .AddHttpMessageHandler<BearerTokenHandler>()
    .AddHttpMessageHandler<ProxyHeadersHandler>()
    .AddHttpMessageHandler<ColdStartRetryHandler>();

// No auth or retry handlers: the warm-up ping only needs the API to receive a request.
builder.Services.AddHttpClient(ApiWarmer.ClientName, (serviceProvider, client) =>
{
    var apiOptions = serviceProvider.GetRequiredService<IOptions<ApiClientOptions>>().Value;
    if (!string.IsNullOrWhiteSpace(apiOptions.BaseUrl)) client.BaseAddress = new Uri(apiOptions.BaseUrl);
});

var app = builder.Build();

// First, so every response gets them, including errors and static files.
var securityHeaders = SecurityHeaders.For(builder.Configuration["Auth0:Domain"]);
app.Use((context, next) =>
{
    foreach (var (name, value) in securityHeaders) context.Response.Headers[name] = value;
    return next(context);
});

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// Page requests only (static files have already been served above). Never waits on the ping.
var apiWarmer = app.Services.GetRequiredService<ApiWarmer>();
app.Use((context, next) =>
{
    apiWarmer.PingIfDue();
    return next(context);
});

app.UseRouting();
app.UseAuthentication();
// After authentication so limits follow the signed-in account; before authorization so unauthenticated
// floods of protected endpoints are limited too.
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

public partial class Program;