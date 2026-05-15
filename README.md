# FloatShot

FloatShot is a lightweight Windows screenshot helper for a very specific remote-work problem: taking usable screenshots while you are connected to a Dev Box, Cloud PC, or Azure Virtual Desktop session through Windows App and Microsoft Teams is running inside that remote desktop.

In optimized Teams VDI sessions, Teams media such as video feeds and incoming screen sharing can be rendered on the local endpoint instead of entirely inside the remote VM. Microsoft documents this architecture in [New VDI solution for Teams](https://learn.microsoft.com/en-us/microsoftteams/vdi-2): the VDI client loads a Teams plugin and starts `MsTeamsVdi.exe` on the user's device, while the Teams app in the VM communicates with it through a virtual channel. Microsoft also lists a known issue for screenshots: users trying to take a screenshot of Teams content, such as incoming screen sharing or video feeds, can't capture that content because it's rendered, or offloaded, on the user's device; the captured result can be a black square.

That means a screenshot tool running inside the Dev Box can capture only what the VM sees, not necessarily the optimized Teams media that Windows App is presenting on your physical PC. FloatShot runs on the local Windows endpoint and floats above the Windows App session, so it captures the pixels as they are presented locally.

## What It Does

- Shows a draggable floating screenshot button on the local desktop.
- Stays visible over Windows App / Dev Box full-screen sessions by using a layered topmost tool window.
- Captures a selected region, all monitors, the primary screen, or the active window.
- Lets you adjust the selected region before confirming.
- Provides region toolbar actions for rectangle mark, pen mark, pin, copy, save, and cancel.
- Supports pinned screenshots that can be moved, zoomed, copied, saved, or closed.
- Saves screenshots to a configurable folder and can optionally copy captures to the clipboard.

## Best Fit Scenarios

- Capturing Teams meeting content while Teams runs inside a Windows App / Dev Box session and normal in-VM screenshots produce blank, black, or white results.
- Capturing incoming Teams screen sharing or video content that is rendered through Teams VDI optimization on the local endpoint.
- Taking screenshots while working inside a full-screen Dev Box, Cloud PC, or Azure Virtual Desktop session.
- Keeping a small screenshot button available without leaving the remote session or switching away from Windows App.
- Quickly marking a selected area with a rectangle or pen before copying or saving.
- Pinning a temporary reference screenshot while comparing information across windows.

FloatShot is intentionally small. It is not trying to replace full annotation suites; it focuses on the common PixPin-like flow of select, mark, pin, copy, or save.

## What FloatShot Is Not

- It is not a Teams VDI optimizer, Teams plugin, or remote desktop component.
- It does not change Teams, Windows App, Dev Box, Azure Virtual Desktop, or screen sharing behavior.
- It does not bypass enterprise security controls. If your organization enables policies such as [AVD Screen Capture Protection](https://learn.microsoft.com/en-us/azure/virtual-desktop/screen-capture-protection), those policies may still block client-side capture.
- It does not guarantee capture of protected content such as DRM video, secure windows, or content blocked by endpoint security software.

## Related Microsoft Documentation

- [New VDI solution for Teams](https://learn.microsoft.com/en-us/microsoftteams/vdi-2) explains the SlimCore-based Teams VDI architecture, including the endpoint-side `MsTeamsVdi.exe` media engine and the known screenshot limitation for offloaded Teams content.
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

The installer will be written to `installer\Output\FloatShotSetup-0.2.2.exe`.

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