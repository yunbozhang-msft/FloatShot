# FloatShot

FloatShot fixes one annoying screenshot problem:

> You are using **Windows App** to connect to a **Dev Box / Cloud PC / Azure Virtual Desktop**, and you join a **Microsoft Teams meeting inside the remote desktop**. When you take a screenshot inside the remote session, Teams meeting content may come out blank, black, or white.

FloatShot must be installed and run on your **local Windows PC**, the same PC where you open **Windows App**. Do not install it only inside the remote Dev Box, Cloud PC, or Azure Virtual Desktop. FloatShot floats above the full-screen Windows App window and captures the pixels shown on your local monitor.

## Where to Install FloatShot

If you sit at **PC A** and use Windows App to connect to **Dev Box B**, install FloatShot on **PC A**.

```mermaid
flowchart LR
	subgraph Local["PC A: Your local Windows PC"]
		FloatShot["FloatShot<br/>INSTALL HERE"]
		WindowsApp["Windows App"]
	end

	subgraph Remote["PC B: Remote Dev Box / Cloud PC / AVD"]
		Teams["Microsoft Teams"]
	end

	WindowsApp -->|"Connects to"| Teams
```

**Correct:** Install FloatShot on the physical/local PC in front of you, alongside Windows App.

**Incorrect:** Install FloatShot only inside the Dev Box and expect it to capture Teams media rendered on the local PC.

## Why This Works

With Teams VDI optimization, Teams runs inside the remote desktop, but some meeting media can be rendered on your local PC. A screenshot tool running inside the remote desktop may not be able to capture those locally rendered pixels, which is why shared content or video can appear black, white, or blank in its screenshots.

FloatShot runs outside the remote desktop on your local PC, so it captures the final image displayed on your local monitor. Microsoft documents the underlying Teams VDI screenshot limitation in [New VDI solution for Teams](https://learn.microsoft.com/en-us/microsoftteams/vdi-2#known-issues).

## Example: Normal Screenshot Tool vs FloatShot

In an optimized Teams VDI session, a normal screenshot tool running **inside the remote desktop** can show the meeting content as blank or black even though the meeting is visible on your local screen.

**Normal in-session screenshot result:** Teams content is missing or black.

![Optimized Teams VDI session where meeting content appears blank or black in a screenshot](docs/images/teams-vdi-optimized-blank-example.png)

FloatShot runs on the **local Windows PC** instead, above the full-screen Windows App window.

**FloatShot result:** the locally rendered remote meeting view is captured.

![FloatShot capturing a remote meeting from the local Windows PC](docs/images/devbox-teams-floatshot-result-20260515.png)

## Why Use It

- Teams runs inside your Dev Box, but normal screenshots from the Dev Box show blank/black/white meeting content.
- You need a small screenshot button that still appears while Windows App is full screen.
- You want a quick PixPin-like flow: select, mark, pin, copy, or save.

## Features

- Shows a draggable floating screenshot button on the local desktop.
- Stays visible above a full-screen Windows App window by using a layered topmost tool window on the local PC.
- Captures a selected region, all monitors, the primary screen, or the active window.
- Lets you adjust the selected region before confirming.
- Provides region toolbar actions for rectangle mark, pen mark, pin, copy, save, and cancel.
- Supports pinned screenshots that can be moved, zoomed, copied, saved, or closed.
- Saves screenshots to a configurable folder and can optionally copy captures to the clipboard.

## Best For

- Windows App + Dev Box / Cloud PC / Azure Virtual Desktop.
- Teams meetings running inside the remote session.
- Full-screen remote work where local screenshot controls are hard to reach.
- Quick annotation and pinned reference screenshots.

FloatShot is intentionally small. It is not trying to replace full annotation suites; it focuses on the common PixPin-like flow of select, mark, pin, copy, or save.

## What FloatShot Is Not

- It is not a Teams VDI optimizer, Teams plugin, or remote desktop component.
- It does not change Teams, Windows App, Dev Box, Azure Virtual Desktop, or screen sharing behavior.
- It does not bypass enterprise security controls. If your organization enables policies such as [AVD Screen Capture Protection](https://learn.microsoft.com/en-us/azure/virtual-desktop/screen-capture-protection), those policies may still block client-side capture.
- It does not guarantee capture of protected content such as DRM video, secure windows, or content blocked by endpoint security software.

## Related Microsoft Documentation

- [New VDI solution for Teams](https://learn.microsoft.com/en-us/microsoftteams/vdi-2) explains the Teams VDI architecture and the known screenshot limitation for offloaded Teams content.
- [Use Microsoft Teams on Azure Virtual Desktop](https://learn.microsoft.com/en-us/azure/virtual-desktop/teams-on-avd) describes Teams media optimization with Windows App / Remote Desktop clients and how to verify whether Teams is optimized.
- [AVD Screen Capture Protection](https://learn.microsoft.com/en-us/azure/virtual-desktop/screen-capture-protection) documents policy-based screen capture blocking for remote desktop clients.

## Requirements

- Windows 10 or later.
- x64 Windows for the packaged release build.
- No .NET runtime installation is required for the release artifact because the app is published self-contained.

## Build From Source

```powershell
cd src\FloatShot
dotnet publish -c Release -o ..\..\publish
```

The published executable will be written to `publish\FloatShot.exe`.

## Build Installer

Install [Inno Setup](https://jrsoftware.org/isinfo.php), then run:

```powershell
.\build\package-installer.ps1
```

The installer will be written to `installer\Output\FloatShotSetup-0.2.4.exe`.

## Troubleshooting

### Setup is stuck at `Closing applications...`

This was a known issue in the old `0.2.0` installer when FloatShot was already running.

Use the latest installer, or exit FloatShot from the tray menu before installing. Starting with `0.2.2`, the installer stops the running FloatShot process before copying files, so this screen should not hang.

## Usage

- Drag the floating button to place it where you want.
- Click the button for the default capture mode.
- Use Settings to change save folder, default mode, hotkeys, clipboard behavior, floating button visibility, and startup behavior.
- In region capture mode, drag to select an area, then resize or move the selection before choosing an action.
- Use the toolbar to mark, draw, pin, copy, save, or cancel.

## Testing

Before publishing a release, run through [docs/TESTING.md](docs/TESTING.md).

## License

MIT. See [LICENSE](LICENSE).