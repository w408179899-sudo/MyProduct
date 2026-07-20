using Roadhog.Core.Common;

namespace Roadhog.Core.Licensing;

public interface IDeviceIdentityProvider
{
    OperationResult<string> GetDeviceHash();
}
