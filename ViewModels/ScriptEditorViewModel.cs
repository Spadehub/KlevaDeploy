using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using KlevaDeploy.Models;
using KlevaDeploy.Services.Interfaces;
using Microsoft.Win32;

namespace KlevaDeploy.ViewModels;

public sealed partial class ScriptEditorViewModel : ObservableObject
{
    private static readonly string[] PowerShellCompletions =
    [
        "$env:KLEVADEPLOY_DATA_DIR",
        "$env:KLEVADEPLOY_TEMP_DIR",
        "$env:KLEVADEPLOY_STORAGE_DIR",
        "$ErrorActionPreference = 'Stop'",
        "param()",
        "if () {\n    \n}",
        "foreach ($item in $items) {\n    \n}",
        "try {\n    \n} catch {\n    throw\n}",
        "Write-Output",
        "Write-Warning",
        "Write-Error",
        "Test-Path",
        "Join-Path",
        "Get-ChildItem",
        "New-Item",
        "Set-Content",
        "Start-Process"
    ];

    private static readonly string[] BatchCompletions =
    [
        "@echo off",
        "setlocal",
        "if \"%ERRORLEVEL%\" NEQ \"0\" exit /b %ERRORLEVEL%",
        "set VAR=value",
        "call ",
        "echo ",
        "timeout /t 1 /nobreak >nul"
    ];

    private static readonly string[] BashCompletions =
    [
        "#!/usr/bin/env bash",
        "set -euo pipefail",
        "if [ -f \"$file\" ]; then\nfi",
        "for item in \"$@\"; do\n    :\ndone",
        "echo ",
        "mkdir -p ",
        "grep -n "
    ];

    private readonly CreateProcessViewModel _sourceViewModel;
    private readonly CreateProcessViewModel _navigationViewModel;
    private readonly IPreferencesService? _preferencesService;
    private readonly ScriptEditorLayoutPreferences _layoutPreferences;
    private readonly DispatcherTimer _diagnosticTimer;
    private readonly DispatcherTimer _undoCaptureTimer;
    private bool _suppressUndoCapture;
    private readonly Regex _wordRegex = new(@"[\w:$-]+$", RegexOptions.Compiled);
    private readonly Dictionary<Guid, EditorRunSession> _activeSessions = new();
    private int _sessionCounter;
    private int _scratchCounter = 1;
    private EditorRunSession? _activeDetachedSession;
    private EditorDocumentKind _currentDocumentKind = EditorDocumentKind.Process;
    private EditorProcessNode? _currentProcessDocument;
    private string? _currentFilePath;
    private int _diagnosticVersion;

    public ObservableCollection<EditorProcessNode> ProcessNodes { get; } = new();
    public ObservableCollection<EditorFileNode> FileNodes { get; } = new();
    public ObservableCollection<EditorDocumentTab> OpenDocuments { get; } = new();
    public ObservableCollection<EditorTerminalEntry> TerminalEntries { get; } = new();
    public ObservableCollection<EditorDiagnostic> Diagnostics { get; } = new();
    public ObservableCollection<string> TerminalLevels { get; } = new()
    {
        "All", "CMD", "STDOUT", "STDERR", "INFO", "WARN", "ERROR"
    };

    public ICollectionView TerminalView { get; }
    public ScriptEditorLayoutPreferences LayoutPreferences => _layoutPreferences;

    private EditorProcessNode? _selectedProcessNode;
    public EditorProcessNode? SelectedProcessNode
    {
        get => _selectedProcessNode;
        set
        {
            if (!SetProperty(ref _selectedProcessNode, value)) return;
            UpdateCommandStates();
        }
    }

    private EditorFileNode? _selectedFileNode;
    public EditorFileNode? SelectedFileNode
    {
        get => _selectedFileNode;
        set
        {
            if (!SetProperty(ref _selectedFileNode, value)) return;
            UpdateCommandStates();
        }
    }

    private EditorDocumentTab? _selectedDocument;
    public EditorDocumentTab? SelectedDocument
    {
        get => _selectedDocument;
        private set => SetProperty(ref _selectedDocument, value);
    }

    private string _scriptText = string.Empty;
    public string ScriptText
    {
        get => _scriptText;
        set
        {
            if (!SetProperty(ref _scriptText, value)) return;
            if (SelectedDocument is not null)
                SelectedDocument.ScriptText = value;
            IsDirty = true;
            OnPropertyChanged(nameof(HasDiagnostics));
            OnPropertyChanged(nameof(DiagnosticsSummary));
            UpdateCommandStates();
            _diagnosticTimer.Stop();
            _diagnosticTimer.Start();
            if (!_suppressUndoCapture)
            {
                _undoCaptureTimer.Stop();
                _undoCaptureTimer.Start();
            }
        }
    }

    private string _documentName = "Untitled";
    public string DocumentName
    {
        get => _documentName;
        private set => SetProperty(ref _documentName, value);
    }

    private string _documentPath = string.Empty;
    public string DocumentPath
    {
        get => _documentPath;
        private set
        {
            if (!SetProperty(ref _documentPath, value)) return;
            OnPropertyChanged(nameof(ShowDocumentPath));
        }
    }

    private string _languageName = "Text";
    public string LanguageName
    {
        get => _languageName;
        private set => SetProperty(ref _languageName, value);
    }

    private string _terminalLevelFilter = "All";
    public string TerminalLevelFilter
    {
        get => _terminalLevelFilter;
        set
        {
            if (!SetProperty(ref _terminalLevelFilter, value)) return;
            TerminalView.Refresh();
            RaiseTerminalStateChanged();
        }
    }

    private string _terminalSearchText = string.Empty;
    public string TerminalSearchText
    {
        get => _terminalSearchText;
        set
        {
            if (!SetProperty(ref _terminalSearchText, value)) return;
            TerminalView.Refresh();
            RaiseTerminalStateChanged();
        }
    }

    private bool _autoFollowTerminal = true;
    public bool AutoFollowTerminal
    {
        get => _autoFollowTerminal;
        set => SetProperty(ref _autoFollowTerminal, value);
    }

    private bool _isCompactLayout;
    public bool IsCompactLayout
    {
        get => _isCompactLayout;
        set
        {
            if (!SetProperty(ref _isCompactLayout, value)) return;
            RaiseLayoutStateChanged();
        }
    }

    private bool _isNavigatorCollapsed;
    public bool IsNavigatorCollapsed
    {
        get => _isNavigatorCollapsed;
        set
        {
            if (!SetProperty(ref _isNavigatorCollapsed, value)) return;
            RaiseLayoutStateChanged();
        }
    }

    private bool _isExplorerCollapsed;
    public bool IsExplorerCollapsed
    {
        get => _isExplorerCollapsed;
        set
        {
            if (!SetProperty(ref _isExplorerCollapsed, value)) return;
            RaiseLayoutStateChanged();
        }
    }

    private bool _isTerminalCollapsed;
    public bool IsTerminalCollapsed
    {
        get => _isTerminalCollapsed;
        set
        {
            if (!SetProperty(ref _isTerminalCollapsed, value)) return;
            RaiseLayoutStateChanged();
        }
    }

    private bool _isDiagnosticsCollapsed;
    public bool IsDiagnosticsCollapsed
    {
        get => _isDiagnosticsCollapsed;
        set
        {
            if (!SetProperty(ref _isDiagnosticsCollapsed, value)) return;
            RaisePresentationStateChanged();
        }
    }

    private bool _isDirty;
    public bool IsDirty
    {
        get => _isDirty;
        private set
        {
            if (!SetProperty(ref _isDirty, value)) return;
            if (SelectedDocument is not null)
                SelectedDocument.IsDirty = value;
            RaisePresentationStateChanged();
        }
    }

