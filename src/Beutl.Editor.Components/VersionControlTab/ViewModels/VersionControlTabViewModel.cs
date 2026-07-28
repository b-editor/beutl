using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive.Disposables;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Threading;
using Beutl.Editor.VersionControl;
using Beutl.Extensibility;
using Beutl.Services;
using FluentAvalonia.UI.Controls;
using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace Beutl.Editor.Components.VersionControlTab.ViewModels;

public sealed class VersionControlTabViewModel : IToolContext
{
    internal const int HistoryPageSize = 50;

    private readonly IEditorContext _editorContext;
    private readonly IProjectVersionControlService? _service;
    private readonly IRepositoryLockRecoveryService? _lockRecoveryService;
    private readonly IVersionControlRestoreCoordinator? _restoreCoordinator;
    private readonly Action<Action> _postToUi;
    private readonly CompositeDisposable _disposables = [];
    private readonly SemaphoreSlim _historyGate = new(1, 1);
    private CancellationTokenSource? _selectionCancellation;
    private int _nextHistoryOffset;
    private bool _disposed;

    public VersionControlTabViewModel(
        ToolTabExtension extension,
        IEditorContext editorContext)
        : this(
            extension,
            editorContext,
            editorContext.GetService(typeof(IProjectVersionControlService))
                as IProjectVersionControlService,
            editorContext.GetService(typeof(IVersionControlRestoreCoordinator))
                as IVersionControlRestoreCoordinator,
            PostToUiThread)
    {
    }

    internal VersionControlTabViewModel(
        ToolTabExtension extension,
        IEditorContext editorContext,
        IProjectVersionControlService? service,
        IVersionControlRestoreCoordinator? restoreCoordinator,
        Action<Action> postToUi)
    {
        Extension = extension ?? throw new ArgumentNullException(nameof(extension));
        _editorContext = editorContext ?? throw new ArgumentNullException(nameof(editorContext));
        _service = service;
        _lockRecoveryService = service as IRepositoryLockRecoveryService;
        _restoreCoordinator = restoreCoordinator;
        _postToUi = postToUi ?? throw new ArgumentNullException(nameof(postToUi));

        IsTracked = new ReactivePropertySlim<bool>(service?.Repository is not null)
            .DisposeWith(_disposables);
        IsUnavailable = new ReactivePropertySlim<bool>()
            .DisposeWith(_disposables);
        IsConflicted = new ReactivePropertySlim<bool>()
            .DisposeWith(_disposables);
        HasBlockingGuidance = new ReactivePropertySlim<bool>()
            .DisposeWith(_disposables);
        HasRecoverableLock = new ReactivePropertySlim<bool>(
                _lockRecoveryService?.RecoverableLock is not null)
            .DisposeWith(_disposables);
        BranchText = new ReactivePropertySlim<string>()
            .DisposeWith(_disposables);
        AheadBehindText = new ReactivePropertySlim<string>()
            .DisposeWith(_disposables);
        DirtySummary = new ReactivePropertySlim<string>()
            .DisposeWith(_disposables);
        StatusMessage = new ReactivePropertySlim<string>(
                IsTracked.Value
                    ? string.Empty
                    : Strings.VersionControl_NoRepository)
            .DisposeWith(_disposables);
        IsLoading = new ReactivePropertySlim<bool>()
            .DisposeWith(_disposables);
        HasMoreHistory = new ReactivePropertySlim<bool>(IsTracked.Value)
            .DisposeWith(_disposables);
        SelectedCommit = new ReactivePropertySlim<VersionControlCommitViewModel?>()
            .DisposeWith(_disposables);
        SelectedFile = new ReactivePropertySlim<VersionControlFileChangeViewModel?>()
            .DisposeWith(_disposables);

        LoadMoreCommand = new AsyncReactiveCommand()
            .WithSubscribe(LoadMoreAsync)
            .DisposeWith(_disposables);
        RemoveStaleLockCommand = new AsyncReactiveCommand(HasRecoverableLock)
            .WithSubscribe(RemoveStaleLockAsync)
            .DisposeWith(_disposables);
        RequestBranchNameAsync = ShowBranchNameDialogAsync;

        if (_service is not null)
        {
            _service.StatusChanged += OnStatusChanged;
        }

        if (_lockRecoveryService is not null)
        {
            _lockRecoveryService.RecoverableLockAvailable += OnRecoverableLockAvailable;
        }

        Initialization = InitializeAsync();
    }

    public ToolTabExtension Extension { get; }

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public string Header => Strings.VersionControl;

    public ReactivePropertySlim<bool> IsTracked { get; }

    public ReactivePropertySlim<bool> IsUnavailable { get; }

    public ReactivePropertySlim<bool> IsConflicted { get; }

    public ReactivePropertySlim<bool> HasBlockingGuidance { get; }

