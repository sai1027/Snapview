using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace Snapview;

// Converts WPF text runs to immutable image-space styles; no UI objects enter history.
internal static class AnnotationText
{
    public static string PlainText(RichTextBox editor)
    {
        string text = new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text;
        if (text.EndsWith("\r\n")) text = text[..^2]; // WPF's mandatory final paragraph marker
        return text.Replace("\r\n", "\n");
    }
    public static TextPointer At(RichTextBox editor, int index)
    {
        var point = editor.Document.ContentStart;
        while (point != null)
        {
            int offset = new TextRange(editor.Document.ContentStart, point).Text.Replace("\r\n", "\n").Length;
            if (offset >= index && point.IsAtInsertionPosition) return point;
            point = point.GetNextInsertionPosition(LogicalDirection.Forward);
        }
        return editor.Document.ContentEnd;
    }
    public static (int Start, int Length) Selection(RichTextBox editor)
    {
        int start = new TextRange(editor.Document.ContentStart, editor.Selection.Start).Text.Replace("\r\n", "\n").Length;
        int end = new TextRange(editor.Document.ContentStart, editor.Selection.End).Text.Replace("\r\n", "\n").Length;
        return (start, end - start);
    }
    public static void Load(RichTextBox editor, Annotation annotation, int start = 0, int length = 0)
    {
        var paragraph = new Paragraph { Margin = new Thickness(0) };
        int index = 0;
        while (index < annotation.Text.Length)
        {
            var style = annotation.AppearanceAt(index);
            int end = index + 1;
            while (end < annotation.Text.Length && annotation.AppearanceAt(end) == style) end++;
            paragraph.Inlines.Add(new Run(annotation.Text[index..end]) { Foreground = new SolidColorBrush(style.Color), FontSize = style.FontSize,
                FontWeight = style.Bold ? FontWeights.Bold : FontWeights.Normal, FontStyle = style.Italic ? FontStyles.Italic : FontStyles.Normal });
            index = end;
        }
        var document = new FlowDocument(paragraph) { PagePadding = new Thickness(0), FontFamily = new FontFamily("Segoe UI"),
            FontSize = annotation.FontSize, Foreground = new SolidColorBrush(annotation.Color),
            FontWeight = annotation.Bold ? FontWeights.Bold : FontWeights.Normal, FontStyle = annotation.Italic ? FontStyles.Italic : FontStyles.Normal };
        document.Resources.Add(typeof(Paragraph), new Style(typeof(Paragraph)) { Setters = { new Setter(Block.MarginProperty, new Thickness(0)) } });
        editor.Document = document;
        int left = Math.Clamp(start, 0, annotation.Text.Length), right = Math.Clamp(start + length, left, annotation.Text.Length);
        editor.Selection.Select(At(editor, left), At(editor, right));
    }
    public static Annotation Read(RichTextBox editor, Annotation original)
    {
        var text = new StringBuilder();
        var styles = new List<TextStyleRange>();
        void Append(string value, Color color, double size, bool bold, bool italic)
        {
            if (value.Length == 0) return;
            int start = text.Length; text.Append(value);
            if (styles.LastOrDefault() is { } last && last.Color == color && last.FontSize == size && last.Bold == bold && last.Italic == italic)
                styles[^1] = last with { Length = last.Length + value.Length };
            else styles.Add(new(start, value.Length, color, size, bold, italic));
        }
        void Inlines(InlineCollection inlines)
        {
            foreach (var inline in inlines)
                if (inline is Run run) Append(run.Text.Replace("\r\n", "\n"), (run.Foreground as SolidColorBrush)?.Color ?? original.Color, run.FontSize,
                    run.FontWeight >= FontWeights.SemiBold, run.FontStyle != FontStyles.Normal);
                else if (inline is Span span) Inlines(span.Inlines);
                else if (inline is LineBreak line) Append("\n", (line.Foreground as SolidColorBrush)?.Color ?? original.Color, line.FontSize,
                    line.FontWeight >= FontWeights.SemiBold, line.FontStyle != FontStyles.Normal);
        }
        foreach (var paragraph in editor.Document.Blocks.OfType<Paragraph>())
        {
            if (text.Length > 0 || paragraph != editor.Document.Blocks.FirstBlock) Append("\n", original.Color, original.FontSize, original.Bold, original.Italic);
            Inlines(paragraph.Inlines);
        }
        return original with { Text = text.ToString(), TextStyles = styles.ToArray() };
    }
}
