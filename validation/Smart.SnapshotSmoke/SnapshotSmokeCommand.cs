using Smart.NativeSmoke;
namespace Smart.SnapshotSmoke;

public static class SnapshotSmokeCommand
{
    public const string Usage = "Read-only provider contract smoke. No arguments/--help opens no device.\n" +
        "Required: --library <absolute vmm.dll> --device <explicit URI> --pid <PID> --module <module name> --duration-ms <500..60000>\n" +
        "Duration is the total managed scenario deadline, including initialization. Optional: --isolated (DMA worker), --diagnostic.\n" +
        "Reads only the selected PE header and address zero; no memory writes or input.";
    public static NativeSmokeCommand Parse(string[] args)
    {
        if (args.Contains("--zero-control", StringComparer.Ordinal))
            throw new ArgumentException("This contract smoke always includes the invalid-address control; do not pass --zero-control.");
        var result = NativeSmokeCommand.Parse(args);
        if (result.DurationMs < 500) throw new ArgumentException("The scenario deadline must be between 500 and 60000 milliseconds.");
        return result;
    }
}
