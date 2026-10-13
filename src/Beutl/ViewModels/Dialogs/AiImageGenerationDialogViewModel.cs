using System.Collections.ObjectModel;
using System.Globalization;
using System.Reactive.Disposables;
using Beutl.Api;
using Beutl.Api.Services;
using Beutl.Editor.Services;
using Beutl.Language;
using Beutl.Logging;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.Services;
using Beutl.Services.AI;
using Beutl.ViewModels;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

internal sealed partial class AiImageGenerationDialogViewModel : IDisposable, IAsyncDisposable, IAiModelListConsumer
{
    private readonly CompositeDisposable _disposables = [];
    private readonly AsyncOperationLifetime _operations = new();
    private readonly IdentityOperationLifetime _identityOperations = new();
    private readonly OnceAsyncDisposal _disposal = new();
    private readonly ILogger _logger = Log.CreateLogger<AiImageGenerationDialogViewModel>();
    private readonly IAiEntitlementService _entitlements;
    private readonly IAiOperationAvailabilityService _availability;
    private IdentityOperationLifetime.Operation? _runningRequest;
    private readonly IAiModelCatalogService _modelCatalog;
    private readonly IAiPlanCoordinator _aiPlanCoordinator;
    private readonly IAiImageGenerationService _images;
    private readonly IAuthenticatedContentService _content;
    private readonly AiRequestKey _requestKey;
    private readonly AiRequestRecoveryContext _requestRecoveryContext;
    // Preserve the user's choices separately from the displayed values, which are narrowed when
    // the model changes. Restoring the old model must also restore these values or the same intent
    // becomes a different request and no longer reaches the result named by the outstanding key.
    private AiImageAspectRatioOption? _chosenAspectRatio;
    private AiImageBackgroundOption? _chosenBackground;
    private int? _chosenSeed;
    private readonly List<string> _chosenReferencePaths = [];
    private readonly AiRecoveryState _recovery;
    // True while model constraints update the UI. Those changes are not user choices and must
    // not replace the preserved values.
    private bool _applyingCapabilities;
    private readonly EditViewModel? _editViewModel;

    internal AiImageGenerationDialogViewModel(
        IAiEntitlementService entitlements,
        IAiOperationAvailabilityService availability,
        IAiModelCatalogService modelCatalog,
        IAiPlanCoordinator aiPlanCoordinator,
        IAiImageGenerationService images,
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
            operation: "image.generate");
        Usage = new AiUsageViewModel(_entitlements.Entitlements).DisposeWith(_disposables);
        ModelPicker = new AiModelPickerViewModel(_modelCatalog, _entitlements)
            .DisposeWith(_disposables);
        _recovery = new AiRecoveryState(
            _requestKey,
            ModelPicker,
            AiOperations.ImageGeneration,
            Error,
            new AiRecoveryLogs(
                ex => _logger.LogError(ex, "Failed to read image-generation recovery attempts."),
                ex => _logger.LogError(ex, "Failed to clear image-generation state after an account change."),
                ex => _logger.LogWarning(ex, "Failed to abandon image-generation recovery attempt.")),
            TryRecoverPendingAttempt,
            () => ApplyModelCapabilities(ModelPicker.Selected.Value?.Model));
        EstimatedUsage = new AiUsageEstimateViewModel(
                Usage,
                _entitlements.Entitlements.Select(value =>
                    value?.Availability.GetState(AiOperations.ImageGeneration)
                    ?? AiOperationAvailabilityState.Unknown))
            .DisposeWith(_disposables);
        PromptLibrary = new AiPromptLibraryViewModel(
                PromptTaskKind.Image,
                ComposePrompt,
                prompt => Prompt.Value = prompt,
                recoveryContext: requestRecoveryContext)
            .DisposeWith(_disposables);

