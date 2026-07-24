using System.Text.Json.Serialization;

namespace Roadhog.Core.Paths;

public sealed class SharedPathDocument
{
    public int Version { get; set; } = 1;

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    public string CleanupNpcName { get; set; } = string.Empty;

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? BagCleanupSellItemClickX { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? BagCleanupSellItemClickY { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? BagCleanupSellButtonClickX { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? BagCleanupSellButtonClickY { get; set; }

    public List<SharedPathPoint> Points { get; set; } = new();

    public int PointCount => Points?.Count ?? 0;

    public double TotalDistance => Points is { Count: > 0 }
        ? Points[^1].TotalDistance
        : 0.0D;

    public SharedPathDocument Clone()
    {
        return new SharedPathDocument
        {
            Version = Version,
            Name = Name,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            CleanupNpcName = CleanupNpcName,
            BagCleanupSellItemClickX = BagCleanupSellItemClickX,
            BagCleanupSellItemClickY = BagCleanupSellItemClickY,
            BagCleanupSellButtonClickX = BagCleanupSellButtonClickX,
            BagCleanupSellButtonClickY = BagCleanupSellButtonClickY,
            Points = Points?.Select(point => point.Clone()).ToList() ?? new List<SharedPathPoint>()
        };
    }

    public bool TryGetBagCleanupClickPoints(
        out int sellItemClickX,
        out int sellItemClickY,
        out int sellButtonClickX,
        out int sellButtonClickY)
    {
        if (BagCleanupSellItemClickX.HasValue &&
            BagCleanupSellItemClickY.HasValue &&
            BagCleanupSellButtonClickX.HasValue &&
            BagCleanupSellButtonClickY.HasValue)
        {
            sellItemClickX = BagCleanupSellItemClickX.Value;
            sellItemClickY = BagCleanupSellItemClickY.Value;
            sellButtonClickX = BagCleanupSellButtonClickX.Value;
            sellButtonClickY = BagCleanupSellButtonClickY.Value;
            return true;
        }

        sellItemClickX = 0;
        sellItemClickY = 0;
        sellButtonClickX = 0;
        sellButtonClickY = 0;
        return false;
    }
}
