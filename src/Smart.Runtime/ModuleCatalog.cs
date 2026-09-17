using Smart.Contracts;
namespace Smart.Runtime;

public static class ModuleCatalog
{
    public static IReadOnlyList<IAccountModule> Validate(IEnumerable<IAccountModule> modules, IReadOnlyCollection<string> channels)
    {
        ArgumentNullException.ThrowIfNull(modules);
        ArgumentNullException.ThrowIfNull(channels);
        // Apply the bound while enumerating: extension code may supply a lazy or infinite sequence.
        var entries = modules.Take(65).ToArray();
        if (entries.Length > 64 || entries.Any(x => x is null || string.IsNullOrWhiteSpace(x.Id)) ||
            entries.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != entries.Length)
            throw new ArgumentException("Require at most 64 uniquely named modules.");
        var byId = entries.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var registered = channels.ToHashSet(StringComparer.Ordinal);
        var visiting = new HashSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<IAccountModule>();
        void Visit(IAccountModule module)
        {
            if (visited.Contains(module.Id)) return;
            if (!visiting.Add(module.Id)) throw new ArgumentException("Module dependency cycle: " + module.Id);
            foreach (var channel in module.RequiredChannels)
                if (!registered.Contains(channel)) throw new ArgumentException($"Module {module.Id} requires unregistered channel {channel}.");
            foreach (var dependency in module.Dependencies)
            {
                if (!byId.TryGetValue(dependency, out var other)) throw new ArgumentException("Missing module dependency: " + dependency);
                Visit(other);
            }
            visiting.Remove(module.Id); visited.Add(module.Id); ordered.Add(module);
        }
        foreach (var module in entries) Visit(module);
        return ordered;
    }
}