        AspectRatioOptions.Replace(
            DefaultAspectRatios.Select(value => new AiImageAspectRatioOption(value)));
        SelectedAspectRatio = new ReactivePropertySlim<AiImageAspectRatioOption>(
                AiAspectRatioSuggestion.Choose(AspectRatioOptions, option => option.Value, editViewModel?.Scene.FrameSize))
            .DisposeWith(_disposables);
        BackgroundOptions.Replace(
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
        SelectedAspectRatio.RememberChoice(() => _applyingCapabilities, option => _chosenAspectRatio = option)
            .DisposeWith(_disposables);
        SelectedBackground.RememberChoice(() => _applyingCapabilities, option => _chosenBackground = option)
            .DisposeWith(_disposables);
        Seed.RememberChoice(() => _applyingCapabilities, seed => _chosenSeed = seed)
            .DisposeWith(_disposables);
        ModelPicker.Selected.Subscribe(option => ApplyModelCapabilities(option?.Model))
            .DisposeWith(_disposables);
        // The shape and the background follow the chosen model, so replacing the
        // list under an outstanding name would rewrite the request waiting to be
        // collected.
        ModelPicker.KeepOffered = operation => _requestKey.PersistedModels(operation);
        ModelPicker.CanReload = _ => !_requestKey.HasOutstandingName.Value;

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
            .WhenAffordable(EstimatedUsage.CanAfford, _requestKey.HasOutstandingName)
            .WhenSomeModelUsable(ModelPicker.OffersNothingUsable, _requestKey.HasOutstandingName)
            .CombineLatest(
                ModelPicker.Selected,
                _recovery.Revision,
                // An unavailable choice cannot start new work. An outstanding request remains
                // collectable on its retained model because the server resolves its name before
                // checking current affordability.
                (can, selected, _) => can && CanUseSelectedModel(selected))
            .WhenModelsLoaded(ModelPicker.IsLoaded)
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

        _requestRecoveryContext.IdentityChanged += OnIdentityChanged;

        _ = LoadEntitlementsAsync();
        _recovery.TryAutoRecoverSingleAttempt();
    }

    /// <summary>The shapes the chosen model publishes; the client's defaults when it publishes none.</summary>
    public ObservableCollection<AiImageAspectRatioOption> AspectRatioOptions { get; } = [];

    public ReactivePropertySlim<AiImageAspectRatioOption> SelectedAspectRatio { get; }

    /// <summary>The backgrounds the chosen model publishes; the client's defaults when it publishes none.</summary>
    public ObservableCollection<AiImageBackgroundOption> BackgroundOptions { get; } = [];

    public ReactivePropertySlim<AiImageBackgroundOption> SelectedBackground { get; }

    // These three are false for a model that publishes no background of its own, takes no seed or
    // takes no picture to work from. The controls are hidden rather than refused after the usage is reserved.
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

    internal ReactivePropertySlim<AiPendingAttempt?> SelectedRecoveryAttempt => _recovery.SelectedAttempt;

    internal ReadOnlyReactivePropertySlim<IReadOnlyList<AiPendingAttempt>> RecoveryAttempts => _recovery.Attempts;

    internal ReadOnlyReactivePropertySlim<bool> RecoveryAvailable => _recovery.Available;

    internal ReactiveCommand RecoverSelectedAttempt => _recovery.RecoverSelected;

    internal ReactiveCommand AbandonSelectedAttempt => _recovery.AbandonSelected;

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

    // Unset in production: the default branches below use Avalonia storage and
    // AiResultImporter exactly as the editor does today.
    internal Func<CancellationToken, Task<IReadOnlyList<string>>>? ReferenceImagePicker { get; set; }

    internal Action<string>? BeforeReferenceImageSizeProbe { get; set; }

    internal Func<CancellationToken, Task<AiSaveFileDestination?>>? SaveFilePicker { get; set; }

    internal Func<Bitmap, AiResultImportOptions, CancellationToken, Task<ElementAddResult>>?
        ResultImporter
    { get; set; }

    public ReadOnlyReactivePropertySlim<bool> ShowJoinPro { get; }

    public ReactivePropertySlim<string?> Error { get; } = new();

    // Where the model sits in the request's parts.
    private const int ModelPartIndex = 4;

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

