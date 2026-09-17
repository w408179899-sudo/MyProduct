using System.Globalization;

namespace Smart.HardwareProbe;

public sealed record ProbeRunOptions(TimeSpan Duration, TimeSpan PollInterval, TimeSpan? MaximumProgressGap = null)
{
    public void Validate()
    {
        if (Duration < TimeSpan.FromMilliseconds(100) || Duration > TimeSpan.FromHours(1))
            throw new ArgumentOutOfRangeException(nameof(Duration), "Duration must be 100..3600000 milliseconds.");
        if (PollInterval < TimeSpan.FromMilliseconds(1) || PollInterval > TimeSpan.FromSeconds(1) || PollInterval > Duration)
            throw new ArgumentOutOfRangeException(nameof(PollInterval), "Poll interval must be 1..1000 milliseconds and no longer than duration.");
        if (MaximumProgressGap is { } gap && (gap < TimeSpan.FromMilliseconds(1) || gap > TimeSpan.FromHours(1)))
            throw new ArgumentOutOfRangeException(nameof(MaximumProgressGap), "Maximum progress gap must be 1..3600000 milliseconds.");
    }
    public TimeSpan ResolveProgressGap(Smart.ProbeProtocol.ProbeManifest manifest) => MaximumProgressGap ??
        TimeSpan.FromMilliseconds(manifest.IntervalMs + (manifest.Mode == "stable" ? 0 : manifest.TornWindowMs) +
            Math.Max(250, PollInterval.TotalMilliseconds * 4));
}

public sealed record ProbeCommand(string LibraryPath, string DeviceUri, string ManifestPath, ProbeRunOptions Run)
{
    public const string Usage = "Smart.HardwareProbe --library C:\\absolute\\vmm.dll --device fpga://EXPLICIT_DEVICE --manifest C:\\absolute\\fixture.json --duration-ms 10000 [--poll-ms 5] [--max-progress-gap-ms 2000]";
    public static ProbeCommand Parse(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || args[i] is not ("--library" or "--device" or "--manifest" or "--duration-ms" or "--poll-ms" or "--max-progress-gap-ms") ||
                !values.TryAdd(args[i], args[i + 1])) throw new ArgumentException("Unknown, repeated, or incomplete argument. " + Usage);
        }
        string Required(string name) => values.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value : throw new ArgumentException("Missing " + name + ". " + Usage);
        var library = Required("--library"); var manifest = Required("--manifest"); var device = Required("--device").Trim();
        if (!Path.IsPathFullyQualified(library) || !Path.IsPathFullyQualified(manifest))
            throw new ArgumentException("Library and manifest paths must be absolute.");
        // MemProcFS endpoints such as fpga://devindex=5 are native URIs, not .NET host-name URIs.
        var separator = device.IndexOf("://", StringComparison.Ordinal);
        if (separator <= 0 || separator + 3 >= device.Length || device.Any(char.IsWhiteSpace) || device.Any(char.IsControl) ||
            device.Length > 2048 || !Uri.CheckSchemeName(device[..separator]))
            throw new ArgumentException("An explicit absolute device URI is required.");
        static int Milliseconds(string value) => int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : throw new ArgumentException("Durations must be unsigned integer milliseconds.");
        var options = new ProbeRunOptions(TimeSpan.FromMilliseconds(Milliseconds(Required("--duration-ms"))),
            TimeSpan.FromMilliseconds(values.TryGetValue("--poll-ms", out var poll) ? Milliseconds(poll) : 5),
            values.TryGetValue("--max-progress-gap-ms", out var gap) ? TimeSpan.FromMilliseconds(Milliseconds(gap)) : null);
        options.Validate();
        return new(library, device, manifest, options);
    }
}
