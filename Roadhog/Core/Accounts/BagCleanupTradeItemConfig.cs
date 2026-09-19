using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Roadhog.Core.Accounts;

[JsonConverter(typeof(BagCleanupTradeItemConfigJsonConverter))]
public sealed class BagCleanupTradeItemConfig
{
    public string Name { get; set; } = string.Empty;

    // Null means unconfigured. Future executors must skip these entries.
    public long? UnitPrice { get; set; } = 1;

    public static List<BagCleanupTradeItemConfig> Normalize(IEnumerable<BagCleanupTradeItemConfig>? items)
    {
        var result = new List<BagCleanupTradeItemConfig>();
        var byName = new Dictionary<string, BagCleanupTradeItemConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items ?? Enumerable.Empty<BagCleanupTradeItemConfig>())
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Name)) continue;
            if (item.UnitPrice is <= 0) throw new ArgumentException("物品单价必须是大于 0 的整数，或留空。");
            var name = item.Name.Trim();
            if (byName.TryGetValue(name, out var existing))
            {
                existing.UnitPrice ??= item.UnitPrice;
                continue;
            }

            var copy = new BagCleanupTradeItemConfig { Name = name, UnitPrice = item.UnitPrice };
            byName.Add(name, copy);
            result.Add(copy);
        }

        return result;
    }

    public static bool TryParseUnitPrice(string? text, out long? price)
    {
        price = null;
        var value = text?.Trim() ?? string.Empty;
        if (value.Length == 0) return true;
        if (!Regex.IsMatch(value, @"\A(?:[0-9]+|[0-9]{1,3}(?:,[0-9]{3})+)\z") ||
            !long.TryParse(value, NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        {
            return false;
        }

        price = parsed;
        return true;
    }
}

// Older shared/account/profile files stored each trading entry as a name string.
public sealed class BagCleanupTradeItemConfigJsonConverter : JsonConverter<BagCleanupTradeItemConfig>
{
    public override BagCleanupTradeItemConfig Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
            return new BagCleanupTradeItemConfig { Name = reader.GetString() ?? string.Empty };

        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object) throw new JsonException("Invalid trading item.");
        var result = new BagCleanupTradeItemConfig();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Name.Equals("name", StringComparison.OrdinalIgnoreCase))
                result.Name = property.Value.GetString() ?? string.Empty;
            else if (property.Name.Equals("unitPrice", StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind == JsonValueKind.Null)
                {
                    result.UnitPrice = null;
                    continue;
                }
                if (!property.Value.TryGetInt64(out var price) || price <= 0) throw new JsonException("Invalid trading unit price.");
                result.UnitPrice = price;
            }
        }

        if (string.IsNullOrWhiteSpace(result.Name)) throw new JsonException("Trading item name is required.");
        return result;
    }

    public override void Write(Utf8JsonWriter writer, BagCleanupTradeItemConfig value, JsonSerializerOptions options)
    {
        if (value.UnitPrice is <= 0) throw new JsonException("Invalid trading unit price.");
        writer.WriteStartObject();
        writer.WriteString("name", value.Name);
        if (value.UnitPrice is { } price) writer.WriteNumber("unitPrice", price);
        else writer.WriteNull("unitPrice");
        writer.WriteEndObject();
    }
}
