namespace Kinsmen.Web.Models.Api;

/// <summary>Matches the API's application/problem+json error body (status, title, detail,
/// traceId). Some error responses (auth failures, rate limiting) can come back with no
/// body at all — KinsmenApiClient handles that case and leaves this null.</summary>
public sealed class ApiProblem
{
    public string? Type { get; set; }
    public string Title { get; set; } = string.Empty;
    public int Status { get; set; }
    public string? Detail { get; set; }
    public string? TraceId { get; set; }
}
