namespace Roadhog.Infrastructure.Diagnostics;

internal sealed record FileLogEntry(DateTimeOffset Timestamp, string Level, string EventName,
    IReadOnlyDictionary<string, object?> Fields, string? Error);

/// <summary>File-output policy only; runtime and in-memory diagnostics retain their original events.</summary>
internal sealed class FileLogCompactor
{
    private sealed record Rule(TimeSpan Interval, string Key, string Signature);
    private sealed class Sample(FileLogEntry entry, Rule rule)
    {
        public FileLogEntry Last { get; set; } = entry;
        public Rule Rule { get; set; } = rule;
        public DateTimeOffset WrittenAt { get; set; } = entry.Timestamp;
        public DateTimeOffset FirstSuppressedAt { get; set; }
        public int Suppressed { get; set; }
    }

    private readonly Dictionary<string, Sample> samples = new(StringComparer.OrdinalIgnoreCase);
    public void Clear() => samples.Clear();

    public IReadOnlyList<FileLogEntry> Accept(FileLogEntry entry)
    {
        var output = new List<FileLogEntry>();
        var flush = entry.Level == "error" || entry.EventName is "account.stop_requested" or "account.stopped"
            or "account.failed" or "worker.loop.exit";
        foreach (var sample in samples.Values)
            if (sample.Suppressed > 0 && (flush || entry.Timestamp - sample.WrittenAt >= sample.Rule.Interval))
                Flush(sample, entry.Timestamp, output);

        // entry_finished already records the final pressCount and outcome. Keep the
        // first send for correlation instead of duplicating every opening retry.
        if (entry.Level == "info" && entry.EventName == "semi_auto.opening_skill.input_sent" &&
            int.TryParse(Text(entry.Fields, "pressCount"), out var pressCount) && pressCount > 1) return output;

        entry = Compact(entry);
        var rule = SamplingRule(entry);
        if (rule is null) { output.Add(entry); return output; }
        if (samples.TryGetValue(rule.Key, out var current))
        {
            if (current.Rule.Signature == rule.Signature && entry.Timestamp - current.WrittenAt < rule.Interval)
            {
                if (current.Suppressed == 0) current.FirstSuppressedAt = entry.Timestamp;
                current.Suppressed++;
                current.Last = entry;
                return output;
            }
            Flush(current, entry.Timestamp, output);
        }
        else if (samples.Count >= 512)
        {
            var oldest = samples.MinBy(pair => pair.Value.WrittenAt);
            Flush(oldest.Value, entry.Timestamp, output);
            samples.Remove(oldest.Key);
        }
        samples[rule.Key] = new(entry, rule);
        output.Add(entry);
        return output;
    }

    private static void Flush(Sample sample, DateTimeOffset now, List<FileLogEntry> output)
    {
        if (sample.Suppressed == 0) return;
        var fields = new Dictionary<string, object?>(sample.Last.Fields)
        {
            ["suppressedCount"] = sample.Suppressed,
            ["firstSuppressedAt"] = sample.FirstSuppressedAt,
            ["lastSuppressedAt"] = sample.Last.Timestamp
        };
        output.Add(sample.Last with { Timestamp = now, Fields = fields });
        sample.Suppressed = 0;
        sample.WrittenAt = now;
    }

    private static FileLogEntry Compact(FileLogEntry entry)
    {
        if (entry.Level != "info" || entry.EventName != "stationary_combat.smart_preaim.candidate_diagnostics") return entry;
        var fields = new Dictionary<string, object?>(entry.Fields);
        foreach (var name in new[] { "eligibleRanked", "nearestToPlayer", "nearestToOrigin", "activeMonsterFilters" }) fields.Remove(name);
        fields["detail"] = "selection_summary";
        return entry with { Fields = fields };
    }

    private static Rule? SamplingRule(FileLogEntry entry)
    {
        var name = entry.EventName;
        var fields = entry.Fields;
        var key = name + "|" + Text(fields, "account");
        string Signature(params string[] names) => string.Join("\u001f", names.Select(field => Text(fields, field)));
        Rule Every(int seconds, string signature = "", string suffix = "") => new(TimeSpan.FromSeconds(seconds), key + suffix, signature);
        if (entry.Level == "warn")
        {
            // Preserve the first warning and any changed cause. Only repeated,
            // known read-quality notices are aggregated; other warnings/errors pass.
            if (name is "vmm.world_objects.read_quality" or "vmm.summoned_pet_roster.read_quality" or
                "vmm.snapshot.fallback" or "snapshot.read.retry")
                return Every(30, Signature("firstIssue", "error", "observedError", "completeness", "presence", "mode"),
                    "|" + Signature("data", "channel"));
            return null;
        }
        if (entry.Level != "info") return null;
        return name switch
        {
            "stationary_combat.smart_preaim.candidate_diagnostics" => Every(30,
                Signature("fightTargetServerObjectId", "selectedTargetServerObjectId", "selectedDecisionReason")),
            "stationary_combat.tab.verify" => Every(5,
                Signature("candidateServerObjectId", "lockedServerObjectId", "matched", "error")),
            "stationary_combat.path_follow" or "stationary_combat.startup_recovery" => Every(5,
                Signature("action", "phase", "reason", "pathName", "targetServerObjectId")),
            "stationary_combat.target.scan_none" => Every(15, Signature("reason")),
            "semi_auto.spiritmaster.pet_buff_dp_skip" => Every(30,
                Signature("requiredDp"), "|" + Signature("skillId", "key")),
            "quickbar_skill.key.pressed" => Every(5, suffix: "|" + Signature("skillId", "key", "node")),
            "semi_auto.key.pressed" => Every(5, suffix: "|" + Signature("phase", "skill", "key")),
            "vmm.world_objects.publication" => Every(5, Signature("mode", "coverageRejected", "issue")),
            "vmm.locked_target.read" or "vmm.locked_target_abnormal.read" or "vmm.loot_corpses.read" or
                "vmm.player.read" or "vmm.player_abnormal.read" or "vmm.skills.read" or "vmm.summoned_pet.read" or
                "vmm.summoned_pet_roster.read" or "vmm.inventory.read" or "vmm.inventory.capacity.read" => Every(5),
            _ => null
        };
    }

    private static string Text(IReadOnlyDictionary<string, object?> fields, string key) =>
        fields.TryGetValue(key, out var value) ? Convert.ToString(value) ?? string.Empty : string.Empty;
}