    public ReactivePropertySlim<bool> HasRecoverableLock { get; }

    public ReactivePropertySlim<string> BranchText { get; }

    public ReactivePropertySlim<string> AheadBehindText { get; }

    public ReactivePropertySlim<string> DirtySummary { get; }

    public ReactivePropertySlim<string> StatusMessage { get; }

    public ReactivePropertySlim<bool> IsLoading { get; }

    public ReactivePropertySlim<bool> HasMoreHistory { get; }

    public ObservableCollection<VersionControlCommitViewModel> Commits { get; } = [];

    public ObservableCollection<VersionControlFileChangeViewModel> ChangedFiles { get; } = [];

    public ObservableCollection<VersionControlDiffLineViewModel> DiffLines { get; } = [];

    public ReactivePropertySlim<VersionControlCommitViewModel?> SelectedCommit { get; }

    public ReactivePropertySlim<VersionControlFileChangeViewModel?> SelectedFile { get; }

    public AsyncReactiveCommand LoadMoreCommand { get; }

    public AsyncReactiveCommand RemoveStaleLockCommand { get; }

    public Task Initialization { get; }

    public Func<CommitInfo, Task<string?>> RequestBranchNameAsync { get; set; }

    public async Task LoadMoreAsync()
    {
        if (_service?.Repository is null || !HasMoreHistory.Value)
        {
            return;
        }

        await _historyGate.WaitAsync();
        try
        {
            await LoadNextPageCoreAsync();
        }
        finally
        {
            _historyGate.Release();
        }
    }

