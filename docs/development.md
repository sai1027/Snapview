# Build, test and extend

## Prerequisites

- Windows 10 or 11, x64. The shipped runtime identifier is `win-x64`; other
  architectures and operating systems are not configured or validated.
- [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0), including
  Windows desktop build support. A runtime alone cannot build the project.
- PowerShell for the helper scripts. Visual Studio with .NET desktop development,
  VS Code, or another C#/XAML editor is optional; the .NET CLI is sufficient.
- Network access for initial NuGet restore, a missing OCR model, and optional codec
  fixtures. Running the viewer and OCR after installation works offline.

Native Tesseract may require the Microsoft Visual C++ 2015–2022 **x64** runtime.
Do not use unofficial DLL download sites if a native loader error occurs.

## First build and run

Run these commands from the source checkout's root:

```powershell
dotnet --info
.\scripts\setup.ps1
dotnet restore .\Snapview.csproj
dotnet build .\Snapview.csproj -c Release
dotnet run --project .\Snapview.csproj -c Release --no-build
# Open a particular file (quote paths containing spaces):
dotnet run --project .\Snapview.csproj -c Release --no-build -- 'C:\Pictures\photo.jpg'
```

The normal build output is `bin\Release\net10.0-windows\win-x64\`. This development
build needs an installed Windows Desktop runtime. The portable publish bundles it.
`App.OnStartup` opens the first existing file argument; the only special CLI flag
currently implemented is `--self-test`.

`setup.ps1` downloads `eng.traineddata` only if it is absent; it does not install the
SDK, replace an existing model or validate its checksum. The checked-in model's
expected SHA-256 is recorded in [third-party notices](../THIRD-PARTY-NOTICES.md):

```powershell
Get-FileHash .\tessdata\eng.traineddata -Algorithm SHA256
```

For an SDK outside PATH, use its full executable path:

```powershell
$sdk = 'C:\Tools\dotnet\dotnet.exe' # replace with your SDK location
& $sdk build .\Snapview.csproj -c Release
.\scripts\test.ps1 -Dotnet $sdk
.\scripts\publish.ps1 -Dotnet $sdk
```

The initial development machine also used an isolated SDK under
`%LOCALAPPDATA%\Snapview\dotnet`; this is a local convenience, not a prerequisite
or a path to hardcode into application code. If directly starting a framework-
dependent apphost cannot find that runtime, use the selected SDK host to run
`Snapview.dll`, or configure `DOTNET_ROOT` for that shell.

## Debugging

Open `Snapview.csproj` in your editor and start a Windows/WPF debug session.
`dotnet build -c Debug` produces a Debug build; `dotnet run -c Debug` starts it.
Useful breakpoints: `App.OnStartup`, `OpenPath` / `LoadPath`, `CanvasDown` /
`CanvasUp`, `Commit`, `SaveInteractive`, `Originals.Find`, and `OcrClick`.

WPF bindings/events, layout, rendering and clipboard access are tied to the UI
thread. Inspect the bitmap's **pixel** dimensions alongside its DPI when diagnosing
size/annotation bugs. Watch `document.Dirty`, history state, `tool`, `busy` and
`metadataRevision` when debugging interaction or stale-information issues.

## Automated verification

```powershell
.\scripts\test.ps1
# Adds HEIC/AVIF decoding checks; downloads upstream fixtures on first use:
.\scripts\test.ps1 -WithCodecFixtures
```

This is an in-app smoke/integration runner, not xUnit/NUnit or `dotnet test`.
The WPF app starts on an STA thread, runs `SmokeTests.Run`, writes results and exits
without opening the main window. The base suite currently has **17** checks;
the optional fixtures add one combined HEIC/AVIF check for **18** total.

| Area | Assertions |
| --- | --- |
| Encoding/decoding | PNG round trip and JPEG/WebP/BMP/TIFF export dimensions |
| Edits | Resize/crop/rotate dimensions, all eight orientation transforms, 96-DPI normalization |
| State / layers | Undo/redo, saved scene identity, grouped gestures, style changes, editable export, layer restoration after raster operations |
| Recovery | Original bytes survive overwrite; external file changes invalidate the backup lookup |
| Metadata | Missing GPS, synthetic GPS coordinates, source EXIF orientation, GPS omitted on export |
| OCR | Bundled English model recognizes generated sample text locally |
| Limits | Invalid/oversized requested dimensions are rejected |
| Optional codecs | Public libheif HEIC and AVIF fixtures decode successfully |

The optional script downloads to `.tools\codec-fixtures` and temporarily sets
`SNAPVIEW_CODEC_FIXTURES` for the test process. `SmokeTests` reads that variable;
without it the optional check is omitted. Fixtures are not shipped with the app.

Results are beside the tested executable:

```text
test-output/results.json    successful checks and elapsed times
test-output/sample.png      generated test image
test-output/ocr.txt          recognized sample text
self-test-error.txt         exception details on failure
```

The runner also writes conversion/recovery fixtures and uses the real local
`Originals` backup store with its own generated files. It does not use your photos.
Check the **process exit code**: 0 means success; 1 means failure. JSON can be partial
after a failure. A successful run removes a previous `self-test-error.txt`.
For a published GUI executable, wait for it explicitly before inspecting its code:

```powershell
$test = Start-Process .\artifacts\Snapview\Snapview.exe -ArgumentList '--self-test' -PassThru -Wait
$test.ExitCode
```

## Manual UI checks

Use generated/sample files or disposable copies, especially for overwrite tests.
Check changed interactions at the 850 × 590 minimum, normal size and maximized:

1. Open/drop/paste, folder navigation and unsaved-change save/discard/cancel.
2. Fit/actual size, zoom/pan, and aligned crop/annotations after zoom or rotation.
3. Type inline; reflow/stretch/move/recolor text; resize shapes; shorten either arrow end; verify the active color ring follows selection/undo; test Undo and Redo buttons.
4. Draw crops in both directions and near image edges; check outside dimming, live pixel dimensions/ratio, Apply, undo and cancel. Verify the bottom status bar in maximized view and after restoring.
5. Save copy in a different format; overwrite a test file; reopen and Revert.
6. Source-vs-canvas details, GPS/no-GPS states, OCR selection/copy and busy controls.
7. Keyboard focus, relevant shortcuts/tooltips and the original empty state.

Do not change personal Windows defaults or remove recovery data just to validate
a patch. Test installation/association behavior in a disposable Windows account or
VM when actual system integration needs verification.

## Adding a feature

Keep small changes within existing responsibilities. Add focused helpers when an
operation grows; do not replace the architecture just to introduce one tool.

- **Image operation:** implement the transform in `Imaging`, validate dimensions,
  return a frozen normalized bitmap, and commit through history. Cover actual
  behavior (orientation, alpha, dimensions or save state), not just method calls.
- **Markup tool:** add immutable geometry/rendering in `Annotations.cs`, interactions
  in `AnnotationEditor.cs`, and contextual controls in XAML. Use `SetAnnotations`,
  group drags/typing into meaningful undo steps, and keep editor chrome out of
  exports. Handle capture loss, image-space coordinates, focus and keyboard access.
- **Codec:** update open extensions/filter, load dispatch, save filter/encoder and
  `CanOverwrite` when writable. Keep associations/uninstall extensions in sync.
  Confirm read/write support separately: Windows HEIC decoding does not imply
  HEIC encoding support. Review native dependencies and notices.
- **OCR language:** add a licensed model with provenance/checksum, ensure publish
  includes it, then expose language selection and pass it to the engine. Current
  code always uses `eng`; adding a file alone does not enable another language.
- **Save/recovery:** preserve original-byte capture, replacement ordering, hash
  validation and cancellation; test failures and reopened recovery as appropriate.
- **UI:** reuse `App.xaml` styles and `Dialogs`, keep controls contextual, and verify
  the minimum size. Update user shortcuts and docs if behavior changes.

Run the relevant checks and state what they cover. Only change dependencies when
needed; review versions, native architecture, added startup work and redistribution
terms. Update docs and `AGENTS.md` when their contracts/source map change.

## Troubleshooting

| Symptom | Check |
| --- | --- |
| SDK/target not found | `dotnet --info`; install/select .NET 10 SDK and build on Windows |
| OCR model missing | Run setup; check `tessdata/eng.traineddata` exists beside the built executable |
| Native OCR load failure | Keep x64 Tesseract/Leptonica DLLs from the output folder; check the official VC++ x64 runtime |
| Codec load/format failure | Keep the full publish folder; confirm the specific decoder/encoder supports that format |
| Build/publish cannot replace files | Close a running executable from that output folder; do not kill an unrelated user session |
| Unexpected Revert availability | Recovery requires the saved file hash to match; a new exported path may only have a session original |
| Tests appear passed but process failed | Read exit code and `self-test-error.txt`; results JSON only lists checks completed before failure |
| Large edit pauses the window | Rendering/export still execute on the UI thread; profile that path before changing architecture |
