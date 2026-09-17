using System.Globalization;
using System.Text.RegularExpressions;
using Smart.Adapters.Dma;
namespace Smart.NativeSmoke;

public sealed record NativeSmokeCommand(string Library, string Device, int ProcessId, string Module,
    int DurationMs, bool Diagnostic, bool ZeroControl)
{
    public bool Isolated { get; init; }
    public const string Usage = "Read-only native PE-header smoke. No arguments/--help opens no device.\n" +
        "Required: --library <absolute vmm.dll> --device <explicit URI> --pid <PID> --module <module name> --duration-ms <100..60000>\n" +
        "Optional: --isolated (run native access in the DMA worker), --diagnostic (VMM -printf -v), --zero-control (read virtual address zero as an invalid-read control).";
    public static NativeSmokeCommand Parse(IReadOnlyList<string> arguments)
    {
        string? library = null, device = null, module = null; int? pid = null, duration = null;
        var diagnostic = false; var zero = false; var isolated = false; var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < arguments.Count; index++)
        {
            var option = arguments[index];
            if (!seen.Add(option)) throw new ArgumentException("Duplicate option: " + option);
            string Value()
            {
                if (++index >= arguments.Count || string.IsNullOrWhiteSpace(arguments[index]) || arguments[index].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException("Missing value for " + option);
                return arguments[index];
            }
            int Number(int minimum, int maximum)
            {
                if (!int.TryParse(Value(), NumberStyles.None, CultureInfo.InvariantCulture, out var value) || value < minimum || value > maximum)
                    throw new ArgumentException($"{option} must be between {minimum} and {maximum}.");
                return value;
            }
            switch (option)
            {
                case "--library": library = Value(); break;
                case "--device": device = Value().Trim(); break;
                case "--pid": pid = Number(1, int.MaxValue); break;
                case "--module": module = Value(); break;
                case "--duration-ms": duration = Number(100, 60000); break;
                case "--diagnostic": diagnostic = true; break;
                case "--zero-control": zero = true; break;
                case "--isolated": isolated = true; break;
                default: throw new ArgumentException("Unknown option: " + option);
            }
        }
        if (library is null || device is null || module is null || pid is null || duration is null)
            throw new ArgumentException("Explicit library, device, PID, module and duration are all required.");
        if (!Path.IsPathFullyQualified(library)) throw new ArgumentException("The native library path must be absolute.");
        if (!Regex.IsMatch(device, "^[A-Za-z][A-Za-z0-9+.-]*://[^\\s]+$", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
            throw new ArgumentException("Use an explicit native device URI, such as fpga://devindex=0.");
        if (module.Length > 260 || module.Contains('\0')) throw new ArgumentException("Invalid module name.");
        return new(library, device, pid.Value, module, duration.Value, diagnostic, zero) { Isolated = isolated };
    }
    public string[] VmmArguments() => VmmTransport.CreateArguments(Device, Diagnostic ? ["-printf", "-v"] : null);
}
