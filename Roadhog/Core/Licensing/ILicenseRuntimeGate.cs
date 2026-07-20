namespace Roadhog.Core.Licensing;

public interface ILicenseRuntimeGate
{
    bool IsAuthorized { get; }
}
