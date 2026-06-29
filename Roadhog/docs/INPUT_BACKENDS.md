# Roadhog Input Backends

Roadhog selects one input backend at service startup. All keyboard and mouse
operations go through `IKeyboardInput`; combat and path logic do not branch on
the concrete device.

## Backends

- `hardware_box`: existing serial hardware box backend.
- `kmbox_net`: KMBox Net UDP backend through `Hardware.KmBox`.

The default is `hardware_box` to preserve existing behavior.

## Environment Configuration

```powershell
$env:ROADHOG_INPUT_BACKEND = "hardware_box"
$env:KMBOX_PORT = "COM11"
```

```powershell
$env:ROADHOG_INPUT_BACKEND = "kmbox_net"
$env:KMBOX_NET_IP = "192.168.2.188"
$env:KMBOX_NET_PORT = "12345"
$env:KMBOX_NET_MAC = "00112233"
```

Optional KMBox Net timeout overrides:

```powershell
$env:KMBOX_NET_COMMAND_TIMEOUT_MS = "1000"
$env:KMBOX_NET_SEND_TIMEOUT_MS = "1000"
$env:KMBOX_NET_RECEIVE_TIMEOUT_MS = "1000"
```

`AccountConfig.HardwareKey` remains the VMM/FTDI account binding key. Do not use
it to select the input backend.
