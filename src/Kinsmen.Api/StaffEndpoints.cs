using Kinsmen.Api.Domain;
using Kinsmen.Api.Infrastructure;

namespace Kinsmen.Api;

// Admin-only staff management: list staff, look people up by email, change roles, and read the audit log.
// Kept out of Program.cs so the feature is self-contained; StaffService enforces the Admin role and the rules.
public static class StaffEndpoints
{
    public static IServiceCollection AddStaffManagement(this IServiceCollection services, IConfiguration configuration)
    {
        var options = new Auth0ManagementOptions
        {
            // Defaults to the tenant the API already trusts for sign-in (Auth:Authority).
            Domain = configuration["Auth0Management:Domain"]
                ?? (Uri.TryCreate(configuration["Auth:Authority"], UriKind.Absolute, out var authority) ? authority.Host : null),
            ClientId = configuration["Auth0Management:ClientId"],
            ClientSecret = configuration["Auth0Management:ClientSecret"]
        };
        services.AddSingleton(options);
        services.AddSingleton<Auth0TokenCache>();
        if (options.IsConfigured)
            services.AddHttpClient<IIdentityDirectory, Auth0ManagementDirectory>(client =>
            {
                client.BaseAddress = new Uri($"https://{options.Domain}/");
                client.Timeout = TimeSpan.FromSeconds(10);
            });
        else
            services.AddSingleton<IIdentityDirectory, DisabledIdentityDirectory>();
        services.AddSingleton<IAuditLog>(s => new MongoAuditLog(s.GetRequiredService<MongoBookingStore>()));
        services.AddTransient<StaffService>();
        return services;
    }

    public static void MapStaffEndpoints(this WebApplication app, Func<HttpContext, Actor> currentActor)
    {
        var staff = app.MapGroup("/api/admin/staff").RequireAuthorization();
        staff.MapGet("/status", (HttpContext ctx, StaffService service) => new { enabled = service.IsEnabled(currentActor(ctx)) });
        staff.MapGet("", (HttpContext ctx, StaffService service, CancellationToken ct) => service.ListStaff(currentActor(ctx), ct));
        staff.MapGet("/lookup", (string? email, HttpContext ctx, StaffService service, CancellationToken ct)
            => service.FindByEmail(currentActor(ctx), email, ct));
        staff.MapPut("/{userId}/role", (string userId, RoleChangeRequest input, HttpContext ctx, StaffService service, CancellationToken ct)
            => service.ChangeRole(currentActor(ctx), userId, input.Role, ct));
        staff.MapGet("/audit", (HttpContext ctx, StaffService service, CancellationToken ct) => service.RecentChanges(currentActor(ctx), ct));
    }
}
