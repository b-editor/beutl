using System.Collections.ObjectModel;
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

public sealed class AiImageGenerationDialogViewModel : IDisposable, IAsyncDisposable, IAiModelListConsumer
{
    private readonly CompositeDisposable _disposables = [];
    private readonly AsyncOperationLifetime _operations = new();
    private readonly object _disposeGate = new();
    private readonly ILogger _logger = Log.CreateLogger<AiImageGenerationDialogViewModel>();
    private readonly IAiEntitlementService _entitlements;
    private readonly IAiOperationAvailabilityService _availability;
    private AsyncOperationLifetime.Operation? _runningRequest;
    private readonly IAiModelCatalogService _modelCatalog;
    private readonly IAiPlanCoordinator _aiPlanCoordinator;
    private readonly IAiImageGenerationService _images;
    private readonly IAuthenticatedContentService _content;
    private readonly AiRequestKey _requestKey = new();
    // The model the outstanding name was built from. A refresh that withdraws
    // that model would otherwise rebuild the name around whatever the picker
    // fell back to, and the job the first attempt paid for would be left behind.
    private AiModelId? _outstandingModel;
    private readonly EditViewModel? _editViewModel;
    private Task? _disposeTask;

    public AiImageGenerationDialogViewModel(
        IAiEntitlementService entitlements,
        IAiOperationAvailabilityService availability,
        IAiModelCatalogService modelCatalog,
        IAiPlanCoordinator aiPlanCoordinator,
        IAiImageGenerationService images,
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
        ModelPicker = new AiModelPickerViewModel(_modelCatalog, _entitlements)
            .DisposeWith(_disposables);
        EstimatedUsage = new AiUsageEstimateViewModel(
                Usage,
                _entitlements.Entitlements.Select(value =>
                    value?.Availability.GetState(AiOperations.ImageGeneration)
                    ?? AiOperationAvailabilityState.Unknown))
            .DisposeWith(_disposables);
        PromptLibrary = new AiPromptLibraryViewModel(
                PromptTaskKind.Image,
                ComposePrompt,
                prompt => Prompt.Value = prompt)
            .DisposeWith(_disposables);

        Replace(
            AspectRatioOptions,
            DefaultAspectRatios.Select(value => new AiImageAspectRatioOption(value)));
        SelectedAspectRatio = new ReactivePropertySlim<AiImageAspectRatioOption>(
                GetSuggestedAspectRatio(AspectRatioOptions, editViewModel?.Scene.FrameSize))
            .DisposeWith(_disposables);
        Replace(
            BackgroundOptions,
            DefaultBackgrounds.Select(value => new AiImageBackgroundOption(value)));
        SelectedBackground = new ReactivePropertySlim<AiImageBackgroundOption>(
                BackgroundOptions[0])
            .DisposeWith(_disposables);
        HasBackgroundChoice = new ReactivePropertySlim<bool>(true)
            .DisposeWith(_disposables);
        SupportsSeed = new ReactivePropertySlim<bool>(true)
            .DisposeWith(_disposables);
        SupportsReferenceImage = new ReactivePropertySlim<bool>(true)
            .DisposeWith(_disposables);
        MaxReferenceImages = new ReactivePropertySlim<int>(AiRequestLimits.MaxImageReferences)
            .DisposeWith(_disposables);
        HasReferenceImages = new ReactivePropertySlim<bool>(false)
            .DisposeWith(_disposables);
        CanAddReferenceImage = new ReactivePropertySlim<bool>(true)
            .DisposeWith(_disposables);
        ReferenceImageCountText = new ReactivePropertySlim<string>(string.Empty)
            .DisposeWith(_disposables);
        Seed = new ReactivePropertySlim<int?>()
            .DisposeWith(_disposables);
        // A model that takes no picture cannot generate from one; offering it
        // would only ever produce a request the server refuses.
        ModelPicker.Filter = model =>
            model.Image is not { } image || image.CanServeAnything(false);
        ModelPicker.Selected.Subscribe(option => ApplyModelCapabilities(option?.Model))
            .DisposeWith(_disposables);

        SelectReferenceImage = new AsyncReactiveCommand()
            .WithSubscribe(SelectReferenceImageAsync);
        ClearReferenceImages = new ReactiveCommand();
        ClearReferenceImages.Subscribe(ClearReferenceImagesCore).DisposeWith(_disposables);
        UpdateReferenceImageState();

        IsGenerating = new ReactivePropertySlim<bool>(false)
            .DisposeWith(_disposables);

        GenerateButtonText = IsGenerating
            .Select(x => x ? Strings.AiGenerating : Strings.AiGenerate)
            .ToReadOnlyReactivePropertySlim(Strings.AiGenerate)
            .DisposeWith(_disposables);

        PromptValidationError = Prompt
            .CombineLatest(
                Style,
                Composition,
                Exclusions,
                (prompt, style, composition, exclusions) =>
                    AiPromptComposer.GetValidationError(new AiPromptParts(
                        prompt,
                        style,
                        composition,
                        Exclusions: exclusions)))
            .ToReadOnlyReactivePropertySlim(Strings.AiPromptRequired)
            .DisposeWith(_disposables);

        VisiblePromptValidationError = AiPromptValidation
            .WhileTyping(PromptValidationError, Prompt, Style, Composition, Exclusions)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        CanGenerate = PromptValidationError
            .CombineLatest(IsGenerating, (error, generating) => error is null && !generating)
            .CombineLatest(
                EstimatedUsage.CanAfford,
                _requestKey.HasOutstandingName,
                // Or a name already handed out: the server answers a repeat with the
                // job that name made before it looks at the balance, so the request
                // that spent the last of it is exactly the one that must stay
                // collectable.
                (canGenerate, canAfford, outstanding) =>
                    canGenerate && (canAfford || outstanding))
            .CombineLatest(
                ModelPicker.OffersNothingUsable,
                _requestKey.HasOutstandingName,
                // Every model the operation registered was ruled out, so a new
                // request would be refused however it is shaped — but a name
                // already handed out is answered from the job it made, whatever
                // the catalog says now.
                (can, nothingUsable, outstanding) =>
                    can && (!nothingUsable || outstanding))
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        Generate = new AsyncReactiveCommand(CanGenerate)
            .WithSubscribe(GenerateCore);

        CanAddToScene = ResultImage
            .Select(x => x != null)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        AddToScene = new AsyncReactiveCommand(CanAddToScene)
            .WithSubscribe(AddToSceneCore);

        SaveToFile = new AsyncReactiveCommand(CanAddToScene)
            .WithSubscribe(SaveToFileCore);

        StopGenerating = new ReactiveCommand(IsGenerating);
        StopGenerating.Subscribe(() => _runningRequest?.Cancel()).DisposeWith(_disposables);

        OpenAiPlan = new ReactiveCommand();
        OpenAiPlan.Subscribe(aiPlanCoordinator.OpenAiPlan).DisposeWith(_disposables);

        ShowJoinPro = Usage.HasSnapshot
            .CombineLatest(Usage.CanUseAi, (hasSnapshot, canUseAi) => hasSnapshot && !canUseAi)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        _ = LoadEntitlementsAsync();
    }

