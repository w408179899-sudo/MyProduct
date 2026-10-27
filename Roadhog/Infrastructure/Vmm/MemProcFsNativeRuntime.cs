using System.Reflection;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace Roadhog.Infrastructure.Vmm;

// The bundled symbol handler cannot use a UTF-8 cache path containing Chinese characters.
// Keep native libraries and their default Symbols directory in an ASCII, user-owned location.
internal static class MemProcFsNativeRuntime
{
    private static readonly object Sync = new();
    private static string? _loadedFrom;
    internal static string LoadedDirectory
    {
        get
        {
            if (_loadedFrom is not null) return _loadedFrom;
            // ASCII installations can use VmmSharp's normal loader. Resolve its
            // actual DLL location, including MEMPROCFS_HOME, instead of guessing.
            using var process = Process.GetCurrentProcess();
            var modules = process.Modules.Cast<ProcessModule>()
                .Where(m => m.ModuleName.Equals("vmm.dll", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (modules.Length != 1) throw new InvalidOperationException("MemProcFS native runtime directory is not unique.");
            return Path.GetDirectoryName(modules[0].FileName)!;
        }
    }
    private static readonly string[] Required = ["vmm.dll", "leechcore.dll", "dbghelp.dll", "symsrv.dll"];
    private static readonly string[] Optional = ["FTD3XX.dll", "leechcore_driver.dll", "tinylz4.dll", "vcruntime140.dll", "info.db", "pdbcrust.dll"];

    internal static bool NeedsAsciiPath(string path) => path.Any(c => c > 127);

    internal static string Load(string sourceDirectory)
    {
        lock (Sync)
        {
            var source = Path.GetFullPath(sourceDirectory);
            var destination = NeedsAsciiPath(source)
                ? Stage(source, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Roadhog", "MemProcFS"))
                : source;
            if (_loadedFrom is not null)
            {
                if (!string.Equals(_loadedFrom, destination, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Cannot change the MemProcFS native runtime inside an active worker.");
                return _loadedFrom;
            }

            // LoadNativeLibrary in vmmsharp tries the application's directory before its path argument.
            // Bind both imports explicitly: mixing two LeechCore copies breaks existing:// handles.
            var leech = NativeLibrary.Load(Path.Combine(destination, "leechcore.dll"));
            var vmm = NativeLibrary.Load(Path.Combine(destination, "vmm.dll"));
            IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath) =>
                name.Equals("vmm", StringComparison.OrdinalIgnoreCase) || name.Equals("vmm.dll", StringComparison.OrdinalIgnoreCase) ? vmm :
                name.Equals("leechcore", StringComparison.OrdinalIgnoreCase) || name.Equals("leechcore.dll", StringComparison.OrdinalIgnoreCase) ? leech : IntPtr.Zero;
            NativeLibrary.SetDllImportResolver(typeof(Vmmsharp.Vmm).Assembly, Resolve);
            NativeLibrary.SetDllImportResolver(typeof(SafeMemProcVmm).Assembly, Resolve);
            // Native handles live for the worker lifetime, including SafeMemProcVmm's failed-init finalizer.
            _loadedFrom = destination;
            return destination;
        }
    }

    internal static string Stage(string sourceDirectory, string cacheRoot)
    {
        cacheRoot = Path.GetFullPath(cacheRoot);
        if (NeedsAsciiPath(cacheRoot))
        {
            Directory.CreateDirectory(cacheRoot);
            var shortPath = new StringBuilder(1024);
            uint size = GetShortPathName(cacheRoot, shortPath, shortPath.Capacity);
            if (size == 0 || size >= shortPath.Capacity || NeedsAsciiPath(shortPath.ToString()))
                throw new IOException("MemProcFS requires an ASCII native cache path; configure MEMPROCFS_HOME to an ASCII directory.");
            cacheRoot = shortPath.ToString();
        }

        var files = Required.Concat(Optional.Where(name => File.Exists(Path.Combine(sourceDirectory, name))))
            .Select(name => (Name: name, Bytes: File.ReadAllBytes(Path.Combine(sourceDirectory, name))))
            .Select(file => (file.Name, file.Bytes, Hash: Convert.ToHexString(SHA256.HashData(file.Bytes))))
            .ToArray();
        var identity = string.Join("\n", files.Select(file => file.Name + ":" + file.Hash));
        string version = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        string destination = Path.Combine(cacheRoot, version);
        // Six account workers can stage the same bundle concurrently. Never publish a partial file.
        using var gate = new Mutex(false, "Local\\Roadhog.MemProcFS." + version);
        bool held = false;
        try
        {
            try { held = gate.WaitOne(TimeSpan.FromSeconds(30)); }
            catch (AbandonedMutexException) { held = true; }
            if (!held) throw new IOException("MemProcFS native cache is being prepared by another worker.");
            Directory.CreateDirectory(destination);
            foreach (var file in files)
            {
                string target = Path.Combine(destination, file.Name);
                if (File.Exists(target) && Hash(target) == file.Hash) continue;
                string temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllBytes(temporary, file.Bytes);
                    if (Hash(temporary) != file.Hash) throw new IOException("MemProcFS native cache verification failed.");
                    File.Move(temporary, target, true);
                }
                finally { if (File.Exists(temporary)) File.Delete(temporary); }
            }
            return destination;
        }
        finally { if (held) gate.ReleaseMutex(); }
    }

    private static string Hash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    [DllImport("kernel32.dll", EntryPoint = "GetShortPathNameW", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint GetShortPathName(string longPath, StringBuilder shortPath, int bufferLength);
}
