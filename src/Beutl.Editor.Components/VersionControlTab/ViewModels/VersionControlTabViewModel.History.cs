using Beutl.Editor.VersionControl;

namespace Beutl.Editor.Components.VersionControlTab.ViewModels;

internal partial class VersionControlTabViewModel
{
    public async Task LoadMoreAsync()
    {
        IProjectVersionControlService? service = _service;
        if (service?.Repository is null || !HasMoreHistory.Value)
        {
            return;
        }

        CancellationToken cancellationToken =
            _serviceBindingCancellation?.Token ?? CancellationToken.None;
        try
        {
            await _historyGate.WaitAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }

        try
        {
            await LoadNextPageCoreAsync(service, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            _historyGate.Release();
        }
    }

    public async Task SelectCommitAsync(VersionControlCommitViewModel? commit)
    {
        SelectedCommit.Value = commit;
        if (commit is null)
        {
            _showingDetail.Value = false;
        }

        SelectedFile.Value = null;
        ChangedFiles.Clear();
        DiffLines.Clear();
        CancellationToken cancellationToken = ReplaceSelectionCancellation();
        if (_service is null || commit is null)
        {
            return;
        }

        IProjectVersionControlService service = _service;
        _previewLoadToken = cancellationToken;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int previewRevision = _previewRevision;
                if (!_fileCache.TryGet(commit.Commit.Sha, out var files))
                {
                    files = await service.GetCommitFilesAsync(commit.Commit.Sha, cancellationToken);
                    if (cancellationToken.IsCancellationRequested) return;
                    // Keep the same selection alive, but retry against the latest metadata.
                    if (previewRevision != _previewRevision) continue;
                    files = files.ToArray();
                    _fileCache.Add(commit.Commit.Sha, files,
                        files.Sum(static item => 64L + 2L * (item.Path.Length + (item.OldPath?.Length ?? 0))));
                }

                foreach (FileChange file in files)
                {
                    ChangedFiles.Add(new VersionControlFileChangeViewModel(file));
                }
                return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (_previewLoadToken == cancellationToken) _previewLoadToken = null;
        }
    }

    internal async Task OpenCommitDetailAsync(VersionControlCommitViewModel commit)
    {
        ArgumentNullException.ThrowIfNull(commit);
        _showingDetail.Value = true;
        await SelectCommitAsync(commit);
    }

    internal void ShowSelectedCommitDetail()
    {
        if (SelectedCommit.Value is not null)
        {
            _showingDetail.Value = true;
        }
    }

