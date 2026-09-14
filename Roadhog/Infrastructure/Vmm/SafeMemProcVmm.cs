using System.Runtime.InteropServices;
using Vmmsharp;

namespace Roadhog.Infrastructure.Vmm;

/// <summary>Protects the bundled vmmsharp 5.16.12 finalizer after a failed constructor.</summary>
internal sealed class SafeMemProcVmm(string[] args) : Vmmsharp.Vmm(args)
{
    protected override void Dispose(bool disposing)
    {
        if (LeechCore is not null)
        {
            base.Dispose(disposing);
            return;
        }

        // The base constructor may throw before assigning LeechCore. Its finalizer still
        // dispatches here; base.Dispose would dereference null before closing hVMM.
        // No instance field initializers may be assumed to have run on this path.
        var handle = Interlocked.Exchange(ref hVMM, IntPtr.Zero);
        if (handle != IntPtr.Zero) CloseNativeHandle(handle);
    }

    [DllImport("vmm", EntryPoint = "VMMDLL_Close", ExactSpelling = true)]
    private static extern void CloseNativeHandle(IntPtr handle);
}
