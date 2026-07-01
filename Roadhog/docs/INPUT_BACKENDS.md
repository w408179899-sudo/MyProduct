# Roadhog Input

Roadhog uses KMBox Net for keyboard and mouse output. All combat and path logic
still goes through `IKeyboardInput`; the composition layer always creates
`KmBoxNetKeyboardInput`.

## Environment Configuration

Set the KMBox Net endpoint:

```powershell
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
it for KMBox Net endpoint selection.