    private void ShowHistory()
    {
        _showingDetail.Value = false;
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

        IProjectVersionControlService service = _service;
        _previewLoadToken = cancellationToken;
        var cacheKey = (commit.Commit.Sha, file.Change.Path);
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                int previewRevision = _previewRevision;
                if (_diffCache.TryGet(cacheKey, out var cachedLines))
                {
                    DiffLines.AddRange(cachedLines);
                    return;
                }

                string diff = await service.GetDiffAsync(commit.Commit.Sha, file.Change.Path, cancellationToken);
                if (cancellationToken.IsCancellationRequested) return;
                if (previewRevision != _previewRevision) continue;

                IReadOnlyList<VersionControlDiffLineViewModel> lines = await Task.Run(
                    () => VersionControlDiffLineViewModel.Parse(diff, cancellationToken), cancellationToken);
                if (cancellationToken.IsCancellationRequested) return;
                if (previewRevision != _previewRevision) continue;

                _diffCache.Add(cacheKey, lines, lines.Sum(static line => 48L + 2L * line.Text.Length));
                DiffLines.AddRange(lines);
                return;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            if (_previewLoadToken == cancellationToken) _previewLoadToken = null;
        }
    }

    private async Task RefreshHistoryAsync(
        IProjectVersionControlService service,
        string? branch,
        int statusRefreshRevision,
        CancellationToken cancellationToken)
    {
        if (service.Repository is null)
        {
            return;
        }

        await _historyGate.WaitAsync(cancellationToken);
        try
        {
            if (!IsCurrentStatusRefresh(
                    service,
                    statusRefreshRevision,
                    cancellationToken))
            {
                return;
            }

            await ReloadHistoryCoreAsync(
                service,
                branch,
                statusRefreshRevision,
                cancellationToken);
        }
        finally
        {
            _historyGate.Release();
        }
    }

    private async Task RefreshHistoryIfChangedAsync(
        IProjectVersionControlService service,
        string? branch,
        int statusRefreshRevision,
        CancellationToken cancellationToken)
    {
        await _historyGate.WaitAsync(cancellationToken);
        try
        {
            if (!IsCurrentStatusRefresh(
                    service,
                    statusRefreshRevision,
                    cancellationToken))
            {
                return;
            }

            IReadOnlyList<CommitInfo> tip = await service.GetHistoryAsync(
                0,
                1,
                cancellationToken);
            if (!IsCurrentStatusRefresh(
                    service,
                    statusRefreshRevision,
                    cancellationToken))
            {
                return;
            }

            var identity = new HistoryIdentity(branch, tip.FirstOrDefault()?.Sha);
            if (_historyIdentity == identity)
            {
                HasMoreHistory.Value = _hasMoreHistory;
                UpdateHistoryStatusMessage();
                return;
            }

            await ReloadHistoryCoreAsync(
                service,
                branch,
                statusRefreshRevision,
                cancellationToken);
        }
        finally
        {
            _historyGate.Release();
        }
    }

    private async Task ReloadHistoryCoreAsync(
        IProjectVersionControlService service,
        string? branch,
        int statusRefreshRevision,
        CancellationToken cancellationToken)
    {
        IsLoading.Value = true;
        try
        {
            IReadOnlyList<CommitInfo> page = await service.GetHistoryAsync(
                0,
                HistoryPageSize,
                cancellationToken);
            if (!IsCurrentStatusRefresh(
                    service,
                    statusRefreshRevision,
                    cancellationToken))
            {
                return;
            }

            string? selectedSha = SelectedCommit.Value?.Commit.Sha;
            _historyIdentity = null;
            CancelSelection();
            DisposeAndClearHistoryItems();
            SelectedCommit.Value = null;
            SelectedFile.Value = null;
            foreach (CommitInfo commit in page)
            {
                Commits.Add(new VersionControlCommitViewModel(
                    this,
                    commit,
                    _relativeTimeFormatter));
            }

            _nextHistoryOffset = page.Count;
            _hasMoreHistory = page.Count == HistoryPageSize;
            HasMoreHistory.Value = _hasMoreHistory;
            UpdateHistoryStatusMessage();
            _historyIdentity = new HistoryIdentity(
                branch,
                page.FirstOrDefault()?.Sha);

            if (selectedSha is not null)
            {
                VersionControlCommitViewModel? restoredCommit = Commits.FirstOrDefault(
                    item => string.Equals(
                        item.Commit.Sha,
                        selectedSha,
                        StringComparison.Ordinal));
                if (restoredCommit is null)
                {
                    _showingDetail.Value = false;
                }
                else
                {
                    await SelectCommitAsync(restoredCommit);
                }
            }
            else
            {
                _showingDetail.Value = false;
            }
        }
        finally
        {
            IsLoading.Value = false;
        }
    }

    private async Task LoadNextPageCoreAsync(
        IProjectVersionControlService service,
        CancellationToken cancellationToken)
    {
        if (!ReferenceEquals(service, _service))
        {
            return;
        }

        IsLoading.Value = true;
        try
        {
            IReadOnlyList<CommitInfo> page = await service.GetHistoryAsync(
                _nextHistoryOffset,
                HistoryPageSize,
                cancellationToken);
            if (cancellationToken.IsCancellationRequested
                || !ReferenceEquals(service, _service))
            {
                return;
            }

            foreach (CommitInfo commit in page)
            {
                Commits.Add(new VersionControlCommitViewModel(
                    this,
                    commit,
                    _relativeTimeFormatter));
            }

            _nextHistoryOffset += page.Count;
            _hasMoreHistory = page.Count == HistoryPageSize;
            HasMoreHistory.Value = _hasMoreHistory;
            UpdateHistoryStatusMessage();
        }
        finally
        {
            IsLoading.Value = false;
        }
    }

    private void DisposeAndClearHistoryItems()
    {
        foreach (VersionControlCommitViewModel commit in Commits)
        {
            commit.Dispose();
        }

        Commits.Clear();
        ChangedFiles.Clear();
        DiffLines.Clear();
    }

    private void UpdateHistoryStatusMessage()
    {
        IsHistoryEmpty.Value = Commits.Count == 0;
        StatusMessage.Value = Commits.Count == 0
            ? Strings.VersionControl_HistoryEmptyHint
            : string.Empty;
    }

    private Task RefreshDisplayedPreviewAsync()
    {
        if (_disposed) return Task.CompletedTask;
        if (!_displayedPreviewRefresh.IsCompleted) return _displayedPreviewRefresh;
        // An active selection already retries when the preview revision changes. Reuse an
        // automatic reload across notifications without starting duplicate reads.
        if (_previewLoadToken is not null) return Task.CompletedTask;

        return _displayedPreviewRefresh = SelectedFile.Value is { } file
            ? SelectFileAsync(file)
            : SelectedCommit.Value is { } commit
                ? SelectCommitAsync(commit)
                : Task.CompletedTask;
    }

    private void InvalidatePreviewCache()
    {
        _previewRevision++;
        _fileCache.Clear();
        _diffCache.Clear();
    }

    private void CancelSelection()
    {
        _selectionCancellation?.Cancel();
        _selectionCancellation?.Dispose();
        _selectionCancellation = null;
        // A new selection or repository state must not reuse an obsolete automatic reload.
        _previewLoadToken = null;
        _displayedPreviewRefresh = Task.CompletedTask;
    }

    private CancellationToken ReplaceSelectionCancellation()
    {
        CancelSelection();
        _selectionCancellation = new CancellationTokenSource();
        return _selectionCancellation.Token;
    }

    private readonly record struct HistoryIdentity(string? Branch, string? TipSha);
}
