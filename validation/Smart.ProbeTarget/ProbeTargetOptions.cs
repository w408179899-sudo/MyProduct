using System.Globalization;
namespace Smart.ProbeTarget;

public sealed record ProbeTargetOptions(string Mode = "mixed", int IntervalMs = 100, int TornWindowMs = 250,
    int? DurationMs = null, string? ManifestPath = null)
{
    public static ProbeTargetOptions Parse(IReadOnlyList<string> arguments)
    {
        var result = new ProbeTargetOptions(); var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < arguments.Count; index++)
        {
            var option = arguments[index];
            if (!seen.Add(option)) throw new ArgumentException("Duplicate option: " + option);
            if (++index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]) || arguments[index].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Missing value for " + option);
            var value = arguments[index];
            int Number(int minimum, int maximum)
            {
                if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number) || number < minimum || number > maximum)
                    throw new ArgumentException($"{option} must be between {minimum} and {maximum}.");
                return number;
            }
            result = option switch
            {
                "--mode" when value is "stable" or "mixed" or "torn" => result with { Mode = value },
                "--interval-ms" => result with { IntervalMs = Number(10, 60000) },
                "--torn-ms" => result with { TornWindowMs = Number(1, 60000) },
                "--duration-ms" => result with { DurationMs = Number(100, int.MaxValue) },
                "--manifest" => result with { ManifestPath = Path.GetFullPath(value) },
                _ => throw new ArgumentException("Unknown option or unsupported value: " + option)
            };
        }
        return result;
    }
    public bool ShouldTear(long counter) => Mode == "torn" || Mode == "mixed" && counter % 8 == 4;
}
