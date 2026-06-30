using System.Windows.Media;
using System.Windows;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Rendering;
using KlevaDeploy.ViewModels;

namespace KlevaDeploy.Editor;

public sealed class DiagnosticLineHighlighter : IBackgroundRenderer
{
    private readonly List<ScriptEditorViewModel.EditorDiagnostic> _diagnostics = new();

    public KnownLayer Layer => KnownLayer.Selection;

    public void SetDiagnostics(IEnumerable<ScriptEditorViewModel.EditorDiagnostic> diagnostics)
    {
        _diagnostics.Clear();
        _diagnostics.AddRange(diagnostics);
    }

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (!_diagnostics.Any() || textView.Document is null)
            return;

        textView.EnsureVisualLines();
        var lines = textView.VisualLines;
        if (lines.Count == 0)
            return;

        foreach (var diagnostic in _diagnostics)
        {
            var line = lines.FirstOrDefault(x => x.FirstDocumentLine.LineNumber == diagnostic.Line);
            if (line is null)
                continue;

            var lineLength = Math.Max(1, line.FirstDocumentLine.Length);
            var startOffset = Math.Min(
                line.FirstDocumentLine.Offset + Math.Max(0, diagnostic.Column - 1),
                line.FirstDocumentLine.Offset + lineLength - 1);
            var endOffset = Math.Min(
                startOffset + Math.Max(1, diagnostic.Length),
                line.FirstDocumentLine.Offset + lineLength);

            var rect = BackgroundGeometryBuilder.GetRectsForSegment(
                    textView,
                    new TextSegment
                    {
                        StartOffset = startOffset,
                        EndOffset = endOffset
                    })
                .FirstOrDefault();

            if (rect.IsEmpty)
                continue;

            var brush = diagnostic.Severity.Equals("ERROR", StringComparison.OrdinalIgnoreCase)
                ? new SolidColorBrush(Color.FromArgb(45, 220, 38, 38))
                : new SolidColorBrush(Color.FromArgb(35, 245, 158, 11));
            brush.Freeze();

            var pen = diagnostic.Severity.Equals("ERROR", StringComparison.OrdinalIgnoreCase)
                ? new Pen(new SolidColorBrush(Color.FromArgb(170, 220, 38, 38)), 1)
                : new Pen(new SolidColorBrush(Color.FromArgb(140, 245, 158, 11)), 1);
            pen.Freeze();

            var highlightRect = new Rect(rect.Left, rect.Top, Math.Max(rect.Width, 8), Math.Max(rect.Height, 18));
            drawingContext.DrawRectangle(brush, pen, highlightRect);
        }
    }
}