    /// <summary>
    /// The shapes on offer, which follow the chosen model: GPT Image-1 renders
    /// 1:1, 3:2 and 2:3 and refuses the rest. A model the server says nothing
    /// about keeps the list this client has always offered.
    /// </summary>
    public ObservableCollection<AiImageAspectRatioOption> AspectRatioOptions { get; } = [];

    public ReactivePropertySlim<AiImageAspectRatioOption> SelectedAspectRatio { get; }

    /// <summary>
    /// The backgrounds on offer, which follow the chosen model the same way the
    /// shapes do: GPT Image-1 publishes auto, opaque and transparent while GPT
    /// Image-2 publishes auto and opaque. "auto" is always among them — it
    /// sends no background at all, which every model takes.
    /// </summary>
    public ObservableCollection<AiImageBackgroundOption> BackgroundOptions { get; } = [];

    public ReactivePropertySlim<AiImageBackgroundOption> SelectedBackground { get; }

    /// <summary>
    /// False for a model that publishes no background of its own, and for a
    /// model that takes no seed or no picture to work from. The controls are
    /// hidden rather than left to fail: the request would be refused after the
    /// usage was reserved.
    /// </summary>
    public ReactivePropertySlim<bool> HasBackgroundChoice { get; }

    public ReactivePropertySlim<bool> SupportsSeed { get; }

