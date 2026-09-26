using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Kinsmen.Api.Domain;
using Kinsmen.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;

// Local maintenance commands have positional arguments, not ASP.NET configuration switches.
var tokenIndex = Array.IndexOf(args, "--demo-token");
if (tokenIndex >= 0 && args.Length < tokenIndex + 3)
    throw new InvalidOperationException("Usage: --demo-token <userId> <Customer|Barber|Admin>.");
var configurationArgs = args.Where((arg, i) => arg is not ("--initialize" or "--seed-demo")
    && !(tokenIndex >= 0 && i >= tokenIndex && i <= tokenIndex + 2)).ToArray();
var builder = WebApplication.CreateBuilder(configurationArgs);
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole();
// The development API uses short-lived JWTs, not persistent authentication cookies.
if (builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing"))
    builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 64 * 1024);
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));
    o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    o.SerializerOptions.Converters.Add(new OffsetDateTimeConverter());
});
builder.Services.AddSingleton(TimeProvider.System);
var policy = builder.Configuration.GetSection("Booking").Get<BookingPolicy>() ?? new();
if (policy.SlotMinutes is < 1 or > 60 || 60 % policy.SlotMinutes != 0 || policy.HorizonDays is < 1 or > 365
    || policy.MinimumNoticeMinutes < 0 || policy.CancellationNoticeMinutes < 0)
    throw new InvalidOperationException("Invalid booking policy configuration.");
builder.Services.AddSingleton(policy);
builder.Services.AddSingleton(_ => new MongoBookingStore(
    builder.Configuration["Mongo:ConnectionString"] ?? throw new InvalidOperationException("Mongo connection required."),
    builder.Configuration["Mongo:Database"] ?? "kinsmen_dev"));
builder.Services.AddSingleton<IBookingStore>(s => s.GetRequiredService<MongoBookingStore>());
builder.Services.AddSingleton<BookingService>();
builder.Services.AddSingleton<CatalogueService>();

var authority = builder.Configuration["Auth:Authority"];
var localKey = builder.Configuration["Auth:DevelopmentSigningKey"];
var audience = builder.Configuration["Auth:Audience"] ?? "kinsmen-api";
var issuer = builder.Configuration["Auth:Issuer"] ?? "kinsmen-local";
if (!string.IsNullOrEmpty(authority))
{
    if (!Uri.TryCreate(authority, UriKind.Absolute, out var uri) || uri.Scheme != "https")
        throw new InvalidOperationException("Auth:Authority must be an HTTPS identity provider URL.");
    builder.Services.AddAuthentication("Bearer").AddJwtBearer(o =>
    {
        o.Authority = authority; o.Audience = audience; o.MapInboundClaims = false;
        o.TokenValidationParameters.NameClaimType = "sub";
        o.TokenValidationParameters.RoleClaimType = "role";
    });
}
else if (builder.Environment.IsDevelopment() && !string.IsNullOrWhiteSpace(localKey))
{
    if (Encoding.UTF8.GetByteCount(localKey) < 32) throw new InvalidOperationException("Development signing key must be at least 32 bytes.");
    builder.Services.AddAuthentication("Bearer").AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true, ValidIssuer = issuer, ValidateAudience = true, ValidAudience = audience,
            ValidateLifetime = true, ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(localKey)),
            NameClaimType = "sub", RoleClaimType = "role", ClockSkew = TimeSpan.FromSeconds(15),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
        };
    });
}
else
{
    if (!builder.Environment.IsDevelopment() && !builder.Environment.IsEnvironment("Testing"))
        throw new InvalidOperationException("Production requires Auth:Authority and Auth:Audience. Development tokens cannot be enabled in production.");
    builder.Services.AddAuthentication("Bearer").AddScheme<AuthenticationSchemeOptions, RejectAuthentication>("Bearer", _ => { });
}
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        RateLimitPartition.GetFixedWindowLimiter(ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});

