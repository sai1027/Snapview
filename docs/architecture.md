# Architecture and data flow

## Source responsibilities

| File | Responsibility |
| --- | --- |
| `App.xaml` | Shared colors, typography and control templates |
| `App.xaml.cs` | Starts the main window, accepts the first existing file argument, dispatches `--self-test` |
| `MainWindow.xaml` | Title bar, toolbar, contextual markup controls, scrollable canvas, details panel, status bar |
| `MainWindow.xaml.cs` | Input/gestures, display scale, command routing, busy state, dialogs and file workflow |
| `Imaging.cs` | `ImageDocument`, codec/transform helpers, metadata extraction and persistent original recovery |
| `Annotations.cs` | Immutable object geometry/text, scene snapshots and lazy compositing |
| `AnnotationEditor.cs` | Inline text, object hit-testing, move/resize/end handles and gesture grouping |
| `AnnotationText.cs` | Rich editor/model conversion and newline-aware selection offsets |
| `Theme.cs` | Light/dark resource palette, native dialog caption theme and saved appearance |
| `FileActions.cs` | Validated rename, recovery reindexing, Recycle Bin deletion and delete preference |
| `RenameHistory.cs` | Rename undo/redo at the correct scene-history position without retaining bitmaps |
| `CropPreview.cs` | UI-only crop shading and live dimensions/ratio; shared pixel bounds for the actual crop |
| `WindowBounds.cs` | Native monitor work-area handling for borderless maximization |
| `Dialogs.cs` | Dimension entry/proportion locking and extracted-text selection/copy |
| `Associations.cs` | Per-user Windows file-handler capabilities and Shell change notification |
| `SmokeTests.cs` | In-process checks against production imaging and persistence code |

## Opening and viewing

1. The Open button, drop handler, folder navigation or startup file argument calls
   `OpenPath`. Unsaved edits must be saved, discarded or explicitly canceled first.
2. `LoadPath` uses `Work` to guard concurrent work and runs decoding through
   `Task.Run`. Standard formats use WPF/WIC with `BitmapCacheOption.OnLoad` so the
   source stream can close. Extended formats use Magick.NET and an in-memory PNG.
3. `Imaging.CheckSize` rejects dimensions over 32,000 per side or 64 million pixels.
   The extended path probes dimensions before decoding; the WIC path checks after
   obtaining the decoded first frame. These are application limits, not a complete
   hostile-input sandbox or a guarantee of low peak decoder memory use.
4. EXIF rotation/mirroring is applied. `Normalize` copies pixels into a frozen
   96-DPI `Pbgra32` bitmap. Original print DPI is not retained in the editing canvas.
5. `ImageDocument.Load` establishes the original/current/saved state. The selected
   folder's matching filenames are cached in case-insensitive ordinal name order.
6. `Refresh` updates controls and status. A `ScaleTransform` on `ImageHost` handles
   zoom; the `ScrollViewer` handles pan. Fit does not enlarge beyond 100%; manual
   zoom is clamped to 1–1,600%. Display zoom does not resample the stored bitmap.

Only the first frame/page of GIF, TIFF and ICO is loaded. Standard Windows codecs
and extended native codecs can fail on unsupported/malformed files; `Work` reports
load failures while retaining the previously opened document if decoding failed.

## Document state and rendering edits

`ImageDocument` owns an immutable `ImageScene`: a frozen base bitmap plus an array
of immutable `Annotation` records. Saved/undo/redo state uses scene identity rather
than comparing pixels. A clipboard document starts dirty. `BaseImage` is the fast
display/dimension path. `Current` lazily produces a composited bitmap for save,
OCR and clipboard copying; do not read it on every pointer move. Composite caches
are released when moving scenes into history.

Object records hold an ID, kind, box, color, stroke width, text/font/stretch values,
immutable `TextStyleRange` arrays (UTF-16 start/length, color, font size, bold and italic),
arrow endpoints and freehand points as appropriate. Replace records/arrays instead
of mutating them. `SetAnnotations` commits scene edits; subsequent updates within
the same drag/typing group coalesce into that gesture's undo step. A saved scene
always establishes an undo boundary. Raster operations still use `Commit`.
History keeps up to 150 undo states, with an approximately 192 MiB budget counting
distinct shared base bitmaps once and a one-state floor. Other images, vector data,
redo, original/saved references and temporary/native buffers add memory.

`Photo` displays the base bitmap. `AnnotationEditor` draws live objects above it
and places UI-only text editing/selection controls on its canvas. The separate
`Overlay` canvas is visible only for crop selection. All coordinates are image
pixels, under the common display transform. Selection handles maintain a roughly
constant screen size as zoom changes. Export draws model objects through
`ImageScene.Composite`; it never captures the editor RichTextBox, handles or borders.