        _applyingCapabilities = true;
        try
        {
            IEnumerable<string> aspectRatios = !image.AspectRatios.IsSpecified
                ? DefaultAspectRatios
                : image.AspectRatios.Values;
            var availableAspectRatios = aspectRatios.ToList();
            if (_recovery.Active?.Form?.AspectRatio is { } recoveredAspect
                && !availableAspectRatios.Contains(recoveredAspect, StringComparer.Ordinal))
                availableAspectRatios.Add(recoveredAspect);
            AspectRatioOptions.Replace(
                availableAspectRatios.Select(value => new AiImageAspectRatioOption(value)));
            SelectedAspectRatio.Value =
                AspectRatioOptions.FirstOrDefault(option => option.Value == _chosenAspectRatio?.Value)
                ?? AiAspectRatioSuggestion.Choose(AspectRatioOptions, option => option.Value, _editViewModel?.Scene.FrameSize);

            IEnumerable<string> backgrounds = !image.Backgrounds.IsSpecified
                ? DefaultBackgrounds
                : image.Backgrounds.Values;
            var availableBackgrounds = backgrounds.ToList();
            if (_recovery.Active?.Form?.Background is { } recoveredBackground
                && !availableBackgrounds.Contains(recoveredBackground, StringComparer.Ordinal))
                availableBackgrounds.Add(recoveredBackground);
            BackgroundOptions.Replace(
                availableBackgrounds.Select(value => new AiImageBackgroundOption(value)));
            // Falling back to the first, which is always "leave it to the
            // model": keeping a background the new model does not take would be
            // refused after the usage was reserved.
            SelectedBackground.Value =
                BackgroundOptions.FirstOrDefault(option => option.Value == _chosenBackground?.Value)
                ?? BackgroundOptions[0];
            HasBackgroundChoice.Value = _recovery.Active?.Form?.HasBackgroundChoice
                ?? BackgroundOptions.Count > 1;
            SupportsSeed.Value = _recovery.Active?.Form?.SupportsSeed
                ?? image.SupportsSeed;
            Seed.Value = SupportsSeed.Value ? _chosenSeed : null;
            // The model publishes its own count and the price covers a fixed
            // one; whichever is smaller is what may actually be sent, and
            // anything the new model will not take is set aside rather than
            // refused after the usage has been reserved.
            int maxReferences = Math.Clamp(
                _recovery.Active?.Form?.MaxReferenceImages
                    ?? image.MaxReferenceImages,
                0,
                AiRequestLimits.MaxImageReferences);
            SupportsReferenceImage.Value = _recovery.Active?.Form?.SupportsReferenceImage
                ?? maxReferences > 0;
            if (!SupportsReferenceImage.Value)
                maxReferences = 0;
            MaxReferenceImages.Value = maxReferences;
            ShowChosenReferenceImages(maxReferences);
        }
        finally
        {
            _applyingCapabilities = false;
        }

