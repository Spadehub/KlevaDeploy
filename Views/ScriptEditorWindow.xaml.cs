using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ICSharpCode.AvalonEdit.CodeCompletion;
using ICSharpCode.AvalonEdit.Document;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Rendering;
using KlevaDeploy.Editor;
using KlevaDeploy.ViewModels;

namespace KlevaDeploy.Views;

public partial class ScriptEditorWindow : Window
{
    private readonly ScriptEditorViewModel _viewModel;
    private readonly ScriptSyntaxColorizer _colorizer = new();
    private readonly DiagnosticLineHighlighter _diagnosticHighlighter = new();
    private readonly DiagnosticGlyphMargin _diagnosticGlyphMargin = new();
    private CompletionWindow? _completionWindow;
    private bool _syncingFromViewModel;
    private bool _revertingSelection;
    private object? _lastAcceptedProcessSelection;
    private object? _lastAcceptedFileSelection;
    private object? _lastAcceptedDocumentSelection;
    private int _completionReplaceLength;
    private Point _tabDragStart;
    private ScriptEditorViewModel.EditorDocumentTab? _draggedDocument;

    public ScriptEditorWindow(ScriptEditorViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = _viewModel;

        EditorView.TextArea.LeftMargins.Insert(0, _diagnosticGlyphMargin);
        EditorView.TextArea.TextView.LineTransformers.Add(_colorizer);
        EditorView.TextArea.TextView.BackgroundRenderers.Add(_diagnosticHighlighter);
        EditorView.Text = _viewModel.ScriptText;
        ApplyEditorLanguage();
        UpdateDiagnosticHighlights();

        EditorView.TextChanged += EditorView_TextChanged;
        EditorView.TextArea.TextEntered += TextArea_TextEntered;
        EditorView.TextArea.PreviewKeyDown += TextArea_PreviewKeyDown;

        _viewModel.PropertyChanged += ViewModel_PropertyChanged;
        _viewModel.Diagnostics.CollectionChanged += Diagnostics_CollectionChanged;
        _viewModel.TerminalEntries.CollectionChanged += TerminalEntries_CollectionChanged;
        Closing += ScriptEditorWindow_Closing;
        Loaded += ScriptEditorWindow_Loaded;
        SizeChanged += ScriptEditorWindow_SizeChanged;

        _lastAcceptedProcessSelection = _viewModel.SelectedProcessNode;
        _lastAcceptedFileSelection = _viewModel.SelectedFileNode;
        _lastAcceptedDocumentSelection = _viewModel.SelectedDocument;
        DocumentsTabControl.SelectedItem = _viewModel.SelectedDocument;
        UpdateCompactLayout();
    }

    private void ScriptEditorWindow_Loaded(object sender, RoutedEventArgs e)
    {
        EditorView.Focus();
        AnimateStatusPulse();
    }

