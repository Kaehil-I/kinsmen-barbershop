using Kinsmen.Web.Models.Api;

namespace Kinsmen.Web.ApiClient;

/// <summary>Thrown for any non-success response from the booking API. Callers should
/// switch on StatusCode/ErrorCode per the table in docs/backend/API-CONTRACT.md
/// ("Error handling for Greg") rather than parsing Message.</summary>
public sealed class KinsmenApiException : Exception
{
    public int StatusCode { get; }

    /// <summary>Parsed application/problem+json body, if the response had one. Some error
    /// responses (auth failures, rate limiting) come back with no body at all.</summary>
    public ApiProblem? Problem { get; }

    /// <summary>The machine-readable conflict code for 409s, e.g. "booking_conflict",
    /// "stale_version", "idempotency_conflict". API-CONTRACT.md doesn't pin down which
    /// problem+json field carries this (its "Shared conventions" section only calls out
    /// status/title/detail, not type) — reads Title first since that's the field the
    /// contract actually documents, falling back to Type. Confirm against a real 409
    /// response the first time this matters, and simplify once confirmed.</summary>
    public string? ErrorCode => Problem?.Title is { Length: > 0 } title ? title : Problem?.Type;

    public KinsmenApiException(int statusCode, ApiProblem? problem, string message)
        : base(message)
    {
        StatusCode = statusCode;
        Problem = problem;
    }
}
