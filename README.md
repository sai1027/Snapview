# Snapview

A native Windows image viewer inspired by the simplicity of macOS Preview. A quiet canvas, essential edits, local text extraction, and no photo library or background jobs.

Developer documentation: [docs/README.md](docs/README.md). Coding-agent orientation: [AGENTS.md](AGENTS.md).

## Run

Windows 10/11, x64. The portable release contains its own .NET runtime:

```powershell
.\artifacts\Snapview\Snapview.exe
# Or open a particular image:
.\artifacts\Snapview\Snapview.exe 'C:\Pictures\photo.jpg'
```

For a stable installation, run `artifacts\install.ps1`. It copies the app to `%LOCALAPPDATA%\Programs\Snapview` and adds a Start menu shortcut. Administrator access is not needed. Keep the whole app folder together; OCR and extended image codecs use files beside the executable.

## Make it the default viewer

1. Install to a stable location first.
2. In Snapview, choose **… → Set as default viewer**.
3. In Windows Settings, find Snapview and assign your preferred image extensions.

Alternatively, right-click an image in Explorer → **Open with → Choose another app → Snapview** (browse to `Snapview.exe` if necessary) and choose **Always**. Repeat for the extensions you use.

Snapview registers its capabilities under the current user's registry keys. It does not overwrite the protected Windows `UserChoice` settings or silently change another app's defaults. [Microsoft's default apps platform](https://learn.microsoft.com/en-us/windows/apps/develop/windows-integration/default-apps-platform) describes this user-controlled flow.

## Features

| Action | How |
| --- | --- |
| Open | Open button, drag and drop, `Ctrl+O`, Explorer file activation |
| Browse a folder | Left/right arrows; files are ordered by name |
| Zoom | Mouse wheel, `+` / `−`; `Ctrl+0` fits and `Ctrl+1` shows actual size |
| Pan | Drag in viewing mode, middle drag, or Space+drag while marking up |
| Crop | `C`, drag a rectangle; outside pixels dim and live dimensions/ratio appear; Enter or Apply crop; Esc cancels |
| Resize | Resize toolbar button; proportions can be locked |
| Rotate | `R`, clockwise in 90° steps |
| Annotate | Markup: pen, rectangle, ellipse, arrow, text; click an object to move, resize or recolor it |
| Add text | Add text or `T`, click the image and type directly; font size and color are in the markup bar |
| Style selected words | Highlight text, then choose a color or font size; only the selection changes. No highlighted text styles the whole box |
| Edit text layout | Drag Move to reposition; side handles change wrapping; corners stretch/squash; double-click or `F2` edits text |
| Edit arrow | Drag the head/tail handles independently to redirect or shorten it |
| Select / remove | `V` selects; Delete removes the selected object; arrow keys nudge (Shift = 10 pixels) |
| Extract text | Extract text button with scan icon or `Ctrl+Shift+C`; select or copy recognized text |
| Details | `Ctrl+I`: file size, format, resolution, camera EXIF and GPS when present |
| Copy path | Click the title-bar filename once, or Copy image path in Image details; GPS details can also copy coordinates |
| Appearance | … → Appearance → Light / Dark; your choice is remembered |
| Map | View on map opens the source coordinates in OpenStreetMap in your browser |
| Save | `Ctrl+S` replaces supported files; Save copy or `Ctrl+Shift+S` converts/exports |
| Undo / redo | Toolbar arrows; `Ctrl+Z` / `Ctrl+Y` (also `Ctrl+Shift+Z` for redo) |
| Recover | … → Revert to original |
| Maximize | Title bar or `F11`; dimensions, format, size and zoom stay in the bottom bar |
| Clipboard | `Ctrl+V` opens a copied image; `Ctrl+C` copies the edited image |

**Opening:** JPEG, PNG, BMP, GIF, TIFF and ICO use Windows imaging. WebP, HEIC/HEIF and AVIF use the bundled ImageMagick codecs on demand. Unsupported or malformed files show an error. **GIF, multipage TIFF and ICO show their first frame only; exported edits are still images.** Animated playback and page browsing are outside this version.

**Exporting:** PNG, JPEG (92% quality), WebP (92% quality), BMP and TIFF. PNG is lossless. JPEG/BMP flatten transparency against white. Exports contain the visible edits and omit camera/GPS metadata. Metadata shown before saving belongs to the source file; resolution belongs to the current canvas. Save a copy if you want to retain an untouched source with its metadata.

**Revert:** the originally opened image remains available during the session. Before overwriting an existing file, Snapview stores its original bytes in `%LOCALAPPDATA%\Snapview\Originals`. These recovery copies also work after reopening the app. A SHA-256 fingerprint prevents applying a recovery copy to a file subsequently changed by another program. Revert loads the original into the canvas; save to persist the restoration. Copies exported to a new path can revert to their session original while that session is open. Recovery copies are retained until you remove them; uninstall preserves them.

**Text extraction:** the bundled English Tesseract model runs locally and starts only on request. No images are uploaded. Large images are reduced to a maximum of 4,000 pixels per side for OCR. Recognition quality depends on the text, language and image. This release supports English recognition; it is an explicit extraction dialog rather than selectable text over the photograph.

**Editable annotations:** new text, shapes, arrows and pen strokes remain separate objects in the current session, including after Save. Click to select, drag to move, and use handles to reshape them. Text edits happen inline; there is no annotation text dialog. Press Esc or Ctrl+Enter to finish typing, then use selection handles. Reflow handles preserve letter proportions; corner/top/bottom handles stretch the letters. Undo/redo tracks layer edits and groups each drag as one change.

**Export and geometry limits:** standard image exports contain flattened pixels, with no project file/sidecar storing editable objects. Reopening an export cannot reselect those annotations, including annotations made by earlier versions. Whole-image crop/resize/rotate currently merge layers into the resulting pixels; undo restores the previous editable scene. Keep the session open until you have finished adjusting objects.

**Other limits:** images up to 64 MP and 32,000 pixels per side; history is capped at 150 states and approximately 192 MiB of distinct base bitmaps with a one-state floor. Current/original/saved images and transient buffers add memory. Zoom has a 1% minimum. HDR/color-critical editing, touch gestures and printing are not included.

There are no library scans, filters, retouching, background removal, cloud sync, telemetry, or app network requests during image viewing. The map action deliberately opens a browser with the image coordinates. CPU-intensive OCR/conversion is user initiated; image loading and OCR run off the UI thread. Rendering edits and writing exports currently run on the UI thread, so unusually large edits or exports can briefly pause the interface.

## Build and verify

Install the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), then:

```powershell
.\scripts\setup.ps1
dotnet build -c Release
# The self-test process writes test-output/results.json beside the executable.
# Check its process exit code: 0 is success; 1 writes self-test-error.txt.
dotnet run -c Release --no-build -- --self-test
# Optional decoder tests download public fixtures from the libheif project:
.\scripts\test.ps1 -WithCodecFixtures
.\scripts\publish.ps1
```

The test runner exercises the production imaging pipeline: PNG loading, crop/resize/rotate, all five output formats, saved/unsaved undo transitions, persistent original recovery, source change detection, metadata, size limits and offline OCR. UI checks are separate.

Publish produces a self-contained, ReadyToRun Windows x64 folder. Native WPF avoids embedding Chromium, a browser server or an always-on worker. The runtime, codecs and OCR model still add distribution size. Startup performance should be measured on target hardware; there is no universal startup time guarantee.

`scripts\publish.ps1 -Dotnet 'C:\path\to\dotnet.exe'` can use a locally installed SDK. Run `artifacts\uninstall.ps1` to remove a per-user installation and Snapview's registrations. It preserves image recovery copies and never deletes user images.

## License

Snapview-owned code, documentation and original assets are [MIT licensed](LICENSE):
fork, modify, redistribute or sell them while retaining the license notice.
Third-party components retain their own licenses. See
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md) and the
[licensing guide](docs/licensing.md), including native codec redistribution terms.
