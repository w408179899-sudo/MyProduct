using Roadhog.Core.Common;

namespace Roadhog.Core.Hardware;

public interface IHardwareDeviceResolver
{
    IReadOnlyList<HardwareDeviceFeature> ListDevices();

    OperationResult<HardwareBinding> BindByKey(string accountName, string hardwareKey);

    OperationResult<HardwareBinding> TryAutoBind(string accountName);
}
