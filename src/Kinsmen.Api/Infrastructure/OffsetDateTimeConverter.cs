using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Kinsmen.Api.Infrastructure;

// Prevent an unspecified client timezone from being interpreted in the server's timezone.
public sealed partial class OffsetDateTimeConverter : JsonConverter<DateTimeOffset>
{
    [GeneratedRegex(@"T.*(?:Z|[+-]\d{2}:\d{2})$", RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitOffset();
    public override DateTimeOffset Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String) throw new JsonException("A timestamp string is required.");
        var value = reader.GetString();
        if (value is null || !ExplicitOffset().IsMatch(value) || !DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var result)) throw new JsonException("Supply an ISO timestamp with Z or a numeric UTC offset.");
        return result;
    }
    public override void Write(Utf8JsonWriter writer, DateTimeOffset value, JsonSerializerOptions options) => writer.WriteStringValue(value);
}