Text is entered directly in an on-image RichTextBox. Esc or Ctrl+Enter finishes typing;
double-click or F2 re-enters it. The blue Move handle repositions it. Left/right
handles change the wrap width while preserving glyph proportions. Corners and
top/bottom handles stretch the letters independently. Color/font controls update
highlighted characters while editing. Bold/Italic buttons sit beside the font input;
Ctrl+B / Ctrl+I while typing use the same scene history as those buttons. Without a highlighted range they style the
whole object, preserving other color/font differences. The font value is measured
in image pixels. The editor retains its selection when toolbar controls take focus.
`AnnotationText` converts WPF runs into immutable ranges and restores the selection
when styles/history refresh. Newlines are normalized to LF; WPF's mandatory final
paragraph marker is excluded. Paste accepts plain text and the 5,000-character
limit remains enforced. The editor's own undo stack is disabled in favor of scene
history. Export applies the ranges to `FormattedText`, including mixed sizes/colors,
font weights and font styles. The editor preserves all four properties when any
one property changes; reflow/stretch/move preserve the style ranges.

Pen strokes, rectangles and ellipses can be selected, moved, recolored and resized.
Arrow head/tail handles independently change direction/length; dragging the line
or Move handle repositions the whole arrow. Arrow keys nudge selected objects by
one pixel, Shift by ten; Delete removes an object. Blank canvas in Select mode
clears selection; Space+drag pans while editing.

Save leaves session objects editable, but PNG/JPEG/WebP/BMP/TIFF contain flattened
pixels with no editable project/sidecar file. Reopening those exports, or images
annotated by earlier versions, cannot recover their individual objects. Whole-image
crop/resize/rotate currently composite layers into the resulting pixels; undo
restores the previous editable scene. Selection alone never changes dirty state.

## Crop, history controls and window layout

Crop selection keeps its original colors while an even-odd mask dims the surrounding
image. A badge shows the exact export dimensions and approximate aspect ratio.
`CropPreview.PixelBounds` rounds outward to integer image pixels and is shared by
the preview and Apply. Selection strokes and badge type remain constant in screen
size as zoom changes. These visuals are never included in the saved bitmap.

Markup colors are radio controls with a persistent selection ring, synchronized
with the selected object's color, including after undo/redo. Undo and Redo have
separate toolbar buttons, enabled from document history. `WindowBounds` handles
`WM_GETMINMAXINFO` using the current monitor's work area in native pixels so the
custom-chrome window keeps its status bar visible above the taskbar when maximized.

## Threading and responsiveness

`MainWindow.Work` serializes guarded load/OCR operations with the `busy` flag,
disables editing, reports errors, and resets state in `finally`. It is a UI
coordinator, not a queue or cancellation system; additional guarded work is ignored
while busy and window closing is canceled while that work is active.

Decoding, filename enumeration and OCR run on worker threads. Frozen bitmap results
can cross threads. Metadata reading is asynchronous and uses path plus revision
checks to avoid displaying results for an outdated document. WPF visual creation,
input, layout, rendering edits and dialogs stay on the UI/STA thread. Export and
parts of backup creation/hashing currently execute synchronously during save;
large edits/exports can pause the interface. Do not move WPF visuals into `Task.Run`
when improving this path: hand off immutable pixels/bytes or use a suitable STA
rendering design, then marshal UI state changes back to the dispatcher.

## Saving and original recovery

`SaveInteractive` selects a writable output or opens a Save dialog. PNG goes through
WPF's PNG encoder. JPEG/WebP/BMP/TIFF go through Magick.NET; quality is 92 for
JPEG/WebP. JPEG/BMP remove alpha over white. Exports are freshly encoded pixels,
with source camera/GPS metadata omitted (`Strip` on the extended path).

For an existing destination, `Originals.Capture` first copies its original bytes,
or reuses a valid earlier recovery copy. `Imaging.Save` writes a unique temporary
file beside the destination and then replaces it with `File.Move(..., true)`.
A failed encode therefore leaves the existing destination intact; the temporary
file is cleaned up. This is a same-volume replacement strategy, not a transaction
covering the image, backup and recovery index together or a power-loss guarantee.

Recovery state lives under `%LOCALAPPDATA%\Snapview\Originals`:

- `<guid><source-extension>`: original bytes, including original metadata.
- `<SHA256-of-uppercase-absolute-path>.json`: `BackupPath` and `SavedHash`.

After a successful image save, `Originals.Record` stores the new destination's
SHA-256. `Find` returns a backup only if the indexed backup exists and the current
file matches that saved hash. An external modification invalidates the lookup;
old backup files remain on disk. Index write failures are reported separately from
the already successful image save. The JSON index itself is written directly.

Revert loads a valid persistent backup when available, otherwise the session's
`Original`, and commits it as another undoable edit. The user must save to persist
the restored pixels. A newly exported copy has no persistent backup unless an
existing destination was replaced; its session original remains available until
the session is replaced/closed. There is no recovery quota or automatic cleanup.

## Metadata, GPS and OCR

