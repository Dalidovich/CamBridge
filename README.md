# CamBridge

Portable Windows console app that turns a phone camera into a camera for local agents on the PC.

## Usage

1. Run `CamBridge.exe`. It prints a QR code and a pairing URL.
2. Scan the QR code with a phone on the same network, accept the self-signed certificate warning once, tap **Start camera**.
3. Agents use the camera through:
   - MCP (Streamable HTTP): `http://127.0.0.1:8780/mcp`
   - REST: `http://127.0.0.1:8780/api` (`status`, `snapshot`, `stream`, `torch`, `zoom`, `clip`)

**Lock frame** on the phone freezes the current frame: until **Unlock**, `snapshot`, `capture_frames` and `stream` return only the locked frame, while `torch`, `zoom` and `clip` fail (REST `423 Locked`). `status` reports `frameLocked` and `frameLockedAt`.

Keys: `D` disconnects the phone and shows a new QR code, `Q` quits.

Only one phone can be paired at a time. Windows Firewall may ask to allow inbound connections on the phone port the first time.

## Files

`settings.json` and `cert.pfx` are created next to the exe. Nothing else is written to disk.

| Setting | Default | Meaning |
|---------|---------|---------|
| PhonePort | 8443 | HTTPS port for the phone, all interfaces |
| ApiPort | 8780 | HTTP port for MCP and REST, 127.0.0.1 only |
| PreferredIp | null | LAN IPv4 used in the QR code; auto when null |
| JpegQuality | 0.85 | Default JPEG quality |
| StreamFps | 10 | Default MJPEG frame rate |
| MaxFrameWidth | 1920 | Default maximum frame width |

## Build

```powershell
powershell -File ..\local-docs\build.ps1
```

Produces a self-contained single-file `P:\Program Files\CamBridge\CamBridge.exe`.
