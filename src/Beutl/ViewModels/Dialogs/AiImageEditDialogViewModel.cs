using System.Globalization;
using System.Reactive.Disposables;
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
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

internal sealed partial class AiImageEditDialogViewModel : IDisposable, IAsyncDisposable, IAiModelListConsumer,
    IAiRecoverableForm, IAiPlanOffer, IAiResultActions
{
    private readonly CompositeDisposable _disposables = [];
    private readonly AsyncOperationLifetime _operations = new();
    private readonly IdentityOperationLifetime _identityOperations = new();
    private readonly OnceAsyncDisposal _disposal = new();
    private readonly ILogger _logger = Log.CreateLogger<AiImageEditDialogViewModel>();
    private readonly IAiEntitlementService _entitlements;
    private readonly IAiOperationAvailabilityService _availability;
    private readonly IAiModelCatalogService _modelCatalog;
    private readonly IAiPlanCoordinator _aiPlanCoordinator;
    private readonly IAiImageEditingService _images;
    private readonly IAuthenticatedContentService _content;
    private readonly AiRequestKey _requestKey;
    private readonly AiRequestRecoveryContext _requestRecoveryContext;
    // The request details associated with every unsettled name. Remembering only one would
    // forget an earlier request's model after another request and charge again when returning.
    private readonly AiOutstandingRequests _outstanding = new();
    private readonly AiRecoveryState _recovery;
    // A revision used only to notify the UI when held names change. _outstanding tracks which
    // tasks still have names.
    private readonly ReactivePropertySlim<int> _outstandingRevision = new();
    private readonly EditViewModel? _editViewModel;
    private string? _sourceElementId;
    private bool _modelsRequireResolution;
    private string? _modelsRequiredBackground;
    private IdentityOperationLifetime.Operation? _runningRequest;

    internal AiImageEditDialogViewModel(
        IAiEntitlementService entitlements,
        IAiOperationAvailabilityService availability,
        IAiModelCatalogService modelCatalog,
        IAiPlanCoordinator aiPlanCoordinator,
        IAiImageEditingService images,
        IAuthenticatedContentService content,
        EditViewModel? editViewModel,
        AiRequestRecoveryContext requestRecoveryContext)
    {
        _entitlements = entitlements ?? throw new ArgumentNullException(nameof(entitlements));
        _availability = availability ?? throw new ArgumentNullException(nameof(availability));
        _modelCatalog = modelCatalog ?? throw new ArgumentNullException(nameof(modelCatalog));
        _aiPlanCoordinator = aiPlanCoordinator
            ?? throw new ArgumentNullException(nameof(aiPlanCoordinator));
        _images = images ?? throw new ArgumentNullException(nameof(images));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _editViewModel = editViewModel;
        _requestRecoveryContext = requestRecoveryContext;
        _requestKey = new(
            recoveryContext: requestRecoveryContext,
            operation: "image.edit");
        Usage = new AiUsageViewModel(_entitlements.Entitlements).DisposeWith(_disposables);
        // Every edit hands the model the picture being edited, so one that takes
        // no reference image is registered and unusable however the request is
        // shaped. An upscale asks for a size on top of that, and removing a
        // background asks for a transparent one — neither of which every model
        // that takes a picture can be asked for.
        ModelPicker = new AiModelPickerViewModel(_modelCatalog, _entitlements)
        {
            Filter = model =>
                model.Image is not { } image
                || image.CanServeAnything(
                    requiresReferenceImages: true,
                    requiresResolution: _modelsRequireResolution,
                    requiredBackground: _modelsRequiredBackground),
        }
            .DisposeWith(_disposables);
        _recovery = new AiRecoveryState(
            _requestKey,
            ModelPicker,
            new AiOperationId("image.edit"),
            Error,
            new AiRecoveryLogs(
                ex => _logger.LogError(ex, "Failed to read image-edit recovery attempts."),
                ex => _logger.LogError(ex, "Failed to clear image-edit state after an account change."),
                ex => _logger.LogWarning(ex, "Failed to abandon image-edit recovery attempt.")),
            TryRecoverPendingAttempt);
        PromptLibrary = new AiPromptLibraryViewModel(
                PromptTaskKind.ImageEdit,
                () => Prompt.Value,
                prompt => Prompt.Value = prompt,
                recoveryContext: requestRecoveryContext)
            .DisposeWith(_disposables);

        Tasks =
        [
            new AiImageEditTaskOption(RemoveBackgroundTask, Strings.AiEditRemoveBackground),
            new AiImageEditTaskOption(UpscaleTask, Strings.AiEditUpscale),
            new AiImageEditTaskOption(RestyleTask, Strings.AiEditRestyle),
            new AiImageEditTaskOption(RemoveObjectTask, Strings.AiEditRemoveObject),
            new AiImageEditTaskOption(OutpaintTask, Strings.AiEditOutpaint),
        ];
        SelectedTask = new ReactivePropertySlim<AiImageEditTaskOption>(Tasks[0])
            .DisposeWith(_disposables);
        EstimatedUsage = new AiUsageEstimateViewModel(
                Usage,
                SelectedTask.CombineLatest(
                    _entitlements.Entitlements,
                    (task, entitlements) => entitlements?.Availability.GetState(
                        AiOperations.ImageEdit(new AiImageEditTaskId(task.Value)))
                        ?? AiOperationAvailabilityState.Unknown))
            .DisposeWith(_disposables);

        ComparisonModes =
        [
            new AiImageComparisonMode("result", Strings.AiPreviewResult),
            new AiImageComparisonMode("original", Strings.AiPreviewOriginal),
            new AiImageComparisonMode("side_by_side", Strings.AiPreviewSideBySide),
        ];
        SelectedComparisonMode = new ReactivePropertySlim<AiImageComparisonMode>(ComparisonModes[0])
            .DisposeWith(_disposables);

        OutpaintExpansionOptions =
        [
            new AiOutpaintExpansionOption(10),
            new AiOutpaintExpansionOption(25),
            new AiOutpaintExpansionOption(50),
        ];
        SelectedOutpaintExpansion = new ReactivePropertySlim<AiOutpaintExpansionOption>(OutpaintExpansionOptions[1])
            .DisposeWith(_disposables);
        // Keep every model named by an uncollected request even after it leaves the catalog.
        // A task can have more than one such request, so return all of them.
        ModelPicker.KeepOffered = requested => ModelsOfOutstandingRequestsFor(requested);
        // Only the list already on screen is held back: a task whose list is not here yet is fetched
        // even while its own request waits, or the screen has no model to send for it.
        ModelPicker.CanReload = requested =>
            ModelPicker.Operation != requested
            || !HoldsNameFor(SelectedTask.Value.Value);
        // Each task is a separate operation with its own models, so the list follows the task.
        SelectedTask
            .Subscribe(task => _ = ReloadModelsAsync(task))
            .DisposeWith(_disposables);
        RequiresPrompt = SelectedTask
            .Select(task => task.Value is RestyleTask or RemoveObjectTask or OutpaintTask)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        ShowOutpaintExpansion = SelectedTask
            .Select(task => task.Value == OutpaintTask)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        PromptWatermark = SelectedTask
            .Select(task => task.Value switch
            {
                RestyleTask => Strings.AiEditRestylePrompt,
                RemoveObjectTask => Strings.AiEditRemoveObjectPrompt,
                OutpaintTask => Strings.AiEditOutpaintPrompt,
                _ => Strings.AiPrompt_Placeholder,
            })
            .ToReadOnlyReactivePropertySlim(Strings.AiPrompt_Placeholder)
            .DisposeWith(_disposables);

        IsEditing = new ReactivePropertySlim<bool>(false)
            .DisposeWith(_disposables);

        PromptValidationError = SelectedTask
            .CombineLatest(
                Prompt,
                (task, prompt) => GetPromptValidationError(task.Value, prompt))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        VisiblePromptValidationError = AiPromptValidation
            .WhileTyping(PromptValidationError, Prompt)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        SelectSourceFileCommand = new AsyncReactiveCommand()
            .WithSubscribe(SelectSourceFileAsync);

        SourceFilePath.Subscribe(LoadOriginalPreview).DisposeWith(_disposables);

        // A name is only outstanding for the task it was built on. Switching
        // tasks is starting a different request, which has to be paid for and
        // has to be offered a model of its own.
        HoldsRequestName = _outstandingRevision
            .CombineLatest(SelectedTask, (_, selected) => HoldsNameFor(selected.Value))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        CanEdit = SourceFilePath
            .Select(x => !string.IsNullOrEmpty(x))
            .CombineLatest(IsEditing, (hasSource, editing) => hasSource && !editing)
            .CombineLatest(PromptValidationError, (canEdit, error) => canEdit && error is null)
            .WhenAffordable(EstimatedUsage.CanAfford, HoldsRequestName)
            .WhenSomeModelUsable(ModelPicker.OffersNothingUsable, HoldsRequestName)
            .WhenModelsLoaded(ModelPicker.IsLoaded)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Edit = new AsyncReactiveCommand(CanEdit)
            .WithSubscribe(EditCore);

        CanAddToScene = ResultImage
            .Select(x => x != null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        AddToScene = new AsyncReactiveCommand(CanAddToScene)
            .WithSubscribe(AddToSceneCore);

        SaveToFile = new AsyncReactiveCommand(CanAddToScene)
            .WithSubscribe(SaveToFileCore);

        StopEditing = new ReactiveCommand(IsEditing);
        StopEditing.Subscribe(() => _runningRequest?.Cancel()).DisposeWith(_disposables);

        OpenAiPlan = new ReactiveCommand();
        OpenAiPlan.Subscribe(aiPlanCoordinator.OpenAiPlan).DisposeWith(_disposables);

        ShowJoinPro = Usage.HasSnapshot
            .CombineLatest(Usage.CanUseAi, (hasSnapshot, canUseAi) => hasSnapshot && !canUseAi)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        ShowOriginalPreview = SelectedComparisonMode
            .CombineLatest(OriginalImage, (mode, image) => mode.Value == "original" && image is not null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        ShowResultPreview = SelectedComparisonMode
            .CombineLatest(ResultImage, (mode, image) => mode.Value == "result" && image is not null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        ShowSideBySidePreview = SelectedComparisonMode
            .CombineLatest(
                OriginalImage,
                ResultImage,
                (mode, original, result) => mode.Value == "side_by_side" && original is not null && result is not null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        ShowPreviewPlaceholder = OriginalImage
            .CombineLatest(ResultImage, (original, result) => original is null && result is null)
            .ToReadOnlyReactivePropertySlim(true)
            .DisposeWith(_disposables);

        _requestRecoveryContext.IdentityChanged += OnIdentityChanged;

        CoreObject? selectedObject = editViewModel?.GetService<IEditorSelection>()?.SelectedObject.Value;
        SourceFilePath.Value = GetSelectedImageSourcePath(selectedObject);
        _sourceElementId = selectedObject is Element selectedElement
            ? selectedElement.Id.ToString("N")
            : null;

        _ = LoadEntitlementsAsync();
        _recovery.TryAutoRecoverSingleAttempt();
    }

    public IReadOnlyList<AiImageEditTaskOption> Tasks { get; }

    public ReactivePropertySlim<AiImageEditTaskOption> SelectedTask { get; }

    public IReadOnlyList<AiImageComparisonMode> ComparisonModes { get; }

    public ReactivePropertySlim<AiImageComparisonMode> SelectedComparisonMode { get; }

    public IReadOnlyList<AiOutpaintExpansionOption> OutpaintExpansionOptions { get; }

    public ReactivePropertySlim<AiOutpaintExpansionOption> SelectedOutpaintExpansion { get; }

    public ReactivePropertySlim<string> Prompt { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> RequiresPrompt { get; }

    public ReadOnlyReactivePropertySlim<bool> ShowOutpaintExpansion { get; }

    public ReadOnlyReactivePropertySlim<string> PromptWatermark { get; }

    public ReadOnlyReactivePropertySlim<string?> PromptValidationError { get; }

    /// <summary>
    /// The same message, held back until the person has typed something.
    /// </summary>
    public ReadOnlyReactivePropertySlim<string?> VisiblePromptValidationError { get; }

    public ReactivePropertySlim<string?> SourceFilePath { get; } = new();

    public AsyncReactiveCommand SelectSourceFileCommand { get; }

    public ReactivePropertySlim<bool> IsEditing { get; }

    /// <summary>
    /// Whether the task on screen holds a name the server may answer from a job
    /// it has already been paid for.
    /// </summary>
    public ReadOnlyReactivePropertySlim<bool> HoldsRequestName { get; }

    public ReadOnlyReactivePropertySlim<bool> CanEdit { get; }

    public AsyncReactiveCommand Edit { get; }

    /// <summary>
    /// Abandons the edit in flight. An edit runs for as long as the server takes,
    /// so a wrong task or picture must be recoverable without closing the tab.
    /// </summary>
    public ReactiveCommand StopEditing { get; }

    public ReactivePropertySlim<AiPendingAttempt?> SelectedRecoveryAttempt => _recovery.SelectedAttempt;

    public ReadOnlyReactivePropertySlim<IReadOnlyList<AiPendingAttempt>> RecoveryAttempts => _recovery.Attempts;

    public ReadOnlyReactivePropertySlim<bool> RecoveryAvailable => _recovery.Available;

    public ReactiveCommand RecoverSelectedAttempt => _recovery.RecoverSelected;

    public ReactiveCommand AbandonSelectedAttempt => _recovery.AbandonSelected;

    public ReadOnlyReactivePropertySlim<bool> CanAddToScene { get; }

    public AsyncReactiveCommand AddToScene { get; }

    public AsyncReactiveCommand SaveToFile { get; }

    public ReactiveCommand OpenAiPlan { get; }

    internal IAiPlanCoordinator AiPlanCoordinator => _aiPlanCoordinator;

    public ReactivePropertySlim<Ref<Bitmap>?> ResultImage { get; } = new();

    public ReactivePropertySlim<Ref<Bitmap>?> OriginalImage { get; } = new();

    public ReadOnlyReactivePropertySlim<bool> ShowOriginalPreview { get; }

    public ReadOnlyReactivePropertySlim<bool> ShowResultPreview { get; }

    public ReadOnlyReactivePropertySlim<bool> ShowSideBySidePreview { get; }

    public ReadOnlyReactivePropertySlim<bool> ShowPreviewPlaceholder { get; }

    internal AiUsageViewModel Usage { get; }

    internal AiModelPickerViewModel ModelPicker { get; }

    internal AiUsageEstimateViewModel EstimatedUsage { get; }

    internal AiPromptLibraryViewModel PromptLibrary { get; }

    // Test seams remain unset in production; the null path below uses the
    // current Avalonia storage provider and result importer.
    internal Func<CancellationToken, Task<string?>>? SourceFilePicker { get; set; }

    internal Func<CancellationToken, Task<AiSaveFileDestination?>>? SaveFilePicker { get; set; }

    internal Func<Bitmap, AiResultImportOptions, CancellationToken, Task<ElementAddResult>>?
        ResultImporter
    { get; set; }

    public ReadOnlyReactivePropertySlim<bool> ShowJoinPro { get; }

    public ReactivePropertySlim<string?> Error { get; } = new();

    public void Dispose() => _ = BeginDisposeAsync();

    public ValueTask DisposeAsync() => new(BeginDisposeAsync());

    private IdentityOperationLifetime.Operation? TryEnterIdentityOperation()
        => _identityOperations.TryEnter(_operations);

    private Task BeginDisposeAsync() => _disposal.Run(DisposeCoreAsync);

    private async Task DisposeCoreAsync()
    {
        _identityOperations.Dispose();
        await _operations.DisposeAsync(async () =>
        {
            ResultImage.Value?.Dispose();
            ResultImage.Dispose();
            OriginalImage.Value?.Dispose();
            OriginalImage.Dispose();
            SourceFilePath.Dispose();
            Prompt.Dispose();
            Error.Dispose();
            _requestRecoveryContext.IdentityChanged -= OnIdentityChanged;
            _requestKey.Dispose();
            _recovery.Dispose();
            _outstandingRevision.Dispose();
            _disposables.Dispose();
        });
    }

    internal static string? GetSelectedImageSourcePath(CoreObject? selectedObject)
    {
        if (selectedObject is not Element element)
            return null;

        return element.Objects
            .OfType<SourceImage>()
            .Select(source => source.Source.CurrentValue)
            .Where(source => source is { HasUri: true } && source.Uri.IsFile)
            .Select(source => source!.Uri.LocalPath)
            .FirstOrDefault(File.Exists);
    }

    private async Task LoadEntitlementsAsync()
    {
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
        if (operation is null)
            return;
        try
        {
            await _entitlements.RefreshAsync(operation.CancellationToken);
            // Never under an outstanding name: the model the picker lands on is
            // part of what names the request waiting to be collected.
            if (!ModelPicker.IsLoaded.Value || !HoldsNameFor(SelectedTask.Value.Value))
            {
                AiOperationId requested = AiOperations.ImageEdit(
                    new AiImageEditTaskId(SelectedTask.Value.Value));
                await ModelPicker.LoadAsync(
                    requested,
                    _requestKey.PreferredPersistedModel(requested),
                    _requestKey.HasExplicitNullPersistedModel(requested),
                    operation.CancellationToken);
                _recovery.SelectRecoveredModel();
            }
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load AI entitlements.");
        }
    }

    // Asking for a size is what upscaling is; the other tasks keep the one they
    // were given.
    private static bool RequiresResolution(string? task)
        => string.Equals(task, UpscaleTask, StringComparison.Ordinal);

    // Where the task sits in the request's parts.
    private const int TaskPartIndex = 0;
    // Where the model sits in the request's parts.
    private const int ModelPartIndex = 2;

    // Cutting a background out is asking for a transparent one.
    private static string? RequiredBackground(string? task)
        => string.Equals(task, RemoveBackgroundTask, StringComparison.Ordinal)
            ? "transparent"
            : null;

    /// <inheritdoc />
    public void RefreshModels()
    {
        // A request waiting to be collected is named partly by the model it was
        // sent with, so an operator's change waits until that name is settled.
        // Switching tasks still reloads: that is a different request.
        if (HoldsNameFor(SelectedTask.Value.Value))
            return;
        _ = ReloadModelsAsync(SelectedTask.Value);
    }

    private async Task ReloadModelsAsync(AiImageEditTaskOption task)
    {
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
        if (operation is null)
            return;
        // Read by the picker's filter, which runs while the list below loads.
        _modelsRequireResolution = RequiresResolution(task.Value);
        _modelsRequiredBackground = RequiredBackground(task.Value);
        try
        {
            await _entitlements.RefreshAsync(operation.CancellationToken);
            await ModelPicker.LoadAsync(
                AiOperations.ImageEdit(new AiImageEditTaskId(task.Value)),
                // Returning to a task with an uncollected request must restore the model that
                // request named. Choosing one affordable now would create a different request
                // and fail to reach the already-paid result.
                ModelOfOutstandingRequestFor(task.Value),
                _requestKey.HasExplicitNullPersistedModel(
                    AiOperations.ImageEdit(new AiImageEditTaskId(task.Value))),
                operation.CancellationToken);
            _recovery.SelectRecoveredModel();
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load the AI models for an image edit task.");
        }
    }

    public Task SelectSourceFileAsync() => SelectSourceFileAsync(null);

    internal async Task SelectSourceFileAsync(string? droppedPath)
    {
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
        if (operation is null)
            return;
        if (droppedPath is not null)
        {
            try
            {
                // Validate before the path subscription releases the current previews.
                using Bitmap validated = AiImageDecodeValidator.LoadValidatedBitmap(
                    droppedPath,
                    AiRequestLimits.MaxImageUploadBytes);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to load the dropped image preview.");
                operation.TryPublish(() => Error.Value = Strings.AiEditSourcePreviewFailed);
                return;
            }
            operation.TryPublish(() =>
            {
                _sourceElementId = null;
                Error.Value = null;
                SourceFilePath.Value = droppedPath;
            });
            return;
        }
        if (SourceFilePicker is { } picker)
        {
            string? path = await picker(operation.CancellationToken);
            if (path is not null)
                operation.TryPublish(() =>
                {
                    _sourceElementId = null;
                    SourceFilePath.Value = path;
                });
            return;
        }
        if (AiDialogStorage.MainWindowStorage() is not { } storage)
            return;

        FilePickerOpenOptions options = SharedFilePickerOptions.OpenAiInputImage();
        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(options);
        using IDisposable fileOwnership = SharedFilePickerOptions.OwnStorageFiles(files);
        if (files.Count > 0)
        {
            operation.TryPublish(() =>
            {
                _sourceElementId = null;
                SourceFilePath.Value = files[0].Path.LocalPath;
            });
        }
    }

    private void LoadOriginalPreview(string? filePath)
    {
        OriginalImage.Value?.Dispose();
        OriginalImage.Value = null;
        ResultImage.Value?.Dispose();
        ResultImage.Value = null;
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            return;

        try
        {
            OriginalImage.Value = Ref<Bitmap>.Create(
                AiImageDecodeValidator.LoadValidatedBitmap(
                    filePath,
                    AiRequestLimits.MaxImageUploadBytes));
            SelectedComparisonMode.Value = ComparisonModes[1];
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load the selected image preview.");
            Error.Value = Strings.AiEditSourcePreviewFailed;
        }
    }

    private async Task EditCore()
    {
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
        if (operation is null)
            return;
        if (SourceFilePath.Value is not { } filePath)
        {
            operation.TryPublish(() => Error.Value = Strings.AiEditSelectSource);
            return;
        }

        if (!operation.TryPublish(() =>
            {
                Error.Value = null;
                IsEditing.Value = true;
            }))
        {
            return;
        }

        _runningRequest = operation;
        string? preparedFilePath = null;
        AiRequestName issued = default;
        try
        {
            string task = SelectedTask.Value.Value;
            string? prompt = RequiresPrompt.Value ? Prompt.Value.Trim() : null;
            int? outpaintExpansionPercent = task == OutpaintTask
                ? SelectedOutpaintExpansion.Value.Percent
                : null;
            string uploadPath = filePath;
            bool recoveredPreparedSource = _recovery.Active?.Form?.SourceIsPrepared == true;
            // The server fingerprints an upload by the name it arrives under, so
            // the expanded canvas is sent under a name derived from the picture
            // it was made from — never the temporary file's own, which is named
            // for uniqueness on disk and would differ on every attempt.
            string uploadName = recoveredPreparedSource
                && _recovery.Active!.Form!.SourceName is { } preparedName
                ? preparedName
                : Path.GetFileName(filePath);
            if (task == OutpaintTask)
            {
                if (!recoveredPreparedSource)
                {
                    preparedFilePath = PrepareOutpaintSource(
                        filePath,
                        outpaintExpansionPercent!.Value);
                    uploadPath = preparedFilePath;
                    uploadName = $"{Path.GetFileNameWithoutExtension(filePath)}-outpaint.png";
                }

                prompt = ToOutpaintPrompt(prompt);
            }

            // Read once, and named by that reading. What the server
            // fingerprints is the picture that arrives — for an outpaint that
            // is the expanded canvas, not the picture it was made from: two
            // different sources and expansions can expand to the same canvas,
            // and naming the source would ask for the same work twice.
            AiRequestRecoverySource? recoveredSource = recoveredPreparedSource
                ? _recovery.Active!.EffectiveSources.FirstOrDefault(source => source.Role == "image")
                : null;
            if (recoveredSource?.Name is { } recoveredName)
                uploadName = recoveredName;
            byte[] uploadBytes = recoveredSource is not null
                ? _requestKey.ReadSourceBytes(recoveredSource)
                : await AiUploadBytes.ReadWithinAsync(
                    uploadPath,
                    AiRequestLimits.MaxImageUploadBytes,
                    operation.CancellationToken);
            AiOperationId editOperation = AiOperations.ImageEdit(new AiImageEditTaskId(task));
            string?[] requestParts =
            [
                task,
                prompt,
                null,
                AiRequestKey.FileStamp(uploadName, uploadBytes),
            ];
            // Name the model this operation's picker shows; a recovered request keeps its own (see
            // AiRecoveryState.ModelForRequest). Substituting another would charge for something the
            // screen does not show.
            AiModelId? model =
                ModelPicker.Operation == editOperation ? _recovery.ModelForRequest(ModelPicker.SelectedModel) : null;
            requestParts[ModelPartIndex] = model?.Value;
            AiRequestFormSnapshot form = new(
                Prompt: Prompt.Value,
                Task: task,
                OutpaintExpansionPercent: outpaintExpansionPercent,
                SourceName: uploadName,
                SourceIsPrepared: task == OutpaintTask,
                SourceElementId: _sourceElementId);
            AiRequestRecoverySource recoverySource = task == OutpaintTask
                ? _requestKey.CreateDurableSource(
                    "image",
                    uploadName,
                    uploadBytes,
                    _sourceElementId)
                : FileAiRequestRecoveryStore.CreateExternalSource(
                    "image",
                    filePath,
                    uploadName,
                    uploadBytes,
                    _sourceElementId);
            if (_recovery.Active is { } selected
                && !_requestKey.MatchesPending(selected, requestParts))
            {
                _requestKey.CleanupUncommittedSources([recoverySource]);
                operation.TryPublish(() => Error.Value = Strings.AiRequestChanged);
                return;
            }
            AiRequestName name = _requestKey.NameFor(requestParts, form, [recoverySource]);
            issued = name;
            _outstanding.Remember(name, requestParts);
            _outstandingRevision.Value++;
            using IDisposable authenticatedScope = _requestKey.EnterAuthenticatedScope(name);
            using AiRequestRecoveryLease? claim = _requestKey.TryClaim(name);
            if (claim is null)
            {
                operation.TryPublish(() => Error.Value = Strings.AiResultUnavailable);
                return;
            }

            AiImageResult response = await AiMeteredDispatch.SendAsync(
                _requestKey,
                name,
                claim,
                token => _availability.CheckAsync(
                    new AiOperationAvailabilityRequest.Fixed(editOperation, model),
                    token),
                token => _images.EditAsync(
                    new AiImageEditRequest(
                        AiUploadSource.FromBytes(uploadName, uploadBytes),
                        new AiImageEditTaskId(task),
                        prompt,
                        model,
                        name.Key),
                    token),
                WithdrawRequestName,
                operation.CancellationToken);

            // Past here the picture has been paid for. Whatever goes wrong
            // while it is fetched, the name stays: it is the way back to it.
            Ref<Bitmap> resultImage = await AiImageResultDownload.DownloadBitmapAsync(
                _content,
                response.ContentUri,
                operation.CancellationToken);
            RetireRequestName(name);
            if (!operation.TryPublish(() =>
                {
                    ResultImage.Value?.Dispose();
                    ResultImage.Value = resultImage;
                    SelectedComparisonMode.Value = ComparisonModes[0];
                    if (prompt is not null)
                    {
                        PromptLibrary.Record(Prompt.Value.Trim());
                    }
                }))
            {
                resultImage.Dispose();
            }
        }
        // Every refusal that reserves nothing withdrew its name where it was
        // raised, next to the request that never left.
        catch (Exception ex) when (AiRequestFailure.Classify(ex) is { } failure)
        {
            if (failure.RetiresName)
                RetireRequestName(issued);
            if (failure.IsResultDownloadFailure)
                _logger.LogError(ex, "Failed to download the AI result.");
            operation.TryPublish(() => Error.Value = failure.Message);
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to edit AI image.");
            operation.TryPublish(() => Error.Value = Strings.AiUnexpectedError);
        }
        finally
        {
            if (ReferenceEquals(_runningRequest, operation))
                _runningRequest = null;
            operation.TryPublish(() => IsEditing.Value = false);
            if (preparedFilePath is not null)
            {
                try
                {
                    File.Delete(preparedFilePath);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Failed to remove temporary outpaint input {Path}", preparedFilePath);
                }
            }
        }
    }

    internal static string PrepareOutpaintSource(string filePath, int expansionPercent)
    {
        if (expansionPercent is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(expansionPercent));

        using Bitmap source = AiImageDecodeValidator.LoadValidatedBitmap(
            filePath,
            AiRequestLimits.MaxImageUploadBytes);
        (int expandedWidth, int expandedHeight, int horizontal, int vertical) =
            GetOutpaintDimensions(source.Width, source.Height, expansionPercent);
        using Bitmap expanded = source.MakeBorder(vertical, vertical, horizontal, horizontal);
        (string result, FileStream stream) = AiTemporaryFileStore.Create("inputs", "outpaint", ".png");
        using (stream)
        {
            expanded.Save(stream, EncodedImageFormat.Png);
        }
        return result;
    }

    internal static (int Width, int Height, int Horizontal, int Vertical) GetOutpaintDimensions(
        int sourceWidth,
        int sourceHeight,
        int expansionPercent)
    {
        (int expandedWidth, int expandedHeight, int horizontal, int vertical) =
            Beutl.NodeGraph.Generative.AiImageEditTasks.GetOutpaintDimensions(
                sourceWidth,
                sourceHeight,
                expansionPercent);
        AiImageDecodeValidator.ValidateDimensions(expandedWidth, expandedHeight);
        return (expandedWidth, expandedHeight, horizontal, vertical);
    }

    // The ids the server knows the edits by. Requests and recovery records carry them.
    private const string RemoveBackgroundTask = "remove_background";
    private const string UpscaleTask = "upscale";
    private const string RestyleTask = "restyle";
    private const string RemoveObjectTask = "remove_object";
    private const string OutpaintTask = "outpaint";

    // An outpaint request leads with this, so its length counts against the prompt limit too.
    private const string OutpaintPromptPrefix =
        "Extend the image naturally into the transparent canvas while preserving the original center.";

    private static string ToOutpaintPrompt(string? prompt) => $"{OutpaintPromptPrefix} {prompt}";

    private static string? GetPromptValidationError(string task, string prompt)
    {
        if (task is not (RestyleTask or RemoveObjectTask or OutpaintTask))
            return null;
        if (string.IsNullOrWhiteSpace(prompt))
            return Strings.AiPromptRequired;

        string finalPrompt = task == OutpaintTask
            ? ToOutpaintPrompt(prompt.Trim())
            : prompt.Trim();
        return finalPrompt.Length > AiRequestLimits.MaxPromptLength
            ? AiPromptComposer.PromptTooLongMessage
            : null;
    }

    private async Task AddToSceneCore()
    {
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
        if (operation is null)
            return;
        if (_editViewModel == null || ResultImage.Value?.Value is not { } bitmap)
            return;
        if (!operation.IsCurrent)
            return;

        await AiDialogResults.AddImageToSceneAsync(
            operation,
            _editViewModel,
            bitmap,
            ResultImporter,
            Strings.AiImageEdit,
            "edited image",
            Error,
            ex => _logger.LogError(ex, "Failed to add the AI edited image to the scene."));
    }

    private async Task SaveToFileCore()
    {
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
        if (operation is null)
            return;
        using Ref<Bitmap>? resultImage = ResultImage.Value?.TryClone();
        if (resultImage?.Value is not { } bitmap)
            return;

        await AiDialogResults.SavePngAsync(
            operation,
            bitmap,
            SaveFilePicker,
            "AI Edit",
            "Failed to encode the edited AI image as PNG.",
            Strings.AiImageEdit,
            Error,
            ex => _logger.LogError(ex, "Failed to save the AI edited image."));
    }

}

internal sealed record AiImageEditTaskOption(string Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}

internal sealed record AiImageComparisonMode(string Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}

internal sealed record AiOutpaintExpansionOption(int Percent)
{
    public override string ToString() => $"{Percent}%";
}
