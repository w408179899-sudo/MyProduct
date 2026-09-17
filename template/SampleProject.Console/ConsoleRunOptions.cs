using System.Globalization;
using Smart.Hosting;
namespace SampleProject.ConsoleHost;

public sealed record ConsoleRunOptions(string? ConfigPath, string? AccountId, string? ProbeId,
    bool ListDevices, bool AllowHardware, TimeSpan? Duration)
{
    public static ConsoleRunOptions Parse(IReadOnlyList<string> arguments)
    {
        string? config = null, account = null, probe = null;
        var list = false; var hardware = false; var continuous = false; int? milliseconds = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
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
            switch (option)
            {
                case "--config": config = Value(); break;
                case "--account": account = Value(); break;
                case "--probe": probe = Value(); break;
                case "--list-devices": list = true; break;
                case "--run-hardware": hardware = true; break;
                case "--run": continuous = true; break;
                case "--duration-ms":
                    if (!int.TryParse(Value(), NumberStyles.None, CultureInfo.InvariantCulture, out var duration) || duration < 100)
                        throw new ArgumentException("--duration-ms must be between 100 and 2147483647.");
                    milliseconds = duration; break;
                default: throw new ArgumentException("Unknown option: " + option);
            }
        }
        if (continuous && milliseconds is not null) throw new ArgumentException("Use --run or --duration-ms, not both.");
        if ((list || probe is not null) && (continuous || milliseconds is not null || account is not null || hardware) || list && probe is not null)
            throw new ArgumentException("Device listing and read-only probes cannot be combined with account execution options.");
        return new(config, account, probe, list, hardware, continuous ? null : TimeSpan.FromMilliseconds(milliseconds ?? 1000));
    }

    public ManagedAccount[] SelectAccounts(IReadOnlyList<ManagedAccount> accounts)
    {
        var selected = accounts.Where(x => AccountId is null || x.Profile.Id == AccountId).ToArray();
        if (selected.Length == 0) throw new ArgumentException(AccountId is null ? "No accounts are configured." : "Account was not found: " + AccountId);
        if (!AllowHardware && selected.Any(x => x.Profile.Mode == RuntimeMode.Hardware))
            throw new ArgumentException("Hardware execution requires --run-hardware. --probe is read-only and does not initialize input.");
        return selected;
    }
}