var app = builder.Build();
if (args.Contains("--initialize"))
{
    if (args.Contains("--seed-demo") && !app.Environment.IsDevelopment())
        throw new InvalidOperationException("Demo seeding is development-only.");
    var mongo = app.Services.GetRequiredService<MongoBookingStore>();
    await mongo.Initialize();
    if (args.Contains("--seed-demo"))
    {
        if (!app.Environment.IsDevelopment()) throw new InvalidOperationException("Demo seeding is development-only.");
        await mongo.SeedDemo();
    }
    Console.WriteLine("MongoDB collections and indexes initialized.");
    return;
}
if (tokenIndex >= 0)
{
    if (!app.Environment.IsDevelopment() || string.IsNullOrEmpty(localKey) || args.Length < tokenIndex + 3)
        throw new InvalidOperationException("Development only: --demo-token <userId> <Customer|Barber|Admin>, with DevelopmentSigningKey configured.");
    var userId = args[tokenIndex + 1]; var role = args[tokenIndex + 2];
    if (role is not ("Customer" or "Barber" or "Admin") || string.IsNullOrWhiteSpace(userId)) throw new InvalidOperationException("Invalid demo identity.");
    var token = new JwtSecurityToken(issuer, audience, [new Claim("sub", userId), new Claim("role", role)],
        expires: DateTime.UtcNow.AddMinutes(30), signingCredentials: new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(localKey)), SecurityAlgorithms.HmacSha256));
    Console.WriteLine(new JwtSecurityTokenHandler().WriteToken(token));
    return;
}

app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers.CacheControl = "no-store";
    try { await next(context); }
    catch (DomainError e)
    {
        await Results.Problem(statusCode: e.Status, title: e.Code, detail: e.Message).ExecuteAsync(context);
    }
    catch (BadHttpRequestException e)
    {
        await Results.Problem(statusCode: e.StatusCode, title: "invalid_request", detail: "Check the request body and query parameter format.").ExecuteAsync(context);
    }
    catch (Exception e) when (e is MongoException or TimeoutException)
    {
        // Do not return connection details, credentials or customer information in errors.
        app.Logger.LogWarning("Database request failed: {ErrorType}", e.GetType().Name);
        await Results.Problem(statusCode: 503, title: "database_unavailable", detail: "The booking service is temporarily unavailable. Refresh before retrying a write.").ExecuteAsync(context);
    }
    catch (Exception e) when (e is not OperationCanceledException && !context.Response.HasStarted)
    {
        app.Logger.LogError("Unhandled request failure: {ErrorType}; trace {TraceId}", e.GetType().Name, context.TraceIdentifier);
        await Results.Problem(statusCode: 500, title: "server_error", detail: "An unexpected error occurred.",
            extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
    }
});
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapGet("/health/live", () => Results.Ok(new { status = "alive" }));
app.MapGet("/health/ready", async (MongoBookingStore store, CancellationToken ct) => { await store.Ping(ct); return Results.Ok(new { status = "ready" }); });
app.MapGet("/api/services", (BookingService service, CancellationToken ct) => service.Services(ct));
app.MapGet("/api/barbers", async (BookingService service, CancellationToken ct) =>
    (await service.Barbers(ct)).Select(b => new { b.Id, b.Name, b.Hours }));
app.MapGet("/api/availability", (DateOnly date, string[] serviceIds, string? barberId, BookingService service, CancellationToken ct)
    => service.Availability(date, serviceIds, barberId, ct));

var secure = app.MapGroup("/api").RequireAuthorization();
secure.MapPost("/bookings", async (CreateBookingRequest input, HttpContext ctx, BookingService service, CancellationToken ct) =>
{
    string? key = null;
    if (ctx.Request.Headers.TryGetValue("Idempotency-Key", out var keys))
    {
        if (keys.Count != 1) throw DomainError.Invalid("Supply exactly one Idempotency-Key header.");
        key = keys[0];
    }
    var booking = await service.Create(CurrentActor(ctx), input, ct, key);
    return Results.Json(booking, statusCode: 201);
});
secure.MapGet("/bookings", (DateTimeOffset from, DateTimeOffset to, string? barberId, HttpContext ctx, BookingService service, CancellationToken ct)
    => service.List(CurrentActor(ctx), from, to, barberId, ct));