    private void ScriptEditorWindow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateCompactLayout();
    }

    private void ProcessTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_revertingSelection) return;
        if (e.NewValue is not ScriptEditorViewModel.EditorProcessNode node) return;

        if (_viewModel.TrySelectProcessNode(node))
        {
            _lastAcceptedProcessSelection = node;
            return;
        }

        Dispatcher.BeginInvoke(() => RestoreTreeSelection(ProcessTree, _lastAcceptedProcessSelection), DispatcherPriority.Background);
    }

    private void FileTree_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (_revertingSelection) return;
        if (e.NewValue is not ScriptEditorViewModel.EditorFileNode node) return;

        if (_viewModel.TryOpenFileNode(node))
        {
            _lastAcceptedFileSelection = node;
            return;
        }

        Dispatcher.BeginInvoke(() => RestoreTreeSelection(FileTree, _lastAcceptedFileSelection), DispatcherPriority.Background);
    }

    private void FileTree_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.SelectedFileNode is not null)
            _viewModel.OpenFileNodeCommand.Execute(_viewModel.SelectedFileNode);
    }

    private void DocumentsTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_revertingSelection || DocumentsTabControl.SelectedItem is not ScriptEditorViewModel.EditorDocumentTab document)
            return;

        if (_viewModel.TryActivateDocument(document))
        {
            _lastAcceptedDocumentSelection = document;
            return;
        }

        Dispatcher.BeginInvoke(() => DocumentsTabControl.SelectedItem = _lastAcceptedDocumentSelection, DispatcherPriority.Background);
    }

    private void CloseDocumentButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not ScriptEditorViewModel.EditorDocumentTab document)
            return;

        if (_viewModel.TryCloseDocument(document))
        {
            _lastAcceptedDocumentSelection = _viewModel.SelectedDocument;
            DocumentsTabControl.SelectedItem = _viewModel.SelectedDocument;
        }
    }

    private void EditorView_TextChanged(object? sender, EventArgs e)
    {
        if (_syncingFromViewModel) return;
        _viewModel.ScriptText = EditorView.Text;
    }

    private void ViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScriptEditorViewModel.ScriptText))
        {
            if (EditorView.Text == _viewModel.ScriptText) return;
            _syncingFromViewModel = true;
            try
            {
                var caretOffset = EditorView.CaretOffset;
                EditorView.Text = _viewModel.ScriptText;
                EditorView.CaretOffset = Math.Min(caretOffset, EditorView.Text.Length);
            }
            finally
            {
                _syncingFromViewModel = false;
            }
        }
        else if (e.PropertyName is nameof(ScriptEditorViewModel.LanguageName) or nameof(ScriptEditorViewModel.DocumentName))
        {
            ApplyEditorLanguage();
        }
        else if (e.PropertyName == nameof(ScriptEditorViewModel.SelectedProcessNode))
        {
            _lastAcceptedProcessSelection = _viewModel.SelectedProcessNode;
        }
        else if (e.PropertyName == nameof(ScriptEditorViewModel.SelectedFileNode))
        {
            _lastAcceptedFileSelection = _viewModel.SelectedFileNode;
        }
        else if (e.PropertyName == nameof(ScriptEditorViewModel.SelectedDocument))
        {
            _lastAcceptedDocumentSelection = _viewModel.SelectedDocument;
            if (!ReferenceEquals(DocumentsTabControl.SelectedItem, _viewModel.SelectedDocument))
                DocumentsTabControl.SelectedItem = _viewModel.SelectedDocument;
        }
        else if (e.PropertyName == nameof(ScriptEditorViewModel.StatusText))
        {
            AnimateStatusPulse();
        }
    }

    private void DiagnosticsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DiagnosticsList.SelectedItem is not ScriptEditorViewModel.EditorDiagnostic diagnostic)
            return;

        NavigateToDiagnostic(diagnostic);
        DiagnosticsList.SelectedItem = null;
    }

    private void ApplyEditorLanguage()
    {
        _colorizer.SetLanguage(_viewModel.GetLanguageKey());
        EditorView.TextArea.TextView.Redraw();
    }

    private void UpdateDiagnosticHighlights()
    {
        _diagnosticHighlighter.SetDiagnostics(_viewModel.Diagnostics);
        _diagnosticGlyphMargin.SetDiagnostics(_viewModel.Diagnostics);
        EditorView.TextArea.TextView.InvalidateLayer(KnownLayer.Selection);
    }

    private void Diagnostics_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateDiagnosticHighlights();
    }

    private void TerminalEntries_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (!_viewModel.AutoFollowTerminal || e.Action is not (NotifyCollectionChangedAction.Add or NotifyCollectionChangedAction.Reset))
            return;

        Dispatcher.BeginInvoke(() =>
        {
            var last = TerminalList.Items.Cast<object>().LastOrDefault();
            if (last is not null)
                TerminalList.ScrollIntoView(last);
        }, DispatcherPriority.Background);
    }

    private void TextArea_TextEntered(object? sender, TextCompositionEventArgs e)
    {
        if (e.Text is not ("$" or "." or ":" or "-")) return;
        ShowCompletionWindow();
    }

    private void TextArea_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.S && Keyboard.Modifiers == ModifierKeys.Control)
        {
            _viewModel.SaveCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None)
        {
            _viewModel.RunCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.W && Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (_viewModel.SelectedDocument is not null)
                _viewModel.TryCloseDocument(_viewModel.SelectedDocument);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Space && Keyboard.Modifiers == ModifierKeys.Control)
        {
            ShowCompletionWindow();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F8 && Keyboard.Modifiers == ModifierKeys.None)
        {
            NavigateToRelativeDiagnostic(forward: true);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F8 && Keyboard.Modifiers == ModifierKeys.Shift)
        {
            NavigateToRelativeDiagnostic(forward: false);
            e.Handled = true;
        }
    }

    private void ShowCompletionWindow()
    {
        _completionWindow?.Close();

        var line = EditorView.Document.GetLineByOffset(EditorView.CaretOffset);
        var lineText = EditorView.Document.GetText(line.Offset, EditorView.CaretOffset - line.Offset);
        var prefix = _viewModel.ExtractCompletionPrefix(lineText);
        _completionReplaceLength = _viewModel.GetCompletionReplaceLength(lineText);
        var items = _viewModel.GetCompletionItems(prefix);
        if (items.Count == 0)
            return;

        _completionWindow = new CompletionWindow(EditorView.TextArea);
        var data = _completionWindow.CompletionList.CompletionData;

        foreach (var item in items)
            data.Add(new SimpleCompletionData(item, _completionReplaceLength));

        _completionWindow.Closed += (_, _) => _completionWindow = null;
        _completionWindow.Show();
    }

    private void NavigateToDiagnostic(ScriptEditorViewModel.EditorDiagnostic diagnostic)
    {
        if (diagnostic.Line < 1 || diagnostic.Line > EditorView.Document.LineCount)
            return;

        var line = EditorView.Document.GetLineByNumber(diagnostic.Line);
        var maxOffset = Math.Max(0, line.Length - 1);
        var startOffset = Math.Min(line.Offset + Math.Max(0, diagnostic.Column - 1), line.Offset + maxOffset);
        var length = Math.Max(1, Math.Min(diagnostic.Length, line.EndOffset - startOffset));

        EditorView.Focus();
        EditorView.Select(startOffset, length);
        EditorView.ScrollTo(diagnostic.Line, Math.Max(1, diagnostic.Column));
        EditorView.CaretOffset = startOffset;
    }

    private void NavigateToRelativeDiagnostic(bool forward)
    {
        if (_viewModel.Diagnostics.Count == 0)
            return;

        var caretLine = EditorView.Document.GetLineByOffset(EditorView.CaretOffset).LineNumber;
        ScriptEditorViewModel.EditorDiagnostic? target;

        if (forward)
        {
            target = _viewModel.Diagnostics
                .OrderBy(x => x.Line)
                .ThenBy(x => x.Column)
                .FirstOrDefault(x => x.Line > caretLine || (x.Line == caretLine && x.Column > GetCurrentColumn()));
            target ??= _viewModel.Diagnostics.OrderBy(x => x.Line).ThenBy(x => x.Column).First();
        }
        else
        {
            target = _viewModel.Diagnostics
                .OrderByDescending(x => x.Line)
                .ThenByDescending(x => x.Column)
                .FirstOrDefault(x => x.Line < caretLine || (x.Line == caretLine && x.Column < GetCurrentColumn()));
            target ??= _viewModel.Diagnostics.OrderByDescending(x => x.Line).ThenByDescending(x => x.Column).First();
        }

        if (target is not null)
            NavigateToDiagnostic(target);
    }

    private int GetCurrentColumn()
    {
        var line = EditorView.Document.GetLineByOffset(EditorView.CaretOffset);
        return Math.Max(1, EditorView.CaretOffset - line.Offset + 1);
    }

    private void RestoreTreeSelection(ItemsControl tree, object? item)
    {
        if (item is null) return;

        _revertingSelection = true;
        try
        {
            SelectTreeItem(tree, item);
        }
        finally
        {
            _revertingSelection = false;
        }
    }

    private static bool SelectTreeItem(ItemsControl parent, object target)
    {
        if (parent.DataContext == target && parent is TreeViewItem currentItem)
        {
            currentItem.IsSelected = true;
            currentItem.BringIntoView();
            return true;
        }

        parent.ApplyTemplate();
        if (parent is TreeViewItem parentItem)
            parentItem.IsExpanded = true;

        foreach (var item in parent.Items)
        {
            var container = parent.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
            if (container is null)
            {
                parent.UpdateLayout();
                container = parent.ItemContainerGenerator.ContainerFromItem(item) as TreeViewItem;
            }

            if (container is null)
                continue;

            if (ReferenceEquals(item, target))
            {
                container.IsSelected = true;
                container.BringIntoView();
                return true;
            }

            if (SelectTreeItem(container, target))
                return true;
        }

        return false;
    }

    private void ScriptEditorWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_viewModel.ConfirmCloseEditor())
            return;

        e.Cancel = true;
    }

    private void PrevIssueButton_Click(object sender, RoutedEventArgs e) => NavigateToRelativeDiagnostic(forward: false);

    private void NextIssueButton_Click(object sender, RoutedEventArgs e) => NavigateToRelativeDiagnostic(forward: true);

    private void DocumentsTabControl_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _tabDragStart = e.GetPosition(DocumentsTabControl);
        _draggedDocument = FindAncestor<TabItem>((DependencyObject)e.OriginalSource)?.DataContext as ScriptEditorViewModel.EditorDocumentTab;
    }

    private void DocumentsTabControl_MouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _draggedDocument is null)
            return;

        var currentPosition = e.GetPosition(DocumentsTabControl);
        if (Math.Abs(currentPosition.X - _tabDragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(currentPosition.Y - _tabDragStart.Y) < SystemParameters.MinimumVerticalDragDistance)
            return;

        DragDrop.DoDragDrop(DocumentsTabControl, _draggedDocument, DragDropEffects.Move);
        _draggedDocument = null;
    }

    private void DocumentsTabControl_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(ScriptEditorViewModel.EditorDocumentTab))
            ? DragDropEffects.Move
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void DocumentsTabControl_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(ScriptEditorViewModel.EditorDocumentTab)))
            return;

        var source = e.Data.GetData(typeof(ScriptEditorViewModel.EditorDocumentTab)) as ScriptEditorViewModel.EditorDocumentTab;
        var target = FindAncestor<TabItem>((DependencyObject)e.OriginalSource)?.DataContext as ScriptEditorViewModel.EditorDocumentTab;
        if (source is null || target is null)
            return;

        _viewModel.MoveDocumentTab(source, target);
        DocumentsTabControl.SelectedItem = source;
    }

    private void UpdateCompactLayout()
    {
        _viewModel.IsCompactLayout = ActualWidth < 1280;
    }

    private void AnimateStatusPulse()
    {
        if (StatusTextBlock is null)
            return;

        var animation = new DoubleAnimation
        {
            From = 0.65,
            To = 1,
            Duration = TimeSpan.FromMilliseconds(220),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        StatusTextBlock.BeginAnimation(OpacityProperty, animation);
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match)
                return match;

            current = VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private sealed class SimpleCompletionData : ICSharpCode.AvalonEdit.CodeCompletion.ICompletionData
    {
        private readonly int _replaceLength;

        public SimpleCompletionData(string text, int replaceLength)
        {
            Text = text;
            _replaceLength = replaceLength;
        }

        public ImageSource? Image => null;
        public string Text { get; }
        public object Content => Text.Replace("\n", " ", StringComparison.Ordinal);
        public object Description => Text;
        public double Priority => 0;

        public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
        {
            var startOffset = Math.Max(0, textArea.Caret.Offset - _replaceLength);
            textArea.Document.Replace(startOffset, _replaceLength, Text);
        }
    }
}