    public ReactivePropertySlim<bool> SupportsReferenceImage { get; }

    /// <summary>
    /// Repeating a seed with the same prompt reproduces the same picture. Null
    /// leaves the choice to the server, which is a different image every run.
    /// </summary>
    public ReactivePropertySlim<int?> Seed { get; }

    public decimal SeedMinimum => AiRequestLimits.MinSeed;

    public decimal SeedMaximum => AiRequestLimits.MaxSeed;

    /// <summary>
    /// The pictures the generation is guided by, in the order the model reads
    /// them. Empty for a generation made from the prompt alone.
    /// </summary>
    public ObservableCollection<AiReferenceImageViewModel> ReferenceImages { get; } = [];

    /// <summary>
    /// How many pictures the chosen model takes, never more than what the
    /// operation's price covers.
    /// </summary>
    public ReactivePropertySlim<int> MaxReferenceImages { get; }

    public ReactivePropertySlim<bool> HasReferenceImages { get; }

    public ReactivePropertySlim<bool> CanAddReferenceImage { get; }

    public ReactivePropertySlim<string> ReferenceImageCountText { get; }

    public AsyncReactiveCommand SelectReferenceImage { get; }

    public ReactiveCommand ClearReferenceImages { get; }

    public ReactivePropertySlim<string> Prompt { get; } = new();

    public ReactivePropertySlim<string> Style { get; } = new();

    public ReactivePropertySlim<string> Composition { get; } = new();

    public ReactivePropertySlim<string> Exclusions { get; } = new();

    public ReactivePropertySlim<bool> IsGenerating { get; }

    public ReadOnlyReactivePropertySlim<string> GenerateButtonText { get; }

    public ReadOnlyReactivePropertySlim<string?> PromptValidationError { get; }

    /// <summary>
    /// The same message, held back until the person has typed something.
    /// </summary>
    public ReadOnlyReactivePropertySlim<string?> VisiblePromptValidationError { get; }

    public ReadOnlyReactivePropertySlim<bool> CanGenerate { get; }

    public AsyncReactiveCommand Generate { get; }

    /// <summary>
    /// Abandons the request in flight. Generation runs for as long as the server
    /// takes, so a wrong prompt must be recoverable without closing the tab.
    /// </summary>
    public ReactiveCommand StopGenerating { get; }

    public ReadOnlyReactivePropertySlim<bool> CanAddToScene { get; }

    public AsyncReactiveCommand AddToScene { get; }

    public AsyncReactiveCommand SaveToFile { get; }

    public ReactiveCommand OpenAiPlan { get; }

    internal IAiPlanCoordinator AiPlanCoordinator => _aiPlanCoordinator;

    public ReactivePropertySlim<Ref<Bitmap>?> ResultImage { get; } = new();

    /// <summary>
    /// The picture as far as the model has taken it, shown while it works.
    /// Cleared when the run ends, whatever it ends in: what is worth keeping is
    /// the finished picture, and a rough one left on screen would be mistaken
    /// for it. Only models whose provider streams send any.
    /// </summary>
    public ReactivePropertySlim<Ref<Bitmap>?> PreviewImage { get; } = new();

    internal AiUsageViewModel Usage { get; }

    internal AiModelPickerViewModel ModelPicker { get; }

    internal AiUsageEstimateViewModel EstimatedUsage { get; }

    internal AiPromptLibraryViewModel PromptLibrary { get; }

    public ReadOnlyReactivePropertySlim<bool> ShowJoinPro { get; }

    public ReactivePropertySlim<string?> Error { get; } = new();

