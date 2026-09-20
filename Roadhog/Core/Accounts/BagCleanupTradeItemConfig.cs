using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Roadhog.Core.Accounts;

public enum AuctionPriceLookupMethod { Manual, DialogMinimum, SearchCalculation }

[JsonConverter(typeof(BagCleanupTradeItemConfigJsonConverter))]
public sealed class BagCleanupTradeItemConfig
{
    public string Name { get; set; } = string.Empty;

    // Retained when automatic lookup is selected, for switching back to manual.
    public long? UnitPrice { get; set; } = 1;
    public AuctionPriceLookupMethod PriceLookupMethod { get; set; } = AuctionPriceLookupMethod.Manual;
    [JsonIgnore]
    public long? EffectiveUnitPrice => PriceLookupMethod == AuctionPriceLookupMethod.Manual ? UnitPrice : null;

    public static List<BagCleanupTradeItemConfig> Normalize(IEnumerable<BagCleanupTradeItemConfig>? items)
    {
        var result = new List<BagCleanupTradeItemConfig>();
        var byName = new Dictionary<string, BagCleanupTradeItemConfig>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items ?? Enumerable.Empty<BagCleanupTradeItemConfig>())
        {
            if (item is null || string.IsNullOrWhiteSpace(item.Name)) continue;
            if (item.EffectiveUnitPrice is <= 0) throw new ArgumentException("物品单价必须是大于 0 的整数，或留空。");
            if (!Enum.IsDefined(item.PriceLookupMethod)) throw new ArgumentException("无效的查价方式。");
            var name = item.Name.Trim();
            if (byName.TryGetValue(name, out var existing))
            {
                if (existing.PriceLookupMethod == AuctionPriceLookupMethod.Manual) existing.UnitPrice ??= item.EffectiveUnitPrice;
                continue;
            }

            var copy = new BagCleanupTradeItemConfig { Name = name, UnitPrice = item.UnitPrice, PriceLookupMethod = item.PriceLookupMethod };
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
        JsonElement? priceValue = null;
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Name.Equals("name", StringComparison.OrdinalIgnoreCase))
                result.Name = property.Value.GetString() ?? string.Empty;
            else if (property.Name.Equals("unitPrice", StringComparison.OrdinalIgnoreCase))
            {
                priceValue = property.Value;
            }
            else if (property.Name.Equals("priceLookupMethod", StringComparison.OrdinalIgnoreCase))
            {
                if (property.Value.ValueKind != JsonValueKind.String ||
                    !Enum.TryParse<AuctionPriceLookupMethod>(property.Value.GetString(), true, out var method) || !Enum.IsDefined(method))
                    throw new JsonException("Invalid auction price lookup method.");
                result.PriceLookupMethod = method;
            }
        }

        if (priceValue is { } value)
        {
            if (value.ValueKind == JsonValueKind.Null) result.UnitPrice = null;
            else if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var price)) result.UnitPrice = price;
            else if (result.PriceLookupMethod == AuctionPriceLookupMethod.Manual) throw new JsonException("Invalid trading unit price.");
            else result.UnitPrice = null;
        }
        if (result.EffectiveUnitPrice is <= 0) throw new JsonException("Invalid trading unit price.");

        if (string.IsNullOrWhiteSpace(result.Name)) throw new JsonException("Trading item name is required.");
        return result;
    }

    public override void Write(Utf8JsonWriter writer, BagCleanupTradeItemConfig value, JsonSerializerOptions options)
    {
        if (value.EffectiveUnitPrice is <= 0) throw new JsonException("Invalid trading unit price.");
        if (!Enum.IsDefined(value.PriceLookupMethod)) throw new JsonException("Invalid auction price lookup method.");
        writer.WriteStartObject();
        writer.WriteString("name", value.Name);
        if (value.UnitPrice is { } price) writer.WriteNumber("unitPrice", price);
        else writer.WriteNull("unitPrice");
        writer.WriteString("priceLookupMethod", value.PriceLookupMethod.ToString());
        writer.WriteEndObject();
    }
}