        UpdateReferenceImageState();
    }

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
            PreviewImage.Value?.Dispose();
            PreviewImage.Dispose();
            DisposeReferenceImages();
            Prompt.Dispose();
            Style.Dispose();
            Composition.Dispose();
            Exclusions.Dispose();
            Error.Dispose();
            _requestRecoveryContext.IdentityChanged -= OnIdentityChanged;
            _requestKey.Dispose();
            _recovery.Dispose();
            _disposables.Dispose();
        });
    }

    /// <inheritdoc />
    public void RefreshModels() => _ = RefreshModelsAsync();

    private async Task RefreshModelsAsync()
    {
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
        if (operation is null)
            return;
        // A request waiting to be collected is named partly by the model it was
        // sent with, and the rest of what names it — the shape, the background —
        // follows whichever model the picker lands on. Moving the picker under an
        // outstanding name would rename the request and buy it again, so an
        // operator's change waits until the name is settled.
        if (_requestKey.HasOutstandingName.Value)
            return;

        try
        {
            await _entitlements.RefreshAsync(operation.CancellationToken);
            await ModelPicker.LoadAsync(
                AiOperations.ImageGeneration,
                operation.CancellationToken);
            _recovery.SelectRecoveredModel();
            TrimReferenceImagesToLimit();
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to reload the AI models for image generation.");
        }
    }

    private async Task LoadEntitlementsAsync()
    {
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
        if (operation is null)
            return;
        try
        {
            await _entitlements.RefreshAsync(operation.CancellationToken);
            // After the entitlements, so the picker knows which models this
            // account can pay for rather than offering them all — but never
            // under an outstanding name, whose request the picker names.
            if (!ModelPicker.IsLoaded.Value || !_requestKey.HasOutstandingName.Value)
            {
                AiOperationId op = AiOperations.ImageGeneration;
                await ModelPicker.LoadAsync(
                    op,
                    _requestKey.PreferredPersistedModel(op),
                    _requestKey.HasExplicitNullPersistedModel(op),
                    operation.CancellationToken);
                _recovery.SelectRecoveredModel();
                TrimReferenceImagesToLimit();
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

    private async Task GenerateCore()
    {
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
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
        AiRequestName issued = default;
        try
        {
            string prompt = ComposePrompt();
            string promptField = Prompt.Value;
            string style = Style.Value;
            string composition = Composition.Value;
            string exclusions = Exclusions.Value;
            string aspectRatio = SelectedAspectRatio.Value.Value;
            string[] referencePaths = ReferenceImages.Select(reference => reference.Path).ToArray();
            string background = SelectedBackground.Value.Value;
            int? seed = Seed.Value;
            // Every picture is part of what makes this request the request it
            // is, so each one is named in the key: the same prompt guided by
            // different pictures is a different run and costs its own.
            AiImageReferenceLimits referenceLimits = _recovery.Active?.Form?.MaxReferenceTotalBytes
                is { } persistedReferenceLimit
                ? new AiImageReferenceLimits(persistedReferenceLimit)
                : ModelPicker.ImageReferenceLimits;
            // Read once, and named by that reading. Reading again to send would
            // name one set of bytes and upload another if a picture changed in
            // between, and the answer would be recorded under a name that
            // describes something else.
            (AiUploadSource[] references, string[] referenceStamps, AiRequestRecoverySource[] recoverySources) =
                await ReadReferencesAsync(
                    referencePaths,
                    referenceLimits.MaxTotalBytes,
                    operation.CancellationToken,
                    _recovery.Active?.EffectiveSources);
            string?[] requestParts =
            [
                prompt,
                aspectRatio,
                background,
                seed?.ToString(CultureInfo.InvariantCulture),
                null,
                .. referenceStamps,
            ];
            AiModelId? model = _recovery.ModelForRequest(ModelPicker.SelectedModel);
            requestParts[ModelPartIndex] = model?.Value;
            AiRequestFormSnapshot form = new(
                Prompt: promptField,
                Style: style,
                Composition: composition,
                Exclusions: exclusions,
                AspectRatio: aspectRatio,
                Background: background,
                Seed: seed,
                MaxReferenceImages: MaxReferenceImages.Value,
                MaxReferenceTotalBytes: referenceLimits.MaxTotalBytes,
                SupportsReferenceImage: SupportsReferenceImage.Value,
                SupportsSeed: SupportsSeed.Value,
                HasBackgroundChoice: HasBackgroundChoice.Value);
            if (_recovery.Active is { } selected
                && !_requestKey.MatchesPending(selected, requestParts))
            {
                // The user changed a recovered form. Keep the old paid key
                // reachable and require an explicit abandon before allowing a
                // new charge; silently issuing another key would lose the path
                // back to the original job.
                operation.TryPublish(() => Error.Value = Strings.AiRequestChanged);
                return;
            }
            AiRequestName name = _requestKey.NameFor(requestParts, form, recoverySources);
            issued = name;
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
                    new AiOperationAvailabilityRequest.Fixed(AiOperations.ImageGeneration, model),
                    token),
                token => _images.GenerateAsync(
                    new AiImageGenerationRequest(
                        prompt,
                        new AiImageAspectRatioId(aspectRatio),
                        new AiImageBackgroundId(background),
                        seed: seed,
                        references: references,
                        model: model,
                        idempotencyKey: name.Key,
                        referenceLimits: referenceLimits),
                    new Progress<AiImagePreview>(preview => ShowPreview(preview, operation)),
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
                    PromptLibrary.Record(prompt);
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
            _logger.LogError(ex, "Failed to generate AI image.");
            operation.TryPublish(() => Error.Value = Strings.AiUnexpectedError);
        }
        finally
        {
            if (ReferenceEquals(_runningRequest, operation))
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
        IdentityOperationLifetime.Operation operation)
    {
        Ref<Bitmap> image;
        try
        {
            using var stream = new MemoryStream(preview.Bytes.ToArray(), writable: false);
            AiImageDecodeValidator.ValidateEncoded(stream, AiRequestLimits.MaxImageUploadBytes);
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
            Strings.AiImageGeneration,
            "generated image",
            Error,
            ex => _logger.LogError(ex, "Failed to add the AI image to the scene."));
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
            "AI Image",
            "Failed to encode the AI image as PNG.",
            Strings.AiImageGeneration,
            Error,
            ex => _logger.LogError(ex, "Failed to save the AI image."));
    }

    private string ComposePrompt() => AiPromptComposer.Compose(new AiPromptParts(
        Prompt.Value,
        Style.Value,
        Composition.Value,
        Exclusions: Exclusions.Value));

}

internal sealed record AiImageAspectRatioOption(string Value)
{
    public override string ToString() => Value;
}

/// <summary>
/// One background the chosen model publishes. The three this client knows are
/// named in the user's language; anything a later server adds is shown as it
/// came, which is still better than dropping a shape the model offers.
/// </summary>
internal sealed record AiImageBackgroundOption(string Value)
{
    public override string ToString() => Value switch
    {
        "auto" => Strings.AiBackgroundAuto,
        "opaque" => Strings.AiBackgroundOpaque,
        "transparent" => Strings.AiBackgroundTransparent,
        _ => Value,
    };
}
