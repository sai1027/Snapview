# Snapview: agent guide

## Product

Native Windows image viewer inspired by macOS Preview. Keep opening/viewing fast,
the UI minimal, and edits local. Core scope: zoom/pan, crop/resize/rotate, format
conversion, editable annotations/text, metadata/GPS, on-demand OCR, save/revert.
Library indexing, filters/retouching, background removal, sync and background jobs
are outside the current scope unless explicitly requested.

## Stack and source map

- C# / .NET 10 WPF, `net10.0-windows`, `win-x64`; nullable + implicit usings.
- `App.xaml[.cs]`: shared styles; startup, file arguments, `--self-test`.
- `MainWindow.xaml[.cs]`: UI, gestures/shortcuts, async coordination, save/revert.
  Current design is code-behind, not MVVM or a dependency-injection container.
- `Imaging.cs`: `ImageDocument`, WIC/Magick codecs, transforms, metadata, `Originals`.
- `Annotations.cs`: immutable annotation geometry/text, scene snapshots, lazy export compositing.
- `AnnotationEditor.cs`: inline text input, object hit-testing, selection handles and grouped drags.
- `AnnotationText.cs`: RichTextBox/FlowDocument conversion, text-selection offsets, immutable color/font runs.
- `Theme.cs`: live light/dark palette and local appearance preference; use dynamic resources for UI colors.
- `FileActions.cs`: validated same-folder rename, original-recovery reindexing, Recycle Bin deletion and confirmation preference.
- `RenameHistory.cs`: file-name undo/redo interleaved with scene edits using weak scene anchors.
- `CropPreview.cs`: UI-only outside shading, crop dimensions/ratio and shared pixel rounding.
- `WindowBounds.cs`: monitor work-area bounds for the borderless maximized window.
- `Dialogs.cs`: resize and OCR copy dialogs; annotation text is edited on the canvas.
- `Associations.cs`: HKCU registrations and Shell notification; defaults stay user-chosen.
- `SmokeTests.cs`: production pipeline checks on an STA thread; `tests/**` is currently
  excluded from project compilation, so do not put new C# tests there without wiring them up.
- `scripts/`: setup, tests, publish, per-user install/uninstall.
- NuGet: Magick.NET-Q8-x64 14.16.0, MetadataExtractor 2.9.3, Tesseract 5.2.0.
  Offline English model: `tessdata/eng.traineddata`. Versions live in the project file.
- Read [docs/README.md](docs/README.md), then the architecture or development guide
  for the area being changed. Keep these docs accurate when behavior changes.

## Commands (PowerShell, repository root; Windows + .NET 10 SDK)

```powershell
.\scripts\setup.ps1
dotnet build -c Release
dotnet run -c Release --no-build
.\scripts\test.ps1
.\scripts\test.ps1 -WithCodecFixtures  # downloads optional upstream fixtures
.\scripts\publish.ps1
```

Scripts with an SDK parameter accept `-Dotnet 'C:\path\to\dotnet.exe'`.
Tests write `bin/Release/net10.0-windows/win-x64/test-output/results.json`;
failure exits 1 and writes `self-test-error.txt` beside the executable.
`bin/`, `obj/`, `artifacts/`, `.tools/` are generated/ignored; edit source instead.

## Invariants for changes

- Canvas units are image pixels. Normalize to frozen 96-DPI `Pbgra32` bitmaps;
  preserve EXIF orientation. Use image-space coordinates, not zoomed screen units.
- Raster edits use `Commit`; object edits use `SetAnnotations` with new immutable values.
  Dirty state compares scene identity with the saved snapshot. Preserve undo/redo and
  coalesce each drag/typing group without swallowing a saved-state undo boundary.
- WPF visuals, dialogs and rendering belong on the UI/STA thread. Freeze bitmap
  results crossing threads. Existing `Work` guards loading/OCR; metadata uses revision
  checks. Keep expensive codecs/OCR off startup. Rendering/export currently block UI.
- Preserve `CheckSize`: 64 MP / 32,000 per side. Undo counts shared base bitmaps once,
  keeps up to 150 states / about 192 MiB with a one-state floor; other buffers add memory.
- Use `BaseImage` for display/dimensions and the editor for live layers. `Current`
  lazily rasterizes the scene for export/OCR/copy; never invoke it on every pointer move.
- Selection borders, move handles and the inline RichTextBox are UI-only. Export draws
  annotation models, not editor visuals. Side text handles reflow; corners stretch.
- TextStyles contain immutable character ranges (UTF-16 offsets, color, font size, bold, italic).
  Keep editor/export rendering and selection offsets consistent across newlines.
  Highlighted text alone receives toolbar color/size/emphasis changes; an empty selection
  styles the whole box. Preserve other properties and surrounding words; plain-text
  paste only, 5,000-character limit, shared scene undo (no separate rich-editor history).
- Save via temporary file + replacement. Capture original bytes before overwrite;
  update recovery hash only after save. Preserve unsaved-change cancellation.
- Exports omit source camera/GPS metadata; JPEG/BMP flatten alpha to white. Metadata
  describes the source file, while displayed canvas dimensions reflect edits.
- Never silently change Windows defaults, upload images, or remove recovery copies.
  Map coordinates are sent to the browser only by the user's map action.
- Current limits: first frame of GIF/TIFF/ICO; English OCR; session-only editable
  annotations (exports and whole-image geometry edits flatten; undo restores layers);
  no HDR/color-managed editing. Changing these requires UI/docs/test updates.
- Snapview-owned code/docs are MIT. Keep third-party licenses/notices and model
  provenance; dependency changes require reviewing [docs/licensing.md](docs/licensing.md).

## Validation and handoff

Build changed C#/XAML. Run the pipeline checks for imaging, document state, OCR or
save/recovery changes; use codec fixtures for codec changes. Visually inspect changed
UI at minimum size and normal/maximized sizes; verify relevant shortcuts and undo.
For packaging changes verify required files in a fresh publish folder. Docs-only
changes need link/accuracy checks, not a full image test run. Report what changed,
checks run, and any remaining limitations. Preserve unrelated user work.

`.local/` is ignored personal task material; do not package it. Appearance is saved
in `%LOCALAPPDATA%\Snapview\appearance.json`; images/annotation colors never follow
the UI theme. Title filename and Image details can copy the current file path;
clipboard images have no path, and photo coordinates have a separate copy action.
Filename single-click copying waits for the Windows double-click interval; a
double-click cancels it and edits the base name in the title bar, selected initially.
Click outside or press Enter to commit an edited name; unchanged text does no file
operation. Esc cancels; Ctrl+Z undoes draft typing, or the committed rename after
leaving the field. Native caption clicks also commit through the window message hook.
Rename preserves extension, bytes, dirty/history state and existing original recovery;
never overwrite a destination. Empty title-bar space retains WindowChrome gestures.
RenameHistory matches scene identity to keep rename/image undo chronological; it
must not retain pixel buffers. New edits clear rename redo; opening/pasting/deleting
or saving to another path clears rename history. Failed moves keep history intact.
Image deletion uses the Recycle Bin, closes the canvas only after success, and
retains original recovery copies. The confirmation defaults to No; remember the
skip checkbox only after Yes and successful deletion. More can restore confirmation.
The preference is in `%LOCALAPPDATA%\Snapview\file-actions.json`.
