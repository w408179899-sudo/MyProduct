using System.Windows.Forms;

namespace Hardware.KmBox;

public interface IKmBoxDevice
{
    Task<bool> ConnectAsync(CancellationToken ct = default);

    Task DisconnectAsync();

    Task MoveMouseAsync(int x, int y, CancellationToken ct = default);

    Task MoveMouseSmoothAsync(int x, int y, int durationMs, CancellationToken ct = default);

    Task MouseDownAsync(MouseButton button, CancellationToken ct = default);

    Task MouseUpAsync(MouseButton button, CancellationToken ct = default);

    Task ClickAsync(MouseButton button, CancellationToken ct = default);

    Task WheelAsync(int delta, CancellationToken ct = default);

    Task KeyDownAsync(Keys key, CancellationToken ct = default);

    Task KeyUpAsync(Keys key, CancellationToken ct = default);

    Task TypeTextAsync(string text, CancellationToken ct = default);

    Task ReleaseAllAsync(CancellationToken ct = default);
}
