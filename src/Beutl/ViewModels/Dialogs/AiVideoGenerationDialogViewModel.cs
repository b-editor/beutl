using System.Diagnostics;
using System.Reactive.Disposables;
using Avalonia;
using System.Text.Json.Nodes;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Editor.Services;
using Beutl.Graphics;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.AI;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

public sealed class AiVideoGenerationDialogViewModel : IToolContext, IAsyncDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly AsyncOperationLifetime _operations = new();
    private readonly object _disposeGate = new();
    private readonly ILogger _logger = Log.CreateLogger<AiVideoGenerationDialogViewModel>();
    private readonly IAiEntitlementService _entitlements;
    private readonly IAiPlanCoordinator _aiPlanCoordinator;
    private readonly IAiVideoService _videos;
    private readonly IAuthenticatedContentService _content;
    private readonly IAiJobKindRegistry _jobKinds;
    private readonly EditViewModel? _editViewModel;
    private readonly HashSet<string> _temporaryFiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _temporaryFileLeases = new(StringComparer.Ordinal);
    private readonly HashSet<string> _temporaryFilesPendingDeletion = new(StringComparer.Ordinal);
    private readonly object _lifetimeGate = new();
    private CancellationTokenSource? _pollingCts;
    private string? _firstFrameElementId;
    private string? _lastFrameElementId;
    private AiVideoResultSnapshot? _resultSnapshot;
    private Task? _disposeTask;

    public AiVideoGenerationDialogViewModel(
        IAiEntitlementService entitlements,
        IAiPlanCoordinator aiPlanCoordinator,
        IAiVideoService videos,
        IAuthenticatedContentService content,
        IAiJobKindRegistry jobKinds,
        EditViewModel? editViewModel = null)
    {
        _entitlements = entitlements ?? throw new ArgumentNullException(nameof(entitlements));
        _aiPlanCoordinator = aiPlanCoordinator
            ?? throw new ArgumentNullException(nameof(aiPlanCoordinator));
        _videos = videos ?? throw new ArgumentNullException(nameof(videos));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _jobKinds = jobKinds ?? throw new ArgumentNullException(nameof(jobKinds));
        _editViewModel = editViewModel;
        Usage = new AiUsageViewModel(_entitlements.Entitlements).DisposeWith(_disposables);
        PromptLibrary = new AiPromptLibraryViewModel(
                PromptTaskKind.Video,
                ComposePrompt,
                prompt => Prompt.Value = prompt)
            .DisposeWith(_disposables);

        DurationOptions =
        [
            new AiVideoDurationOption(4),
            new AiVideoDurationOption(6),
            new AiVideoDurationOption(8),
        ];
        SelectedDuration = new ReactivePropertySlim<AiVideoDurationOption>(DurationOptions[1])
            .DisposeWith(_disposables);
        EstimatedUsage = new AiUsageEstimateViewModel(
                Usage,
                _entitlements.Entitlements.Select(entitlements =>
                    entitlements?.Availability.CanStart(AiOperations.VideoGeneration) ?? false))
            .DisposeWith(_disposables);

        ResolutionOptions =
        [
            new AiVideoResolutionOption("720p"),
            new AiVideoResolutionOption("1080p"),
        ];
        SelectedResolution = new ReactivePropertySlim<AiVideoResolutionOption>(ResolutionOptions[0])
            .DisposeWith(_disposables);

        IsGenerating = new ReactivePropertySlim<bool>(false)
            .DisposeWith(_disposables);

        SelectFirstFrame = new AsyncReactiveCommand()
            .WithSubscribe(() => SelectFrameAsync(isFirstFrame: true));
        SelectLastFrame = new AsyncReactiveCommand()
            .WithSubscribe(() => SelectFrameAsync(isFirstFrame: false));
        CaptureCurrentFrame = new AsyncReactiveCommand()
            .WithSubscribe(CaptureCurrentFrameAsync);
        ClearFirstFrame = new ReactiveCommand();
        ClearFirstFrame.Subscribe(() => SetFrame(isFirstFrame: true, null)).DisposeWith(_disposables);
        ClearLastFrame = new ReactiveCommand();
        ClearLastFrame.Subscribe(() => SetFrame(isFirstFrame: false, null)).DisposeWith(_disposables);

        CanGenerate = Prompt
            .CombineLatest(IsGenerating, (prompt, generating) =>
                !string.IsNullOrWhiteSpace(prompt) && !generating)
            .CombineLatest(
                FirstFramePath,
                LastFramePath,
                (canGenerate, firstFrame, lastFrame) =>
                    canGenerate && (string.IsNullOrEmpty(lastFrame) || !string.IsNullOrEmpty(firstFrame)))
            .CombineLatest(EstimatedUsage.CanAfford, (canGenerate, canAfford) => canGenerate && canAfford)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Generate = new AsyncReactiveCommand(CanGenerate)
            .WithSubscribe(GenerateCore);

        CanAddToScene = ResultVideoPath
            .Select(x => x != null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        AddToScene = new AsyncReactiveCommand(CanAddToScene)
            .WithSubscribe(AddToSceneCore);

        SaveToFile = new AsyncReactiveCommand(CanAddToScene)
            .WithSubscribe(SaveToFileCore);

        OpenResult = new ReactiveCommand(CanAddToScene);
        OpenResult.Subscribe(OpenResultCore).DisposeWith(_disposables);

        OpenAiPlan = new ReactiveCommand();
        OpenAiPlan.Subscribe(aiPlanCoordinator.OpenAiPlan).DisposeWith(_disposables);

        ShowJoinPro = Usage.HasSnapshot
            .CombineLatest(Usage.CanUseAi, (hasSnapshot, canUseAi) => hasSnapshot && !canUseAi)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        CoreObject? selectedObject = editViewModel?.GetService<IEditorSelection>()?.SelectedObject.Value;
        SetFrame(
            isFirstFrame: true,
            AiImageEditDialogViewModel.GetSelectedImageSourcePath(selectedObject),
            selectedObject is Element selectedElement ? selectedElement.Id.ToString("N") : null);

        _ = LoadEntitlementsAsync();
    }

    public ToolTabExtension Extension => AiVideoGenerationTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public IReadOnlyReactiveProperty<string> Header { get; } = new ReactivePropertySlim<string>(Strings.AiVideoGeneration);

    public IReadOnlyList<AiVideoDurationOption> DurationOptions { get; }

    public ReactivePropertySlim<AiVideoDurationOption> SelectedDuration { get; }

    public IReadOnlyList<AiVideoResolutionOption> ResolutionOptions { get; }

    public ReactivePropertySlim<AiVideoResolutionOption> SelectedResolution { get; }

    public ReactivePropertySlim<string> Prompt { get; } = new();

    public ReactivePropertySlim<string> Style { get; } = new();

    public ReactivePropertySlim<string> Composition { get; } = new();

    public ReactivePropertySlim<string> Motion { get; } = new();

    public ReactivePropertySlim<string> Exclusions { get; } = new();

    public ReactivePropertySlim<string?> FirstFramePath { get; } = new();

    public ReactivePropertySlim<string?> LastFramePath { get; } = new();

    public ReactivePropertySlim<Ref<Bitmap>?> FirstFramePreview { get; } = new();

    public ReactivePropertySlim<Ref<Bitmap>?> LastFramePreview { get; } = new();

    public AsyncReactiveCommand SelectFirstFrame { get; }

    public AsyncReactiveCommand SelectLastFrame { get; }

    public AsyncReactiveCommand CaptureCurrentFrame { get; }

    public ReactiveCommand ClearFirstFrame { get; }

    public ReactiveCommand ClearLastFrame { get; }

    public ReactivePropertySlim<bool> IsGenerating { get; }

    public ReadOnlyReactivePropertySlim<bool> CanGenerate { get; }

    public AsyncReactiveCommand Generate { get; }

    public ReadOnlyReactivePropertySlim<bool> CanAddToScene { get; }

    public AsyncReactiveCommand AddToScene { get; }

    public AsyncReactiveCommand SaveToFile { get; }

    public ReactiveCommand OpenResult { get; }

    public ReactiveCommand OpenAiPlan { get; }

    internal IAiPlanCoordinator AiPlanCoordinator => _aiPlanCoordinator;

    public ReactivePropertySlim<string?> ResultVideoPath { get; } = new();

    public ReactivePropertySlim<string> StatusText { get; } = new(Strings.AiVideoIdle);

    internal AiUsageViewModel Usage { get; }

    internal AiUsageEstimateViewModel EstimatedUsage { get; }

    internal AiPromptLibraryViewModel PromptLibrary { get; }

    internal Func<CancellationToken, Task<Bitmap>>? CurrentFrameRenderer { get; set; }

    public ReadOnlyReactivePropertySlim<bool> ShowJoinPro { get; }

    public ReactivePropertySlim<string?> Error { get; } = new();

    public object? GetService(Type serviceType) => _editViewModel?.GetService(serviceType);

    public void ReadFromJson(JsonObject json)
    {
        // Prompts, frame references, and generated content are not persisted with the dock layout.
    }

    public void WriteToJson(JsonObject json)
    {
        // Prompts, frame references, and generated content are not persisted with the dock layout.
    }

    public void Dispose() => _ = BeginDisposeAsync();

    public ValueTask DisposeAsync() => new(BeginDisposeAsync());

    private Task BeginDisposeAsync()
    {
        lock (_disposeGate)
        {
            return _disposeTask ??= DisposeCoreAsync();
        }
    }

    private async Task DisposeCoreAsync()
    {
        await _operations.DisposeAsync();

        string[] temporaryFiles;
        CancellationTokenSource? pollingCts;
        lock (_lifetimeGate)
        {
            temporaryFiles = _temporaryFiles.ToArray();
            _temporaryFiles.Clear();
            _temporaryFileLeases.Clear();
            _temporaryFilesPendingDeletion.Clear();
            pollingCts = _pollingCts;
            _pollingCts = null;
        }

        pollingCts?.Dispose();
        Prompt.Dispose();
        Style.Dispose();
        Composition.Dispose();
        Motion.Dispose();
        Exclusions.Dispose();
        FirstFramePreview.Value?.Dispose();
        FirstFramePreview.Value = null;
        LastFramePreview.Value?.Dispose();
        LastFramePreview.Value = null;
        FirstFramePath.Value = null;
        LastFramePath.Value = null;
        FirstFramePreview.Dispose();
        LastFramePreview.Dispose();
        FirstFramePath.Dispose();
        LastFramePath.Dispose();
        Error.Dispose();
        IsSelected.Dispose();
        _disposables.Dispose();
        foreach (string path in temporaryFiles)
        {
            DeleteTemporaryFile(path);
        }
    }

    private async Task LoadEntitlementsAsync()
    {
        using AsyncOperationLifetime.Operation? operation = _operations.TryEnter();
        if (operation is null)
            return;
        try
        {
            await _entitlements.RefreshAsync(operation.CancellationToken);
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load AI entitlements.");
        }
    }

    private async Task SelectFrameAsync(bool isFirstFrame)
    {
        using AsyncOperationLifetime.Operation? operation = _operations.TryEnter();
        if (operation is null)
            return;
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime
            { MainWindow: { } window }
            || TopLevel.GetTopLevel(window)?.StorageProvider is not { } storage)
        {
            return;
        }

        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(SharedFilePickerOptions.OpenImage());
        if (files.Count > 0)
        {
            operation.TryPublish(() =>
                SetFrameCore(isFirstFrame, files[0].Path.LocalPath, sourceElementId: null));
        }
    }

    private async Task CaptureCurrentFrameAsync()
    {
        using AsyncOperationLifetime.Operation? operation = _operations.TryEnter();
        if (operation is null)
            return;
        if (_editViewModel is null)
            return;

        CancellationToken lifetimeToken = operation.CancellationToken;
        if (!operation.TryPublish(() => Error.Value = null))
            return;

        string? unpublishedPath = null;
        try
        {
            using Bitmap bitmap = CurrentFrameRenderer is { } renderer
                ? await renderer(lifetimeToken)
                : await _editViewModel.Player.DrawFrameAtFullScale();
            lifetimeToken.ThrowIfCancellationRequested();

            string directory = Path.Combine(Path.GetTempPath(), "Beutl", "AI", "Inputs");
            Directory.CreateDirectory(directory);
            lifetimeToken.ThrowIfCancellationRequested();
            unpublishedPath = Path.Combine(directory, $"frame-{Guid.NewGuid():N}.png");
            bitmap.Save(unpublishedPath, EncodedImageFormat.Png);
            lifetimeToken.ThrowIfCancellationRequested();

            bool published = operation.TryPublish(() =>
            {
                lock (_lifetimeGate)
                {
                    _temporaryFiles.Add(unpublishedPath);
                    try
                    {
                        SetFrameCore(isFirstFrame: true, unpublishedPath, sourceElementId: null);
                    }
                    catch
                    {
                        _temporaryFiles.Remove(unpublishedPath);
                        throw;
                    }
                }
            });
            if (!published)
                return;

            unpublishedPath = null;
        }
        catch (OperationCanceledException) when (lifetimeToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to capture the current frame for AI video generation.");
            operation.TryPublish(() => Error.Value = Strings.AiVideoFrameCaptureFailed);
        }
        finally
        {
            if (unpublishedPath is not null)
            {
                DeleteTemporaryFile(unpublishedPath);
            }
        }
    }

    private void SetFrame(bool isFirstFrame, string? path, string? sourceElementId = null)
    {
        using AsyncOperationLifetime.Operation? operation = _operations.TryEnter();
        operation?.TryPublish(() => SetFrameCore(isFirstFrame, path, sourceElementId));
    }

    private void SetFrameCore(bool isFirstFrame, string? path, string? sourceElementId)
    {
        ReactivePropertySlim<string?> pathProperty = isFirstFrame ? FirstFramePath : LastFramePath;
        ReactivePropertySlim<Ref<Bitmap>?> previewProperty = isFirstFrame ? FirstFramePreview : LastFramePreview;
        string? previousPath = pathProperty.Value;
        previewProperty.Value?.Dispose();
        previewProperty.Value = null;
        pathProperty.Value = path;
        if (isFirstFrame)
        {
            _firstFrameElementId = sourceElementId;
        }
        else
        {
            _lastFrameElementId = sourceElementId;
        }

        if (!string.Equals(previousPath, path, StringComparison.Ordinal))
        {
            RequestTemporaryFileDeletion(previousPath);
        }

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return;

        try
        {
            previewProperty.Value = Ref<Bitmap>.Create(Bitmap.FromFile(path));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load an AI video frame preview from {Path}", path);
            Error.Value = Strings.AiEditSourcePreviewFailed;
        }
    }

    private void OpenResultCore()
    {
        using AsyncOperationLifetime.Operation? operation = _operations.TryEnter();
        if (operation is null)
            return;
        if (ResultVideoPath.Value is not { } path || !File.Exists(path))
            return;

        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "open" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open the generated AI video preview.");
            operation.TryPublish(() => Error.Value = Strings.AiVideoPreviewOpenFailed);
        }
    }

    private async Task GenerateCore()
    {
        using AsyncOperationLifetime.Operation? operation = _operations.TryEnter();
        if (operation is null)
            return;
        if (!operation.TryPublish(() =>
            {
                Error.Value = null;
                IsGenerating.Value = true;
                StatusText.Value = Strings.AiVideoSubmitting;
            }))
        {
            return;
        }

        try
        {
            string prompt = ComposePrompt();
            int durationSeconds = SelectedDuration.Value.Seconds;
            string resolution = SelectedResolution.Value.Value;
            string? firstFramePath = FirstFramePath.Value;
            string? lastFramePath = LastFramePath.Value;
            string? firstFrameElementId = _firstFrameElementId;
            string? lastFrameElementId = _lastFrameElementId;
            using IDisposable firstFrameLease = AcquireTemporaryFileLease(firstFramePath);
            using IDisposable lastFrameLease = AcquireTemporaryFileLease(lastFramePath);
            AiVideoGenerationResult response = await _videos.CreateAsync(
                new AiVideoGenerationRequest(
                    prompt,
                    durationSeconds,
                    new AiVideoResolutionId(resolution),
                    firstFramePath is null ? null : AiUploadSource.FromFile(firstFramePath),
                    lastFramePath is null ? null : AiUploadSource.FromFile(lastFramePath)),
                operation.CancellationToken);

            var pendingSnapshot = new AiVideoResultSnapshot(
                response.JobId,
                null,
                prompt,
                durationSeconds,
                resolution,
                !string.IsNullOrEmpty(firstFramePath),
                !string.IsNullOrEmpty(lastFramePath),
                firstFrameElementId,
                lastFrameElementId,
                DateTimeOffset.UtcNow);
            if (!operation.TryPublish(() =>
                {
                    PromptLibrary.Record(prompt);
                }))
            {
                return;
            }

            await PollJobAsync(response.JobId, operation, pendingSnapshot);
        }
        catch (AuthenticationRequiredException)
        {
            operation.TryPublish(() => Error.Value = Strings.AiAuthenticationRequired);
        }
        catch (AiPlanRequiredException)
        {
            operation.TryPublish(() => Error.Value = Strings.AiProRequired);
        }
        catch (AiUsageLimitExceededException)
        {
            operation.TryPublish(() => Error.Value = Strings.AiUsageLimitExceeded);
        }
        catch (AiProviderErrorException)
        {
            operation.TryPublish(() => Error.Value = Strings.AiProviderError);
        }
        catch (AiJobLimitReachedException)
        {
            operation.TryPublish(() => Error.Value = Strings.AiVideoJobLimitReached);
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate AI video.");
            operation.TryPublish(() => Error.Value = Strings.AiUnexpectedError);
        }
        finally
        {
            operation.TryPublish(() => IsGenerating.Value = false);
        }
    }

    private async Task PollJobAsync(
        AiJobId jobId,
        AsyncOperationLifetime.Operation operation,
        AiVideoResultSnapshot pendingSnapshot)
    {
        CancellationTokenSource pollingCts;
        lock (_lifetimeGate)
        {
            _pollingCts?.Cancel();
            _pollingCts?.Dispose();
            pollingCts = CancellationTokenSource.CreateLinkedTokenSource(operation.CancellationToken);
            _pollingCts = pollingCts;
        }
        CancellationToken token = pollingCts.Token;

        try
        {
            while (!token.IsCancellationRequested)
            {
                AiVideoJob job = await _videos.GetAsync(jobId, token);
                AiJobStatusSemantics status = _jobKinds.GetStatus(AiJobKinds.Video, job.Status);
                if (status.Outcome == AiJobOutcomes.Succeeded)
                {
                    if (job.ContentUri is not { } contentUri)
                    {
                        throw new InvalidOperationException("A successful video job did not provide content.");
                    }

                    string? localPath = await DownloadVideoAsync(contentUri, operation);
                    if (localPath is null)
                        return;

                    operation.TryPublish(() =>
                    {
                        string? previousPath = ResultVideoPath.Value;
                        StatusText.Value = Strings.AiVideoCompleted;
                        ResultVideoPath.Value = localPath;
                        _resultSnapshot = pendingSnapshot with { FileId = job.FileId };
                        if (!string.Equals(previousPath, localPath, StringComparison.Ordinal))
                        {
                            RequestTemporaryFileDeletion(previousPath);
                        }
                    });
                    return;
                }
                if (status.IsTerminal || !status.ShouldPoll)
                {
                    operation.TryPublish(() =>
                    {
                        StatusText.Value = Strings.AiVideoFailed;
                        Error.Value = job.Error ?? Strings.AiProviderError;
                    });
                    return;
                }

                operation.TryPublish(() => StatusText.Value = Strings.AiVideoProcessing);
                await Task.Delay(TimeSpan.FromSeconds(5), token);
            }
        }
        finally
        {
            lock (_lifetimeGate)
            {
                if (ReferenceEquals(_pollingCts, pollingCts))
                    _pollingCts = null;
            }

            pollingCts.Dispose();
        }
    }

    private async Task<string?> DownloadVideoAsync(
        Uri contentUri,
        AsyncOperationLifetime.Operation operation)
    {
        string? filePath = null;
        try
        {
            string projectDir = Path.Combine(Path.GetTempPath(), "Beutl", "AI", "Results");
            Directory.CreateDirectory(projectDir);
            string fileName = $"ai-video-{Guid.NewGuid():N}.mp4";
            filePath = Path.Combine(projectDir, fileName);
            await using (FileStream destination = new(
                filePath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.Asynchronous))
            {
                await _content.CopyToAsync(contentUri, destination, operation.CancellationToken);
            }

            if (!operation.TryPublish(() =>
                {
                    lock (_lifetimeGate)
                    {
                        _temporaryFiles.Add(filePath);
                    }
                }))
            {
                DeleteTemporaryFile(filePath);
                return null;
            }

            return filePath;
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
            if (filePath is not null)
            {
                DeleteTemporaryFile(filePath);
            }

            throw;
        }
        catch (Exception ex)
        {
            if (filePath is not null)
            {
                DeleteTemporaryFile(filePath);
            }
            _logger.LogError(ex, "Failed to download the AI video.");
            operation.TryPublish(() => Error.Value = Strings.AiUnexpectedError);
            return null;
        }
    }

    private IDisposable AcquireTemporaryFileLease(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return Disposable.Empty;

        lock (_lifetimeGate)
        {
            if (!_temporaryFiles.Contains(path))
                return Disposable.Empty;

            _temporaryFileLeases[path] = _temporaryFileLeases.GetValueOrDefault(path) + 1;
        }

        return Disposable.Create(() => ReleaseTemporaryFileLease(path));
    }

    private void ReleaseTemporaryFileLease(string path)
    {
        lock (_lifetimeGate)
        {
            if (!_temporaryFileLeases.TryGetValue(path, out int count))
                return;

            if (count > 1)
            {
                _temporaryFileLeases[path] = count - 1;
                return;
            }

            _temporaryFileLeases.Remove(path);
            if (_temporaryFilesPendingDeletion.Contains(path))
            {
                DeleteTrackedTemporaryFile(path);
            }
        }
    }

    private void RequestTemporaryFileDeletion(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return;

        lock (_lifetimeGate)
        {
            if (!_temporaryFiles.Contains(path))
                return;

            _temporaryFilesPendingDeletion.Add(path);
            if (!_temporaryFileLeases.ContainsKey(path))
            {
                DeleteTrackedTemporaryFile(path);
            }
        }
    }

    // Called with _lifetimeGate held so a new lease cannot race with deletion.
    private void DeleteTrackedTemporaryFile(string path)
    {
        if (DeleteTemporaryFile(path))
        {
            _temporaryFiles.Remove(path);
            _temporaryFilesPendingDeletion.Remove(path);
        }
    }

    private bool DeleteTemporaryFile(string path)
    {
        try
        {
            File.Delete(path);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Failed to remove temporary AI file {Path}", path);
            return false;
        }
    }

    private async Task AddToSceneCore()
    {
        using AsyncOperationLifetime.Operation? operation = _operations.TryEnter();
        if (operation is null)
            return;
        if (_editViewModel == null || ResultVideoPath.Value is not { } filePath)
            return;
        using IDisposable fileLease = AcquireTemporaryFileLease(filePath);

        try
        {
            TimeSpan start = _editViewModel.Player.CurrentFrame.Value;
            int layer = _editViewModel.Scene.Children
                .Where(item => item.Start <= start && start < item.Range.End)
                .Select(item => item.ZIndex)
                .DefaultIfEmpty(-1)
                .Max() + 1;
            AiVideoResultSnapshot snapshot = _resultSnapshot
                ?? new AiVideoResultSnapshot(
                    null,
                    null,
                    string.Empty,
                    SelectedDuration.Value.Seconds,
                    SelectedResolution.Value.Value,
                    !string.IsNullOrEmpty(FirstFramePath.Value),
                    !string.IsNullOrEmpty(LastFramePath.Value),
                    _firstFrameElementId,
                    _lastFrameElementId,
                    DateTimeOffset.UtcNow);
            GenerationProvenance provenance = AiProvenanceFactory.VideoGeneration(
                snapshot.DurationSeconds,
                snapshot.Resolution,
                snapshot.HasFirstFrame,
                snapshot.HasLastFrame,
                snapshot.FirstFrameElementId,
                snapshot.LastFrameElementId,
                snapshot.GeneratedAt);
            var importer = new AiResultImporter(
                _editViewModel.Scene,
                _editViewModel.GetRequiredService<IElementAdder>());
            ElementAddResult result = await importer.ImportVideoAsync(
                filePath,
                new AiResultImportOptions(
                    start,
                    TimeSpan.FromSeconds(snapshot.DurationSeconds),
                    layer,
                    Strings.AiVideoGeneration,
                    provenance),
                operation.CancellationToken);

            if (result.Failure is LockedElementLayerFailure)
            {
                operation.TryPublish(() =>
                    NotificationService.ShowWarning(Strings.Lock, Strings.LayerIsLocked));
                return;
            }
            EnsureImportSucceeded(result);
            if (result.IsSuccess)
            {
                operation.TryPublish(() =>
                    NotificationService.ShowSuccess(Strings.AiVideoGeneration, Strings.AiVideoAddedToScene));
            }
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add the AI video to the scene.");
            operation.TryPublish(() => Error.Value = Strings.AiUnexpectedError);
        }

    }

    private static void EnsureImportSucceeded(ElementAddResult result)
    {
        if (result.IsSuccess)
            return;
        throw new InvalidOperationException(
            $"Failed to add the generated video: {result.Failure?.Id}.",
            result.Failure?.Exception);
    }

    private async Task SaveToFileCore()
    {
        using AsyncOperationLifetime.Operation? operation = _operations.TryEnter();
        if (operation is null)
            return;
        if (ResultVideoPath.Value is not { } filePath)
            return;
        using IDisposable fileLease = AcquireTemporaryFileLease(filePath);

        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime
            { MainWindow: { } window })
            return;

        if (TopLevel.GetTopLevel(window)?.StorageProvider is not { } storage)
            return;

        FilePickerSaveOptions options = SharedFilePickerOptions.SaveVideo();
        options.SuggestedFileName = $"AI Video {DateTime.Now:yyyy-MM-dd HHmmss}";
        options.SuggestedStartLocation = await storage.TryGetWellKnownFolderAsync(WellKnownFolder.Videos);
        options.DefaultExtension = "mp4";

        IStorageFile? file = await storage.SaveFilePickerAsync(options);
        if (file == null)
            return;

        try
        {
            await using Stream source = File.OpenRead(filePath);
            await using Stream destination = await file.OpenWriteAsync();
            destination.SetLength(0);
            await source.CopyToAsync(destination, operation.CancellationToken);
            operation.TryPublish(() =>
                NotificationService.ShowSuccess(Strings.AiVideoGeneration, Strings.AiVideoSaved));
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save the AI video.");
            operation.TryPublish(() => Error.Value = Strings.AiUnexpectedError);
        }
    }

    private string ComposePrompt() => AiPromptComposer.Compose(new AiPromptParts(
        Prompt.Value,
        Style.Value,
        Composition.Value,
        Motion.Value,
        Exclusions.Value));

    private sealed record AiVideoResultSnapshot(
        AiJobId? JobId,
        AiContentId? FileId,
        string Prompt,
        int DurationSeconds,
        string Resolution,
        bool HasFirstFrame,
        bool HasLastFrame,
        string? FirstFrameElementId,
        string? LastFrameElementId,
        DateTimeOffset GeneratedAt);

}

public sealed record AiVideoDurationOption(int Seconds)
{
    public override string ToString() => $"{Seconds} {Strings.AiVideoSeconds}";
}

public sealed record AiVideoResolutionOption(string Value)
{
    public override string ToString() => Value;
}
