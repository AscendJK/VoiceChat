---
name: run-voicechat
description: Run, build, screenshot, and drive the VoiceChat WPF desktop app (LAN voice chat).
---

# VoiceChat — .NET 8 WPF LAN Voice Chat

Build, launch, and drive this project's WPF desktop app from a Windows machine with .NET 8+ SDK. The app is a **WPF GUI** on `net8.0-windows` — it requires Windows with a display. A PowerShell driver (`driver.ps1`) wraps launch, screenshot, and UI-automated interaction via UIAutomation.

**All paths below are relative to `<unit>/` = `e:/ClaudeCode/VoiceChat/`.**

## Prerequisites

- Windows 10/11 with a display (WPF requires a graphical session)
- .NET 8 SDK or later (the project targets `net8.0-windows`)

Verify:

```powershell
dotnet --version
# Should be >= 8.0
```

## Build

```bash
dotnet build -c Release
```

Output goes to `VoiceChat.App/bin/Release/net8.0-windows/`. Build succeeds with 0 errors.

## Run (agent path)

A published single-file exe already exists at `publish/VoiceChat.App.exe`. Launch it with the PowerShell driver:

```powershell
# Dot-source the driver from the skill directory
$driver = "E:\ClaudeCode\VoiceChat\.claude\skills\run-voicechat\driver.ps1"
. $driver

# Launch the app (starts VoiceChat.App.exe, waits 3s for window)
$proc = Invoke-VoiceChatLaunch

# Take a full-screen screenshot
Get-VoiceChatScreenshot -Path "ss.png"

# Stop the app gracefully
Stop-VoiceChat
```

The driver is also available at `E:\ClaudeCode\VoiceChat\driver.ps1` (project root).

### Driver functions

| Function | Purpose |
|---|---|
| `Invoke-VoiceChatLaunch` | Launch the EXE, wait for the window (3 s) |
| `Stop-VoiceChat` | Gracefully close the app (CloseMainWindow + force) |
| `Get-VoiceChatMainWindow` | Get the `AutomationElement` for the main WPF window |
| `Get-VoiceChatScreenshot` | Save a full-screen screenshot to PNG |
| `Invoke-VoiceChatAction` | Find + click a button by partial text match (btn id=50000) |

**Note on Chinese text:** UIAutomation returns garbled text for Chinese UI elements in this environment. Button matching with Chinese characters (e.g. `-ButtonName "刷新"`) may not match. ASCII endpoints (like the app's title) and numeric patterns are fine.

## Run (human path)

```bash
dotnet run --project VoiceChat.App
```

or double-click `publish/VoiceChat.App.exe`. A blue splash window appears first (~2 s), then the main window. Press Ctrl-C in terminal or close the window to quit. Not useful headless — WPF requires a graphical Windows session.

## Test

The project has no formal test suite. Build succeeds = the main verification.

## Direct invocation (internals)

The `VoiceChat.Core` library (in `VoiceChat.Core/`) can be tested independently by referencing its assemblies. Core classes include `AudioCapture`, `AudioPlayer`, `OpusCodec`, `SignalingServer`/`Client`, `VoiceSender`/`Receiver`, `UdpRoomDiscoveryServer`, `UdpBroadcasterScanner`, and the `RoomHost`/`RoomClient` session managers.

## Gotchas

1. **Windows-only.** Targets `net8.0-windows` with `<UseWPF>true</UseWPF>`. Will not run on Linux, macOS, or headless Windows.
2. **Audio devices required for voice features.** The audio panels show empty combo boxes on machines without microphones/speakers. The UI itself still works.
3. **LAN-only.** Room discovery uses UDP broadcast on port 9999. Rooms are visible only to machines on the same subnet.
4. **Single-file EXE needs .NET Desktop Runtime.** The `publish/` exe is `--no-self-contained` (~1.3 MB). Install .NET 8 Desktop Runtime to run it standalone; the debug build from `dotnet build` includes everything.
5. **Firewall prompts.** First run triggers a Windows Firewall dialog — the app uses TCP/UDP for signaling and voice.
6. **Splash screen.** The app shows a blue splash window on startup that auto-closes after device initialization (~2 s). The driver's 3 s wait covers this.
7. **Chinese text in UIAutomation.** The `system.windows.automation` API may return garbled text for Chinese UI elements in mixed-encoding environments. Button matching by Chinese name may fail. Structural interaction (screenshot, process management) is unaffected.

## Troubleshooting

| Symptom | Fix |
|---|---|
| `dotnet build` fails with `NETSDK1083` | Install .NET 8 SDK or later. `dotnet --version` to check. |
| EXE launches but no window appears | The app may be minimized or behind other windows. Check Task Manager for `VoiceChat.App.exe`. |
| Splash window stays forever | Audio device init failed. Check Windows sound settings for available devices. |
| PowerShell script won't load | `Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass` to allow scripts. |
| Driver functions not found after `. $driver` | The file path contains Chinese chars; use the full 8.3 path or copy driver to a simpler path. |
