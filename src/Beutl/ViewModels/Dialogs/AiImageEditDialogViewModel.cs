using System.Globalization;
using System.Reactive.Disposables;
using Avalonia;
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
using Beutl.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

public sealed class AiImageEditDialogViewModel : IDisposable, IAsyncDisposable, IAiModelListConsumer
{
    private readonly CompositeDisposable _disposables = [];
    private readonly AsyncOperationLifetime _operations = new();
    private readonly object _disposeGate = new();
    private readonly ILogger _logger = Log.CreateLogger<AiImageEditDialogViewModel>();
    private readonly IAiEntitlementService _entitlements;
    private readonly IAiOperationAvailabilityService _availability;
    private readonly IAiModelCatalogService _modelCatalog;
    private readonly IAiPlanCoordinator _aiPlanCoordinator;
    private readonly IAiImageEditingService _images;
    private readonly IAuthenticatedContentService _content;
    private readonly AiRequestKey _requestKey = new();
    // The model the outstanding name was built from. A refresh that withdraws
    // that model would otherwise rebuild the name around whatever the picker
    // fell back to, and the job the first attempt paid for would be left behind.
    // 決着していない名前ごとに、その依頼が何を名乗ったか。1 件だけ覚えていると、
    // 別の依頼を出した時点で前の依頼のモデルを忘れ、戻ってきたときに買い直す。
    private readonly AiOutstandingRequests _outstanding = new();
    // 抱えている名前が変わったことを画面へ伝えるためだけの数。どの task に
    // 名前が残っているかは _outstanding が知っている。
    private readonly ReactivePropertySlim<int> _outstandingRevision = new();
    private readonly EditViewModel? _editViewModel;
    private string? _sourceElementId;
    private bool _modelsRequireResolution;
    private string? _modelsRequiredBackground;
    private Task? _disposeTask;
    private AsyncOperationLifetime.Operation? _runningRequest;

