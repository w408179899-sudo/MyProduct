using System.Security.Cryptography;
using System.Security;
using System.Text;
using Microsoft.Win32;
using Roadhog.Core.Common;
using Roadhog.Core.Licensing;

namespace Roadhog.Infrastructure.Licensing;

public sealed class WindowsDeviceIdentityProvider : IDeviceIdentityProvider
{
    private const string MachineGuidKeyPath = @"SOFTWARE\Microsoft\Cryptography";
    private const string MachineGuidValueName = "MachineGuid";

    public OperationResult<string> GetDeviceHash()
    {
        try
        {
            using var localMachine = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = localMachine.OpenSubKey(MachineGuidKeyPath, writable: false);
            var machineGuid = Convert.ToString(key?.GetValue(MachineGuidValueName))?.Trim();
            if (string.IsNullOrWhiteSpace(machineGuid))
            {
                return OperationResult<string>.Fail("Windows MachineGuid is unavailable.");
            }

            var material = "Roadhog.Device.v1|" + machineGuid.ToUpperInvariant();
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(material));
            return OperationResult<string>.Ok(Convert.ToHexString(hash).ToLowerInvariant());
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            return OperationResult<string>.Fail("Failed to read Windows device identity: " + ex.Message);
        }
    }
}