    /// <summary>
    /// What this client asks for when the server says nothing about a model —
    /// the shapes it offered before models could publish their own.
    /// </summary>
    private static readonly string[] DefaultAspectRatios =
        ["16:9", "1:1", "9:16", "4:3", "3:4", "3:2", "2:3"];

    /// <summary>
    /// What a server that publishes no backgrounds is read as. "auto" leads
    /// because it is the one every model takes.
    /// </summary>
    private static readonly string[] DefaultBackgrounds = ["auto", "opaque", "transparent"];

    /// <summary>
    /// Rebuilds the shapes around the chosen model, keeping the selection where
    /// the model still takes it and falling back to the one nearest the scene.
    /// </summary>
    private void ApplyModelCapabilities(AiModelOption? model)
    {
        AiImageModelCapabilities image =
            model?.Image ?? AiImageModelCapabilities.Unrestricted;

        IEnumerable<string> aspectRatios = image.AspectRatios.IsDefaultOrEmpty
            ? DefaultAspectRatios
            : image.AspectRatios;
        Replace(
            AspectRatioOptions,
            aspectRatios.Select(value => new AiImageAspectRatioOption(value)));
        SelectedAspectRatio.Value =
            AspectRatioOptions.FirstOrDefault(option => option == SelectedAspectRatio.Value)
            ?? GetSuggestedAspectRatio(AspectRatioOptions, _editViewModel?.Scene.FrameSize);

        IEnumerable<string> backgrounds = image.Backgrounds.IsDefaultOrEmpty
            ? DefaultBackgrounds
            : image.Backgrounds;
        Replace(
            BackgroundOptions,
            backgrounds.Select(value => new AiImageBackgroundOption(value)));
        // Falling back to the first, which is always "leave it to the model":
        // keeping a background the new model does not take would be refused
        // after the usage was reserved.
        SelectedBackground.Value =
            BackgroundOptions.FirstOrDefault(option => option == SelectedBackground.Value)
            ?? BackgroundOptions[0];
        HasBackgroundChoice.Value = BackgroundOptions.Count > 1;
        SupportsSeed.Value = image.SupportsSeed;
        if (!image.SupportsSeed)
            Seed.Value = null;
        // The model publishes its own count and the price covers a fixed one;
        // whichever is smaller is what may actually be sent, and anything the
        // new model will not take is dropped rather than refused after the usage
        // has been reserved.
        int maxReferences = Math.Clamp(
            image.MaxReferenceImages,
            0,
            AiRequestLimits.MaxImageReferences);
        SupportsReferenceImage.Value = maxReferences > 0;
        MaxReferenceImages.Value = maxReferences;
        while (ReferenceImages.Count > maxReferences)
        {
            AiReferenceImageViewModel dropped = ReferenceImages[^1];
            ReferenceImages.RemoveAt(ReferenceImages.Count - 1);
            dropped.Dispose();
        }

        UpdateReferenceImageState();
    }

    private void UpdateReferenceImageState()
    {
        HasReferenceImages.Value = ReferenceImages.Count > 0;
        CanAddReferenceImage.Value = ReferenceImages.Count < MaxReferenceImages.Value;
        ReferenceImageCountText.Value = string.Format(
            CultureInfo.CurrentCulture,
            Strings.AiReferenceImageCount,
            ReferenceImages.Count,
            MaxReferenceImages.Value);
    }

    private void ClearReferenceImagesCore()
    {
        foreach (AiReferenceImageViewModel reference in ReferenceImages)
            reference.Dispose();
        ReferenceImages.Clear();
        UpdateReferenceImageState();
    }

    private void RemoveReferenceImage(AiReferenceImageViewModel reference)
    {
        if (!ReferenceImages.Remove(reference))
            return;
        reference.Dispose();
        UpdateReferenceImageState();
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> values)
    {
        target.Clear();
        foreach (T value in values)
            target.Add(value);
    }