    private bool _isRunning;
    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (!SetProperty(ref _isRunning, value)) return;
            RunCommand.NotifyCanExecuteChanged();
            StopCommand.NotifyCanExecuteChanged();
            RestartCommand.NotifyCanExecuteChanged();
            RaisePresentationStateChanged();
        }
    }

    private string _statusText = "Ready";
    public string StatusText
    {
        get => _statusText;
        private set => SetProperty(ref _statusText, value);
    }

    public bool HasDiagnostics => Diagnostics.Count > 0;
    public bool HasOpenDocuments => OpenDocuments.Count > 0;
    public string DiagnosticsSummary => Diagnostics.Count == 0 ? "No syntax issues detected." : $"{Diagnostics.Count} issue(s) detected.";
    public string DiagnosticsSummaryHint => Diagnostics.Count == 0
        ? "Parser looks clean. Inline highlighting stays active while you edit."
        : "Review flagged items below or use F8 / Shift+F8 to jump between issues.";
    public bool HasErrorDiagnostics => Diagnostics.Any(d => string.Equals(d.Severity, "ERROR", StringComparison.OrdinalIgnoreCase));
    public bool HasWarningDiagnostics => Diagnostics.Any(d => string.Equals(d.Severity, "WARN", StringComparison.OrdinalIgnoreCase));
    public bool HasTerminalEntries => TerminalEntries.Count > 0;
    public bool HasVisibleTerminalEntries => !TerminalView.IsEmpty;
    public bool ShowTerminalPlaceholder => !HasVisibleTerminalEntries;
    public bool ShowTerminalEmptyState => !HasTerminalEntries;
    public bool ShowTerminalFilteredEmptyState => HasTerminalEntries && !HasVisibleTerminalEntries;
    public bool IsProcessDocumentActive => _currentDocumentKind == EditorDocumentKind.Process;
    public bool IsFileDocumentActive => _currentDocumentKind == EditorDocumentKind.File;
    public bool IsScratchDocumentActive => _currentDocumentKind == EditorDocumentKind.Scratch;
    public bool ShowExecutionToolbar => IsProcessDocumentActive || IsFileDocumentActive || IsScratchDocumentActive;
    public bool ShowDiagnosticsToolbar => HasDiagnostics;
    public bool ShowDiagnosticsPanel => HasDiagnostics && !IsDiagnosticsCollapsed;
    public bool ShowNavigatorPane => !IsNavigatorCollapsed;
    public bool ShowExplorerPane => !IsCompactLayout && !IsExplorerCollapsed;
    public bool ShowTerminalPane => !IsTerminalCollapsed;
    public bool ShowEmptyEditorState => !HasOpenDocuments;
    public bool ShowHeaderHint => !IsCompactLayout;
    public bool ShowHeaderSecondaryBadges => !IsCompactLayout;
    public bool ShowDocumentPath => !IsCompactLayout && !string.IsNullOrWhiteSpace(DocumentPath);
    public bool ShowDocumentLanguageBadge => !IsCompactLayout;
    public bool ShowDocumentWorkspaceCaption => !IsCompactLayout;
    public bool ShowDocumentMetaGroups => !IsCompactLayout;
    public bool ShowTerminalDescription => !IsCompactLayout;
    public double TerminalLevelFilterWidth => IsCompactLayout ? 104 : 120;
    public double TerminalSearchBoxWidth => IsCompactLayout ? 160 : 220;
    public string OpenDocumentsBadgeText => IsCompactLayout
        ? $"{OpenDocuments.Count} tabs"
        : $"Tabs: {OpenDocuments.Count}";
    public string LayoutModeLabel => IsCompactLayout ? "Compact layout" : "Expanded layout";
    public string CurrentDocumentKindLabel => _currentDocumentKind switch
    {
        EditorDocumentKind.None => "No Document",
        EditorDocumentKind.Process => "Process Script",
        EditorDocumentKind.File => "External File",
        EditorDocumentKind.Scratch => "Scratch Pad",
        _ => "Editor Document"
    };
    public string CurrentDocumentSubtitle => _currentDocumentKind switch
    {
        EditorDocumentKind.None => "Choose a process, file, or scratch document to start editing.",
        EditorDocumentKind.Process when SelectedProcessNode is not null => BuildDocumentSubtitle(GetProcessKindLabel(SelectedProcessNode.Kind)),
        EditorDocumentKind.File => BuildDocumentSubtitle("External file"),
        EditorDocumentKind.Scratch => BuildDocumentSubtitle("Scratch pad"),
        _ => BuildDocumentSubtitle("Editor document")
    };
    public string CurrentDocumentHint => _currentDocumentKind switch
    {
        EditorDocumentKind.None => "The editor workspace is empty. Open a file, select a process, or create a scratch document.",
        EditorDocumentKind.Process => "Editing inline process behavior stored in the KDP definition.",
        EditorDocumentKind.File => "Editing a file-backed script with local filesystem context.",
        EditorDocumentKind.Scratch => "Editing an unsaved draft for quick experiments or snippet authoring.",
        _ => "Editing the current document."
    };
    public string ActivityStateLabel => IsRunning ? "Running" : IsDirty ? "Unsaved" : "Ready";
    public string DiagnosticsStateLabel => Diagnostics.Count == 0 ? "Clean" : $"{Diagnostics.Count} issue(s)";
    public string DiagnosticsPanelToggleLabel => IsDiagnosticsCollapsed ? "Show Issue Panel" : "Hide Issue Panel";
    public string TerminalEmptyStateTitle => ShowTerminalFilteredEmptyState ? "No entries match current filters" : "Terminal is ready";
    public string TerminalEmptyStateMessage => ShowTerminalFilteredEmptyState
        ? "Adjust the level filter or search text to bring matching output back into view."
        : "Run the current document to stream output, progress updates, and session logs here.";

    private bool _canUndo;
    public bool CanUndo
    {
        get => _canUndo;
        private set
        {
            if (!SetProperty(ref _canUndo, value)) return;
            UndoCommand.NotifyCanExecuteChanged();
        }
    }

    private bool _canRedo;
    public bool CanRedo
    {
        get => _canRedo;
        private set
        {
            if (!SetProperty(ref _canRedo, value)) return;
            RedoCommand.NotifyCanExecuteChanged();
        }
    }

    public IRelayCommand SaveCommand { get; }
    public IRelayCommand NewCommand { get; }
    public IRelayCommand OpenCommand { get; }
    public IRelayCommand UndoCommand { get; }
    public IRelayCommand RedoCommand { get; }
    public IAsyncRelayCommand RunCommand { get; }
    public IRelayCommand StopCommand { get; }
    public IAsyncRelayCommand RestartCommand { get; }
    public IRelayCommand RefreshFilesCommand { get; }
    public IRelayCommand ClearTerminalCommand { get; }
    public IRelayCommand ExportTerminalCommand { get; }
    public IRelayCommand<EditorProcessNode?> RunNodeCommand { get; }
    public IRelayCommand<EditorProcessNode?> StopNodeCommand { get; }
    public IRelayCommand<EditorProcessNode?> RestartNodeCommand { get; }
    public IRelayCommand<EditorFileNode?> OpenFileNodeCommand { get; }

    public ScriptEditorViewModel(CreateProcessViewModel sourceViewModel)
    {
        _sourceViewModel = sourceViewModel;
        _navigationViewModel = sourceViewModel.EditorNavigationSourceViewModel ?? sourceViewModel;
        _preferencesService = sourceViewModel.PreferencesService;
        _layoutPreferences = _preferencesService?.Preferences.ScriptEditorLayout ?? new ScriptEditorLayoutPreferences();
        _isNavigatorCollapsed = _layoutPreferences.NavigatorCollapsed;
        _isExplorerCollapsed = _layoutPreferences.ExplorerCollapsed;
        _isTerminalCollapsed = _layoutPreferences.TerminalCollapsed;
        _isDiagnosticsCollapsed = _layoutPreferences.DiagnosticsCollapsed;

        TerminalView = CollectionViewSource.GetDefaultView(TerminalEntries);
        TerminalView.Filter = FilterTerminalEntry;
        OpenDocuments.CollectionChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(OpenDocumentsBadgeText));
            OnPropertyChanged(nameof(HasOpenDocuments));
            OnPropertyChanged(nameof(ShowEmptyEditorState));
        };

        _diagnosticTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };
        _diagnosticTimer.Tick += (_, _) =>
        {
            _diagnosticTimer.Stop();
            _ = RefreshDiagnosticsAsync();
        };

        _undoCaptureTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(350)
        };
        _undoCaptureTimer.Tick += (_, _) =>
        {
            _undoCaptureTimer.Stop();
            CaptureUndoSnapshot();
        };

        SaveCommand = new RelayCommand(SaveDocument);
        NewCommand = new RelayCommand(TryCreateNewScratchDocument);
        OpenCommand = new RelayCommand(TryOpenExternalFile);
        UndoCommand = new RelayCommand(Undo, () => CanUndo);
        RedoCommand = new RelayCommand(Redo, () => CanRedo);
        RunCommand = new AsyncRelayCommand(RunCurrentAsync, CanRunCurrent);
        StopCommand = new RelayCommand(StopCurrent, CanStopCurrent);
        RestartCommand = new AsyncRelayCommand(RestartCurrentAsync, CanRestartCurrent);
        RefreshFilesCommand = new RelayCommand(RefreshFiles);
        ClearTerminalCommand = new RelayCommand(ClearTerminal);
        ExportTerminalCommand = new RelayCommand(ExportTerminal);
        RunNodeCommand = new RelayCommand<EditorProcessNode?>(RunNodeFromCommand);
        StopNodeCommand = new RelayCommand<EditorProcessNode?>(StopNodeFromCommand);
        RestartNodeCommand = new RelayCommand<EditorProcessNode?>(RestartNodeFromCommand);
        OpenFileNodeCommand = new RelayCommand<EditorFileNode?>(node => TryOpenFileNode(node));

        BuildProcessTree();
        RefreshFiles();

        SelectedProcessNode = ResolveInitialProcessNode() ?? ProcessNodes.FirstOrDefault();
        if (SelectedProcessNode is null)
            CreateNewScratchDocument();
        else
            OpenOrActivateProcessDocument(SelectedProcessNode, promptForDirty: false);
    }

    public void PersistLayoutState(
        double leftPaneWidth,
        double rightPaneWidth,
        double terminalHeight,
        Rect restoreBounds,
        bool isMaximized)
    {
        _layoutPreferences.LeftPaneWidth = Clamp(leftPaneWidth, 220, 640);
        _layoutPreferences.RightPaneWidth = Clamp(rightPaneWidth, 240, 720);
        _layoutPreferences.TerminalHeight = Clamp(terminalHeight, 160, 520);
        _layoutPreferences.NavigatorCollapsed = IsNavigatorCollapsed;
        _layoutPreferences.ExplorerCollapsed = IsExplorerCollapsed;
        _layoutPreferences.TerminalCollapsed = IsTerminalCollapsed;
        _layoutPreferences.DiagnosticsCollapsed = IsDiagnosticsCollapsed;
        _layoutPreferences.WindowWidth = Clamp(restoreBounds.Width, 960, 3840);
        _layoutPreferences.WindowHeight = Clamp(restoreBounds.Height, 700, 2160);
        _layoutPreferences.WindowLeft = restoreBounds.Left;
        _layoutPreferences.WindowTop = restoreBounds.Top;
        _layoutPreferences.IsMaximized = isMaximized;
        _preferencesService?.Save();
    }

    public void RestoreDefaultLayout()
    {
        _layoutPreferences.LeftPaneWidth = 256;
        _layoutPreferences.RightPaneWidth = 288;
        _layoutPreferences.TerminalHeight = 180;
        IsNavigatorCollapsed = false;
        IsExplorerCollapsed = false;
        IsTerminalCollapsed = false;
        IsDiagnosticsCollapsed = true;
    }

    public IReadOnlyList<string> GetCompletionItems(string prefix)
    {
        var source = LanguageName switch
        {
            "PowerShell" => PowerShellCompletions,
            "Batch" => BatchCompletions,
            "Bash" => BashCompletions,
            _ => Array.Empty<string>()
        };

        prefix ??= string.Empty;
        var dynamicItems = GetDynamicCompletionItems();

        return source
            .Concat(dynamicItems)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(item => prefix.Length == 0 || item.Contains(prefix, StringComparison.OrdinalIgnoreCase))
            .OrderBy(item => item.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(item => item, StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();
    }

    public string ExtractCompletionPrefix(string lineText)
    {
        if (string.IsNullOrEmpty(lineText))
            return string.Empty;

        var match = _wordRegex.Match(lineText);
        return match.Success ? match.Value : string.Empty;
    }

    public int GetCompletionReplaceLength(string lineText) => ExtractCompletionPrefix(lineText).Length;

    public string GetLanguageKey()
    {
        return LanguageName switch
        {
            "PowerShell" => "powershell",
            "Batch" => "batch",
            "Bash" => "bash",
            _ => "text"
        };
    }

    private IEnumerable<string> GetDynamicCompletionItems()
    {
        var values = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (Match match in Regex.Matches(ScriptText ?? string.Empty, @"\$[A-Za-z_][\w:]*"))
            values.Add(match.Value);

        foreach (Match match in Regex.Matches(ScriptText ?? string.Empty, @"\{[A-Z0-9_]+\}"))
            values.Add(match.Value);

        foreach (var node in EnumerateProcessNodes(ProcessNodes))
        {
            if (!string.IsNullOrWhiteSpace(node.DisplayName))
                values.Add(node.DisplayName);

            var args = node.Arguments ?? string.Empty;
            foreach (Match match in Regex.Matches(args, @"\{[A-Z0-9_]+\}"))
                values.Add(match.Value);
        }

        if (!string.IsNullOrWhiteSpace(_sourceViewModel.ProcessName))
            values.Add(_sourceViewModel.ProcessName.Trim());

        return values;
    }

    private static IEnumerable<EditorProcessNode> EnumerateProcessNodes(IEnumerable<EditorProcessNode> roots)
    {
        foreach (var node in roots)
        {
            yield return node;
            foreach (var child in EnumerateProcessNodes(node.Children))
                yield return child;
        }
    }

    private void BuildProcessTree()
    {
        ProcessNodes.Clear();
        ProcessNodes.Add(CreateRootNode());
    }

    private EditorProcessNode CreateRootNode()
    {
        var node = new EditorProcessNode(
            string.IsNullOrWhiteSpace(_navigationViewModel.ProcessName) ? "Current Process" : _navigationViewModel.ProcessName.Trim(),
            _navigationViewModel.SelectedProcessKind,
            _navigationViewModel.EditingProcessId,
            () => _navigationViewModel.ScriptContent,
            value => _navigationViewModel.ScriptContent = value,
            () => _navigationViewModel.FilePath,
            value => _navigationViewModel.FilePath = value,
            () => _navigationViewModel.Arguments,
            () => _navigationViewModel.RunAsAdmin);

        foreach (var sub in _navigationViewModel.SubProcesses.Where(s => s.Process is not null))
            node.Children.Add(CreateSubProcessNode(sub.Process!));

        return node;
    }

    private static EditorProcessNode CreateProcessNode(DeploymentProcess process, bool isRoot = false)
    {
        var node = new EditorProcessNode(
            string.IsNullOrWhiteSpace(process.Name) ? (isRoot ? "Current Process" : "Subprocess") : process.Name.Trim(),
            process.Kind,
            process.Id,
            () => process.ScriptContent,
            value => process.ScriptContent = value,
            () => process.RelativePath,
            value => process.RelativePath = value,
            () => process.Arguments,
            () => process.RunAsAdmin);

        foreach (var sub in process.SubProcesses.Where(s => s.Process is not null))
            node.Children.Add(CreateProcessNode(sub.Process!));

        return node;
    }

    private static EditorProcessNode CreateSubProcessNode(DeploymentProcess process)
    {
        var node = new EditorProcessNode(
            string.IsNullOrWhiteSpace(process.Name) ? "Subprocess" : process.Name.Trim(),
            process.Kind,
            process.Id,
            () => process.ScriptContent,
            value => process.ScriptContent = value,
            () => process.RelativePath,
            value => process.RelativePath = value,
            () => process.Arguments,
            () => process.RunAsAdmin);

        foreach (var sub in process.SubProcesses.Where(s => s.Process is not null))
            node.Children.Add(CreateSubProcessNode(sub.Process!));

        return node;
    }

    private EditorProcessNode? ResolveInitialProcessNode()
    {
        if (!_sourceViewModel.IsSubProcessEditor || string.IsNullOrWhiteSpace(_sourceViewModel.EditingProcessId))
            return null;

        return EnumerateProcessNodes(ProcessNodes)
            .FirstOrDefault(node => string.Equals(node.ProcessId, _sourceViewModel.EditingProcessId, StringComparison.OrdinalIgnoreCase));
    }

    private void OpenProcessDocument(EditorProcessNode node)
    {
        var document = FindOrCreateProcessDocument(node);
        ActivateDocument(document);
    }

    public bool TrySelectProcessNode(EditorProcessNode? node)
    {
        if (node is null)
            return false;

        if (ReferenceEquals(_currentProcessDocument, node))
        {
            SelectedProcessNode = node;
            return true;
        }

        return OpenOrActivateProcessDocument(node, promptForDirty: false);
    }

    private void OpenFileNode(EditorFileNode? node)
    {
        if (node is null || node.IsDirectory || !File.Exists(node.FullPath))
            return;

        var document = FindOrCreateFileDocument(node.FullPath);
        ActivateDocument(document);
    }

    public bool TryOpenFileNode(EditorFileNode? node)
    {
        if (node is null || node.IsDirectory || !File.Exists(node.FullPath))
            return false;

        if (string.Equals(_currentFilePath, node.FullPath, StringComparison.OrdinalIgnoreCase))
        {
            SelectedFileNode = node;
            return true;
        }

        return OpenOrActivateFileDocument(node.FullPath, node, promptForDirty: true);
    }

    private void CreateNewScratchDocument()
    {
        var document = CreateScratchDocument();
        ActivateDocument(document);
        StatusText = "Created empty scratch document.";
    }

    private void TryCreateNewScratchDocument()
    {
        if (!ResolvePendingDocumentChange("Create a new scratch document?"))
            return;

        CreateNewScratchDocument();
    }

    private void OpenExternalFile()
    {
        var dlg = new OpenFileDialog
        {
            Title = "Open file in editor",
            Filter = "Script files (*.ps1;*.bat;*.cmd;*.sh;*.txt)|*.ps1;*.bat;*.cmd;*.sh;*.txt|All files (*.*)|*.*",
            CheckFileExists = true,
            CheckPathExists = true,
            Multiselect = false
        };

        if (dlg.ShowDialog() != true)
            return;

        OpenOrActivateFileDocument(dlg.FileName, null, promptForDirty: false);
        StatusText = $"Opened file: {Path.GetFileName(dlg.FileName)}";
    }

    private void TryOpenExternalFile()
    {
        if (!ResolvePendingDocumentChange("Open another file in the editor?"))
            return;

        OpenExternalFile();
    }

    public bool TryActivateDocument(EditorDocumentTab? document)
    {
        if (document is null)
            return false;

        if (ReferenceEquals(SelectedDocument, document))
            return true;

        if (!ResolvePendingDocumentChange($"Switch to document '{document.Name}'?"))
            return false;

        ActivateDocument(document);
        return true;
    }

    public bool TryCloseDocument(EditorDocumentTab? document)
    {
        if (document is null)
            return false;

        if (!ResolveDocumentState(document, $"Close document '{document.Name}'?"))
            return false;

        var index = OpenDocuments.IndexOf(document);
        OpenDocuments.Remove(document);

        if (ReferenceEquals(SelectedDocument, document))
        {
            var fallback = OpenDocuments.ElementAtOrDefault(Math.Max(0, index - 1)) ?? OpenDocuments.FirstOrDefault();
            if (fallback is null)
            {
                ActivateEmptyWorkspace();
            }
            else
            {
                ActivateDocument(fallback);
            }
        }

        return true;
    }

    public void MoveDocumentTab(EditorDocumentTab source, EditorDocumentTab target)
    {
        if (ReferenceEquals(source, target))
            return;

        var sourceIndex = OpenDocuments.IndexOf(source);
        var targetIndex = OpenDocuments.IndexOf(target);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
            return;

        OpenDocuments.Move(sourceIndex, targetIndex);
        SelectedDocument = source;
    }

    private bool OpenOrActivateProcessDocument(EditorProcessNode node, bool promptForDirty)
    {
        if (promptForDirty && !ResolvePendingDocumentChange($"Switch to process '{node.DisplayName}'?"))
            return false;

        SelectedProcessNode = node;
        ActivateDocument(FindOrCreateProcessDocument(node));
        return true;
    }

    private bool OpenOrActivateFileDocument(string fullPath, EditorFileNode? sourceNode, bool promptForDirty)
    {
        if (promptForDirty && !ResolvePendingDocumentChange($"Open file '{Path.GetFileName(fullPath)}'?"))
            return false;

        if (sourceNode is not null)
            SelectedFileNode = sourceNode;

        ActivateDocument(FindOrCreateFileDocument(fullPath));
        return true;
    }

    private EditorDocumentTab FindOrCreateProcessDocument(EditorProcessNode node)
    {
        var existing = OpenDocuments.FirstOrDefault(d => d.Kind == EditorDocumentKind.Process && ReferenceEquals(d.ProcessNode, node));
        if (existing is not null)
            return existing;

        var document = new EditorDocumentTab(
            EditorDocumentKind.Process,
            node.DisplayName,
            node.ResolveDisplayPath(),
            node.GetLanguageName(),
            node.LoadDocumentText(),
            false,
            node,
            null);
        OpenDocuments.Add(document);
        return document;
    }

    private EditorDocumentTab FindOrCreateFileDocument(string fullPath)
    {
        var existing = OpenDocuments.FirstOrDefault(d =>
            d.Kind == EditorDocumentKind.File &&
            string.Equals(d.FilePath, fullPath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
            return existing;

        var document = new EditorDocumentTab(
            EditorDocumentKind.File,
            Path.GetFileName(fullPath),
            fullPath,
            GetLanguageNameFromPath(fullPath),
            File.ReadAllText(fullPath),
            false,
            null,
            fullPath);
        OpenDocuments.Add(document);
        return document;
    }

    private EditorDocumentTab CreateScratchDocument()
    {
        var name = $"Untitled {_scratchCounter++}";
        var document = new EditorDocumentTab(
            EditorDocumentKind.Scratch,
            name,
            string.Empty,
            "Text",
            string.Empty,
            false,
            null,
            null);
        OpenDocuments.Add(document);
        return document;
    }

    private void ActivateDocument(EditorDocumentTab document)
    {
        SelectedDocument = document;
        _currentDocumentKind = document.Kind;
        _currentProcessDocument = document.ProcessNode;
        _currentFilePath = document.FilePath;
        SetDocumentState(document.Name, document.Path, document.ScriptText, document.LanguageName, markClean: !document.IsDirty);
        UpdateUndoRedoState();

        if (document.ProcessNode is not null)
            SelectedProcessNode = document.ProcessNode;

        if (!string.IsNullOrWhiteSpace(document.FilePath))
        {
            var existingFileNode = FindFileNode(FileNodes, document.FilePath);
            if (existingFileNode is not null)
                SelectedFileNode = existingFileNode;
        }
    }

    private void ActivateEmptyWorkspace()
    {
        SelectedDocument = null;
        _currentDocumentKind = EditorDocumentKind.None;
        _currentProcessDocument = null;
        _currentFilePath = null;
        DocumentName = "No document open";
        DocumentPath = string.Empty;
        LanguageName = string.Empty;
        _scriptText = string.Empty;
        OnPropertyChanged(nameof(ScriptText));
        IsDirty = false;
        UpdateUndoRedoState();
        RaisePresentationStateChanged();
        StatusText = "All documents closed.";
    }

    private static EditorFileNode? FindFileNode(IEnumerable<EditorFileNode> nodes, string fullPath)
    {
        foreach (var node in nodes)
        {
            if (string.Equals(node.FullPath, fullPath, StringComparison.OrdinalIgnoreCase))
                return node;

            var child = FindFileNode(node.Children, fullPath);
            if (child is not null)
                return child;
        }

        return null;
    }

    public bool ConfirmCloseEditor()
    {
        if (IsRunning)
        {
            var result = MessageBox.Show(
                "One or more editor runs are still active. Stop them and close the editor?",
                "Close Script Editor",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return false;

            StopAllSessions();
        }

        foreach (var document in OpenDocuments.ToList())
        {
            if (!ResolveDocumentState(document, $"Close document '{document.Name}'?"))
                return false;
        }

        return true;
    }

    private bool ResolvePendingDocumentChange(string actionLabel)
    {
        return ResolveDocumentState(SelectedDocument, actionLabel);
    }

    private bool ResolveDocumentState(EditorDocumentTab? document, string actionLabel)
    {
        if (document is null || !document.IsDirty)
            return true;

        var result = MessageBox.Show(
            $"{actionLabel}\n\nChoose Yes to save, No to discard changes, or Cancel to stay on the current document.",
            "Unsaved Changes",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Warning);

        return result switch
        {
            MessageBoxResult.Yes => SaveDocumentState(document),
            MessageBoxResult.No => true,
            _ => false
        };
    }

    private void SaveDocument()
    {
        SaveCurrentDocument();
    }

    private bool SaveCurrentDocument()
    {
        return SaveDocumentState(SelectedDocument);
    }

    private bool SaveDocumentState(EditorDocumentTab? document)
    {
        if (document is null)
            return false;

        var isActiveDocument = ReferenceEquals(SelectedDocument, document);
        var textToSave = isActiveDocument ? ScriptText : document.ScriptText;

        switch (document.Kind)
        {
            case EditorDocumentKind.Process when document.ProcessNode is not null:
                document.ProcessNode.ApplyDocumentText(textToSave);
                document.Path = document.ProcessNode.ResolveDisplayPath();
                StatusText = $"Applied editor changes to process: {document.ProcessNode.DisplayName}";
                break;

            case EditorDocumentKind.File when !string.IsNullOrWhiteSpace(document.FilePath):
                File.WriteAllText(document.FilePath, textToSave, Encoding.UTF8);
                document.Path = document.FilePath;
                document.Name = Path.GetFileName(document.FilePath);
                document.LanguageName = GetLanguageNameFromPath(document.FilePath);
                StatusText = $"Saved file: {Path.GetFileName(document.FilePath)}";
                RefreshFiles();
                break;

            case EditorDocumentKind.Scratch:
                var dlg = new SaveFileDialog
                {
                    Title = "Save editor document",
                    Filter = "PowerShell (*.ps1)|*.ps1|Batch (*.bat)|*.bat|Bash (*.sh)|*.sh|Text (*.txt)|*.txt|All files (*.*)|*.*",
                    FileName = "script.ps1",
                    AddExtension = true,
                    OverwritePrompt = true
                };

                if (dlg.ShowDialog() != true)
                    return false;

                File.WriteAllText(dlg.FileName, textToSave, Encoding.UTF8);
                document.Kind = EditorDocumentKind.File;
                document.FilePath = dlg.FileName;
                document.Name = Path.GetFileName(dlg.FileName);
                document.Path = dlg.FileName;
                document.LanguageName = GetLanguageNameFromPath(dlg.FileName);
                StatusText = $"Saved file: {Path.GetFileName(dlg.FileName)}";
                RefreshFiles();
                break;
        }

        document.ScriptText = textToSave;
        document.IsDirty = false;

        if (isActiveDocument)
        {
            _currentDocumentKind = document.Kind;
            _currentProcessDocument = document.ProcessNode;
            _currentFilePath = document.FilePath;
            DocumentName = document.Name;
            DocumentPath = document.Path;
            LanguageName = document.LanguageName;
            IsDirty = false;
            _ = RefreshDiagnosticsAsync();
        }

        return true;
    }

    private bool CanRunCurrent()
    {
        if (_currentProcessDocument is not null)
            return !_currentProcessDocument.IsRunning;

        return _activeDetachedSession is null && (!string.IsNullOrWhiteSpace(_currentFilePath) || !string.IsNullOrWhiteSpace(ScriptText));
    }

    private bool CanStopCurrent()
    {
        if (_currentProcessDocument is not null)
            return _currentProcessDocument.IsRunning;

        return _activeDetachedSession is not null;
    }

    private bool CanRestartCurrent()
    {
        if (CanStopCurrent())
            return true;

        return CanRunCurrent();
    }

    private async Task RunCurrentAsync()
    {
        if (_currentProcessDocument is not null)
        {
            await RunNodeAsync(_currentProcessDocument);
            return;
        }

        if (_currentDocumentKind == EditorDocumentKind.File && !string.IsNullOrWhiteSpace(_currentFilePath))
        {
            var fileNode = EditorProcessNode.CreateDetached(Path.GetFileNameWithoutExtension(_currentFilePath), InferKindFromPath(_currentFilePath), ScriptText, _currentFilePath, string.Empty, false);
            await RunDetachedAsync(fileNode);
            return;
        }

        if (!string.IsNullOrWhiteSpace(ScriptText))
        {
            var scratchNode = EditorProcessNode.CreateDetached(DocumentName, InferKindFromPath(DocumentPath), ScriptText, string.Empty, string.Empty, false);
            await RunDetachedAsync(scratchNode);
        }
    }

    private async Task RestartCurrentAsync()
    {
        StopCurrent();
        await Task.Delay(150);
        await RunCurrentAsync();
    }

    private void RunNodeFromCommand(EditorProcessNode? node)
    {
        if (node is null) return;
        _ = RunNodeAsync(node);
    }

    private void StopNodeFromCommand(EditorProcessNode? node)
    {
        if (node is null) return;
        StopNode(node);
    }

    private void RestartNodeFromCommand(EditorProcessNode? node)
    {
        if (node is null) return;
        _ = RestartNodeAsync(node);
    }

    private async Task RestartNodeAsync(EditorProcessNode node)
    {
        StopNode(node);
        await Task.Delay(150);
        await RunNodeAsync(node);
    }

    private async Task RunNodeAsync(EditorProcessNode node)
    {
        if (_activeSessions.ContainsKey(node.NodeId))
            return;

        var session = new EditorRunSession(node, Interlocked.Increment(ref _sessionCounter));
        _activeSessions[node.NodeId] = session;
        node.IsRunning = true;
        UpdateRunState();
        StatusText = $"Running: {node.DisplayName}";

        AppendSessionHeader(session, $"Started {node.DisplayName}");
        AppendTerminal("INFO", node.DisplayName, $"Run started for {node.DisplayName}", session);

        try
        {
            var result = await ExecuteNodeAsync(node, session);
            AppendTerminal(result.ExitCode == 0 ? "INFO" : "WARN", node.DisplayName, $"Run finished with exit code {result.ExitCode}", session);
            StatusText = result.ExitCode == 0
                ? $"Execution completed: {node.DisplayName}"
                : $"Execution failed with code {result.ExitCode}: {node.DisplayName}";
        }
        catch (OperationCanceledException)
        {
            AppendTerminal("WARN", node.DisplayName, "Execution stopped by user.", session);
            StatusText = $"Execution stopped: {node.DisplayName}";
        }
        catch (Exception ex)
        {
            AppendTerminal("ERROR", node.DisplayName, ex.Message, session);
            StatusText = $"Execution error: {node.DisplayName}";
        }
        finally
        {
            node.IsRunning = false;
            _activeSessions.Remove(node.NodeId);
            session.Dispose();
            UpdateRunState();
        }
    }

    private void StopCurrent()
    {
        if (_currentProcessDocument is not null)
        {
            StopNode(_currentProcessDocument);
            return;
        }

        StopDetachedSession();
    }

    private async Task RunDetachedAsync(EditorProcessNode node)
    {
        if (_activeDetachedSession is not null)
            return;

        var session = new EditorRunSession(node, Interlocked.Increment(ref _sessionCounter));
        _activeDetachedSession = session;
        UpdateRunState();
        StatusText = $"Running: {node.DisplayName}";
        AppendSessionHeader(session, $"Started {node.DisplayName}");
        AppendTerminal("INFO", node.DisplayName, $"Run started for {node.DisplayName}", session);

        try
        {
            var result = await ExecuteNodeAsync(node, session);
            AppendTerminal(result.ExitCode == 0 ? "INFO" : "WARN", node.DisplayName, $"Run finished with exit code {result.ExitCode}", session);
        }
        catch (OperationCanceledException)
        {
            AppendTerminal("WARN", node.DisplayName, "Execution stopped by user.", session);
        }
        catch (Exception ex)
        {
            AppendTerminal("ERROR", node.DisplayName, ex.Message, session);
        }
        finally
        {
            _activeDetachedSession = null;
            session.Dispose();
            UpdateRunState();
        }
    }

    private void StopNode(EditorProcessNode node)
    {
        if (!_activeSessions.TryGetValue(node.NodeId, out var session))
            return;

        StopSession(session);
    }

    private void StopDetachedSession()
    {
        if (_activeDetachedSession is null)
            return;

        StopSession(_activeDetachedSession);
    }

    private void StopAllSessions()
    {
        foreach (var session in _activeSessions.Values.ToList())
            StopSession(session);

        if (_activeDetachedSession is not null)
            StopSession(_activeDetachedSession);
    }

    private static void StopSession(EditorRunSession session)
    {
        try
        {
            session.Cts.Cancel();
        }
        catch
        {
        }

        try
        {
            if (session.Process is not null && !session.Process.HasExited)
                session.Process.Kill(entireProcessTree: true);
        }
        catch
        {
        }
    }

    private void UpdateRunState()
    {
        IsRunning = _activeSessions.Count > 0 || _activeDetachedSession is not null;
    }

    private async Task<EditorRunResult> ExecuteNodeAsync(EditorProcessNode node, EditorRunSession session)
    {
        var content = ScriptText;
        if (!ReferenceEquals(_currentProcessDocument, node))
            content = node.LoadDocumentText();

        return node.Kind switch
        {
            ProcessKind.PowerShellScript => await RunProcessAsync(
                "powershell.exe",
                BuildScriptArguments(node, content, ".ps1", "-ExecutionPolicy Bypass -NoProfile -File "),
                node.DisplayName,
                node.RunAsAdmin,
                session),
            ProcessKind.BatchScript => await RunProcessAsync(
                "cmd.exe",
                BuildScriptArguments(node, content, ".bat", "/c "),
                node.DisplayName,
                node.RunAsAdmin,
                session),
            ProcessKind.BashScript => await RunProcessAsync(
                "bash.exe",
                BuildBashArguments(node, content),
                node.DisplayName,
                false,
                session),
            ProcessKind.Installer => await RunProcessAsync(
                ResolveRunFile(node) ?? throw new FileNotFoundException($"Executable not found for {node.DisplayName}"),
                node.Arguments ?? string.Empty,
                node.DisplayName,
                node.RunAsAdmin,
                session),
            _ => new EditorRunResult(0)
        };
    }

    private string BuildScriptArguments(EditorProcessNode node, string content, string extension, string prefix)
    {
        var existingPath = ResolveRunFile(node);
        if (!string.IsNullOrWhiteSpace(content))
        {
            var tempPath = Path.Combine(GetEditorTempDir(), $"KlevaEditor_{Guid.NewGuid():N}{extension}");
            File.WriteAllText(tempPath, content, Encoding.UTF8);
            node.TransientExecutionPath = tempPath;
            return prefix + $"\"{tempPath}\"";
        }

        if (string.IsNullOrWhiteSpace(existingPath))
            throw new FileNotFoundException($"Script not found for {node.DisplayName}");

        return prefix + $"\"{existingPath}\"";
    }

    private string BuildBashArguments(EditorProcessNode node, string content)
    {
        var existingPath = ResolveRunFile(node);
        string pathToRun;

        if (!string.IsNullOrWhiteSpace(content))
        {
            pathToRun = Path.Combine(GetEditorTempDir(), $"KlevaEditor_{Guid.NewGuid():N}.sh");
            File.WriteAllText(pathToRun, content, Encoding.UTF8);
            node.TransientExecutionPath = pathToRun;
        }
        else if (!string.IsNullOrWhiteSpace(existingPath))
        {
            pathToRun = existingPath;
        }
        else
        {
            throw new FileNotFoundException($"Bash script not found for {node.DisplayName}");
        }

        var bashPath = ToBashPath(Path.GetFullPath(pathToRun));
        return $"-lc \"bash '{bashPath}'\"";
    }

    private async Task<EditorRunResult> RunProcessAsync(string fileName, string arguments, string sourceName, bool runAsAdmin, EditorRunSession session)
    {
        var ct = session.Cts.Token;
        var tempDir = GetEditorTempDir();
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = DetermineWorkingDirectory(fileName),
            CreateNoWindow = true,
            UseShellExecute = runAsAdmin,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = !runAsAdmin,
            RedirectStandardError = !runAsAdmin
        };

        if (!runAsAdmin)
        {
            psi.Environment["KLEVADEPLOY_STORAGE_DIR"] = GetStorageDir();
            psi.Environment["KLEVADEPLOY_DATA_DIR"] = GetStorageDir();
            psi.Environment["KLEVADEPLOY_TEMP_DIR"] = tempDir;
            psi.Environment["TEMP"] = tempDir;
            psi.Environment["TMP"] = tempDir;
            psi.StandardOutputEncoding = Encoding.UTF8;
            psi.StandardErrorEncoding = Encoding.UTF8;
        }

        AppendTerminal("CMD", sourceName, $"{fileName} {arguments}".Trim(), session);

        if (runAsAdmin)
        {
            psi.Verb = "runas";
            AppendTerminal("WARN", sourceName, "Running as admin. Live output streaming is not available for this session.", session);
        }

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        session.Process = process;

        process.Start();

        var stdoutTask = runAsAdmin ? Task.CompletedTask : PumpStreamAsync(process.StandardOutput, "STDOUT", sourceName, session, ct);
        var stderrTask = runAsAdmin ? Task.CompletedTask : PumpStreamAsync(process.StandardError, "STDERR", sourceName, session, ct);

        await process.WaitForExitAsync(ct);
        await Task.WhenAll(stdoutTask, stderrTask);

        return new EditorRunResult(process.ExitCode);
    }

    private async Task PumpStreamAsync(StreamReader reader, string level, string sourceName, EditorRunSession session, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct);
            if (line is null)
                break;
            if (string.IsNullOrWhiteSpace(line))
                continue;
            AppendTerminal(level, sourceName, line, session);
        }
    }

    private void AppendSessionHeader(EditorRunSession session, string message)
    {
        AppendTerminal("SESSION", session.Node.DisplayName, message, session, isSessionHeader: true);
    }

    private void AppendTerminal(string level, string source, string message, EditorRunSession? session = null, bool isSessionHeader = false)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        App.Current?.Dispatcher.Invoke(() =>
        {
            TerminalEntries.Add(new EditorTerminalEntry(DateTime.Now, session?.SessionLabel ?? string.Empty, level, source, message, isSessionHeader));
            if (TerminalEntries.Count > 5000)
                TerminalEntries.RemoveAt(0);
            TerminalView.Refresh();
            RaiseTerminalStateChanged();
        });
    }

    private void ClearTerminal()
    {
        TerminalEntries.Clear();
        TerminalView.Refresh();
        RaiseTerminalStateChanged();
        StatusText = "Terminal cleared.";
    }

    private void ExportTerminal()
    {
        var dlg = new SaveFileDialog
        {
            Title = "Export terminal log",
            Filter = "Text files (*.txt)|*.txt|Log files (*.log)|*.log|All files (*.*)|*.*",
            FileName = "editor-terminal.log",
            AddExtension = true,
            OverwritePrompt = true
        };

        if (dlg.ShowDialog() != true)
            return;

        var lines = TerminalEntries.Select(entry => $"{entry.Timestamp:HH:mm:ss}\t{entry.Session}\t{entry.Level}\t{entry.Source}\t{entry.Message}");
        File.WriteAllLines(dlg.FileName, lines, Encoding.UTF8);
        StatusText = $"Terminal exported: {Path.GetFileName(dlg.FileName)}";
    }

    private bool FilterTerminalEntry(object obj)
    {
        if (obj is not EditorTerminalEntry entry)
            return false;

        if (!string.Equals(TerminalLevelFilter, "All", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(entry.Level, TerminalLevelFilter, StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.IsNullOrWhiteSpace(TerminalSearchText))
            return true;

        return entry.Message.Contains(TerminalSearchText, StringComparison.OrdinalIgnoreCase) ||
               entry.Source.Contains(TerminalSearchText, StringComparison.OrdinalIgnoreCase) ||
               entry.Session.Contains(TerminalSearchText, StringComparison.OrdinalIgnoreCase);
    }

    private void RefreshFiles()
    {
        FileNodes.Clear();
        var rootPath = DetermineExplorerRoot();
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            return;

        FileNodes.Add(BuildFileNode(rootPath));
        StatusText = $"Explorer rooted at: {rootPath}";
    }

    private EditorFileNode BuildFileNode(string path)
    {
        var node = new EditorFileNode(path, Directory.Exists(path));
        if (!node.IsDirectory)
            return node;

        try
        {
            foreach (var dir in Directory.GetDirectories(path).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                node.Children.Add(BuildFileNode(dir));

            foreach (var file in Directory.GetFiles(path).OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
                node.Children.Add(BuildFileNode(file));
        }
        catch
        {
        }

        return node;
    }

    private void SetDocumentState(string name, string path, string text, string language, bool markClean)
    {
        DocumentName = name;
        DocumentPath = path;
        LanguageName = language;
        if (SelectedDocument is not null)
        {
            SelectedDocument.Name = name;
            SelectedDocument.Path = path;
            SelectedDocument.LanguageName = language;
            SelectedDocument.ScriptText = text;
            SelectedDocument.IsDirty = !markClean;
        }
        _scriptText = text;
        OnPropertyChanged(nameof(ScriptText));
        _ = RefreshDiagnosticsAsync();
        IsDirty = !markClean;
        UpdateCommandStates();
        RaisePresentationStateChanged();
    }

    private void UpdateCommandStates()
    {
        RunCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
        RestartCommand.NotifyCanExecuteChanged();
        UpdateUndoRedoState();
    }

    private void CaptureUndoSnapshot()
    {
        if (_suppressUndoCapture)
            return;

        var document = SelectedDocument;
        if (document is null)
            return;

        var state = document.UndoState;
        var current = ScriptText;
        if (string.Equals(state.LastCaptured, current, StringComparison.Ordinal))
            return;

        state.Undo.Add(state.LastCaptured);
        state.LastCaptured = current;
        state.Redo.Clear();

        if (state.Undo.Count > 60)
            state.Undo.RemoveAt(0);

        UpdateUndoRedoState();
    }

    private void UpdateUndoRedoState()
    {
        var state = SelectedDocument?.UndoState;
        CanUndo = state is not null && state.Undo.Count > 0;
        CanRedo = state is not null && state.Redo.Count > 0;
    }

    private void Undo()
    {
        var document = SelectedDocument;
        if (document is null)
            return;

        var state = document.UndoState;
        if (state.Undo.Count == 0)
            return;

        _suppressUndoCapture = true;
        try
        {
            state.Redo.Add(state.LastCaptured);
            var restored = state.Undo[^1];
            state.Undo.RemoveAt(state.Undo.Count - 1);
            state.LastCaptured = restored;
            ScriptText = restored;
            StatusText = "Undo applied.";
        }
        finally
        {
            _suppressUndoCapture = false;
            UpdateUndoRedoState();
        }
    }

    private void Redo()
    {
        var document = SelectedDocument;
        if (document is null)
            return;

        var state = document.UndoState;
        if (state.Redo.Count == 0)
            return;

        _suppressUndoCapture = true;
        try
        {
            state.Undo.Add(state.LastCaptured);
            var restored = state.Redo[^1];
            state.Redo.RemoveAt(state.Redo.Count - 1);
            state.LastCaptured = restored;
            ScriptText = restored;
            StatusText = "Redo applied.";
        }
        finally
        {
            _suppressUndoCapture = false;
            UpdateUndoRedoState();
        }
    }

    private void RaisePresentationStateChanged()
    {
        OnPropertyChanged(nameof(HasOpenDocuments));
        OnPropertyChanged(nameof(ShowEmptyEditorState));
        OnPropertyChanged(nameof(IsProcessDocumentActive));
        OnPropertyChanged(nameof(IsFileDocumentActive));
        OnPropertyChanged(nameof(IsScratchDocumentActive));
        OnPropertyChanged(nameof(ShowExecutionToolbar));
        OnPropertyChanged(nameof(ShowDiagnosticsToolbar));
        OnPropertyChanged(nameof(ShowDiagnosticsPanel));
        OnPropertyChanged(nameof(DiagnosticsSummary));
        OnPropertyChanged(nameof(DiagnosticsSummaryHint));
        OnPropertyChanged(nameof(CurrentDocumentKindLabel));
        OnPropertyChanged(nameof(CurrentDocumentSubtitle));
        OnPropertyChanged(nameof(CurrentDocumentHint));
        OnPropertyChanged(nameof(ActivityStateLabel));
        OnPropertyChanged(nameof(DiagnosticsStateLabel));
        OnPropertyChanged(nameof(DiagnosticsPanelToggleLabel));
        OnPropertyChanged(nameof(HasErrorDiagnostics));
        OnPropertyChanged(nameof(HasWarningDiagnostics));
    }

    private void RaiseLayoutStateChanged()
    {
        OnPropertyChanged(nameof(ShowNavigatorPane));
        OnPropertyChanged(nameof(ShowExplorerPane));
        OnPropertyChanged(nameof(ShowTerminalPane));
        OnPropertyChanged(nameof(ShowHeaderHint));
        OnPropertyChanged(nameof(ShowHeaderSecondaryBadges));
        OnPropertyChanged(nameof(ShowDocumentPath));
        OnPropertyChanged(nameof(ShowDocumentLanguageBadge));
        OnPropertyChanged(nameof(ShowDocumentWorkspaceCaption));
        OnPropertyChanged(nameof(ShowDocumentMetaGroups));
        OnPropertyChanged(nameof(ShowTerminalDescription));
        OnPropertyChanged(nameof(TerminalLevelFilterWidth));
        OnPropertyChanged(nameof(TerminalSearchBoxWidth));
        OnPropertyChanged(nameof(OpenDocumentsBadgeText));
        OnPropertyChanged(nameof(LayoutModeLabel));
    }

    private void RaiseTerminalStateChanged()
    {
        OnPropertyChanged(nameof(HasTerminalEntries));
        OnPropertyChanged(nameof(HasVisibleTerminalEntries));
        OnPropertyChanged(nameof(ShowTerminalPlaceholder));
        OnPropertyChanged(nameof(ShowTerminalEmptyState));
        OnPropertyChanged(nameof(ShowTerminalFilteredEmptyState));
        OnPropertyChanged(nameof(TerminalEmptyStateTitle));
        OnPropertyChanged(nameof(TerminalEmptyStateMessage));
    }

    private async Task RefreshDiagnosticsAsync()
    {
        var normalized = ScriptText.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var version = Interlocked.Increment(ref _diagnosticVersion);
        var diagnostics = await Task.Run(() => BuildDiagnosticsSnapshot(normalized));

        if (version != _diagnosticVersion)
            return;

        Diagnostics.Clear();
        foreach (var item in diagnostics.OrderBy(d => d.Line).ThenBy(d => d.Severity))
            Diagnostics.Add(item);

        OnPropertyChanged(nameof(HasDiagnostics));
        OnPropertyChanged(nameof(DiagnosticsSummary));
        RaisePresentationStateChanged();
    }

    private List<EditorDiagnostic> BuildDiagnosticsSnapshot(string normalized)
    {
        var results = new List<EditorDiagnostic>();
        if (string.IsNullOrWhiteSpace(normalized))
            return results;

        var lines = normalized.Split('\n');
        var stack = new Stack<(char ch, int line)>();
        var inSingle = false;
        var inDouble = false;

        for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
        {
            var line = lines[lineIndex];

            for (var i = 0; i < line.Length; i++)
            {
                var ch = line[i];
                var escaped = i > 0 && line[i - 1] == '\\';

                if (ch == '\'' && !inDouble && !escaped)
                {
                    inSingle = !inSingle;
                    continue;
                }

                if (ch == '"' && !inSingle && !escaped)
                {
                    inDouble = !inDouble;
                    continue;
                }

                if (inSingle || inDouble)
                    continue;

                if (ch is '(' or '{' or '[')
                    stack.Push((ch, lineIndex + 1));
                else if (ch is ')' or '}' or ']')
                {
                    if (stack.Count == 0)
                    {
                        results.Add(new EditorDiagnostic(lineIndex + 1, $"Unexpected closing token '{ch}'.", "ERROR", i + 1, 1));
                        continue;
                    }

                    var open = stack.Pop();
                    if (!TokensMatch(open.ch, ch))
                        results.Add(new EditorDiagnostic(lineIndex + 1, $"Mismatched token '{open.ch}' ... '{ch}'.", "ERROR", i + 1, 1));
                }
            }

            if (LanguageName == "PowerShell" &&
                line.TrimEnd().EndsWith("`", StringComparison.Ordinal))
            {
                results.Add(new EditorDiagnostic(lineIndex + 1, "Line ends with a PowerShell continuation character.", "WARN", Math.Max(1, line.TrimEnd().Length), 1));
            }
        }

        while (stack.Count > 0)
        {
            var open = stack.Pop();
            results.Add(new EditorDiagnostic(open.line, $"Unclosed token '{open.ch}'.", "ERROR", 1, 1));
        }

        if (inSingle)
            results.Add(new EditorDiagnostic(lines.Length, "Unclosed single-quoted string.", "ERROR", 1, Math.Max(1, lines[^1].Length)));
        if (inDouble)
            results.Add(new EditorDiagnostic(lines.Length, "Unclosed double-quoted string.", "ERROR", 1, Math.Max(1, lines[^1].Length)));

        if (LanguageName == "PowerShell")
            results.AddRange(GetPowerShellParserDiagnostics(normalized));

        return results
            .DistinctBy(d => $"{d.Line}|{d.Severity}|{d.Message}")
            .ToList();
    }

    private static bool TokensMatch(char open, char close) =>
        open == '(' && close == ')' ||
        open == '{' && close == '}' ||
        open == '[' && close == ']';

    private List<EditorDiagnostic> GetPowerShellParserDiagnostics(string scriptText)
    {
        var tempFile = Path.Combine(GetEditorTempDir(), $"KlevaEditor_diag_{Guid.NewGuid():N}.ps1");
        try
        {
            File.WriteAllText(tempFile, scriptText, Encoding.UTF8);
            var quotedPath = tempFile.Replace("'", "''", StringComparison.Ordinal);
            var command =
                "$p='" + quotedPath + "';" +
                "$tokens=$null;$errors=$null;" +
                "[System.Management.Automation.Language.Parser]::ParseInput((Get-Content -LiteralPath $p -Raw),[ref]$tokens,[ref]$errors)|Out-Null;" +
                "$errors|ForEach-Object{" +
                "$line=$_.Extent.StartLineNumber;" +
                "$col=$_.Extent.StartColumnNumber;" +
                "$len=[Math]::Max(1,$_.Extent.EndColumnNumber-$_.Extent.StartColumnNumber);" +
                "$msg=(($_.Message -replace '[\\r\\n]+',' ').Trim());" +
                "('{0}`t{1}`t{2}`t{3}' -f $line,$col,$len,$msg)" +
                "}";

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{command}\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = new Process { StartInfo = psi };
            process.Start();
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit(2500);

            var results = new List<EditorDiagnostic>();
            foreach (var line in stdout.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('\t', 4);
                if (parts.Length != 4) continue;
                if (!int.TryParse(parts[0], out var parsedLine)) parsedLine = 1;
                if (!int.TryParse(parts[1], out var parsedColumn)) parsedColumn = 1;
                if (!int.TryParse(parts[2], out var parsedLength)) parsedLength = 1;
                results.Add(new EditorDiagnostic(parsedLine, parts[3], "ERROR", parsedColumn, Math.Max(1, parsedLength)));
            }

            if (results.Count == 0 && !string.IsNullOrWhiteSpace(stderr))
                results.Add(new EditorDiagnostic(1, $"PowerShell parser diagnostics unavailable: {stderr.Trim()}", "WARN", 1, 1));

            return results;
        }
        catch (Exception ex)
        {
            return [new EditorDiagnostic(1, $"PowerShell parser diagnostics unavailable: {ex.Message}", "WARN", 1, 1)];
        }
        finally
        {
            try { File.Delete(tempFile); } catch { }
        }
    }

    private static ProcessKind InferKindFromPath(string? path)
    {
        var ext = Path.GetExtension(path ?? string.Empty).ToLowerInvariant();
        return ext switch
        {
            ".ps1" => ProcessKind.PowerShellScript,
            ".bat" or ".cmd" => ProcessKind.BatchScript,
            ".sh" => ProcessKind.BashScript,
            _ => ProcessKind.PowerShellScript
        };
    }

    private static string GetLanguageNameFromPath(string? path)
    {
        return InferKindFromPath(path) switch
        {
            ProcessKind.PowerShellScript => "PowerShell",
            ProcessKind.BatchScript => "Batch",
            ProcessKind.BashScript => "Bash",
            _ => "Text"
        };
    }

    private static string GetProcessKindLabel(ProcessKind kind) => kind switch
    {
        ProcessKind.PowerShellScript => "PowerShell script",
        ProcessKind.BatchScript => "Batch script",
        ProcessKind.BashScript => "Bash script",
        ProcessKind.RegistryFile => "Registry action",
        ProcessKind.ConfigAction => "Config action",
        _ => "Installer"
    };

    private string BuildDocumentSubtitle(string primaryText)
    {
        var parts = new List<string> { primaryText };

        if (!string.IsNullOrWhiteSpace(ActivityStateLabel))
            parts.Add(ActivityStateLabel);

        if (!string.IsNullOrWhiteSpace(DiagnosticsStateLabel))
            parts.Add(DiagnosticsStateLabel);

        return string.Join("  •  ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
    }

    private string DetermineExplorerRoot()
    {
        foreach (var candidate in EnumerateCandidatePaths(_sourceViewModel.FilePath))
        {
            if (File.Exists(candidate))
                return Path.GetDirectoryName(candidate) ?? AppContext.BaseDirectory;
            if (Directory.Exists(candidate))
                return candidate;
        }

        return AppContext.BaseDirectory;
    }

    private static IEnumerable<string> EnumerateCandidatePaths(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            yield break;

        if (Path.IsPathRooted(path))
        {
            yield return path;
            yield break;
        }

        var storageDir = GetStorageDir();
        yield return Path.Combine(storageDir, path);
        yield return Path.Combine(AppContext.BaseDirectory, path);
    }

    private static string? ResolveCandidatePath(string? path)
    {
        foreach (var candidate in EnumerateCandidatePaths(path))
        {
            if (File.Exists(candidate) || Directory.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string? ResolveRunFile(EditorProcessNode node) =>
        !string.IsNullOrWhiteSpace(node.TransientExecutionPath) && File.Exists(node.TransientExecutionPath)
            ? node.TransientExecutionPath
            : ResolveCandidatePath(node.RelativePath);

    private static string DetermineWorkingDirectory(string fileName)
    {
        if (File.Exists(fileName))
            return Path.GetDirectoryName(fileName) ?? GetEditorTempDir();
        return GetEditorTempDir();
    }

    private static double Clamp(double value, double min, double max)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
            return min;

        return Math.Min(max, Math.Max(min, value));
    }

    private static string GetStorageDir()
    {
        var storageDir = Environment.GetEnvironmentVariable("KLEVADEPLOY_STORAGE_DIR");
        return string.IsNullOrWhiteSpace(storageDir)
            ? Path.Combine(AppContext.BaseDirectory, "Data")
            : storageDir;
    }

    private static string GetEditorTempDir()
    {
        var dir = Path.Combine(GetStorageDir(), "temp", "Editor");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string ToBashPath(string windowsPath)
    {
        var full = Path.GetFullPath(windowsPath).Replace('\\', '/');
        if (full.Length >= 2 && full[1] == ':')
        {
            var drive = char.ToLowerInvariant(full[0]);
            return $"/mnt/{drive}{full[2..]}";
        }

        return full;
    }

    internal sealed class UndoRedoState
    {
        public List<string> Undo { get; } = new();
        public List<string> Redo { get; } = new();
        public string LastCaptured { get; set; } = string.Empty;
    }

    public enum EditorDocumentKind
    {
        None,
        Process,
        File,
        Scratch
    }

    public sealed partial class EditorDocumentTab : ObservableObject
    {
        [ObservableProperty]
        private EditorDocumentKind kind;

        [ObservableProperty]
        private string name;

        [ObservableProperty]
        private string path;

        [ObservableProperty]
        private string languageName;

        [ObservableProperty]
        private string scriptText;

        [ObservableProperty]
        private bool isDirty;

        public EditorProcessNode? ProcessNode { get; }
        public string? FilePath { get; set; }
        public string HeaderText => IsDirty ? $"{Name} *" : Name;
        internal UndoRedoState UndoState { get; } = new();

        public EditorDocumentTab(
            EditorDocumentKind kind,
            string name,
            string path,
            string languageName,
            string scriptText,
            bool isDirty,
            EditorProcessNode? processNode,
            string? filePath)
        {
            this.kind = kind;
            this.name = name;
            this.path = path;
            this.languageName = languageName;
            this.scriptText = scriptText;
            this.isDirty = isDirty;
            ProcessNode = processNode;
            FilePath = filePath;
            UndoState.LastCaptured = scriptText;
        }

        partial void OnNameChanged(string value) => OnPropertyChanged(nameof(HeaderText));
        partial void OnIsDirtyChanged(bool value) => OnPropertyChanged(nameof(HeaderText));
    }

    public sealed partial class EditorProcessNode : ObservableObject
    {
        private readonly Func<string> _readScript;
        private readonly Action<string> _writeScript;
        private readonly Func<string> _readPath;
        private readonly Action<string> _writePath;
        private readonly Func<string> _readArguments;
        private readonly Func<bool> _readRunAsAdmin;

        public string DisplayName { get; }
        public Guid NodeId { get; } = Guid.NewGuid();
        public ProcessKind Kind { get; }
        public string KindLabel => GetProcessKindLabel(Kind);
        public string? ProcessId { get; }
        public ObservableCollection<EditorProcessNode> Children { get; } = new();

        [ObservableProperty]
        private bool isRunning;

        internal string? TransientExecutionPath { get; set; }

        public string RelativePath => _readPath();
        public string Arguments => _readArguments();
        public bool RunAsAdmin => _readRunAsAdmin();

        public EditorProcessNode(
            string displayName,
            ProcessKind kind,
            string? processId,
            Func<string> readScript,
            Action<string> writeScript,
            Func<string> readPath,
            Action<string> writePath,
            Func<string> readArguments,
            Func<bool> readRunAsAdmin)
        {
            DisplayName = displayName;
            Kind = kind;
            ProcessId = processId;
            _readScript = readScript;
            _writeScript = writeScript;
            _readPath = readPath;
            _writePath = writePath;
            _readArguments = readArguments;
            _readRunAsAdmin = readRunAsAdmin;
        }

        public static EditorProcessNode CreateDetached(string displayName, ProcessKind kind, string scriptContent, string relativePath, string arguments, bool runAsAdmin)
        {
            var script = scriptContent;
            var path = relativePath;
            return new EditorProcessNode(
                displayName,
                kind,
                null,
                () => script,
                value => script = value,
                () => path,
                value => path = value,
                () => arguments,
                () => runAsAdmin);
        }

        public string LoadDocumentText()
        {
            var script = _readScript();
            if (!string.IsNullOrWhiteSpace(script))
                return script;

            var path = ResolveCandidatePath(_readPath());
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                return File.ReadAllText(path);

            return string.Empty;
        }

        public void ApplyDocumentText(string text)
        {
            _writeScript(text);
            if (!string.IsNullOrWhiteSpace(text))
                _writePath(string.Empty);
            OnPropertyChanged(nameof(RelativePath));
        }

        public string ResolveDisplayPath()
        {
            var resolved = ResolveCandidatePath(_readPath());
            return resolved ?? string.Empty;
        }

        public string GetLanguageName() => Kind switch
        {
            ProcessKind.PowerShellScript => "PowerShell",
            ProcessKind.BatchScript => "Batch",
            ProcessKind.BashScript => "Bash",
            _ => "Text"
        };
    }

    public sealed class EditorFileNode
    {
        public string FullPath { get; }
        public string Name => Path.GetFileName(FullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        public bool IsDirectory { get; }
        public ObservableCollection<EditorFileNode> Children { get; } = new();

        public EditorFileNode(string fullPath, bool isDirectory)
        {
            FullPath = fullPath;
            IsDirectory = isDirectory;
        }
    }

    public sealed record EditorTerminalEntry(DateTime Timestamp, string Session, string Level, string Source, string Message, bool IsSessionHeader);
    public sealed record EditorDiagnostic(int Line, string Message, string Severity, int Column = 1, int Length = 1);
    private sealed record EditorRunResult(int ExitCode);
    private sealed class EditorRunSession : IDisposable
    {
        public EditorProcessNode Node { get; }
        public CancellationTokenSource Cts { get; } = new();
        public Process? Process { get; set; }
        public string SessionLabel { get; }

        public EditorRunSession(EditorProcessNode node, int sessionNumber)
        {
            Node = node;
            SessionLabel = $"Run {sessionNumber}";
        }

        public void Dispose()
        {
            try { Process?.Dispose(); } catch { }
            try { Cts.Dispose(); } catch { }
        }
    }
}
