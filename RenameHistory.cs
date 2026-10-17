namespace Snapview;

// File names and pixel edits share an undo order without copying/retaining bitmap buffers.
internal sealed class RenameHistory
{
    private sealed record Entry(string Before, string After, WeakReference<ImageScene> Scene);
    private readonly List<Entry> undo = [];
    private readonly List<Entry> redo = [];
    private static bool Matches(Entry entry, ImageDocument document, bool forward) =>
        document.Path == (forward ? entry.Before : entry.After) && entry.Scene.TryGetTarget(out var scene) && document.Scene == scene;
    public bool CanUndo(ImageDocument document) => undo.Count > 0 && Matches(undo[^1], document, false);
    public bool CanRedo(ImageDocument document) => redo.Count > 0 && Matches(redo[^1], document, true);
    public void Record(string before, ImageDocument document)
    {
        if (document.Path == null || document.Scene == null || before == document.Path) return;
        undo.Add(new(before, document.Path, new(document.Scene)));
        if (undo.Count > 150) undo.RemoveAt(0);
        redo.Clear();
    }
    public void ClearRedo() => redo.Clear();
    public void Clear() { undo.Clear(); redo.Clear(); }
    public bool Undo(ImageDocument document) => Move(document, false);
    public bool Redo(ImageDocument document) => Move(document, true);
    private bool Move(ImageDocument document, bool forward)
    {
        var source = forward ? redo : undo; var destination = forward ? undo : redo;
        if (source.Count == 0 || !Matches(source[^1], document, forward)) return false;
        var entry = source[^1];
        string path = forward ? entry.After : entry.Before;
        var result = FileActions.Rename(document.Path!, System.IO.Path.GetFileNameWithoutExtension(path));
        document.Relocate(result.Path);
        // A conflicting destination throws before either the path or the history changes.
        source.RemoveAt(source.Count - 1); destination.Add(entry);
        return result.RecoveryPreserved;
    }
}
