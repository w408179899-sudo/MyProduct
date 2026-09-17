using Hardware.KmBox;
using Smart.Contracts;
namespace Smart.Adapters.KmBox;

// The original transport is vendored unchanged under Vendor/Hardware.KmBox.
// Construct only in explicit hardware composition; mock hosts never construct this class.
public sealed class KmBoxInputDevice : IInputDevice
{
    private readonly KmBoxNetDevice _device;
    private readonly SemaphoreSlim _connection = new(1, 1);
    private bool _connected;
    public string DeviceId { get; }
    public KmBoxInputDevice(KmBoxOptions options)
    {
        var validated = options.CloneAndValidate();
        validated.IpAddress = System.Net.IPAddress.Parse(validated.IpAddress).ToString();
        DeviceId = $"kmbox:{validated.IpAddress}:{validated.Port}";
        _device = new(validated);
    }
    public async ValueTask InitializeAsync(CancellationToken token)
    {
        await ConnectAsync(token).ConfigureAwait(false);
        await _device.ReleaseAllAsync(token).ConfigureAwait(false);
    }
    public async ValueTask SendAsync(InputCommand command, CancellationToken cancellationToken)
    {
        await ConnectAsync(cancellationToken).ConfigureAwait(false);
        switch (command.Operation)
        {
            case InputOperation.KeyDown: await _device.KeyDownAsync(command.Code, cancellationToken).ConfigureAwait(false); break;
            case InputOperation.KeyUp: await _device.KeyUpAsync(command.Code, cancellationToken).ConfigureAwait(false); break;
            case InputOperation.MouseDown: await _device.MouseDownAsync(ConvertButton(command.Code), cancellationToken).ConfigureAwait(false); break;
            case InputOperation.MouseUp: await _device.MouseUpAsync(ConvertButton(command.Code), cancellationToken).ConfigureAwait(false); break;
            case InputOperation.MoveRelative: await _device.MoveMouseAsync(command.X, command.Y, cancellationToken).ConfigureAwait(false); break;
            case InputOperation.MoveAbsolute: await _device.MoveMouseToAsync(command.X, command.Y, cancellationToken).ConfigureAwait(false); break;
            case InputOperation.Scroll: await _device.WheelAsync(command.Y, cancellationToken).ConfigureAwait(false); break;
            default: throw new ArgumentException("Unsupported physical input command.");
        }
    }
    public async ValueTask ReleaseAllAsync(CancellationToken cancellationToken)
    {
        if (_connected) await _device.ReleaseAllAsync(cancellationToken).ConfigureAwait(false);
    }
    public ValueTask DisposeAsync()
    {
        _device.Dispose(); _connection.Dispose();
        return ValueTask.CompletedTask;
    }
    private async Task ConnectAsync(CancellationToken ct)
    {
        if (_connected) return;
        await _connection.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!_connected) _connected = await _device.ConnectAsync(ct).ConfigureAwait(false);
            if (!_connected) throw new IOException("KMBox connection failed: " + DeviceId);
        }
        finally { _connection.Release(); }
    }
    private static Hardware.KmBox.MouseButton ConvertButton(int value) => (Smart.Contracts.MouseButton)value switch
    {
        Smart.Contracts.MouseButton.Left => Hardware.KmBox.MouseButton.Left,
        Smart.Contracts.MouseButton.Right => Hardware.KmBox.MouseButton.Right,
        Smart.Contracts.MouseButton.Middle => Hardware.KmBox.MouseButton.Middle,
        _ => throw new ArgumentOutOfRangeException(nameof(value))
    };
}