The title filename is a chrome-aware button with a full-path tooltip. A single click
copies its current full path after the Windows double-click interval. Double-click
cancels that pending copy and shows a TextBox in the title bar with the base name
selected. The extension is displayed separately and retained. Clicking outside or
pressing Enter commits changed text; an unchanged name does no file operation.
Esc cancels. Ctrl+Z in the field uses native TextBox undo for the draft. No rename
window is created. Native TextBox selection/caret behavior handles whole-name
replacement and character editing. Empty title space keeps WindowChrome
double-click maximization. Image details also offers Copy image path; clipboard-only images
have no file path. GPS coordinates have a separate Copy coordinates action. Copy
actions stay local and report clipboard errors without changing the image.

## Rename and delete

`FileActions.RenameDestination` rejects invalid/reserved Windows base names and keeps
the original directory/extension. `Rename` never overwrites another file. It moves
the source bytes without encoding, then reindexes an existing valid `Originals`
recovery entry under the new path. Failure to reindex is reported separately from
the successful rename; existing recovery files/entries are retained. The window
calls `ImageDocument.Relocate`, preserving scene identity, dirty state, original,
layers and undo/redo; folder navigation is resorted around the new name.

The inline draft commits on outside pointer input, lost focus, deactivation or
Enter. A native non-client mouse hook covers caption clicks, which WPF's preview
mouse event does not receive. Invalid names and collisions leave the editor open
and show an in-window message. Esc discards a draft without touching the file.

After commit, Ctrl+Z / Undo moves the file back; Ctrl+Y / Redo reapplies the rename.
`RenameHistory` stores before/after paths with weak references to the scene at each
rename. Scene identity orders file operations around pixel/layer edits; it does not
capture bitmap buffers or add image dirty state for a file-only rename. A collision
leaves the file and pending history entry intact. New edits discard rename redo;
opening/pasting/deleting or saving to a different path resets file-name history.

The file-delete toolbar button and plain Delete with no selected annotation share
the same file workflow. A selected annotation takes priority; Delete/Backspace remove
that object through scene history. Text fields retain their own Delete behavior,
clipboard images have no disk-delete action, and held Delete repeats are ignored.
`Dialogs.DeleteImage` shows the requested Yes/No prompt,
with No as the default and Esc/window-close canceling. Its checkbox is persisted
only after Yes and successful deletion. **… → Confirm before deleting images**
can restore confirmation. The boolean lives in `file-actions.json` under the local
Snapview settings folder; missing/invalid settings keep confirmation enabled.

Deletion sends the file to the Windows Recycle Bin through the Shell and then
clears the deleted document, history and metadata. Missing files are pruned from the
cached folder list; the next image in case-insensitive filename order opens, or the
previous image if there is no next one. If no images remain, the canvas returns to
the open screen. Neighbor decoding uses the same busy guard as deletion. A failed
or canceled file operation keeps the loaded image. Confirmed deletion discards
unsaved edits; when confirmation is disabled, the toolbar action acts immediately.
Undo cannot recover a deleted file: restore it from the Recycle Bin. Persistent
original recovery copies are kept. Windows may show its own errors/cancellation for
files or locations it cannot recycle normally.

`MetadataReader` uses MetadataExtractor to select camera tags and parse GPS
latitude/longitude. The panel labels source-file properties separately from current
canvas dimensions. A map button appears only when coordinates exist and opens an
OpenStreetMap URL in the user's browser. It does not reverse-geocode in the app.

OCR takes the current edited bitmap, scales its longest side down to at most 4,000
pixels, encodes PNG, and starts a short-lived Tesseract engine in `LstmOnly` mode
with the bundled English model. Native objects are disposed after recognition.
The result opens in a selectable/copyable dialog. No OCR engine runs at startup;
there is no text overlay selection, model download or additional-language picker.

## Appearance

**… → About Snapview** opens a themed dialog with the running assembly's informational
version (without the source-revision suffix). The project file remains the version source;
the UI does not hardcode a separate version number. Enter or Esc closes the dialog.

`Theme.Load` reads a boolean from `%LOCALAPPDATA%\Snapview\appearance.json`, with
Light as the default/fallback. **… → Appearance → Light / Dark** replaces semantic
brush resources immediately and saves the preference. UI brushes must use
`DynamicResource` / `SetResourceReference`, including contextual controls, menus
and input fields. Rich annotation text and image pixels deliberately keep their
own colors. Owned WPF dialogs request dark native captions where Windows supports
them; Windows file pickers and system message boxes follow Windows' own appearance.

## Extension boundaries

Add image operations in `Imaging`, and route completed raster results through document history.
Add object geometry/rendering in `Annotations.cs` and interactions in `AnnotationEditor.cs`;
route immutable object replacements through `SetAnnotations`. Keep command/UI coordination in the window and reusable prompts in
`Dialogs`. New codecs also affect `Imaging.Extensions`, `OpenFilter`, load/export
dispatch, `CanOverwrite`, Save dialog filters, file associations, uninstall cleanup,
tests and licensing. See the feature workflow in [development.md](development.md).
