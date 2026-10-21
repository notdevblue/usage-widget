using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UsageMonitor.Services;

internal static class ProfileJson
{
    internal static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
        WriteIndented = true,
        Converters =
        {
            new EnumConverter<FiveHourDisplay>(),
            new EnumConverter<WidgetTheme>(),
            new EnumConverter<LayoutBlockKind>(),
            new EnumConverter<UsageVisualization>(),
            new EnumConverter<UsageWindowSource>()
        }
    };

    private sealed class EnumConverter<TEnum> : JsonConverter<TEnum> where TEnum : struct, Enum
    {
        public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var text = reader.GetString() ?? "";
                if (!text.Contains(',') && Enum.TryParse<TEnum>(text, true, out var parsed) && Enum.IsDefined(parsed))
                    return parsed;
            }
            if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
            {
                var value = (TEnum)Enum.ToObject(typeof(TEnum), number);
                return Enum.IsDefined(value) ? value : new();
            }
            if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                reader.Skip();
            return new();
        }

        public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString());
    }
}
