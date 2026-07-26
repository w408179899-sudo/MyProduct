namespace Roadhog.Core.Paths;

public sealed class GatherPointAction
{
    public const double DefaultSearchRadiusMeters = 6.0D;

    public const double DefaultOccupiedCheckRadiusMeters = 5.0D;

    public uint ExpectedGatherSourceId { get; set; }

    public string GatherName { get; set; } = string.Empty;

    public string GatherKey { get; set; } = string.Empty;

    public double SearchRadiusMeters { get; set; } = DefaultSearchRadiusMeters;

    public double OccupiedCheckRadiusMeters { get; set; } = DefaultOccupiedCheckRadiusMeters;

    public GatherPointAction Clone()
    {
        return new GatherPointAction
        {
            ExpectedGatherSourceId = ExpectedGatherSourceId,
            GatherName = GatherName,
            GatherKey = GatherKey,
            SearchRadiusMeters = SearchRadiusMeters,
            OccupiedCheckRadiusMeters = OccupiedCheckRadiusMeters
        };
    }
}
