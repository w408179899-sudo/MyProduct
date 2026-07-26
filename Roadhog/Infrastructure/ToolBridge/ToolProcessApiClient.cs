using System.Diagnostics;
using System.Text;
using Roadhog.Core.Api;
using Roadhog.Core.Common;
using Roadhog.Core.Diagnostics;
using Roadhog.Core.Model;
using Roadhog.Infrastructure.Gathering;

namespace Roadhog.Infrastructure.ToolBridge;

public sealed class ToolProcessApiClient : IRoadhogGameApi
{
    private readonly ToolBridgeOptions _options;
    private readonly IRoadhogLogger _logger;

    public ToolProcessApiClient(ToolBridgeOptions options, IRoadhogLogger logger)
    {
        _options = options;
        _logger = logger;
    }

    public async Task<OperationResult<PlayerSnapshot>> ReadPlayerAsync(CancellationToken cancellationToken = default)
    {
        var output = await RunToolModeAsync(ToolApiMode.Player, cancellationToken).ConfigureAwait(false);
        if (!output.Success || output.Value is null)
        {
            return OperationResult<PlayerSnapshot>.Fail(output.Error ?? "Tool player mode failed.");
        }

        var snapshot = ToolOutputParsers.ParseLastPlayerSnapshot(output.Value.StandardOutput);
        return snapshot is null
            ? OperationResult<PlayerSnapshot>.Fail("Tool player output did not contain a parseable player snapshot.")
            : OperationResult<PlayerSnapshot>.Ok(snapshot);
    }

