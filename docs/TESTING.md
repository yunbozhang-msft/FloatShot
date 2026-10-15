# FloatShot Testing Checklist

Use this checklist before publishing a GitHub release.

## Build Checks

```powershell
cd src\FloatShot
dotnet restore
dotnet build -c Release
dotnet publish -c Release -o ..\..\publish
```

Expected result: build and publish succeed with no warnings or errors.

## Release Privacy Checks

- Review `git status --short --untracked-files=all` before committing.
- Keep temporary presentations, recordings, prototypes, local settings, and build caches out of Git.
- Check source and documentation for credentials, internal endpoints, company email addresses, customer data, and personal absolute paths.
- Use synthetic content for example screenshots; do not rely on blurring real workplace or customer information.
- Release binaries must not embed debug symbols or ship application PDB files that expose local source paths.
- Package only the publish output, not a workspace archive or local temporary files.
- Removing a sensitive file from the current tree does not remove it from Git history or older release assets; review those separately when necessary.

## Installer Checks

```powershell
.\build\package-installer.ps1
```

Expected result: `installer\Output\FloatShotSetup-<version>.exe` is created.

Manual install validation:

- Install FloatShot for the current user.
- Confirm Start Menu shortcut is created.
- Confirm `Launch FloatShot` is checked by default on the installer finish page.
- Launch FloatShot from the installer finish page and from Start Menu.
- Uninstall from Windows Settings and confirm the app files are removed.

## Floating Button

- Floating button appears after launch.
- Button can be dragged and remembers its position after restart.
- Button remains visible over a full-screen Windows App / devbox session.
- Right-click menu opens Settings and Exit.
- Hiding the floating button in Settings removes it after saving settings.

## Windows App / Dev Box / Teams VDI Scenario

- Connect to a Dev Box, Cloud PC, or Azure Virtual Desktop session with Windows App.
- Join a Microsoft Teams meeting inside the remote session.
- Confirm that a screenshot tool running inside the remote session has trouble capturing optimized Teams meeting content, such as incoming screen sharing or video, when applicable to the environment.
- Use FloatShot from the local endpoint while Windows App is full screen.
- Confirm FloatShot can capture the locally presented Windows App pixels for the same area.
- Confirm this behavior is documented as endpoint/offload-sensitive in Microsoft Teams VDI documentation, and do not treat FloatShot as a bypass for AVD Screen Capture Protection or other enterprise security controls.

## Capture Modes

- Region capture selects the intended area.
- Region selection can be moved by dragging inside the selected box.
- Region selection can be resized from edges and corners.
- Full screen captures all monitors.
- Primary screen captures the primary monitor.
- Active window captures the foreground window.
- Non-region captures save directly to the configured folder without opening a dialog.

## Region Toolbar

- Hovering over each button shows its name and shortcut after a short delay; moving away or clicking hides the hint.
- Toolbar and hints remain on the selected monitor, including selections near its bottom edge.
- Rectangle mark draws an outline without a filled center.
- Pen mark draws freehand strokes.
- `Ctrl+Z` undoes the most recent annotation.
- Pin creates a floating pinned image with annotations included.
- Copy places the annotated image on the clipboard.
- Save remains a neutral icon and writes directly to the configured folder; no Save As dialog appears.
- With Open folder after capture disabled (the default), quick Save must leave the foreground application unchanged.
- Hovering over Save shows the configured destination and shortcut.
- The small Save arrow, or right-clicking Save, opens a menu containing the destination, Save, and Save as.
- `S` and `Enter` invoke quick Save; `Ctrl+Shift+S` invokes explicit Save As.
- Save As opens a dialog with a suggested filename containing date, hours, minutes, seconds, and milliseconds.
- The configured folder, or the last successfully selected folder, is used as the initial Save As location.
- Existing suggested filenames receive `_1`, `_2`, and subsequent suffixes instead of being offered again.
- Choosing another filename or folder saves a valid annotated PNG at that location.
- Manually selecting an existing filename asks for overwrite confirmation; declining preserves the existing file.
- Cancelling Save As writes no image, changes no clipboard content or remembered folder, and creates no directory.
- Cancel closes the overlay without saving.

## Pinned Images

- Pinned image can be dragged.
- Mouse wheel zooms in and out.
- Double-click closes the pinned image.
- Right-click Copy, Save, Reset zoom, and Close work.
- Save As suggests a timestamped filename with milliseconds and avoids names already present in its initial folder.

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