    public async Task SelectCommitAsync(VersionControlCommitViewModel? commit)
    {
        SelectedCommit.Value = commit;
        SelectedFile.Value = null;
        ChangedFiles.Clear();
        DiffLines.Clear();
        CancellationToken cancellationToken = ReplaceSelectionCancellation();
        if (_service is null || commit is null)
        {
            return;
        }

        IReadOnlyList<FileChange> files = await _service.GetCommitFilesAsync(
            commit.Commit.Sha,
            cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        foreach (FileChange file in files)
        {
            ChangedFiles.Add(new VersionControlFileChangeViewModel(file));
        }
    }

    public async Task SelectFileAsync(VersionControlFileChangeViewModel? file)
    {
        SelectedFile.Value = file;
        DiffLines.Clear();
        CancellationToken cancellationToken = ReplaceSelectionCancellation();
        if (_service is null || SelectedCommit.Value is not { } commit || file is null)
        {
            return;
        }

        string diff = await _service.GetDiffAsync(
            commit.Commit.Sha,
            file.Change.Path,
            cancellationToken);
        if (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        foreach (VersionControlDiffLineViewModel line in VersionControlDiffLineViewModel.Parse(diff))
        {
            DiffLines.Add(line);
        }
    }

    public object? GetService(Type serviceType)
    {
        return _editorContext.GetService(serviceType);
    }

    public void ReadFromJson(JsonObject json)
    {
    }

    public void WriteToJson(JsonObject json)
    {
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_service is not null)
        {
            _service.StatusChanged -= OnStatusChanged;
        }

        if (_lockRecoveryService is not null)
        {
            _lockRecoveryService.RecoverableLockAvailable -= OnRecoverableLockAvailable;
        }

        _selectionCancellation?.Cancel();
        _selectionCancellation?.Dispose();
        foreach (VersionControlCommitViewModel commit in Commits)
        {
            commit.Dispose();
        }

        Commits.Clear();
        ChangedFiles.Clear();
        DiffLines.Clear();
        IsSelected.Dispose();
        _disposables.Dispose();
    }

    internal async Task<bool> RestoreAsync(CommitInfo commit)
    {
        if (_restoreCoordinator is null)
        {
            return false;
        }

        return await _restoreCoordinator.RestoreAsync(
            commit.Sha,
            CancellationToken.None);
    }

    internal async Task<bool> RestoreToNewBranchAsync(CommitInfo commit)
    {
        if (_restoreCoordinator is null)
        {
            return false;
        }

        string? branchName = await RequestBranchNameAsync(commit);
        if (string.IsNullOrWhiteSpace(branchName))
        {
            return false;
        }

        return await _restoreCoordinator.RestoreToNewBranchAsync(
            commit.Sha,
            branchName.Trim(),
            CancellationToken.None);
    }

    internal async Task RemoveStaleLockAsync()
    {
        if (_lockRecoveryService is null)
        {
            return;
        }

        await _lockRecoveryService.RemoveRecoverableLockAsync(CancellationToken.None);
        HasRecoverableLock.Value = _lockRecoveryService.RecoverableLock is not null;
    }

    private async Task InitializeAsync()
    {
        if (_service is null)
        {
            return;
        }

        GitAvailability availability = await _service.GetAvailabilityAsync(CancellationToken.None);
        if (availability.State != GitAvailabilityState.Installed)
        {
            IsUnavailable.Value = true;
            HasBlockingGuidance.Value = true;
            IsTracked.Value = false;
            HasMoreHistory.Value = false;
            StatusMessage.Value = GetAvailabilityMessage(availability);
            return;
        }

        if (_service.Repository is null)
        {
            StatusMessage.Value = Strings.VersionControl_NoRepository;
            return;
        }

        WorkspaceStatus status = await _service.GetStatusAsync(CancellationToken.None);
        ApplyStatus(status);
        if (!status.HasConflicts)
        {
            await RefreshHistoryAsync();
        }
    }

    internal static string GetAvailabilityMessage(GitAvailability availability)
    {
        ArgumentNullException.ThrowIfNull(availability);
        string stateMessage = availability.State switch
        {
            GitAvailabilityState.VersionTooOld => string.Format(
                CultureInfo.CurrentCulture,
                Strings.VersionControl_GitTooOldFormat,
                availability.Version?.ToString() ?? "—"),
            _ => Strings.VersionControl_GitNotInstalled,
        };
        string installMessage = OperatingSystem.IsWindows()
            ? Strings.VersionControl_InstallGitWindows
            : OperatingSystem.IsMacOS()
                ? Strings.VersionControl_InstallGitMacOS
                : Strings.VersionControl_InstallGitLinux;
        return $"{stateMessage}\n\n{installMessage}";
    }

    private async Task RefreshHistoryAsync()
    {
        if (_service?.Repository is null)
        {
            return;
        }

        await _historyGate.WaitAsync();
        try
        {
            string? selectedSha = SelectedCommit.Value?.Commit.Sha;
            foreach (VersionControlCommitViewModel commit in Commits)
            {
                commit.Dispose();
            }

            Commits.Clear();
            ChangedFiles.Clear();
            DiffLines.Clear();
            SelectedCommit.Value = null;
            SelectedFile.Value = null;
            _nextHistoryOffset = 0;
            HasMoreHistory.Value = true;
            await LoadNextPageCoreAsync();
            if (selectedSha is not null)
            {
                SelectedCommit.Value = Commits.FirstOrDefault(
                    item => string.Equals(
                        item.Commit.Sha,
                        selectedSha,
                        StringComparison.Ordinal));
            }
        }
        finally
        {
            _historyGate.Release();
        }
    }

    private async Task LoadNextPageCoreAsync()
    {
        if (_service is null)
        {
            return;
        }

        IsLoading.Value = true;
        try
        {
            IReadOnlyList<CommitInfo> page = await _service.GetHistoryAsync(
                _nextHistoryOffset,
                HistoryPageSize,
                CancellationToken.None);
            foreach (CommitInfo commit in page)
            {
                Commits.Add(new VersionControlCommitViewModel(this, commit));
            }

            _nextHistoryOffset += page.Count;
            HasMoreHistory.Value = page.Count == HistoryPageSize;
            StatusMessage.Value = Commits.Count == 0
                ? Strings.VersionControl_NoHistory
                : string.Empty;
        }
        finally
        {
            IsLoading.Value = false;
        }
    }

    private void OnStatusChanged(object? sender, WorkspaceStatus status)
    {
        _postToUi(() =>
        {
            if (_disposed)
            {
                return;
            }

            ApplyStatus(status);
            if (!status.HasConflicts)
            {
                _ = RefreshHistoryAsync();
            }
        });
    }

    private void OnRecoverableLockAvailable(object? sender, RepositoryLockInfo lockInfo)
    {
        _postToUi(() =>
        {
            if (!_disposed
                && Equals(_lockRecoveryService?.RecoverableLock, lockInfo))
            {
                HasRecoverableLock.Value = true;
            }
        });
    }

    private void ApplyStatus(WorkspaceStatus status)
    {
        IsConflicted.Value = status.HasConflicts;
        HasBlockingGuidance.Value = IsUnavailable.Value || status.HasConflicts;
        if (status.HasConflicts)
        {
            StatusMessage.Value = Strings.VersionControl_ConflictGuidance;
            HasMoreHistory.Value = false;
        }

        BranchText.Value = string.Format(
            CultureInfo.CurrentCulture,
            Strings.VersionControl_BranchFormat,
            status.Branch ?? "—");
        AheadBehindText.Value = string.Format(
            CultureInfo.CurrentCulture,
            Strings.VersionControl_AheadBehindFormat,
            status.Ahead,
            status.Behind);
        DirtySummary.Value = status.IsClean
            ? Strings.VersionControl_WorktreeClean
            : string.Format(
                CultureInfo.CurrentCulture,
                Strings.VersionControl_DirtySummaryFormat,
                status.Changes.Count);
    }

    private CancellationToken ReplaceSelectionCancellation()
    {
        _selectionCancellation?.Cancel();
        _selectionCancellation?.Dispose();
        _selectionCancellation = new CancellationTokenSource();
        return _selectionCancellation.Token;
    }

    private static void PostToUiThread(Action action)
    {
        if (Dispatcher.UIThread.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.UIThread.Post(action);
        }
    }

    private static async Task<string?> ShowBranchNameDialogAsync(CommitInfo commit)
    {
        var textBox = new TextBox
        {
            Watermark = Strings.VersionControl_BranchName,
            Text = $"restore-{commit.ShortSha}",
        };
        var dialog = new ContentDialog
        {
            Title = Strings.VersionControl_CreateBranchTitle,
            Content = textBox,
            PrimaryButtonText = Strings.VersionControl_RestoreToNewBranch,
            CloseButtonText = Strings.Cancel,
            DefaultButton = ContentDialogButton.Primary,
        };
        ContentDialogResult result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary ? textBox.Text : null;
    }
}

public sealed class VersionControlCommitViewModel : IDisposable
{
    private readonly VersionControlTabViewModel _owner;

