namespace Kinsmen.Web.ApiClient;

/// <summary>Bound from the "Api" configuration section.</summary>
public sealed class ApiClientOptions
{
    /// <summary>Base URL of src/Kinsmen.Api — e.g. http://127.0.0.1:5080 locally.
    /// Kaehil sets the hosted value once deployment exists.</summary>
    public string BaseUrl { get; set; } = string.Empty;
}
