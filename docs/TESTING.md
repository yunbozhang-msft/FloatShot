# FloatShot Testing Checklist

Use this checklist before publishing a GitHub release.

## Build Checks

```powershell
cd src\FloatShot
dotnet restore
dotnet build -c Release
dotnet publish -c Release -o ..\..\publish
```

Expected result: build and publish succeed. The current known warning is `WFAC010` for high DPI settings in the app manifest.

## Installer Checks

```powershell
.\build\package-installer.ps1
```

Expected result: `installer\Output\FloatShotSetup-0.1.0.exe` is created.

Manual install validation:

- Install FloatShot for the current user.
- Confirm Start Menu shortcut is created.
- Launch FloatShot from the installer finish page and from Start Menu.
- Uninstall from Windows Settings and confirm the app files are removed.

## Floating Button

- Floating button appears after launch.
- Button can be dragged and remembers its position after restart.
- Button remains visible over a full-screen Windows App / devbox session.
- Right-click menu opens Settings and Exit.
- Hiding the floating button in Settings removes it after saving settings.

## Capture Modes

- Region capture selects the intended area.
- Region selection can be moved by dragging inside the selected box.
- Region selection can be resized from edges and corners.
- Full screen captures all monitors.
- Primary screen captures the primary monitor.
- Active window captures the foreground window.

## Region Toolbar

- Rectangle mark draws an outline without a filled center.
- Pen mark draws freehand strokes.
- `Ctrl+Z` undoes the most recent annotation.
- Pin creates a floating pinned image with annotations included.
- Copy places the annotated image on the clipboard.
- Save writes the annotated image to the configured folder.
- Cancel closes the overlay without saving.

## Pinned Images

- Pinned image can be dragged.
- Mouse wheel zooms in and out.
- Double-click closes the pinned image.
- Right-click Copy, Save, Reset zoom, and Close work.

## Settings

- Save folder can be changed with Browse.
- Default mode changes the floating button action.
- Hotkey fields persist after Save.
- Behavior checkboxes persist after Save.
- Run at Windows startup adds or removes the current-user Run key.

## Regression Areas

- Capture should use the pre-overlay snapshot, not a screenshot of the overlay itself.
- Floating UI should keep `WS_EX_LAYERED`, `WS_EX_TOOLWINDOW`, and `WS_EX_TOPMOST` behavior.
- Embedded Fluent SVG icons should render without fallback squares.