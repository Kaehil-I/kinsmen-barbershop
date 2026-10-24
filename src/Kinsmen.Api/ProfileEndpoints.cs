using Kinsmen.Api.Domain;
using Kinsmen.Api.Infrastructure;

namespace Kinsmen.Api;

// A signed-in customer's own profile. Kept out of Program.cs so the feature is self-contained;
// ProfileService enforces the Customer role and the validation rules.
public static class ProfileEndpoints
{
    public static IServiceCollection AddCustomerProfiles(this IServiceCollection services)
    {
        services.AddSingleton<IProfileStore>(s => new MongoProfileStore(s.GetRequiredService<MongoBookingStore>()));
        services.AddTransient<ProfileService>();
        return services;
    }

    public static void MapProfileEndpoints(this WebApplication app, Func<HttpContext, Actor> currentActor)
    {
        var me = app.MapGroup("/api/me").RequireAuthorization();
        me.MapGet("/profile", (HttpContext ctx, ProfileService service, CancellationToken ct)
            => service.Get(currentActor(ctx), ct));
        me.MapPut("/profile", (ProfileRequest input, HttpContext ctx, ProfileService service, CancellationToken ct)
            => service.Save(currentActor(ctx), input, ct));
    }
}
