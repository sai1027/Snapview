# Developer documentation

Snapview is a local Windows desktop image viewer. This documentation describes the
current implementation, rather than a proposed architecture.

| Guide | Contents |
| --- | --- |
| [Architecture](architecture.md) | Source map, image pipeline, editing state, threading, OCR, metadata and recovery |
| [Development](development.md) | Prerequisites, build/debug, tests, troubleshooting and feature development |
| [Windows distribution](windows-distribution.md) | Portable publish, installation, file associations, uninstall and release checks |
| [Licensing](licensing.md) | MIT scope, third-party components and redistribution responsibilities |

Start with the [user README](../README.md) for features, shortcuts and limitations.
Coding agents should read the root `AGENTS.md` before editing the repository.

## Technology stack

| Layer | Technology / version | Use |
| --- | --- | --- |
| Language | C#, compiler default supplied by .NET 10 SDK | Application and imaging logic |
| Runtime / target | .NET 10; `net10.0-windows`; `win-x64` | Windows x64 application; `WinExe` |
| UI | WPF / XAML, Windows desktop SDK | Native window, controls, canvas, dialogs, clipboard and input |
| Fast image path | Windows Imaging Component through WPF `BitmapDecoder` | JPEG, PNG, BMP, GIF, TIFF, ICO |
| Extended codecs | `Magick.NET-Q8-x64` **14.16.0** | WebP/HEIC/HEIF/AVIF opening; non-PNG export |
| Metadata | `MetadataExtractor` **2.9.3** | Camera EXIF and GPS; transitive XmpCore dependency |
| OCR | `Tesseract` **5.2.0** .NET wrapper + native OCR/Leptonica | On-demand English text recognition |
| OCR data | `tessdata_fast/eng.traineddata` | Bundled model for offline recognition |
| Persistence | Files + `System.Text.Json` + SHA-256 | Original-byte backups and recovery index |
| Windows integration | `Microsoft.Win32.Registry`, Shell notification, Settings URI | Per-user Open With/default-app registration |
| Build / distribution | .NET CLI / MSBuild + PowerShell scripts | Self-contained ReadyToRun folder, Start menu shortcut |
| Tests | In-app `SmokeTests` runner on WPF's STA thread | Real rendering, codecs, history, recovery, metadata and OCR |

`Snapview.csproj` is the source of truth for direct dependency versions and build
properties. Restore resolves transitive packages into `obj/project.assets.json`;
there is currently no committed SDK version pin or NuGet lock file.

## Design boundaries

The application uses code-behind and small internal service classes. There is no
server, database, web UI, dependency-injection container, cloud account or telemetry.
Folder navigation enumerates image filenames in the selected directory; it does not
index a library. New file activations currently create separate processes/windows.

The quiet light palette and common control styles are in `App.xaml`. Layout and
window chrome are in `MainWindow.xaml`. Use the existing styles and tooltips when
adding controls; keep advanced controls contextual to the current image/tool.
`Assets/Snapview.drawing.xml` is the native vector icon source; its small `Snapview-mark.png`
derivative supplies the colorful title-bar mark, and `Assets/Snapview.ico` supplies
the executable/window icon. The mark and ICO are embedded. Asset generation details
and ICO rebuild instructions are in `Assets/README.md` in the source checkout.

Viewing needs no application network access. Development setup/restore and optional
test fixtures use the network. The explicit map action opens OpenStreetMap with
the source file's coordinates. OCR runs on the user's device.