    public Task<OperationResult<PlayerAbnormalStatusSnapshot>> ReadPlayerAbnormalStatusesAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<PlayerAbnormalStatusSnapshot>.Fail(
            "Tool bridge player abnormal-status snapshot is not implemented."));
    }

    public Task<OperationResult<SummonedPetSnapshot>> ReadSummonedPetAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<SummonedPetSnapshot>.Fail(
            "Tool bridge summoned-pet snapshot is not implemented."));
    }

    public Task<OperationResult<SummonedPetRosterSnapshot>> ReadSummonedPetRosterAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<SummonedPetRosterSnapshot>.Fail(
            "Tool bridge summoned-pet roster snapshot is not implemented."));
    }

    public Task<OperationResult<LockedTargetSnapshot>> ReadLockedTargetAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<LockedTargetSnapshot>.Fail("Tool bridge locked-target snapshot is not implemented."));
    }

    public Task<OperationResult<LockedTargetAbnormalStatusSnapshot>> ReadLockedTargetAbnormalStatusesAsync(
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OperationResult<LockedTargetAbnormalStatusSnapshot>.Fail(
            "Tool bridge locked-target abnormal-status snapshot is not implemented."));
    }

    public async Task<OperationResult<IReadOnlyList<SkillSnapshot>>> ReadSkillsAsync(CancellationToken cancellationToken = default)
    {
        var output = await RunToolModeAsync(ToolApiMode.Skills, cancellationToken).ConfigureAwait(false);
        if (!output.Success || output.Value is null)
        {
            return OperationResult<IReadOnlyList<SkillSnapshot>>.Fail(output.Error ?? "Tool skills mode failed.");
        }

        return OperationResult<IReadOnlyList<SkillSnapshot>>.Ok(ToolOutputParsers.ParseSkills(output.Value.StandardOutput));
    }

    public async Task<OperationResult<IReadOnlyList<InventoryItemSnapshot>>> ReadInventoryAsync(CancellationToken cancellationToken = default)
    {
        var output = await RunToolModeAsync(ToolApiMode.Inventory, cancellationToken).ConfigureAwait(false);
        return output.Success && output.Value is not null
            ? OperationResult<IReadOnlyList<InventoryItemSnapshot>>.Ok(ToolOutputParsers.ParseInventory(output.Value.StandardOutput))
            : OperationResult<IReadOnlyList<InventoryItemSnapshot>>.Fail(output.Error ?? "Tool inventory mode failed.");
    }

    public async Task<OperationResult<IReadOnlyList<WorldObjectSnapshot>>> ReadWorldObjectsAsync(CancellationToken cancellationToken = default)
    {
        var output = await RunToolModeAsync(ToolApiMode.Monsters, cancellationToken).ConfigureAwait(false);
        return output.Success && output.Value is not null
            ? OperationResult<IReadOnlyList<WorldObjectSnapshot>>.Ok(ToolOutputParsers.ParseWorldObjects(output.Value.StandardOutput))
            : OperationResult<IReadOnlyList<WorldObjectSnapshot>>.Fail(output.Error ?? "Tool world-object mode failed.");
    }

    public async Task<OperationResult<GatherSnapshot>> ReadGatherSnapshotAsync(CancellationToken cancellationToken = default)
    {
        var output = await RunToolModeAsync(ToolApiMode.Gather, cancellationToken).ConfigureAwait(false);
        if (!output.Success || output.Value is null)
        {
            return OperationResult<GatherSnapshot>.Fail(output.Error ?? "Tool gather mode failed.");
        }

        var catalog = GatherSourceCatalog.Default;
        var capturedAt = DateTimeOffset.Now;
        var objects = ToolOutputParsers.ParseGatherObjects(output.Value.StandardOutput)
            .Select(item =>
                catalog.TryGet(item.GatherSourceId, out var source)
                    ? item with { Source = source }
                    : item)
            .ToArray();
        return OperationResult<GatherSnapshot>.Ok(
            new GatherSnapshot(
                0,
                0,
                null,
                objects,
                Array.Empty<GatherCompetitionPlayerSnapshot>(),
                false,
                capturedAt));
    }

    public async Task<OperationResult<IReadOnlyList<LootCorpseSnapshot>>> ReadLootCorpsesAsync(CancellationToken cancellationToken = default)
    {
        var output = await RunToolModeAsync(ToolApiMode.Loot, cancellationToken).ConfigureAwait(false);
        return output.Success && output.Value is not null
            ? OperationResult<IReadOnlyList<LootCorpseSnapshot>>.Ok(ToolOutputParsers.ParseLootCorpses(output.Value.StandardOutput))
            : OperationResult<IReadOnlyList<LootCorpseSnapshot>>.Fail(output.Error ?? "Tool loot corpse mode failed.");
    }

    public async Task<OperationResult<ToolCommandOutput>> RunToolModeAsync(ToolApiMode mode, CancellationToken cancellationToken = default)
    {
        var executable = ResolveToolExecutablePath();
        if (!File.Exists(executable))
        {
            return OperationResult<ToolCommandOutput>.Fail("Tool executable was not found: " + executable);
        }

        var startedAt = Stopwatch.StartNew();
        using var process = new Process
        {
            StartInfo = BuildStartInfo(executable, mode),
            EnableRaisingEvents = true
        };

        try
        {
            if (!process.Start())
            {
                return OperationResult<ToolCommandOutput>.Fail("Tool process failed to start.");
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            var waitTask = process.WaitForExitAsync(cancellationToken);
            var timeoutTask = Task.Delay(_options.Timeout, cancellationToken);
            var completed = await Task.WhenAny(waitTask, timeoutTask).ConfigureAwait(false);

            var timedOut = completed == timeoutTask;
            if (timedOut)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (Exception ex)
                {
                    _logger.Warn("tool.kill_failed", new Dictionary<string, object?> { ["error"] = ex.Message });
                }
            }
            else
            {
                await waitTask.ConfigureAwait(false);
            }

            var stdout = await CompleteReadAsync(stdoutTask).ConfigureAwait(false);
            var stderr = await CompleteReadAsync(stderrTask).ConfigureAwait(false);
            startedAt.Stop();

            var output = new ToolCommandOutput(
                mode,
                process.HasExited ? process.ExitCode : null,
                timedOut,
                startedAt.Elapsed,
                SplitLines(stdout),
                SplitLines(stderr));

            _logger.Info("tool.mode.completed", new Dictionary<string, object?>
            {
                ["mode"] = mode.ToString(),
                ["exitCode"] = output.ExitCode,
                ["timedOut"] = output.TimedOut,
                ["durationMs"] = (int)output.Duration.TotalMilliseconds
            });

            return output.Success || (timedOut && output.StandardOutput.Count > 0)
                ? OperationResult<ToolCommandOutput>.Ok(output)
                : OperationResult<ToolCommandOutput>.Fail(BuildToolError(output));
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error("tool.mode.exception", ex, new Dictionary<string, object?> { ["mode"] = mode.ToString() });
            return OperationResult<ToolCommandOutput>.Fail(ex.Message);
        }
    }

    private ProcessStartInfo BuildStartInfo(string executable, ToolApiMode mode)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executable,
            WorkingDirectory = Path.GetDirectoryName(executable) ?? AppContext.BaseDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        startInfo.Environment["AION_TEST_MODE"] = ToToolModeText(mode);
        startInfo.Environment["VMM_PROCESS"] = _options.ProcessName;
        startInfo.Environment["VMM_MODULE"] = _options.ModuleName;
        startInfo.Environment["VMM_DEVICE"] = _options.VmmDevice;
        if (mode == ToolApiMode.Monsters)
        {
            startInfo.Environment["AION_MONSTER_LIST_SAMPLES"] = "1";
            startInfo.Environment["AION_MONSTER_LIST_INCLUDE_NPCS"] = "0";
        }
        else if (mode == ToolApiMode.Loot)
        {
            startInfo.Environment["AION_LOOT_LIST_SAMPLES"] = "1";
            startInfo.Environment["AION_LOOT_ONLY_LOOTABLE"] = "0";
        }
        else if (mode == ToolApiMode.Gather)
        {
            startInfo.Environment["AION_GATHER_LIST_RADIUS"] = "120";
            startInfo.Environment["AION_GATHER_LIST_LIMIT"] = "100";
        }

        if (!string.IsNullOrWhiteSpace(_options.MemProcFsHome))
        {
            startInfo.Environment["MEMPROCFS_HOME"] = _options.MemProcFsHome;
        }

        foreach (var pair in _options.EnvironmentVariables)
        {
            startInfo.Environment[pair.Key] = pair.Value;
        }

        return startInfo;
    }

    private string ResolveToolExecutablePath()
    {
        if (!string.IsNullOrWhiteSpace(_options.ToolExecutablePath))
        {
            return Path.GetFullPath(_options.ToolExecutablePath);
        }

        var envPath = Environment.GetEnvironmentVariable("ROADHOG_TOOL_EXE");
        if (!string.IsNullOrWhiteSpace(envPath))
        {
            return Path.GetFullPath(envPath);
        }

        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "Tool.exe"),
            Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Tool", "bin", "Debug", "Tool.exe")),
            Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "Tool", "bin", "Debug", "Tool.exe"))
        };

        return candidates.FirstOrDefault(File.Exists) ?? candidates[^1];
    }

    private static string ToToolModeText(ToolApiMode mode)
    {
        return mode switch
        {
            ToolApiMode.Player => "player",
            ToolApiMode.Skills => "skills",
            ToolApiMode.Inventory => "inventory",
            ToolApiMode.Monsters => "monsters",
            ToolApiMode.Loot => "loot",
            ToolApiMode.Gather => "gather",
            ToolApiMode.Abnormal => "abnormal",
            ToolApiMode.Target => "target",
            _ => "skills"
        };
    }

    private static async Task<string> CompleteReadAsync(Task<string> readTask)
    {
        try
        {
            return await readTask.ConfigureAwait(false);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static IReadOnlyList<string> SplitLines(string text)
    {
        return text
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string BuildToolError(ToolCommandOutput output)
    {
        if (output.TimedOut)
        {
            return "Tool mode timed out: " + output.Mode;
        }

        if (output.StandardError.Count > 0)
        {
            return string.Join(Environment.NewLine, output.StandardError);
        }

        return "Tool mode failed: " + output.Mode + " exit=" + output.ExitCode;
    }
}
