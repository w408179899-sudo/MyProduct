using System.Text.Json;
using Roadhog.Infrastructure.Diagnostics;

internal static class FileLogCompactionTests
{
    public static async Task ConcurrentRotationAsync()
    {
        var root = NewRoot();
        try
        {
            var first = new FileRoadhogLogger(root, maxLogFileBytes: 512);
            var second = new FileRoadhogLogger(Path.Combine(root, "."), maxLogFileBytes: 512);
            await Task.WhenAll(Enumerable.Range(0, 80).Select(index => Task.Run(() =>
                (index % 2 == 0 ? first : second).Info("test.rotation", new Dictionary<string, object?>
                    { ["index"] = index, ["payload"] = new string('x', 130) }))));
            var records = Read(root);
            Require(records.Length == 80, "single active file plus archives must contain each record exactly once");
            Require(records.Select(e => e.GetProperty("Fields").GetProperty("index").GetInt32()).Distinct().Count() == 80,
                "two loggers sharing a canonical directory must not lose or duplicate records during rotation");
            Require(Directory.GetFiles(root, "roadhog-*.log").Length > 0 && File.Exists(Path.Combine(root, "latest.log")),
                "rotation retains the active latest path and timestamped archives");
            Require(Directory.GetFiles(root, "*.log").All(path => new FileInfo(path).Length <= 512), "ordinary segments stay within cap");
        }
        finally { Directory.Delete(root, true); }
    }

    public static Task RetentionAndCapacityAsync()
    {
        var root = NewRoot();
        try
        {
            var clock = new Clock();
            var logger = new FileRoadhogLogger(root, maxLogFileBytes: 512, maxDirectoryBytes: 2048, timeProvider: clock);
            logger.Info("test.first");
            var expired = Path.Combine(root, "roadhog-20200101.log");
            var unrelated = Path.Combine(root, "customer.log");
            File.WriteAllText(expired, "expired");
            File.WriteAllText(unrelated, "preserve");
            File.SetLastWriteTimeUtc(expired, clock.GetUtcNow().UtcDateTime.AddDays(-2));
            File.SetLastWriteTimeUtc(unrelated, clock.GetUtcNow().UtcDateTime.AddDays(-2));
            clock.Advance(TimeSpan.FromMinutes(6));
            logger.Info("test.periodic");
            Require(!File.Exists(expired), "the same logger must clean expired archives again after its first write");
            Require(File.ReadAllText(unrelated) == "preserve", "retention must never delete unrelated log files");
            for (var i = 0; i < 40; i++) logger.Info("test.cap", new Dictionary<string, object?>
                { ["index"] = i, ["payload"] = new string('x', 140) });
            var owned = Directory.GetFiles(root, "roadhog-*.log").Append(Path.Combine(root, "latest.log"));
            Require(owned.Sum(path => new FileInfo(path).Length) <= 2048, "capacity includes active and archived segments");
            Require(File.ReadAllText(Path.Combine(root, "latest.log")).Contains("\"index\":39"), "newest record survives pruning");
        }
        finally { Directory.Delete(root, true); }
        return Task.CompletedTask;
    }