    public AiImageEditDialogViewModel(
        IAiEntitlementService entitlements,
        IAiOperationAvailabilityService availability,
        IAiModelCatalogService modelCatalog,
        IAiPlanCoordinator aiPlanCoordinator,
        IAiImageEditingService images,
        IAuthenticatedContentService content,
        EditViewModel? editViewModel = null)
    {
        _entitlements = entitlements ?? throw new ArgumentNullException(nameof(entitlements));
        _availability = availability ?? throw new ArgumentNullException(nameof(availability));
        _modelCatalog = modelCatalog ?? throw new ArgumentNullException(nameof(modelCatalog));
        _aiPlanCoordinator = aiPlanCoordinator
            ?? throw new ArgumentNullException(nameof(aiPlanCoordinator));
        _images = images ?? throw new ArgumentNullException(nameof(images));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _editViewModel = editViewModel;
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
        PromptLibrary = new AiPromptLibraryViewModel(
                PromptTaskKind.ImageEdit,
                () => Prompt.Value,
                prompt => Prompt.Value = prompt)
            .DisposeWith(_disposables);

        Tasks =
        [
            new AiImageEditTaskOption("remove_background", Strings.AiEditRemoveBackground),
            new AiImageEditTaskOption("upscale", Strings.AiEditUpscale),
            new AiImageEditTaskOption("restyle", Strings.AiEditRestyle),
            new AiImageEditTaskOption("remove_object", Strings.AiEditRemoveObject),
            new AiImageEditTaskOption("outpaint", Strings.AiEditOutpaint),
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
            new AiImageComparisonMode("result", Strings.AiPreviewResult, false, true),
            new AiImageComparisonMode("original", Strings.AiPreviewOriginal, true, false),
            new AiImageComparisonMode("side_by_side", Strings.AiPreviewSideBySide, true, true),
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
        // Each task is a separate operation with its own models, so the list
        // has to follow the task rather than be read once.
        SelectedTask
            .Subscribe(task => _ = ReloadModelsAsync(task))
            .DisposeWith(_disposables);
        ModelPicker.CanReload = () => !HoldsNameFor(SelectedTask.Value.Value);
        RequiresPrompt = SelectedTask
            .Select(task => task.Value is "restyle" or "remove_object" or "outpaint")
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        ShowOutpaintExpansion = SelectedTask
            .Select(task => task.Value == "outpaint")
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        PromptWatermark = SelectedTask
            .Select(task => task.Value switch
            {
                "restyle" => Strings.AiEditRestylePrompt,
                "remove_object" => Strings.AiEditRemoveObjectPrompt,
                "outpaint" => Strings.AiEditOutpaintPrompt,
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
            .CombineLatest(
                EstimatedUsage.CanAfford,
                HoldsRequestName,
                // Or a name already handed out: the server answers a repeat with the
                // job that name made before it looks at the balance, so the request
                // that spent the last of it is exactly the one that must stay
                // collectable.
                (canEdit, canAfford, outstanding) => canEdit && (canAfford || outstanding))
            .CombineLatest(
                ModelPicker.OffersNothingUsable,
                HoldsRequestName,
                // Every model the operation registered was ruled out, so a new
                // request would be refused however it is shaped — but a name
                // already handed out is answered from the job it made, whatever
                // the catalog says now.
                (can, nothingUsable, outstanding) =>
                    can && (!nothingUsable || outstanding))
            // Until the list has been asked for, a request would name no model
            // and run on the server's default, which may cost more than what
            // this task was about to offer.
            .CombineLatest(ModelPicker.IsLoaded, (can, loaded) => can && loaded)
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

        CoreObject? selectedObject = editViewModel?.GetService<IEditorSelection>()?.SelectedObject.Value;
        SourceFilePath.Value = GetSelectedImageSourcePath(selectedObject);
        _sourceElementId = selectedObject is Element selectedElement
            ? selectedElement.Id.ToString("N")
            : null;

        _ = LoadEntitlementsAsync();
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

    public ReadOnlyReactivePropertySlim<bool> ShowJoinPro { get; }

    public ReactivePropertySlim<string?> Error { get; } = new();

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
        ResultImage.Value?.Dispose();
        ResultImage.Dispose();
        OriginalImage.Value?.Dispose();
        OriginalImage.Dispose();
        SourceFilePath.Dispose();
        Prompt.Dispose();
        Error.Dispose();
        _outstandingRevision.Dispose();
        _disposables.Dispose();
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

    // The model a request should carry: the one the outstanding name was built
    // from while there is one, and the picker's otherwise.
    // Only this one request is settled. Retiring the whole run instead would
    // throw away the name of anything else still waiting to be collected.
    private void RetireRequestName(AiRequestName name)
    {
        _requestKey.Retire(name);
        Forget(name);
        // Reloads were held back while that name was outstanding, so this is
        // where an operator's change to the model list finally lands.
        _ = ReloadModelsAsync(SelectedTask.Value);
    }

    // A name the server never made a job under. Withdrawing it lets the picker
    // move again and puts the balance check back in front of the next attempt.
    private void WithdrawRequestName(AiRequestName name)
    {
        _requestKey.Withdraw(name);
        Forget(name);
    }

    private void Forget(AiRequestName name)
    {
        _outstanding.Forget(name);
        _outstandingRevision.Value++;
    }

    // Whatever the outstanding name was built from, including no model at all:
    // a request that named none was fingerprinted without one, and letting a
    // catalog that has since loaded name one would make it a different request.
    // Only for the same request: an edit of another picture, or with another
    // prompt, is a new request and is priced and run on the model on screen.
    // Whether any request still waiting to be collected belongs to this task.
    // Each of the five is its own operation with its own models and its own
    // price, so a name outstanding on one says nothing about another.
    private bool HoldsNameFor(string task)
        => _outstanding.Any(request =>
            string.Equals(request[TaskPartIndex], task, StringComparison.Ordinal));

    private async Task LoadEntitlementsAsync()
    {
        using AsyncOperationLifetime.Operation? operation = _operations.TryEnter();
        if (operation is null)
            return;
        try
        {
            await _entitlements.RefreshAsync(operation.CancellationToken);
            // Never under an outstanding name: the model the picker lands on is
            // part of what names the request waiting to be collected.
            if (!HoldsNameFor(SelectedTask.Value.Value))
            {
                await ModelPicker.LoadAsync(
                    AiOperations.ImageEdit(new AiImageEditTaskId(SelectedTask.Value.Value)),
                    operation.CancellationToken);
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
        => string.Equals(task, "upscale", StringComparison.Ordinal);

    // Cutting a background out is asking for a transparent one.
    // Where the model sits in the request's parts. It is filled in last: which
    // model a request carries depends on whether a name is already outstanding
    // for the rest of it.
    // Where the task and the model sit in the request's parts.
    private const int TaskPartIndex = 0;
    private const int ModelPartIndex = 2;

    private static string? RequiredBackground(string? task)
        => string.Equals(task, "remove_background", StringComparison.Ordinal)
            ? "transparent"
            : null;

    /// <summary>
    /// Re-reads the model list. The catalog is cached with a freshness window,
    /// so this costs nothing while it is fresh and picks up a model an operator
    /// added, removed or reordered once it is not — which a workspace tab left
    /// open would otherwise never see.
    /// </summary>
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
        using AsyncOperationLifetime.Operation? operation = _operations.TryEnter();
        if (operation is null)
            return;
        // Read by the picker's filter, which runs while the list below loads.
        _modelsRequireResolution = RequiresResolution(task.Value);
        _modelsRequiredBackground = RequiredBackground(task.Value);
        try
        {
            await ModelPicker.LoadAsync(
                AiOperations.ImageEdit(new AiImageEditTaskId(task.Value)),
                operation.CancellationToken);
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load the AI models for an image edit task.");
        }
    }

    public async Task SelectSourceFileAsync()
    {
        using AsyncOperationLifetime.Operation? operation = _operations.TryEnter();
        if (operation is null)
            return;
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime
            { MainWindow: { } window })
            return;

        if (TopLevel.GetTopLevel(window)?.StorageProvider is not { } storage)
            return;

        FilePickerOpenOptions options = SharedFilePickerOptions.OpenAiInputImage();
        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(options);
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
            OriginalImage.Value = Ref<Bitmap>.Create(Bitmap.FromFile(filePath));
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
        using AsyncOperationLifetime.Operation? operation = _operations.TryEnter();
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
            int? outpaintExpansionPercent = task == "outpaint"
                ? SelectedOutpaintExpansion.Value.Percent
                : null;
            string uploadPath = filePath;
            // The server fingerprints an upload by the name it arrives under, so
            // the expanded canvas is sent under a name derived from the picture
            // it was made from — never the temporary file's own, which is named
            // for uniqueness on disk and would differ on every attempt.
            string uploadName = Path.GetFileName(filePath);
            if (task == "outpaint")
            {
                preparedFilePath = PrepareOutpaintSource(
                    filePath,
                    outpaintExpansionPercent!.Value);
                uploadPath = preparedFilePath;
                uploadName = $"{Path.GetFileNameWithoutExtension(filePath)}-outpaint.png";
                prompt = $"Extend the image naturally into the transparent canvas while preserving the original center. {prompt}";
            }

            AiOperationId editOperation = AiOperations.ImageEdit(new AiImageEditTaskId(task));
            // Only the model the picker is currently showing for this task; a
            // selection left over from another task belongs to another
            // operation and would be refused.
            // The model's place is left empty until it is known, because which
            // model this request carries depends on whether a name is already
            // outstanding for the rest of it.
            string?[] requestParts =
            [
                task,
                prompt,
                null,
                outpaintExpansionPercent?.ToString(CultureInfo.InvariantCulture),
                AiRequestKey.FileStamp(filePath),
            ];
            AiModelId? model =
                ModelPicker.Operation == editOperation ? ModelPicker.SelectedModel : null;
            requestParts[ModelPartIndex] = model?.Value;
            AiRequestName name = _requestKey.NameFor(requestParts);
            issued = name;
            _outstanding.Remember(name, requestParts);
            _outstandingRevision.Value++;

            // Before it goes out. A name that ends here reached nothing.
            try
            {
                // Not for a repeat: the server looks up the job this name
                // already made before it looks at the balance, so refusing here
                // would refuse to collect something already paid for.
                if (!name.IsRepeat
                    && !await _availability.CheckAsync(
                        new AiOperationAvailabilityRequest.Fixed(editOperation, model),
                        operation.CancellationToken))
                {
                    throw new AiUsageLimitExceededException();
                }
            }
            catch
            {
                WithdrawRequestName(name);
                throw;
            }

            AiImageResult response;
            try
            {
                response = await _images.EditAsync(
                    new AiImageEditRequest(
                        AiUploadSource.FromFile(uploadPath, uploadName),
                        new AiImageEditTaskId(task),
                        prompt,
                        model,
                        name.Key),
                    operation.CancellationToken);
            }
            catch (Exception ex) when (AiRequestOutcome.ReservedNothing(ex))
            {
                WithdrawRequestName(name);
                throw;
            }

            // Past here the picture has been paid for. Whatever goes wrong
            // while it is fetched, the name stays: it is the way back to it.
            using var stream = new MemoryStream();
            await _content.CopyToAsync(response.ContentUri, stream, operation.CancellationToken);
            operation.CancellationToken.ThrowIfCancellationRequested();
            stream.Position = 0;
            var resultImage = Ref<Bitmap>.Create(Bitmap.FromStream(stream));
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
        // raised, next to the request that never left. These only say what
        // happened.
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
        catch (AiFileTooLargeException)
        {
            operation.TryPublish(() => Error.Value = Strings.AiFileTooLarge);
        }
        // The job ran and was charged for; only fetching what it produced
        // failed, and that is still waiting in the job history.
        catch (AiContentUnavailableException ex)
        {
            _logger.LogError(ex, "Failed to download the AI result.");
            operation.TryPublish(() => Error.Value = Strings.AiResultDownloadFailed);
        }
        // Settled and refunded server-side: the key that named it would keep
        // answering with that failure, so the next attempt asks under a new one.
        // Charged for and still the server's; asking again under the same name
        // is what recovers it, so the key stays.
        catch (AiResultUnavailableException)
        {
            operation.TryPublish(() => Error.Value = Strings.AiResultUnavailable);
        }
        catch (AiModelUnavailableException)
        {
            operation.TryPublish(() => Error.Value = Strings.AiModelUnavailable);
        }
        // Refused before the operation was reserved, so nothing was charged;
        // what has to change is the model or the shape of the request.
        catch (AiModelDoesNotSupportRequestException)
        {
            operation.TryPublish(() => Error.Value = Strings.AiModelDoesNotSupportRequest);
        }
        // The server settled this job as failed and refunded it. Its name would
        // keep answering with that failure, so the next attempt takes a new one.
        catch (AiProviderErrorException)
        {
            RetireRequestName(issued);
            operation.TryPublish(() => Error.Value = Strings.AiProviderError);
        }
        // Reachable because a request keeps its name across attempts: asking
        // again for one the server is still working on is how its result is
        // recovered rather than bought twice, and until it finishes the answer
        // is this. The key stays — it is still the way back to that job.
        catch (AiRequestInProgressException)
        {
            operation.TryPublish(() => Error.Value = Strings.AiRequestInProgress);
        }
        // The job that key created is gone, so the key can only ever answer
        // with that. The next attempt has to be a new request.
        // 送った名前が、別の依頼のものだった。画面の中身を戻せばその依頼として
        // 送り直せるので、名前は残す——ここで捨てると、支払い済みかもしれない
        // job へ戻る道が閉じる。
        catch (AiRequestChangedException)
        {
            operation.TryPublish(() => Error.Value = Strings.AiRequestChanged);
        }
        catch (AiRequestWasDeletedException)
        {
            RetireRequestName(issued);
            operation.TryPublish(() => Error.Value = Strings.AiRequestWasDeleted);
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

        using Bitmap source = Bitmap.FromFile(filePath);
        int horizontal = Math.Max(1, (int)Math.Round(source.Width * expansionPercent / 100d));
        int vertical = Math.Max(1, (int)Math.Round(source.Height * expansionPercent / 100d));
        using Bitmap expanded = source.MakeBorder(vertical, vertical, horizontal, horizontal);
        (string result, FileStream stream) = AiTemporaryFileStore.Create("inputs", "outpaint", ".png");
        using (stream)
        {
            expanded.Save(stream, EncodedImageFormat.Png);
        }
        return result;
    }

    private static string? GetPromptValidationError(string task, string prompt)
    {
        if (task is not ("restyle" or "remove_object" or "outpaint"))
            return null;
        if (string.IsNullOrWhiteSpace(prompt))
            return Strings.AiPromptRequired;

        string finalPrompt = task == "outpaint"
            ? $"Extend the image naturally into the transparent canvas while preserving the original center. {prompt.Trim()}"
            : prompt.Trim();
        return finalPrompt.Length > AiRequestLimits.MaxPromptLength
            ? AiPromptComposer.PromptTooLongMessage
            : null;
    }

    private async Task AddToSceneCore()
    {
        using AsyncOperationLifetime.Operation? operation = _operations.TryEnter();
        if (operation is null)
            return;
        if (_editViewModel == null || ResultImage.Value?.Value is not { } bitmap)
            return;

        try
        {
            TimeSpan start = _editViewModel.Player.CurrentFrame.Value;
            int layer = _editViewModel.Scene.Children
                .Where(item => item.Start <= start && start < item.Range.End)
                .Select(item => item.ZIndex)
                .DefaultIfEmpty(-1)
                .Max() + 1;
            var importer = new AiResultImporter(
                _editViewModel.Scene,
                _editViewModel.GetRequiredService<IElementAdder>());
            ElementAddResult result = await importer.ImportImageAsync(
                bitmap,
                new AiResultImportOptions(
                    start,
                    TimeSpan.FromSeconds(5),
                    layer,
                    Strings.AiImageEdit),
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
                    NotificationService.ShowSuccess(Strings.AiImageEdit, Strings.AiImageAddedToScene));
            }
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add the AI edited image to the scene.");
            operation.TryPublish(() => Error.Value = Strings.AiUnexpectedError);
        }

    }

    private static void EnsureImportSucceeded(ElementAddResult result)
    {
        if (result.IsSuccess)
            return;
        throw new InvalidOperationException(
            $"Failed to add the edited image: {result.Failure?.Id}.",
            result.Failure?.Exception);
    }

    private async Task SaveToFileCore()
    {
        using AsyncOperationLifetime.Operation? operation = _operations.TryEnter();
        if (operation is null)
            return;
        using Ref<Bitmap>? resultImage = AiResultImageLease.Acquire(ResultImage.Value);
        if (resultImage?.Value is not { } bitmap)
            return;

        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime
            { MainWindow: { } window })
            return;

        if (TopLevel.GetTopLevel(window)?.StorageProvider is not { } storage)
            return;

        FilePickerSaveOptions options = SharedFilePickerOptions.SaveImage();
        options.SuggestedFileName = $"AI Edit {DateTime.Now:yyyy-MM-dd HHmmss}";
        options.SuggestedStartLocation = await storage.TryGetWellKnownFolderAsync(WellKnownFolder.Pictures);
        options.DefaultExtension = "png";

        IStorageFile? file = await storage.SaveFilePickerAsync(options);
        if (file == null)
            return;

        try
        {
            await using Stream stream = await file.OpenWriteAsync();
            operation.CancellationToken.ThrowIfCancellationRequested();
            stream.SetLength(0);
            bitmap.Save(stream, EncodedImageFormat.Png);
            operation.TryPublish(() =>
                NotificationService.ShowSuccess(Strings.AiImageEdit, Strings.AiImageSaved));
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save the AI edited image.");
            operation.TryPublish(() => Error.Value = Strings.AiUnexpectedError);
        }
    }

}

public sealed record AiImageEditTaskOption(string Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}

public sealed record AiImageComparisonMode(
    string Value,
    string DisplayName,
    bool ShowOriginal,
    bool ShowResult)
{
    public override string ToString() => DisplayName;
}

public sealed record AiOutpaintExpansionOption(int Percent)
{
    public override string ToString() => $"{Percent}%";
}
