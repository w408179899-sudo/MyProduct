using Roadhog.Core.Model;

namespace Roadhog.Application.StationaryCombat;

internal static class MonsterKillPriority
{
    public static Func<WorldObjectSnapshot, int>? CreateRanker(IReadOnlyList<string>? priorities)
    {
        if (priorities is not { Count: > 0 }) return null;

        var ranks = new Dictionary<string, int>(priorities.Count, StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < priorities.Count; index++)
        {
            var name = priorities[index];
            if (!string.IsNullOrWhiteSpace(name)) ranks.TryAdd(name.Trim(), index);
        }

        return ranks.Count == 0 ? null : target => ranks.GetValueOrDefault(target.Name?.Trim() ?? string.Empty, int.MaxValue);
    }
}