secure.MapGet("/bookings/{id}", (string id, HttpContext ctx, BookingService service, CancellationToken ct)
    => service.Get(CurrentActor(ctx), id, ct));
secure.MapPatch("/bookings/{id}/reschedule", (string id, RescheduleRequest input, HttpContext ctx, BookingService service, CancellationToken ct)
    => service.Reschedule(CurrentActor(ctx), id, input, ct));
secure.MapPost("/bookings/{id}/cancel", (string id, VersionRequest input, HttpContext ctx, BookingService service, CancellationToken ct)
    => service.Cancel(CurrentActor(ctx), id, input.Version, ct));
secure.MapPatch("/bookings/{id}/status", (string id, StatusRequest input, HttpContext ctx, BookingService service, CancellationToken ct)
    => service.ChangeStatus(CurrentActor(ctx), id, input, ct));
secure.MapGet("/barbers/{barberId}/blocks", (string barberId, DateTimeOffset from, DateTimeOffset to, HttpContext ctx, BookingService service, CancellationToken ct)
    => service.ListBlocks(CurrentActor(ctx), barberId, from, to, ct));
secure.MapPost("/barbers/{barberId}/blocks", async (string barberId, BlockRequest input, HttpContext ctx, BookingService service, CancellationToken ct)
    => Results.Json(await service.AddBlock(CurrentActor(ctx), barberId, input, ct), statusCode: 201));
secure.MapDelete("/blocks/{id}", async (string id, HttpContext ctx, BookingService service, CancellationToken ct) =>
{
    await service.RemoveBlock(CurrentActor(ctx), id, ct);
    return Results.NoContent();
});

// Admin catalogue: CatalogueService enforces the Admin role and returns 403 for everyone else.
var admin = app.MapGroup("/api/admin").RequireAuthorization();
admin.MapGet("/services", (HttpContext ctx, CatalogueService catalogue, CancellationToken ct)
    => catalogue.AllServices(CurrentActor(ctx), ct));
admin.MapPost("/services", async (ServiceRequest input, HttpContext ctx, CatalogueService catalogue, CancellationToken ct)
    => Results.Json(await catalogue.CreateService(CurrentActor(ctx), input, ct), statusCode: 201));
admin.MapPut("/services/{id}", (string id, ServiceRequest input, HttpContext ctx, CatalogueService catalogue, CancellationToken ct)
    => catalogue.UpdateService(CurrentActor(ctx), id, input, ct));
admin.MapPatch("/services/{id}/active", (string id, ActiveRequest input, HttpContext ctx, CatalogueService catalogue, CancellationToken ct)
    => catalogue.SetServiceActive(CurrentActor(ctx), id, input.Active, ct));
admin.MapGet("/barbers", (HttpContext ctx, CatalogueService catalogue, CancellationToken ct)
    => catalogue.AllBarbers(CurrentActor(ctx), ct));
admin.MapPost("/barbers", async (BarberRequest input, HttpContext ctx, CatalogueService catalogue, CancellationToken ct)
    => Results.Json(await catalogue.CreateBarber(CurrentActor(ctx), input, ct), statusCode: 201));
admin.MapPut("/barbers/{id}", (string id, BarberRequest input, HttpContext ctx, CatalogueService catalogue, CancellationToken ct)
    => catalogue.UpdateBarber(CurrentActor(ctx), id, input, ct));
admin.MapPatch("/barbers/{id}/active", (string id, ActiveRequest input, HttpContext ctx, CatalogueService catalogue, CancellationToken ct)
    => catalogue.SetBarberActive(CurrentActor(ctx), id, input.Active, ct));
app.Run();

static Actor CurrentActor(HttpContext context)
{
    var sub = context.User.FindFirstValue("sub"); var role = context.User.FindFirstValue("role");
    if (string.IsNullOrWhiteSpace(sub) || sub.Length > 128) throw new DomainError(401, "invalid_identity", "A subject claim is required.");
    if (role is not ("Customer" or "Barber" or "Admin")) throw DomainError.Forbidden();
    return new Actor(sub, role);
}

public sealed class RejectAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(AuthenticateResult.NoResult());
}
public partial class Program;
