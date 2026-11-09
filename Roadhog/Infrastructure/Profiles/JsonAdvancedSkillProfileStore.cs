using System.Text.Json;
using Roadhog.Core.Common;
using Roadhog.Core.Profiles;
using Roadhog.Infrastructure.Config;

namespace Roadhog.Infrastructure.Profiles;

/// <summary>One independent library, with atomic reads and revision-checked writes across processes.</summary>
public sealed class JsonAdvancedSkillProfileStore(string path) : IAdvancedSkillProfileStore
{
    private readonly string path = Path.GetFullPath(path);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static string PathFor(string accountConfigPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(accountConfigPath))!, "advanced-skill-profiles.json");

    public async Task<OperationResult<IReadOnlyList<AdvancedSkillProfile>>> LoadAllAsync(CancellationToken token = default)
    {
        try { return OperationResult<IReadOnlyList<AdvancedSkillProfile>>.Ok(await ReadAsync(token).ConfigureAwait(false)); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { return OperationResult<IReadOnlyList<AdvancedSkillProfile>>.Fail(ex.Message); }
    }

    private async Task<List<AdvancedSkillProfile>> ReadAsync(CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (!AtomicJsonFile.Exists(path)) return new();
        await using var stream = AtomicJsonFile.OpenRead(path);
        var data = await JsonSerializer.DeserializeAsync<Library>(stream, Json, token).ConfigureAwait(false);
        if (data is null || data.Version != 1 || data.Profiles is null ||
            data.Profiles.Any(p => p is null || string.IsNullOrWhiteSpace(p.Name) || string.IsNullOrWhiteSpace(p.Revision) || p.Settings is null) ||
            data.Profiles.Select(p => p.Name.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() != data.Profiles.Count)
            throw new InvalidDataException("高级方案库格式无效，未覆盖原文件。");
        return data.Profiles.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).Select(p => p.Clone()).ToList();
    }

    private async Task<FileStream> LockAsync(CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var until = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            token.ThrowIfCancellationRequested();
            try { return new FileStream(path + ".lock", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
            catch (IOException) when (DateTime.UtcNow < until) { await Task.Delay(25, token).ConfigureAwait(false); }
        }
    }

    public async Task<OperationResult<AdvancedSkillProfile>> SaveAsync(AdvancedSkillProfile profile, CancellationToken token = default)
    {
        var next = profile.Clone(); next.Name = next.Name.Trim();
        if (next.Name.Length is 0 or > 80 || next.Name.Any(char.IsControl))
            return OperationResult<AdvancedSkillProfile>.Fail("方案名称须为 1 至 80 个字符。");
        if (next.Settings.DuplicateMonster() is { } duplicate)
            return OperationResult<AdvancedSkillProfile>.Fail("怪物重复归类：" + duplicate);
        try
        {
            await using var gate = await LockAsync(token).ConfigureAwait(false);
            var profiles = await ReadAsync(token).ConfigureAwait(false);
            var previous = profiles.Find(p => string.Equals(p.Name, next.Name, StringComparison.OrdinalIgnoreCase));
            if ((previous?.Revision ?? "") != next.Revision)
                return OperationResult<AdvancedSkillProfile>.Fail("方案已被修改、删除或名称已存在，请重新选择后再保存，或另存为新名称。");
            if (previous is not null) { next.Name = previous.Name; profiles.Remove(previous); }
            next.Revision = Guid.NewGuid().ToString("N");
            next.Settings.Enabled = false; // The master switch belongs to the account, never the shared library.
            profiles.Add(next);
            await AtomicJsonFile.WriteAsync(path, new Library { Profiles = profiles }, Json, token).ConfigureAwait(false);
            return OperationResult<AdvancedSkillProfile>.Ok(next.Clone());
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { return OperationResult<AdvancedSkillProfile>.Fail(ex.Message); }
    }

    public async Task<OperationResult> DeleteAsync(string name, string revision, CancellationToken token = default)
    {
        try
        {
            await using var gate = await LockAsync(token).ConfigureAwait(false);
            var profiles = await ReadAsync(token).ConfigureAwait(false);
            var previous = profiles.Find(p => string.Equals(p.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));
            if (previous is null || previous.Revision != revision)
                return OperationResult.Fail("方案已变化，请重新选择后再删除。");
            profiles.Remove(previous);
            await AtomicJsonFile.WriteAsync(path, new Library { Profiles = profiles }, Json, token).ConfigureAwait(false);
            return OperationResult.Ok();
        }
        catch (Exception ex) when (ex is not OperationCanceledException) { return OperationResult.Fail(ex.Message); }
    }

    private sealed class Library
    {
        [System.Text.Json.Serialization.JsonRequired]
        public int Version { get; set; } = 1;
        [System.Text.Json.Serialization.JsonRequired]
        public List<AdvancedSkillProfile> Profiles { get; set; } = new();
    }
}
