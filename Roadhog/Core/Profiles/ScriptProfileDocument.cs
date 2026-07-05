using Roadhog.Core.Accounts;

namespace Roadhog.Core.Profiles;

public sealed class ScriptProfileDocument
{
    public int Version { get; set; } = 1;

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.Now;

    public ScriptSettings Settings { get; set; } = new();

    public ScriptProfileDocument Clone()
    {
        return new ScriptProfileDocument
        {
            Version = Version,
            Name = Name,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            Settings = (Settings ?? new ScriptSettings()).Clone()
        };
    }
}
