using System.Collections.Immutable;
using System.Runtime.InteropServices;
namespace Smart.Adapters.Dma;

// Direct documented MemProcFS C ABI; native files are supplied by the hardware deployment.
public sealed partial class VmmTransport : IProcessMemoryTransport
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr InitializeDelegate(uint argc, IntPtr argv);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void CloseDelegate(IntPtr handle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private delegate bool ReadDelegate(IntPtr handle, uint pid, ulong address,
        [Out] byte[] bytes, uint count, out uint read, ulong flags);

    private readonly object _sync = new();
    private readonly IntPtr _library;
    private readonly CloseDelegate _close;
    private readonly ReadDelegate _read;
    private IntPtr _handle;
    private bool _disposed;
    private bool _closeComplete;
    public string DeviceId { get; }
    public string ConnectionId { get; } = Guid.NewGuid().ToString("N");

    /// <summary>Builds native VMM options with a blank first argument, never an executable name.</summary>
    public static string[] CreateArguments(string deviceUri, IReadOnlyList<string>? extraArguments = null)
    {
        ValidateDeviceUri(deviceUri, nameof(deviceUri));
        if (extraArguments is { Count: > 32 })
            throw new ArgumentException("Provide at most 32 additional VMM arguments.", nameof(extraArguments));
        var arguments = new string[3 + (extraArguments?.Count ?? 0)];
        arguments[0] = ""; arguments[1] = "-device"; arguments[2] = deviceUri;
        for (var i = 3; i < arguments.Length; i++) arguments[i] = extraArguments![i - 3];
        ValidateArguments(arguments);
        return arguments;
    }

    private static void ValidateDeviceUri(string deviceUri, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceUri, parameterName);
        // The native device configuration has a 260-byte, NUL-terminated UTF-8 field.
        if (deviceUri.StartsWith('-') || deviceUri.Contains('\0') || System.Text.Encoding.UTF8.GetByteCount(deviceUri) >= 260)
            throw new ArgumentException("VMM device URI must fit its 259-byte native field and contain no NUL.", parameterName);
    }

    private static void ValidateArguments(IReadOnlyList<string> arguments)
    {
        if (arguments.Count is < 1 or > 35)
            throw new ArgumentException("Provide 1..35 VMM arguments.", nameof(arguments));
        for (var i = 0; i < arguments.Count; i++)
            if (arguments[i] is null || arguments[i].Length > 4096 || arguments[i].Contains('\0') ||
                (string.IsNullOrWhiteSpace(arguments[i]) && !(i == 0 && arguments[i] == "")))
                throw new ArgumentException("VMM arguments must be bounded, nonempty strings without NUL; only the first argument may be blank.", nameof(arguments));
        // VMMDLL parses index zero as an option; unlike C main(), it does not skip a program name.
        if (arguments[0].Length != 0 && !arguments[0].StartsWith('-'))
            throw new ArgumentException("The first VMM argument must be blank or an option, never a program name.", nameof(arguments));
        var devices = 0;
        for (var i = 0; i < arguments.Count; i++)
        {
            var argument = arguments[i];
            if (argument.Equals("-norefresh", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Live process lifecycle tracking requires VMM refresh; -norefresh is unsupported.", nameof(arguments));
            if (argument.Equals("-device", StringComparison.OrdinalIgnoreCase) ||
                argument.Equals("-f", StringComparison.OrdinalIgnoreCase) || argument.Equals("-z", StringComparison.OrdinalIgnoreCase))
            {
                if (++devices > 1 || i + 1 == arguments.Count)
                    throw new ArgumentException("Provide exactly one value for the VMM device; duplicate -device/-f/-z options are unsupported.", nameof(arguments));
                ValidateDeviceUri(arguments[i + 1], nameof(arguments));
            }
        }
    }

    internal static void ValidateWorkerArguments(IReadOnlyList<string> arguments, string deviceId)
    {
        ValidateArguments(arguments);
        for (var i = 0; i < arguments.Count - 1; i++)
            if (arguments[i].Equals("-device", StringComparison.OrdinalIgnoreCase) || arguments[i].Equals("-f", StringComparison.OrdinalIgnoreCase) || arguments[i].Equals("-z", StringComparison.OrdinalIgnoreCase))
            {
                if (!StringComparer.OrdinalIgnoreCase.Equals(arguments[i + 1], deviceId))
                    throw new ArgumentException("Worker device identity must match its native device argument.");
                return;
            }
        throw new ArgumentException("Worker initialization requires an explicit native device argument.");
    }

    public VmmTransport(string libraryPath, string deviceId, IReadOnlyList<string> arguments)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        if (IntPtr.Size != 8) throw new PlatformNotSupportedException("VMM requires an x64 process.");
        if (!Path.IsPathFullyQualified(libraryPath)) throw new ArgumentException("Use an absolute native library path.");
        ArgumentNullException.ThrowIfNull(arguments);
        if (arguments.Count is < 1 or > 35) throw new ArgumentException("Provide 1..35 VMM arguments.", nameof(arguments));
        var nativeArguments = arguments.ToArray();
        ValidateArguments(nativeArguments);
        DeviceId = deviceId;
        _library = NativeLibrary.Load(libraryPath);
        var strings = Array.Empty<IntPtr>();
        var argv = IntPtr.Zero;
        try
        {
            var init = Marshal.GetDelegateForFunctionPointer<InitializeDelegate>(NativeLibrary.GetExport(_library, "VMMDLL_Initialize"));
            _close = Marshal.GetDelegateForFunctionPointer<CloseDelegate>(NativeLibrary.GetExport(_library, "VMMDLL_Close"));
            _read = Marshal.GetDelegateForFunctionPointer<ReadDelegate>(NativeLibrary.GetExport(_library, "VMMDLL_MemReadEx"));
            strings = nativeArguments.Select(Marshal.StringToCoTaskMemUTF8).ToArray();
            argv = Marshal.AllocHGlobal(IntPtr.Size * strings.Length);
            Marshal.Copy(strings, 0, argv, strings.Length);
            _handle = init((uint)strings.Length, argv);
            if (_handle == IntPtr.Zero) throw new IOException("VMM initialization failed.");
            InitializeInspection();
        }
        catch { if (_handle != IntPtr.Zero) _close?.Invoke(_handle); NativeLibrary.Free(_library); throw; }
        finally
        {
            foreach (var str in strings) Marshal.FreeCoTaskMem(str);
            if (argv != IntPtr.Zero) Marshal.FreeHGlobal(argv);
        }
    }

    public ImmutableArray<MemoryBlock> ReadBatch(int processId, IReadOnlyList<MemoryReadRequest> requests)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(processId);
        if (requests.Count is < 1 or > 256 || requests.Sum(x => (long)x.Length) > 1_048_576 ||
            requests.Any(x => x.Length is <= 0 or > 1_048_576 || x.Address > ulong.MaxValue - (ulong)x.Length))
            throw new ArgumentException("Read batch exceeds count/address/byte budget.");
        lock (_sync)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_scatterInitialize is not null) return ReadScatter(processId, requests);
            var blocks = ImmutableArray.CreateBuilder<MemoryBlock>(requests.Count);
            foreach (var request in requests)
            {
                var buffer = new byte[request.Length];
                var success = _read(_handle, (uint)processId, request.Address, buffer, (uint)buffer.Length, out var count, 1);
                blocks.Add(MemoryBlock.FromNativeOwnedBuffer(request.Address, buffer, success, count));
            }
            return blocks.MoveToImmutable();
        }
    }
    public void Dispose()
    {
        lock (_sync)
        {
            if (_closeComplete) return;
            _disposed = true;
            if (_handle != IntPtr.Zero) { _close(_handle); _handle = IntPtr.Zero; }
            NativeLibrary.Free(_library);
            _closeComplete = true;
        }
    }
}
