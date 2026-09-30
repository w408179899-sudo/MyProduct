using System.Reflection;
using System.Runtime.CompilerServices;
using Roadhog;
using Roadhog.Core.Common;
using Roadhog.Infrastructure.Config;
using Roadhog.Infrastructure.Hardware;
using Roadhog.Infrastructure.Input;
using Roadhog.Core.Model;

internal static class LegacyHardwareSaveTests
{
    public static async Task KmBoxSaveRollsBackWhenFpgaSaveFailsAsync()
    {
        var directory = Path.Combine(Path.GetTempPath(), "roadhog-legacy-hardware-save-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "kmbox-net.json");
        try
        {
            var original = "{\r\n  \"IpAddress\": \"127.0.0.1\",\r\n  \"Port\": 4967,\r\n  \"Mac\": \"A1B2C3D4\"\r\n}\r\n";
            await File.WriteAllTextAsync(path, original);
            var originalBytes = await File.ReadAllBytesAsync(path);
            var next = new KmBoxNetDeviceConfig { IpAddress = "127.0.0.2", Port = 4968, Mac = "B1B2C3D4" };
            var failure = await SaveAsync(path, next, () => Task.FromResult(OperationResult.Fail("DMA 设备已占用")));
            Require(!failure.Success && failure.Error?.Contains("KMBox 配置已恢复", StringComparison.Ordinal) == true,
                "FPGA rejection reports the KMBox rollback");
            Require((await File.ReadAllBytesAsync(path)).SequenceEqual(originalBytes),
                "existing KMBox JSON is restored byte for byte after FPGA rejection");
            var oldConfig = new JsonKmBoxNetDeviceConfigStore(path).Load();
            Require(oldConfig.Success && oldConfig.Value?.IpAddress == "127.0.0.1",
                "the old KMBox endpoint remains the next-start configuration");

            File.Delete(path);
            failure = await SaveAsync(path, next, () => Task.FromResult(OperationResult.Fail("FPGA 保存失败")));
            Require(!failure.Success && !File.Exists(path),
                "a legacy client with no KMBox file remains file-free after FPGA rejection");

            failure = await SaveAsync(path, next, () => throw new IOException("FPGA 写入异常"));
            Require(!failure.Success && !File.Exists(path),
                "an exception during FPGA save also removes the newly created KMBox file");

            var success = await SaveAsync(path, next, () => Task.FromResult(OperationResult.Ok()));
            Require(success.Success, "both settings save successfully in the existing order");
            var saved = new JsonKmBoxNetDeviceConfigStore(path).Load();
            Require(saved.Success && saved.Value?.IpAddress == next.IpAddress && saved.Value.Port == next.Port,
                "successful save leaves the new KMBox configuration on disk");

            await File.WriteAllTextAsync(path, original);
            FileStream? blocker = null;
            try
            {
                failure = await SaveAsync(path, next, () =>
                {
                    blocker = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
                    return Task.FromResult(OperationResult.Fail("FPGA 保存失败"));
                });
                Require(!failure.Success && failure.Error?.Contains("KMBox 配置回滚失败", StringComparison.Ordinal) == true,
                    "a locked original file produces an explicit rollback failure instead of claiming restoration");
            }
            finally { blocker?.Dispose(); }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            Directory.Delete(directory);
        }
    }

    public static async Task DeviceReadRequiresLeaseAndCommittedSaveSurvivesUiRefreshFailureAsync()
    {
        var readMethod = typeof(Form1).GetMethod("ReadWithDeviceLeaseAsync", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException("Form1.ReadWithDeviceLeaseAsync");
        var nativeReads = 0;
        var rejected = false;
        try
        {
            _ = readMethod.Invoke(null, new object[]
            {
                (Func<OperationResult>)(() => OperationResult.Fail("device occupied")),
                (Func<Task<PlayerSnapshot>>)(() => { nativeReads++; return Task.FromResult<PlayerSnapshot>(null!); })
            });
        }
        catch (TargetInvocationException ex) when (ex.InnerException is InvalidOperationException)
        {
            rejected = true;
        }
        Require(rejected && nativeReads == 0, "an unavailable lease blocks the native player read callback");

        var granted = (Task<PlayerSnapshot>)readMethod.Invoke(null, new object[]
        {
            (Func<OperationResult>)(OperationResult.Ok),
            (Func<Task<PlayerSnapshot>>)(() => { nativeReads++; return Task.FromResult<PlayerSnapshot>(null!); })
        })!;
        await granted;
        Require(nativeReads == 1, "the player read starts only after lease acquisition succeeds");

        var form = (Form1)RuntimeHelpers.GetUninitializedObject(typeof(Form1));
        var currentLease = new DeviceLease(Environment.ProcessId, DateTimeOffset.UtcNow,
            @"C:\script\1", "P0004.H0001", "fpga", DateTimeOffset.UtcNow);
        typeof(Form1).GetField("_deviceLease", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(form, currentLease);
        var acquireMethod = typeof(Form1).GetMethod("TryAcquireDeviceLease", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException("Form1.TryAcquireDeviceLease");
        var acquireArguments = new object?[] { "P0004.H0002", "fpga", null };
        var switched = (bool)acquireMethod.Invoke(form, acquireArguments)!;
        Require(!switched && !string.IsNullOrWhiteSpace(acquireArguments[2] as string),
            "the legacy form cannot switch its active physical device lease during another player read");

        var complete = typeof(Form1).GetMethod("CompleteFpgaSaveAfterCommit", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException("Form1.CompleteFpgaSaveAfterCommit");
        var reported = false;
        var completed = (OperationResult)complete.Invoke(null, new object[]
        {
            (Action)(() => throw new ObjectDisposedException("closed UI")),
            (Action<Exception>)(_ => reported = true)
        })!;
        Require(completed.Success && reported,
            "a UI refresh failure is logged but cannot reclassify an already committed FPGA save as failed");

        var directory = Path.Combine(Path.GetTempPath(), "roadhog-legacy-ui-refresh-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "kmbox-net.json");
        try
        {
            var next = new KmBoxNetDeviceConfig { IpAddress = "127.0.0.2", Port = 4968, Mac = "B1B2C3D4" };
            var result = await SaveAsync(path, next, () => Task.FromResult(completed));
            Require(result.Success && new JsonKmBoxNetDeviceConfigStore(path).Load().Value?.IpAddress == next.IpAddress,
                "KMBox remains committed after the FPGA save succeeded and UI refresh failed");
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
            Directory.Delete(directory);
        }
    }

    private static Task<OperationResult> SaveAsync(string path, KmBoxNetDeviceConfig config,
        Func<Task<OperationResult>> saveFpga)
    {
        var method = typeof(Form1).GetMethod("SaveKmBoxAndFpgaAsync", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException("Form1.SaveKmBoxAndFpgaAsync");
        return (Task<OperationResult>)method.Invoke(null, new object[] { path, config, saveFpga })!;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
