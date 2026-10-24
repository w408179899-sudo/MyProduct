using System.Text.Json;

namespace Roadhog.Infrastructure.Login;

// External read/projection errors stay inside the provider/input adapter. Cancellation,
// disposal and programming faults must not be silently turned into another login attempt.
internal static class LoginReadErrors
{
    internal static bool IsRetryable(Exception exception) => exception is not ObjectDisposedException
        && exception is InvalidOperationException or IOException or ArgumentException
            or KeyNotFoundException or FormatException or OverflowException or JsonException or Vmmsharp.VmmException;
}
