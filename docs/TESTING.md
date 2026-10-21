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
- New captures start without a drawing tool selected; clicking an active tool again restores frame move/resize and hides its options.
- Rectangle supports Outline and Solid; Solid hides the unused width control and fully covers the selected area.
- Arrow and Pen use the chosen color and width; highlighter stays translucent, including self-overlapping strokes.
- Mosaic supports Rectangle and Brush, exposes only a Mosaic strength slider, and leaves pixels outside the mask untouched.
- Text supports color, size, bold, multiline and non-English input. Click away or switch tools to finish; Esc while typing cancels only the current text edit.
- Text remains editable on double-click and movable by dragging in selection mode.
- Typing letters such as C, S, P, R, and M inside text must not trigger capture actions or tool changes.
- `Ctrl+Z` undoes the latest operation across all tools, including text moves and edits; `Ctrl+Y` and `Ctrl+Shift+Z` redo it.
- Tool preferences survive a restart, are shared with pinned images, and do not reset existing save folders or hotkeys. Cancelling a capture does not undo preference changes.
- Preview, clipboard, and saved image agree, without exporting the toolbar, text editor border, selection frame, or tooltips.
- Pin creates a floating pinned image with annotations included.
- Copy places the annotated image on the clipboard.
- `Enter`, `C`, and `Ctrl+C` invoke Copy without creating an image file or save directory.
- Save remains a neutral icon and writes directly to the configured folder; no Save As dialog appears.
- With Open folder after capture disabled (the default), quick Save must leave the foreground application unchanged.
- Hovering over Save shows the configured destination and shortcut.
- The small Save arrow, or right-clicking Save, opens a menu containing the destination, Save as, and Set save folder, without a duplicate Copy command.
- Set save folder opens a folder picker without saving or discarding the current screenshot selection.
- Confirming a folder immediately updates the menu path and Save hover hint, and remembers the preference across restarts even if this screenshot is cancelled.
- Cancelling the folder picker leaves the configured folder and current selection unchanged.
- `S` and `Ctrl+S` invoke quick Save; `Ctrl+Shift+S` invokes explicit Save As.
- Save As opens a dialog with a suggested filename containing date, hours, minutes, seconds, and milliseconds.
- The configured folder, or the last successfully selected folder, is used as the initial Save As location.
- Existing suggested filenames receive `_1`, `_2`, and subsequent suffixes instead of being offered again.
- Choosing another filename or folder saves a valid annotated PNG at that location.
- Manually selecting an existing filename asks for overwrite confirmation; declining preserves the existing file.
- Cancelling Save As writes no image, changes no clipboard content or remembered folder, and creates no directory.
- Cancel closes the overlay without saving.

## Pinned Images

- Pinned image can be dragged.
- Space and the Annotate menu toggle the shared toolbar. A drawing-tool shortcut also reveals it; deselecting the tool restores image dragging.
- All annotation tools work at 50%, 100%, and 200% zoom; copy/save export at original image resolution without shifting annotations.
- Pinned toolbar stays on-screen and outside the image when space is available; closing the image closes its toolbar.
- Mouse wheel zooms in and out.
- Double-click closes the pinned image.
- Right-click Copy, Save, Reset zoom, and Close work.
- Save As suggests a timestamped filename with milliseconds and avoids names already present in its initial folder.

## Settings

- Fresh settings default to the Windows Pictures folder plus `FloatShotScreenshots`, with no spaces in the new subfolder name.
- The Pictures path follows Windows known-folder resolution on English and non-English systems, including redirected folders.
- Existing saved locations, including the old `Screenshots` directory and custom paths with spaces or non-English characters, are preserved after upgrading and restarting.
- Save folder can be changed with Browse.
- Default mode changes the floating button action.
- Hotkey fields persist after Save.
- Behavior checkboxes persist after Save.
- Run at Windows startup adds or removes the current-user Run key.

## Regression Areas

- Capture should use the pre-overlay snapshot, not a screenshot of the overlay itself.
- Floating UI should keep `WS_EX_LAYERED`, `WS_EX_TOOLWINDOW`, and `WS_EX_TOPMOST` behavior.
- Embedded Fluent SVG icons should render without fallback squares.