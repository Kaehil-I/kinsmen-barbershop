using Auth0.AspNetCore.Authentication;
using Kinsmen.Web.ApiClient;
using Kinsmen.Web.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Text.Json.Serialization;

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
    .AddHttpMessageHandler<BearerTokenHandler>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();

public partial class Program;