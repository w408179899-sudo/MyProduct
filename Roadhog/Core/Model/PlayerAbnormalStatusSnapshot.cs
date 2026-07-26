namespace Roadhog.Core.Model;

public sealed record PlayerAbnormalStatusSnapshot(
    ushort EntityId,
    DateTimeOffset CapturedAt,
    uint AbnormalCategory2Count,
    IReadOnlyList<AbnormalStatusEntrySnapshot> Entries)
{
    public const uint BuffCategory = 1;

    public const uint PhysicalDebuffCategory = 2;

    public static PlayerAbnormalStatusSnapshot Empty(ushort entityId = 0)
    {
        return new PlayerAbnormalStatusSnapshot(
            entityId,
            DateTimeOffset.Now,
            0,
            Array.Empty<AbnormalStatusEntrySnapshot>());
    }

    public int Category2EntryCount
    {
        get
        {
            var count = 0;
            foreach (var entry in Entries)
            {
                if (entry.IsPhysicalDebuffCategory)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public string Category2EntrySummary
    {
        get
        {
            var samples = new List<string>(8);
            var count = 0;
            foreach (var entry in Entries)
            {
                if (!entry.IsPhysicalDebuffCategory)
                {
                    continue;
                }

                count++;
                if (samples.Count < 8)
                {
                    samples.Add(entry.AbnormalId.ToString() + ":" + entry.Category.ToString());
                }
            }

            if (samples.Count == 0)
            {
                return string.Empty;
            }

            if (count > samples.Count)
            {
                samples.Add("+" + (count - samples.Count).ToString());
            }

            return string.Join(",", samples);
        }
    }

    public int HarmfulAbnormalCount => Category2EntryCount;

    public bool HasHarmfulAbnormalForRest => HarmfulAbnormalCount > 0;

    public string HarmfulAbnormalSummary => Category2EntrySummary;
}
