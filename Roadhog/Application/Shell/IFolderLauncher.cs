using Roadhog.Core.Common;

namespace Roadhog.Application.Shell;

public interface IFolderLauncher
{
    OperationResult Open(string directory);
}
