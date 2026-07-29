using System.Diagnostics;
using Roadhog.Application.Shell;
using Roadhog.Core.Common;

namespace Roadhog.Infrastructure.Shell;

public sealed class WindowsFolderLauncher : IFolderLauncher
{
    public OperationResult Open(string directory)
    {
        if (string.IsNullOrWhiteSpace(directory))
        {
            return OperationResult.Fail("文件夹路径不能为空。");
        }

        try
        {
            var fullPath = Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(directory.Trim()));
            Directory.CreateDirectory(fullPath);

            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = fullPath,
                UseShellExecute = true
            });
            return process is null
                ? OperationResult.Fail("无法启动文件资源管理器。")
                : OperationResult.Ok();
        }
        catch (Exception ex)
        {
            return OperationResult.Fail(ex.Message);
        }
    }
}
