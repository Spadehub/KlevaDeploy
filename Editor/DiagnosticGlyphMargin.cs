using System.Windows;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using KlevaDeploy.ViewModels;

namespace KlevaDeploy.Editor;

public sealed class DiagnosticGlyphMargin : AbstractMargin
{
    private readonly List<ScriptEditorViewModel.EditorDiagnostic> _diagnostics = new();

    protected override Size MeasureOverride(Size availableSize) => new(10, 0);

    public void SetDiagnostics(IEnumerable<ScriptEditorViewModel.EditorDiagnostic> diagnostics)
    {
        _diagnostics.Clear();
        _diagnostics.AddRange(diagnostics);
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);

        if (TextView is null || !_diagnostics.Any())
            return;

        TextView.EnsureVisualLines();
        foreach (var visualLine in TextView.VisualLines)
        {
            var diagnostic = _diagnostics.FirstOrDefault(x => x.Line == visualLine.FirstDocumentLine.LineNumber);
            if (diagnostic is null)
                continue;

            var brush = diagnostic.Severity.Equals("ERROR", StringComparison.OrdinalIgnoreCase)
                ? Brushes.IndianRed
                : Brushes.Goldenrod;
            var centerY = visualLine.VisualTop - TextView.VerticalOffset + (visualLine.Height / 2);
            drawingContext.DrawEllipse(brush, null, new Point(5, centerY), 3, 3);
        }
    }
}
