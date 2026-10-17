# Windows packaging and installation

## Publish

From the source root, with the .NET 10 SDK:

```powershell
.\scripts\setup.ps1
.\scripts\publish.ps1
# Select a particular SDK when necessary:
.\scripts\publish.ps1 -Dotnet 'C:\Tools\dotnet\dotnet.exe'
```

The script uses `dotnet publish -c Release -r win-x64 --self-contained true
-p:PublishReadyToRun=true`. It produces a **folder**, not a single-file executable,
MSIX, MSI, signed installer or Store submission. ReadyToRun reduces some JIT work;
it is not NativeAOT or an assurance of a particular startup time. No code-signing,
automatic updating or CI release workflow is configured.

Expected layout:

```text
artifacts/
  install.ps1
  uninstall.ps1
  README.md
  LICENSE
  THIRD-PARTY-NOTICES.md
  docs/
  licenses/
  Snapview/
    Snapview.exe
    Snapview.dll
    Snapview.deps.json
    Snapview.runtimeconfig.json
    ... .NET, WPF, managed/native codec and OCR dependencies
    tessdata/eng.traineddata
    README.md
    LICENSE
    THIRD-PARTY-NOTICES.md
    docs/
    licenses/
```

Project content items copy the model, docs and license files into build/publish
outputs. The script additionally places setup wrappers and documentation at the
artifact root. Keep the complete `Snapview` directory together: copying only the
EXE loses its runtime, native libraries and OCR data. The self-contained publish
does not require a separately installed .NET runtime, but native OCR can still
require Microsoft's VC++ x64 redistributable.

Publish destinations are reused without automatic cleanup. For a release, use a
fresh staging folder so obsolete native DLLs/docs from earlier versions are not
accidentally distributed. For example, after a build:

```powershell
dotnet publish .\Snapview.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishReadyToRun=true -o .\.tools\release-staging
```

Use a new destination for subsequent releases, or clean only an explicitly verified
generated directory. Direct `dotnet publish` includes the application's content
but does not create the artifact-root install/uninstall wrapper layout.

## Portable use and per-user install

```powershell
.\artifacts\Snapview\Snapview.exe
.\artifacts\Snapview\Snapview.exe 'C:\Pictures\photo.jpg'
# Install from the generated wrapper layout:
.\artifacts\install.ps1
```

`install.ps1` copies the app directory to `%LOCALAPPDATA%\Programs\Snapview` and
creates `Snapview.lnk` under the current user's Start menu Programs folder using
`WScript.Shell`. It needs no elevation, does not register defaults automatically,
and is not a transactional installer. Close an installed copy before updating;
copying over locked files can fail, and old files are not automatically pruned.

Do not hardcode a developer's checkout location into shortcuts or registry commands.
For portable builds, registration points to the current executable location; moving
it later requires registering the new path again. Install to a stable location
before choosing defaults.

## Default image viewer

Choose **… → Set as default viewer** in the installed app. `Associations.Register`
uses the current executable's absolute path, quoted along with the `%1` argument,
and writes these current-user keys:

| Key below `HKEY_CURRENT_USER` | Purpose |
| --- | --- |
| `Software\Classes\Applications\Snapview.exe` | Friendly app name, open command and supported types |
| `Software\Classes\Snapview.Image` | ProgID description, executable icon and open command |
| `Software\Classes\<extension>\OpenWithProgids` | Advertises `Snapview.Image` for each supported extension |
| `Software\Snapview\Capabilities` | App description and file-association capabilities |
| `Software\RegisteredApplications` value `Snapview` | Points Windows to those capabilities |

The app sends `SHChangeNotify(SHCNE_ASSOCCHANGED)` and launches
`ms-settings:defaultapps?registeredAppUser=Snapview`. Windows then lets the user
choose the extensions. Registration does **not** force a default or edit protected
Explorer `UserChoice` keys. See Microsoft's [default apps platform](https://learn.microsoft.com/en-us/windows/apps/develop/windows-integration/default-apps-platform).

Explorer's **Open with → Choose another app → Always** is an alternative.
Supported extensions are defined by `Imaging.Extensions`; the uninstall script
currently maintains a separate list that must be updated when that list changes.

## Local data and removal

| Location | Contents / lifetime |
| --- | --- |
| `%LOCALAPPDATA%\Programs\Snapview` | Per-user installed executable, dependencies, docs and notices |
| `%LOCALAPPDATA%\Snapview\Originals` | Original image bytes and recovery JSON; retained across sessions/uninstall |
| Output folder's `test-output` | Generated only when `--self-test` is run there |

Run `artifacts\uninstall.ps1` from the generated layout after closing Snapview.
It removes the expected per-user application directory, Start menu shortcut and
Snapview's registrations/Open With values. It never removes user images or original
recovery copies. Windows may require the user to choose another default viewer.
It does not remove arbitrary portable copies or the optional development SDK.

## Release verification

- Run the relevant [pipeline and UI checks](development.md) and inspect the fresh
  publish output on a clean Windows x64 environment.
- Confirm startup, image activation with a path containing spaces, each advertised
  codec, local OCR and missing-native-dependency errors.
- Verify install/uninstall and user-selected default associations in a disposable
  Windows account/VM. Check that recovery originals survive uninstall.
- Include `LICENSE`, `THIRD-PARTY-NOTICES.md`, `licenses/`, the model and all required
  DLLs; resolve the [third-party source/linkage obligations](licensing.md) for the
  exact binaries being redistributed. The publish script copies notices but does
  not collect corresponding codec source or establish full distribution compliance.
- Record the SDK, dependency versions and release hashes. Optional test fixtures,
  `test-output`, personal photos and development SDKs are not release payloads.
