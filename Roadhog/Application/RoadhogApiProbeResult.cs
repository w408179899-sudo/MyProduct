#if DEBUG
using System.Globalization;

namespace Roadhog.Application;

public sealed record RoadhogApiProbeCheckResult(
    string Name,
    bool Success,
    string Detail)
{
    public static RoadhogApiProbeCheckResult Pass(string name, string detail)
    {
        return new RoadhogApiProbeCheckResult(name, true, NormalizeDetail(detail));
    }

    public static RoadhogApiProbeCheckResult Fail(string name, string detail)
    {
        return new RoadhogApiProbeCheckResult(name, false, NormalizeDetail(detail));
    }

    private static string NormalizeDetail(string detail)
    {
        return string.IsNullOrWhiteSpace(detail) ? "no detail" : detail.Trim();
    }
}

public sealed record RoadhogApiProbeResult(
    IReadOnlyList<RoadhogApiProbeCheckResult> Checks)
{
    public static readonly IReadOnlyList<string> RequiredCheckNames = Array.AsReadOnly(new[]
    {
        "Player",
        "PlayerAbnormalStatuses",
        "LockedTarget",
        "LockedTargetAbnormalStatuses",
        "SummonedPet",
        "SummonedPetRoster",
        "Skills",
        "Inventory",
        "InventoryMoney",
        "InventoryCapacity",
        "WorldObjects",
        "LootCorpses",
        "InventoryWindow.LegacyDialogRect",
        "InventoryWindow.RootWidgetRectExperimental"
    });

    public int TotalCount => Checks.Count;

    public int PassedCount => Checks.Count(check => check.Success);

    public int FailedCount => Checks.Count(check => !check.Success);

    public bool AllPassed => FailedCount == 0;

    public string ToDisplayText()
    {
        var lines = new List<string>
        {
            "API探针结果: " +
            PassedCount.ToString(CultureInfo.InvariantCulture) +
            "/" +
            TotalCount.ToString(CultureInfo.InvariantCulture) +
            " 通过"
        };

        foreach (var check in Checks)
        {
            lines.Add((check.Success ? "PASS " : "FAIL ") + check.Name + ": " + check.Detail);
        }

        return string.Join(Environment.NewLine, lines);
    }
}
#endif
