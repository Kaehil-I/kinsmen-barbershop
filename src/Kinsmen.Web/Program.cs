using Kinsmen.Web.ApiClient;
using Kinsmen.Web.Auth;
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

builder.Services.Configure<ApiClientOptions>(builder.Configuration.GetSection("Api"));
builder.Services.Configure<DevTokenOptions>(builder.Configuration.GetSection("DevTokens"));

// Swappable seam: DevelopmentTokenProvider reads Zario's short-lived dev tokens from
// config for now. Replace with a session/claims-based provider once Kyra's real login
// exists — nothing else here needs to change (see Auth/ITokenProvider.cs).
builder.Services.AddScoped<ITokenProvider, DevelopmentTokenProvider>();
builder.Services.AddTransient<BearerTokenHandler>();

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
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
