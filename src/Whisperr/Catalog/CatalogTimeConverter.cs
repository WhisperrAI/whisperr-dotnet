using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Whisperr;

/// <summary>
/// RFC 3339 in UTC with <c>Z</c>; milliseconds only when they are not zero.
/// </summary>
internal sealed class CatalogTimeConverter : JsonConverter<DateTimeOffset?>
{
    public override bool HandleNull => true;

    public static string Format(DateTimeOffset value)
    {
        var utc = value.ToUniversalTime();
        var format = utc.Millisecond == 0 ? "yyyy-MM-dd'T'HH:mm:ss'Z'" : "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";
        return utc.ToString(format, CultureInfo.InvariantCulture);
    }

    public override DateTimeOffset? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        var text = reader.GetString();
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        return DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
    }

    public override void Write(Utf8JsonWriter writer, DateTimeOffset? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
        {
            writer.WriteStringValue(Format(value.Value));
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}
