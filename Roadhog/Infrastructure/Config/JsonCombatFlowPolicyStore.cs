using System.Text.Json;
using Roadhog.Core.Accounts;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;

namespace Roadhog.Infrastructure.Config;

/// <summary>A client-wide rollback switch, independent of shared profiles and account edits.</summary>
public sealed class JsonCombatFlowPolicyStore(string path)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public string FilePath { get; } = Path.GetFullPath(path);

    public static string PathFor(string accountConfigPath) =>
        Path.Combine(Path.GetDirectoryName(Path.GetFullPath(accountConfigPath))!, "combat-flow-policy.json");

    public async Task<OperationResult<CombatFlowPolicy>> LoadAsync(CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            if (!AtomicJsonFile.Exists(FilePath)) return OperationResult<CombatFlowPolicy>.Ok(new());
            await using var stream = AtomicJsonFile.OpenRead(FilePath);
            var policy = await JsonSerializer.DeserializeAsync<CombatFlowPolicy>(stream, JsonOptions, token).ConfigureAwait(false);
            return policy is null
                ? OperationResult<CombatFlowPolicy>.Fail("战斗版本回退配置为空。")
                : OperationResult<CombatFlowPolicy>.Ok(policy);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return OperationResult<CombatFlowPolicy>.Fail(error.Message);
        }
    }

    public async Task<OperationResult> SaveAsync(bool forceLegacy, CancellationToken token = default)
    {
        try
        {
            await AtomicJsonFile.WriteAsync(FilePath, new CombatFlowPolicy { ForceLegacy = forceLegacy }, JsonOptions, token)
                .ConfigureAwait(false);
            return OperationResult.Ok();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException)
        {
            return OperationResult.Fail(error.Message);
        }
    }

    public async Task<CombatFlowVersion> ResolveAsync(CombatFlowVersion requested, IRoadhogLogger logger,
        CancellationToken token = default)
    {
        var loaded = await LoadAsync(token).ConfigureAwait(false);
        if (loaded.Success && loaded.Value is { } policy) return policy.Resolve(requested);
        logger.Warn("combat_flow.policy.invalid_using_legacy", new Dictionary<string, object?>
        {
            ["path"] = FilePath, ["error"] = loaded.Error, ["requested"] = requested.ToString()
        });
        return CombatFlowVersion.Legacy;
    }
}