    internal static AiImageAspectRatioOption GetSuggestedAspectRatio(
        IReadOnlyList<AiImageAspectRatioOption> options,
        Beutl.Media.PixelSize? frameSize)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Count == 0)
        {
            throw new ArgumentException(
                "At least one aspect ratio option is required.",
                nameof(options));
        }

        string ratio = AiAspectRatioSuggestion.Nearest(
            options.Select(option => option.Value).ToArray(),
            frameSize,
            "16:9");
        return options.FirstOrDefault(option => option.Value == ratio) ?? options[0];
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
        ResultImage.Value?.Dispose();
        ResultImage.Dispose();
        PreviewImage.Value?.Dispose();
        PreviewImage.Dispose();
        foreach (AiReferenceImageViewModel reference in ReferenceImages)
            reference.Dispose();
        ReferenceImages.Clear();
        Prompt.Dispose();
        Style.Dispose();
        Composition.Dispose();
        Exclusions.Dispose();
        Error.Dispose();
        _disposables.Dispose();
    }

    /// <summary>
    /// Re-reads the model list. The catalog is cached with a freshness window,
    /// so this costs nothing while it is fresh and picks up a model an operator
    /// added, removed or reordered once it is not — which a workspace tab left
    /// open would otherwise never see.
    /// </summary>
    public void RefreshModels() => _ = RefreshModelsAsync();

    private async Task RefreshModelsAsync()
    {
        try
        {
            await ModelPicker.LoadAsync(AiOperations.ImageGeneration, CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reload the AI models for image generation.");
        }
    }

    // The model a request should carry: the one the outstanding name was built
    // from while there is one, and the picker's otherwise.
    private void RetireRequestName()
    {
        _requestKey.Retire();
        _outstandingModel = null;
    }

    private AiModelId? PinnedOrSelectedModel(AiModelId? selected)
        => _requestKey.HasOutstandingName.Value && _outstandingModel is { } pinned
            ? pinned
            : selected;

    private async Task LoadEntitlementsAsync()
    {
        using AsyncOperationLifetime.Operation? operation = _operations.TryEnter();
        if (operation is null)
            return;
        try
        {
            await _entitlements.RefreshAsync(operation.CancellationToken);
            // After the entitlements, so the picker knows which models this
            // account can pay for rather than offering them all.
            await ModelPicker.LoadAsync(
                AiOperations.ImageGeneration,
                operation.CancellationToken);
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load AI entitlements.");
        }
    }

    private async Task GenerateCore()
    {
        using AsyncOperationLifetime.Operation? operation = _operations.TryEnter();
        if (operation is null
            || !operation.TryPublish(() =>
            {
                Error.Value = null;
                IsGenerating.Value = true;
            }))
        {
            return;
        }

        _runningRequest = operation;
        try
        {
            string prompt = ComposePrompt();
            string aspectRatio = SelectedAspectRatio.Value.Value;
            AiModelId? model = PinnedOrSelectedModel(ModelPicker.SelectedModel);
            string[] referencePaths = ReferenceImages.Select(reference => reference.Path).ToArray();
            string background = SelectedBackground.Value.Value;
            // Every picture is part of what makes this request the request it
            // is, so each one is named in the key: the same prompt guided by
            // different pictures is a different run and costs its own.
            string?[] requestParts =
            [
                prompt,
                aspectRatio,
                background,
                Seed.Value?.ToString(CultureInfo.InvariantCulture),
                model?.Value,
                .. referencePaths.Select(AiRequestKey.FileStamp),
            ];
            AiRequestName name = _requestKey.NameFor(requestParts);
            _outstandingModel = model;
            // Not for a repeat: the server looks up the job this name already
            // made before it looks at the balance, so refusing here would refuse
            // to collect a picture already paid for — which is exactly what the
            // request that emptied the balance is.
            if (!name.IsRepeat
                && !await _availability.CheckAsync(
                    new AiOperationAvailabilityRequest.Fixed(AiOperations.ImageGeneration, model),
                    operation.CancellationToken))
            {
                throw new AiUsageLimitExceededException();
            }

            AiImageResult response = await _images.GenerateAsync(
                new AiImageGenerationRequest(
                    prompt,
                    new AiImageAspectRatioId(aspectRatio),
                    new AiImageBackgroundId(background),
                    seed: Seed.Value,
                    references: Array.ConvertAll(referencePaths, AiUploadSource.FromFile),
                    model: model,
                    idempotencyKey: name.Key,
                    referencesTotalLimitBytes: ModelPicker.MaxImageReferencesTotalBytes),
                new Progress<AiImagePreview>(preview => ShowPreview(preview, operation)),
                operation.CancellationToken);

            using var stream = new MemoryStream();
            await _content.CopyToAsync(response.ContentUri, stream, operation.CancellationToken);
            operation.CancellationToken.ThrowIfCancellationRequested();
            stream.Position = 0;
            var resultImage = Ref<Bitmap>.Create(Bitmap.FromStream(stream));
            RetireRequestName();
            if (!operation.TryPublish(() =>
                {
                    ResultImage.Value?.Dispose();
                    ResultImage.Value = resultImage;
                    PromptLibrary.Record(prompt);
                }))
            {
                resultImage.Dispose();
            }
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
        // Charged for and still the server's; asking again under the same name
        // is what recovers it, so the key stays.
        catch (AiResultUnavailableException)
        {
            operation.TryPublish(() => Error.Value = Strings.AiResultUnavailable);
        }
        catch (AiModelUnavailableException)
        {
            RetireRequestName();
            operation.TryPublish(() => Error.Value = Strings.AiModelUnavailable);
        }
        // Refused before the operation was reserved, so nothing was charged;
        // what has to change is the model or the shape of the request.
        catch (AiModelDoesNotSupportRequestException)
        {
            operation.TryPublish(() => Error.Value = Strings.AiModelDoesNotSupportRequest);
        }
        catch (AiProviderErrorException)
        {
            RetireRequestName();
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
        catch (AiRequestWasDeletedException)
        {
            RetireRequestName();
            operation.TryPublish(() => Error.Value = Strings.AiRequestWasDeleted);
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
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate AI image.");
            operation.TryPublish(() => Error.Value = Strings.AiUnexpectedError);
        }
        finally
        {
            _runningRequest = null;
            operation.TryPublish(() =>
            {
                IsGenerating.Value = false;
                PreviewImage.Value?.Dispose();
                PreviewImage.Value = null;
            });
        }
    }

    // Shown on the way, and only on the way: the run that is publishing it has
    // to still be the running one, or a preview from a cancelled run would
    // arrive after the next one started.
    private void ShowPreview(
        AiImagePreview preview,
        AsyncOperationLifetime.Operation operation)
    {
        Ref<Bitmap> image;
        try
        {
            using var stream = new MemoryStream(preview.Bytes.ToArray(), writable: false);
            image = Ref<Bitmap>.Create(Bitmap.FromStream(stream));
        }
        catch (Exception ex)
        {
            // A rough version that cannot be decoded is not worth a failure; the
            // finished picture is what the caller is waiting for.
            _logger.LogWarning(ex, "Failed to decode a partial AI image.");
            return;
        }

        if (!operation.TryPublish(() =>
            {
                PreviewImage.Value?.Dispose();
                PreviewImage.Value = image;
            }))
        {
            image.Dispose();
        }
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
                    Strings.AiImageGeneration),
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
                    NotificationService.ShowSuccess(Strings.AiImageGeneration, Strings.AiImageAddedToScene));
            }
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add the AI image to the scene.");
            operation.TryPublish(() => Error.Value = Strings.AiUnexpectedError);
        }
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
        options.SuggestedFileName = $"AI Image {DateTime.Now:yyyy-MM-dd HHmmss}";
        options.SuggestedStartLocation = await storage.TryGetWellKnownFolderAsync(WellKnownFolder.Pictures);
        options.DefaultExtension = "png";

        IStorageFile? file = await storage.SaveFilePickerAsync(options);
        if (file == null)
            return;

        try
        {
            using Stream stream = await file.OpenWriteAsync();
            operation.CancellationToken.ThrowIfCancellationRequested();
            stream.SetLength(0);
            bitmap.Save(stream, EncodedImageFormat.Png);
            operation.TryPublish(() =>
                NotificationService.ShowSuccess(Strings.AiImageGeneration, Strings.AiImageSaved));
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save the AI image.");
            operation.TryPublish(() => Error.Value = Strings.AiUnexpectedError);
        }
    }

    private async Task SelectReferenceImageAsync()
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

        FilePickerOpenOptions options = SharedFilePickerOptions.OpenAiInputImage();
        options.AllowMultiple = true;
        IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(options);
        if (files.Count == 0)
            return;

        operation.TryPublish(() => AddReferenceImages(files.Select(file => file.Path.LocalPath)));
    }

    /// <summary>
    /// Adds pictures in the order given, stopping at what the chosen model
    /// takes. One that cannot be read is reported and skipped rather than
    /// stopping the rest.
    /// </summary>
    internal void AddReferenceImages(IEnumerable<string> paths)
    {
        long total = ReferenceImages.Sum(reference => SizeOf(reference.Path));
        foreach (string path in paths)
        {
            if (ReferenceImages.Count >= MaxReferenceImages.Value)
                break;

            // The server holds every picture raw, again as base64 and again
            // through JSON, so what they come to together is bounded as well as
            // what each one may be. Said here rather than after the whole set
            // has been sent for the server to refuse.
            long size = SizeOf(path);
            if (total + size > ModelPicker.MaxImageReferencesTotalBytes)
            {
                Error.Value = Strings.AiFileTooLarge;
                break;
            }

            if (LoadReference(path) is { } reference)
            {
                ReferenceImages.Add(reference);
                total += size;
            }
        }

        UpdateReferenceImageState();
    }

    private static long SizeOf(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private AiReferenceImageViewModel? LoadReference(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;

        // The upload is refused server-side once it is too big, so the file is
        // measured here instead of after the account has waited for a round trip.
        if (new FileInfo(path).Length > AiRequestLimits.MaxImageUploadBytes)
        {
            Error.Value = Strings.AiFileTooLarge;
            return null;
        }

        try
        {
            return new AiReferenceImageViewModel(
                path,
                Ref<Bitmap>.Create(Bitmap.FromFile(path)),
                RemoveReferenceImage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to load an AI reference image preview from {Path}", path);
            Error.Value = Strings.AiEditSourcePreviewFailed;
            return null;
        }
    }

    private string ComposePrompt() => AiPromptComposer.Compose(new AiPromptParts(
        Prompt.Value,
        Style.Value,
        Composition.Value,
        Exclusions: Exclusions.Value));

    private static void EnsureImportSucceeded(ElementAddResult result)
    {
        if (result.IsSuccess)
            return;
        throw new InvalidOperationException(
            $"Failed to add the generated image: {result.Failure?.Id}.",
            result.Failure?.Exception);
    }

}

/// <summary>
/// One picture guiding a generation, shown with the preview the user picked it
/// by. Removing it is the item's own business so a list of them binds without
/// each row having to reach back through its parent.
/// </summary>
public sealed class AiReferenceImageViewModel : IDisposable
{
    private readonly Action<AiReferenceImageViewModel> _remove;
    private bool _disposed;

    internal AiReferenceImageViewModel(
        string path,
        Ref<Bitmap> preview,
        Action<AiReferenceImageViewModel> remove)
    {
        Path = path;
        Preview = preview;
        _remove = remove;
        Remove = new ReactiveCommand();
        Remove.Subscribe(() => _remove(this));
    }

    public string Path { get; }

    public Ref<Bitmap> Preview { get; }

    public string FileName => System.IO.Path.GetFileName(Path);

    public ReactiveCommand Remove { get; }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Remove.Dispose();
        Preview.Dispose();
    }
}

public sealed record AiImageAspectRatioOption(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// One background the chosen model publishes. The three this client knows are
/// named in the user's language; anything a later server adds is shown as it
/// came, which is still better than dropping a shape the model offers.
/// </summary>
public sealed record AiImageBackgroundOption(string Value)
{
    public override string ToString() => Value switch
    {
        "auto" => Strings.AiBackgroundAuto,
        "opaque" => Strings.AiBackgroundOpaque,
        "transparent" => Strings.AiBackgroundTransparent,
        _ => Value,
    };
}
