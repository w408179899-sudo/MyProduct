using Roadhog.Core.Accounts;
using Roadhog.Core.Common;

namespace Roadhog.Core.Profiles;

public sealed class AdvancedSkillProfile
{
    public string Name { get; set; } = string.Empty;
    public string Revision { get; set; } = string.Empty;
    public AdvancedSkillSettings Settings { get; set; } = new();
    public AdvancedSkillProfile Clone() => new() { Name = Name, Revision = Revision, Settings = Settings.Clone() };
}

public interface IAdvancedSkillProfileStore
{
    Task<OperationResult<IReadOnlyList<AdvancedSkillProfile>>> LoadAllAsync(CancellationToken token = default);
    // Empty revision creates a new name; an existing name requires its last-read revision.
    Task<OperationResult<AdvancedSkillProfile>> SaveAsync(AdvancedSkillProfile profile, CancellationToken token = default);
    Task<OperationResult> DeleteAsync(string name, string revision, CancellationToken token = default);
}