    internal VersionControlCommitViewModel(
        VersionControlTabViewModel owner,
        CommitInfo commit)
    {
        _owner = owner;
        Commit = commit;
        KindText = GetKindText(commit.Kind);
        DisplayMessage = commit.Subject;
        AuthorAndDate = string.Format(
            CultureInfo.CurrentCulture,
            "{0} · {1:g}",
            commit.AuthorName,
            commit.AuthorDate);
        RestoreCommand = new AsyncReactiveCommand()
            .WithSubscribe(() => _owner.RestoreAsync(Commit));
        RestoreToNewBranchCommand = new AsyncReactiveCommand()
            .WithSubscribe(() => _owner.RestoreToNewBranchAsync(Commit));
    }

    public CommitInfo Commit { get; }

    public string KindText { get; }

    public string DisplayMessage { get; }

    public string AuthorAndDate { get; }

    public AsyncReactiveCommand RestoreCommand { get; }

    public AsyncReactiveCommand RestoreToNewBranchCommand { get; }

    public void Dispose()
    {
        RestoreCommand.Dispose();
        RestoreToNewBranchCommand.Dispose();
    }

    private static string GetKindText(SnapshotKind kind)
    {
        return kind switch
        {
            SnapshotKind.Save => Strings.VersionControl_SnapshotSave,
            SnapshotKind.Close => Strings.VersionControl_SnapshotClose,
            SnapshotKind.Safety => Strings.VersionControl_SnapshotSafety,
            SnapshotKind.Restore => Strings.VersionControl_SnapshotRestore,
            SnapshotKind.Init => Strings.VersionControl_SnapshotInit,
            _ => Strings.VersionControl_SnapshotManual,
        };
    }
}

public sealed class VersionControlFileChangeViewModel
{
    public VersionControlFileChangeViewModel(FileChange change)
    {
        Change = change;
    }

    public FileChange Change { get; }

    public string StatusText => Change.Status switch
    {
        FileChangeStatus.Added => "A",
        FileChangeStatus.Deleted => "D",
        FileChangeStatus.Renamed => "R",
        _ => "M",
    };

    public string PathText => Change.OldPath is null
        ? Change.Path
        : $"{Change.OldPath} → {Change.Path}";
}

public enum VersionControlDiffLineKind
{
    Context,
    Added,
    Removed,
    Header,
}

public sealed record VersionControlDiffLineViewModel(
    string Text,
    VersionControlDiffLineKind Kind)
{
    public bool IsAdded => Kind == VersionControlDiffLineKind.Added;

    public bool IsRemoved => Kind == VersionControlDiffLineKind.Removed;

    public bool IsHeader => Kind == VersionControlDiffLineKind.Header;

    public static IReadOnlyList<VersionControlDiffLineViewModel> Parse(string diff)
    {
        ArgumentNullException.ThrowIfNull(diff);
        return diff.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .Select(line => new VersionControlDiffLineViewModel(line, GetKind(line)))
            .ToArray();
    }

    private static VersionControlDiffLineKind GetKind(string line)
    {
        if (line.StartsWith("+++", StringComparison.Ordinal)
            || line.StartsWith("---", StringComparison.Ordinal)
            || line.StartsWith("@@", StringComparison.Ordinal)
            || line.StartsWith("diff ", StringComparison.Ordinal)
            || line.StartsWith("index ", StringComparison.Ordinal))
        {
            return VersionControlDiffLineKind.Header;
        }

        if (line.StartsWith("+", StringComparison.Ordinal))
        {
            return VersionControlDiffLineKind.Added;
        }

        if (line.StartsWith("-", StringComparison.Ordinal))
        {
            return VersionControlDiffLineKind.Removed;
        }

        return VersionControlDiffLineKind.Context;
    }
}
