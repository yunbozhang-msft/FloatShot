# FloatShot

FloatShot is a lightweight Windows screenshot helper with an always-on-top floating capture button. It is designed for people who spend a lot of time inside full-screen remote desktop, Windows App, Cloud PC, or devbox sessions and still need a local screenshot button that stays reachable.

## What It Does

- Shows a draggable floating screenshot button on the desktop.
- Stays visible over Windows App / devbox full-screen sessions by using a layered topmost tool window.
- Captures a selected region, all monitors, the primary screen, or the active window.
- Lets you adjust the selected region before confirming.
- Provides region toolbar actions for rectangle mark, pen mark, pin, copy, save, and cancel.
- Supports pinned screenshots that can be moved, zoomed, copied, saved, or closed.
- Saves screenshots to a configurable folder and can optionally copy captures to the clipboard.

## Best Fit Scenarios

- Taking screenshots while working inside a full-screen devbox or Cloud PC.
- Keeping a small screenshot button available without leaving the remote session.
- Quickly marking a selected area with a rectangle or pen before copying or saving.
- Pinning a temporary reference screenshot while comparing information across windows.

FloatShot is intentionally small. It is not trying to replace full annotation suites; it focuses on the common PixPin-like flow of select, mark, pin, copy, or save.

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