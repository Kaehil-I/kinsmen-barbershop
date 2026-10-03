namespace Kinsmen.Web.Models.Api;

/// <summary>Admin view of a barber (GET /api/admin/barbers). Unlike the public barber
/// list this includes the linked login (UserId) and the active flag - which is why it's
/// Admin-only. Services reuse the plain Service model, since the admin list returns the
/// same shape (just including inactive ones).</summary>
public sealed class AdminBarber
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public List<WorkingPeriod> Hours { get; set; } = [];
    public bool Active { get; set; }
}

/// <summary>Body for POST/PUT /api/admin/services. The API rejects any JSON member it
/// doesn't recognise, so this holds exactly the three fields it accepts and nothing else.</summary>
public sealed class ServiceInput
{
    public string Name { get; set; } = string.Empty;

    /// <summary>Integer ZAR cents, 0 to 1,000,000.</summary>
    public int PriceCents { get; set; }

    /// <summary>1 to 480 minutes.</summary>
    public int DurationMinutes { get; set; }
}

/// <summary>Body for POST/PUT /api/admin/barbers. Exactly the fields the API accepts.</summary>
public sealed class BarberInput
{
    public string Name { get; set; } = string.Empty;

    /// <summary>The barber's Auth0 user ID. Required and unique across barbers.</summary>
    public string UserId { get; set; } = string.Empty;

    public List<WorkingPeriod> Hours { get; set; } = [];
}

/// <summary>Body for PATCH /api/admin/services/{id}/active and
/// /api/admin/barbers/{id}/active.</summary>
public sealed class ActiveInput
{
    public bool Active { get; set; }
}