    public static Task EvidenceAndSummariesAsync()
    {
        var root = NewRoot();
        try
        {
            var clock = new Clock();
            var logger = new FileRoadhogLogger(root, timeProvider: clock);
            logger.Info("stationary_combat.smart_preaim.candidate_diagnostics", new Dictionary<string, object?>
            {
                ["selectedTargetServerObjectId"] = 12, ["selectedDecisionReason"] = "selected", ["eligibleCandidateCount"] = 8,
                ["eligibleRanked"] = new string('x', 6000), ["nearestToPlayer"] = "long list",
                ["nearestToOrigin"] = "long list", ["activeMonsterFilters"] = "long list"
            });
            for (var i = 0; i < 10; i++) logger.Warn("vmm.world_objects.read_quality",
                new Dictionary<string, object?> { ["account"] = "脚本6", ["firstIssue"] = "partial", ["captureSequence"] = i });
            logger.Warn("vmm.world_objects.read_quality", new Dictionary<string, object?>
                { ["account"] = "脚本6", ["firstIssue"] = "changed" });
            try { throw new InvalidOperationException("完整异常"); }
            catch (Exception ex) { logger.Error("worker.loop.exception", ex); }
            var rows = Read(root);
            var preaim = rows.Single(e => e.GetProperty("EventName").GetString() == "stationary_combat.smart_preaim.candidate_diagnostics").GetProperty("Fields");
            Require(!preaim.TryGetProperty("eligibleRanked", out _) && preaim.GetProperty("eligibleCandidateCount").GetInt32() == 8,
                "candidate summary retains the decision and counts without repeated full lists");
            var warnings = rows.Where(e => e.GetProperty("EventName").GetString() == "vmm.world_objects.read_quality")
                .Select(e => e.GetProperty("Fields")).ToArray();
            Require(warnings.Length == 3 && warnings.Where(e => e.GetProperty("firstIssue").GetString() == "partial")
                .Sum(e => e.TryGetProperty("suppressedCount", out var count) ? count.GetInt32() : 1) == 10,
                "first warning plus suppressed count preserve all occurrences; changed cause is immediate");
            var error = rows.Single(e => e.GetProperty("Level").GetString() == "error").GetProperty("Fields");
            Require(error.GetProperty("exceptionMessage").GetString() == "完整异常" &&
                error.GetProperty("stackTrace").GetString()!.Contains(nameof(EvidenceAndSummariesAsync)), "error text and stack remain intact");
            Require(File.ReadAllText(Path.Combine(root, "latest.log")).Contains("脚本6"), "Chinese remains readable UTF-8 instead of ASCII escapes");

            logger.Info("semi_auto.spiritmaster.pet_buff_dp_skip", new Dictionary<string, object?> { ["skillId"] = 20 });
            logger.Info("semi_auto.spiritmaster.pet_buff_dp_skip", new Dictionary<string, object?> { ["skillId"] = 20 });
            clock.Advance(TimeSpan.FromSeconds(31));
            logger.Info("test.heartbeat");
            Require(Read(root).Any(e => e.GetProperty("EventName").GetString() == "semi_auto.spiritmaster.pet_buff_dp_skip" &&
                e.GetProperty("Fields").TryGetProperty("suppressedCount", out _)), "due summaries flush even when the repeated event stops");
        }
        finally { Directory.Delete(root, true); }
        return Task.CompletedTask;
    }

    public static Task OpeningAndOversizedErrorAsync()
    {
        var root = NewRoot();
        try
        {
            var logger = new FileRoadhogLogger(root, maxLogFileBytes: 1024, maxDirectoryBytes: 8192);
            for (var i = 1; i <= 50; i++) logger.Info("semi_auto.opening_skill.input_sent",
                new Dictionary<string, object?> { ["skillId"] = 1671, ["pressCount"] = i });
            logger.Info("semi_auto.opening_skill.entry_finished", new Dictionary<string, object?>
                { ["skillId"] = 1671, ["pressCount"] = 50, ["reason"] = "confirmation_timeout" });
            var rows = Read(root);
            Require(rows.Length == 2 && rows[1].GetProperty("Fields").GetProperty("pressCount").GetInt32() == 50,
                "opening retains first attempt and exact final count without every retry");
            logger.Error("test.large_error", new Exception(new string('x', 5000)));
            Require(Read(root).Single(e => e.GetProperty("EventName").GetString() == "test.large_error")
                .GetProperty("Error").GetString()!.Length == 5000, "oversized exceptions remain whole rather than truncated or dropped");
        }
        finally { Directory.Delete(root, true); }
        return Task.CompletedTask;
    }

    private sealed class Clock : TimeProvider
    {
        private DateTimeOffset now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => now;
        public void Advance(TimeSpan amount) => now += amount;
    }
    private static string NewRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "RoadhogCompactLog-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
    private static JsonElement[] Read(string root) => Directory.GetFiles(root, "roadhog-*.log")
        .Append(Path.Combine(root, "latest.log")).Where(File.Exists).SelectMany(File.ReadLines)
        .Select(line => { using var json = JsonDocument.Parse(line); return json.RootElement.Clone(); }).ToArray();
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
