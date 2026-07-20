using Roadhog.Infrastructure.Composition;
using Roadhog.Application.Licensing;

internal static class LicenseLiveProbe
{
    private const string LiveArgument = "--license-live";
    private const string TestCdkeyEnvironmentVariable = "ROADHOG_LICENSE_TEST_CDKEY";

    public static bool ShouldRun(string[] args)
    {
        return args.Any(argument => string.Equals(argument, LiveArgument, StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<int> RunAsync()
    {
        var cdkey = Environment.GetEnvironmentVariable(TestCdkeyEnvironmentVariable);
        var options = RoadhogServiceOptions.FromEnvironment();
        options.EnableLogging = false;
        options.LicenseHeartbeatInterval = TimeSpan.FromSeconds(1);
        options.LicenseHeartbeatRetryDelay = TimeSpan.FromMilliseconds(100);
        using var services = RoadhogServices.Create(options);
        var coordinator = services.LicenseCoordinator;

        var state = await coordinator.InitializeAsync().ConfigureAwait(false);
        if (state.Kind == LicenseRuntimeStateKind.ActivationRequired)
        {
            if (string.IsNullOrWhiteSpace(cdkey))
            {
                Console.Error.WriteLine("Missing " + TestCdkeyEnvironmentVariable + ".");
                return 2;
            }

            state = await coordinator.ActivateAsync(cdkey).ConfigureAwait(false);
        }

        if (!state.IsAuthorized)
        {
            Console.Error.WriteLine("LICENSE_LIVE_FAILED State=" + state.Kind + " Error=" + state.ErrorCode);
            return 1;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(1500)).ConfigureAwait(false);
        state = coordinator.State;
        if (!state.IsAuthorized)
        {
            Console.Error.WriteLine("LICENSE_HEARTBEAT_FAILED State=" + state.Kind + " Error=" + state.ErrorCode);
            return 1;
        }

        Console.WriteLine(
            "LICENSE_LIVE_OK LicenseId=" + state.LicenseId
            + " ExpiresAt=" + (state.LicenseExpiresAt?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "permanent")
            + " CredentialPath=" + options.LicenseCredentialPath);
        return 0;
    }
}
