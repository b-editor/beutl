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

internal sealed class AiImageGenerationDialogViewModel : IDisposable, IAsyncDisposable, IAiModelListConsumer
{
    private readonly CompositeDisposable _disposables = [];
    private readonly AsyncOperationLifetime _operations = new();
    private readonly IdentityOperationLifetime _identityOperations = new();
    private readonly object _disposeGate = new();
    private readonly ILogger _logger = Log.CreateLogger<AiImageGenerationDialogViewModel>();
    private readonly IAiEntitlementService _entitlements;
    private readonly IAiOperationAvailabilityService _availability;
    private IdentityOperationLifetime.Operation? _runningRequest;
    private readonly IAiModelCatalogService _modelCatalog;
    private readonly IAiPlanCoordinator _aiPlanCoordinator;
    private readonly IAiImageGenerationService _images;
    private readonly IAuthenticatedContentService _content;
    private readonly AiRequestKey _requestKey;
    private readonly AiRequestRecoveryContext? _requestRecoveryContext;
    // 利用者が選んだもの。画面に出ているものとは別に持つ——モデルを選び直すと
    // 画面のほうは、そのモデルが取れる範囲へ寄せられてしまう。元のモデルに
    // 戻したときにここから戻せないと、同じつもりの依頼が別の依頼になり、
    // 出してある名前が指すものへ届かなくなる。
    private AiImageAspectRatioOption? _chosenAspectRatio;
    private AiImageBackgroundOption? _chosenBackground;
    private int? _chosenSeed;
    private readonly List<string> _chosenReferencePaths = [];
    private AiPendingAttempt? _selectedRecovery;
    private readonly ReactivePropertySlim<int> _recoveryRevision = new();
    // モデルの都合で画面を書き換えている最中。そのあいだの変化は利用者の選択では
    // ないので、覚えない。
    private bool _applyingCapabilities;
    private readonly EditViewModel? _editViewModel;
    private Task? _disposeTask;

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
        SelectedAspectRatio.Subscribe(option =>
            {
                if (!_applyingCapabilities)
                    _chosenAspectRatio = option;
            })
            .DisposeWith(_disposables);
        SelectedBackground.Subscribe(option =>
            {
                if (!_applyingCapabilities)
                    _chosenBackground = option;
            })
            .DisposeWith(_disposables);
        Seed.Subscribe(seed =>
            {
                if (!_applyingCapabilities)
                    _chosenSeed = seed;
            })
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
            // Until the list has been asked for, a request would name no model
            // and run on the server's default, which may cost more than what
            // this screen was about to offer.
            .CombineLatest(ModelPicker.IsLoaded, (can, loaded) => can && loaded)
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

        RecoverSelectedAttempt = new ReactiveCommand();
        RecoverSelectedAttempt.Subscribe(() =>
        {
            if (SelectedRecoveryAttempt!.Value is { } attempt)
                TryRecoverPendingAttempt(attempt);
        }).DisposeWith(_disposables);
        AbandonSelectedAttempt = new ReactiveCommand();
        AbandonSelectedAttempt.Subscribe(() =>
        {
            if (SelectedRecoveryAttempt!.Value is { } attempt)
                AbandonPendingAttempt(attempt);
        }).DisposeWith(_disposables);

        OpenAiPlan = new ReactiveCommand();
        OpenAiPlan.Subscribe(aiPlanCoordinator.OpenAiPlan).DisposeWith(_disposables);

        ShowJoinPro = Usage.HasSnapshot
            .CombineLatest(Usage.CanUseAi, (hasSnapshot, canUseAi) => hasSnapshot && !canUseAi)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        SelectedRecoveryAttempt = new ReactivePropertySlim<AiPendingAttempt?>()
            .DisposeWith(_disposables);
        RecoveryAttempts = _recoveryRevision
            .Select(_ => (IReadOnlyList<AiPendingAttempt>)GetPendingRecoveryAttempts())
            .ToReadOnlyReactivePropertySlim(Array.Empty<AiPendingAttempt>())
            .DisposeWith(_disposables);
        RecoveryAvailable = RecoveryAttempts
            .Select(attempts => attempts.Count != 0)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);
        if (_requestRecoveryContext is not null)
            _requestRecoveryContext.IdentityChanged += OnIdentityChanged;

        _ = LoadEntitlementsAsync();
        TryAutoRecoverSingleAttempt();
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

    /// <summary>Pending form attempts that can be explicitly recovered or abandoned.</summary>
    internal IReadOnlyList<AiPendingAttempt> PendingRecoveryAttempts
        => GetPendingRecoveryAttempts();

    internal ReactivePropertySlim<AiPendingAttempt?> SelectedRecoveryAttempt { get; }

    internal ReadOnlyReactivePropertySlim<IReadOnlyList<AiPendingAttempt>> RecoveryAttempts { get; }

    internal ReadOnlyReactivePropertySlim<bool> RecoveryAvailable { get; }

    internal ReactiveCommand RecoverSelectedAttempt { get; }

    internal ReactiveCommand AbandonSelectedAttempt { get; }

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

    internal Func<CancellationToken, Task<AiSaveFileDestination?>>? SaveFilePicker { get; set; }

    internal Func<Bitmap, AiResultImportOptions, CancellationToken, Task<ElementAddResult>>?
        ResultImporter
    { get; set; }

    public ReadOnlyReactivePropertySlim<bool> ShowJoinPro { get; }

    public ReactivePropertySlim<string?> Error { get; } = new();

    /// <summary>
    /// What this client asks for when the server says nothing about a model —
    /// the shapes it offered before models could publish their own.
    /// </summary>
    // Where the model sits in the request's parts. It is filled in last: which
    // model a request carries depends on whether a name is already outstanding
    // for the rest of it.
    private const int ModelPartIndex = 4;

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

        // What the user asked for is remembered apart from what the model on
        // screen will take. Reading the choice back off the screen loses it the
        // moment a model that takes something narrower is picked, and going
        // back to the first model then rebuilds a different request — one the
        // name already handed out does not belong to.
        _applyingCapabilities = true;
        try
        {
            IEnumerable<string> aspectRatios = !image.AspectRatios.IsSpecified
                ? DefaultAspectRatios
                : image.AspectRatios.Values;
            var availableAspectRatios = aspectRatios.ToList();
            if (_selectedRecovery?.Form?.AspectRatio is { } recoveredAspect
                && !availableAspectRatios.Contains(recoveredAspect, StringComparer.Ordinal))
                availableAspectRatios.Add(recoveredAspect);
            Replace(
                AspectRatioOptions,
                availableAspectRatios.Select(value => new AiImageAspectRatioOption(value)));
            SelectedAspectRatio.Value =
                AspectRatioOptions.FirstOrDefault(option => option.Value == _chosenAspectRatio?.Value)
                ?? GetSuggestedAspectRatio(AspectRatioOptions, _editViewModel?.Scene.FrameSize);

            IEnumerable<string> backgrounds = !image.Backgrounds.IsSpecified
                ? DefaultBackgrounds
                : image.Backgrounds.Values;
            var availableBackgrounds = backgrounds.ToList();
            if (_selectedRecovery?.Form?.Background is { } recoveredBackground
                && !availableBackgrounds.Contains(recoveredBackground, StringComparer.Ordinal))
                availableBackgrounds.Add(recoveredBackground);
            Replace(
                BackgroundOptions,
                availableBackgrounds.Select(value => new AiImageBackgroundOption(value)));
            // Falling back to the first, which is always "leave it to the
            // model": keeping a background the new model does not take would be
            // refused after the usage was reserved.
            SelectedBackground.Value =
                BackgroundOptions.FirstOrDefault(option => option.Value == _chosenBackground?.Value)
                ?? BackgroundOptions[0];
            HasBackgroundChoice.Value = _selectedRecovery?.Form?.HasBackgroundChoice
                ?? BackgroundOptions.Count > 1;
            SupportsSeed.Value = _selectedRecovery?.Form?.SupportsSeed
                ?? image.SupportsSeed;
            Seed.Value = SupportsSeed.Value ? _chosenSeed : null;
            // The model publishes its own count and the price covers a fixed
            // one; whichever is smaller is what may actually be sent, and
            // anything the new model will not take is set aside rather than
            // refused after the usage has been reserved.
            int maxReferences = Math.Clamp(
                _selectedRecovery?.Form?.MaxReferenceImages
                    ?? image.MaxReferenceImages,
                0,
                AiRequestLimits.MaxImageReferences);
            SupportsReferenceImage.Value = _selectedRecovery?.Form?.SupportsReferenceImage
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

    // The pictures the user picked, as many of them as this model takes. A
    // model that takes fewer sets the rest aside rather than throwing them
    // away, so going back to one that takes them all asks for the same request
    // again rather than a smaller one.
    private void ShowChosenReferenceImages(int maxReferences)
    {
        string[] wanted = WithinTotalLimit(_chosenReferencePaths.Take(maxReferences));
        if (ReferenceImages.Select(reference => reference.Path).SequenceEqual(
                wanted,
                StringComparer.Ordinal))
        {
            return;
        }

        foreach (AiReferenceImageViewModel shown in ReferenceImages)
            shown.Dispose();
        ReferenceImages.Clear();
        foreach (string path in wanted)
        {
            if (LoadReference(path) is { } reference)
                ReferenceImages.Add(reference);
        }
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
        _chosenReferencePaths.Clear();
        UpdateReferenceImageState();
    }

    private void RemoveReferenceImage(AiReferenceImageViewModel reference)
    {
        if (!ReferenceImages.Remove(reference))
            return;
        _chosenReferencePaths.Remove(reference.Path);
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

    private IdentityOperationLifetime.Operation? TryEnterIdentityOperation()
        => _identityOperations.TryEnter(_operations);

    private Task BeginDisposeAsync()
    {
        lock (_disposeGate)
        {
            if (_disposeTask is not null)
                return _disposeTask;

            var completion = new TaskCompletionSource(
                TaskCreationOptions.RunContinuationsAsynchronously);
            _disposeTask = completion.Task;
            _ = CompleteDisposeAsync(completion);
            return completion.Task;
        }
    }

    private async Task CompleteDisposeAsync(TaskCompletionSource completion)
    {
        try
        {
            await DisposeCoreAsync();
            completion.TrySetResult();
        }
        catch (Exception ex)
        {
            completion.TrySetException(ex);
        }
    }

    private async Task DisposeCoreAsync()
    {
        await _operations.DisposeAsync(async () =>
        {
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
            if (_requestRecoveryContext is not null)
                _requestRecoveryContext.IdentityChanged -= OnIdentityChanged;
            _requestKey.Dispose();
            _recoveryRevision.Dispose();
            _disposables.Dispose();
        });
        _identityOperations.Dispose();
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
            await ModelPicker.LoadAsync(
                AiOperations.ImageGeneration,
                operation.CancellationToken);
            SelectRecoveredModel();
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

    private IReadOnlyList<AiPendingAttempt> GetPendingRecoveryAttempts()
    {
        try
        {
            return _requestKey.PendingAttempts(AiOperations.ImageGeneration);
        }
        catch (InvalidDataException ex)
        {
            _logger.LogError(ex, "Failed to read image-generation recovery attempts.");
            return Array.Empty<AiPendingAttempt>();
        }
    }

    private void TryAutoRecoverSingleAttempt()
    {
        IReadOnlyList<AiPendingAttempt> attempts = GetPendingRecoveryAttempts();
        if (attempts.Count == 1 && attempts[0].HasCanonicalForm)
        {
            if (!TryRecoverPendingAttempt(attempts[0]))
                ClearActiveRecovery();
        }

        _recoveryRevision.Value++;
    }

    private void OnIdentityChanged()
    {
        string? account = _requestKey.CurrentAccountId;
        _identityOperations.Switch(() =>
        {
            _runningRequest = null;
            IsGenerating.Value = false;
            ClearActiveRecovery();
            _chosenAspectRatio = null;
            _chosenBackground = null;
            _chosenSeed = null;
            ClearReferenceImagesCore();
            Prompt.Value = string.Empty;
            Style.Value = string.Empty;
            Composition.Value = string.Empty;
            Exclusions.Value = string.Empty;
            ResultImage.Value?.Dispose();
            ResultImage.Value = null;
            PreviewImage.Value?.Dispose();
            PreviewImage.Value = null;
            Error.Value = null;
            ModelPicker.ReconcileRecoveryModels();
            ApplyModelCapabilities(ModelPicker.Selected.Value?.Model);
            UpdateReferenceImageState();
            _recoveryRevision.Value++;
        });
        if (account is not null)
            TryAutoRecoverSingleAttempt();
    }

    internal bool TryRecoverPendingAttempt(AiPendingAttempt attempt)
    {
        if (_requestKey.CurrentAccountId is not { } account
            || !StringComparer.Ordinal.Equals(account, attempt.AccountId))
        {
            Error.Value = Strings.AiAuthenticationRequired;
            return false;
        }
        if (!attempt.HasCanonicalForm
            || !string.Equals(attempt.Operation, AiOperations.ImageGeneration.Value, StringComparison.Ordinal))
        {
            Error.Value = Strings.AiResultUnavailable;
            return false;
        }

        IReadOnlyList<string> paths;
        try
        {
            paths = _requestKey.ResolveSources(attempt);
        }
        catch (InvalidDataException ex)
        {
            // Keep the row and key. The caller must explicitly abandon it if
            // the original source can no longer be verified.
            _logger.LogWarning(ex, "Image-generation recovery source is unavailable.");
            Error.Value = Strings.AiResultUnavailable;
            return false;
        }

        AiRequestFormSnapshot form = attempt.Form!;
        string[] referencePaths = paths.ToArray();
        if (referencePaths.Length != attempt.EffectiveSources.Count)
        {
            Error.Value = Strings.AiResultUnavailable;
            return false;
        }

        _applyingCapabilities = true;
        try
        {
            Prompt.Value = form.Prompt ?? string.Empty;
            Style.Value = form.Style ?? string.Empty;
            Composition.Value = form.Composition ?? string.Empty;
            Exclusions.Value = form.Exclusions ?? string.Empty;
            if (form.AspectRatio is { } aspect)
            {
                _chosenAspectRatio = new AiImageAspectRatioOption(aspect);
                if (AspectRatioOptions.FirstOrDefault(option => option.Value == aspect) is { } aspectOption)
                    SelectedAspectRatio.Value = aspectOption;
            }
            if (form.Background is { } background)
            {
                _chosenBackground = new AiImageBackgroundOption(background);
                if (BackgroundOptions.FirstOrDefault(option => option.Value == background) is { } backgroundOption)
                    SelectedBackground.Value = backgroundOption;
            }
            Seed.Value = form.Seed;
            _chosenSeed = form.Seed;
        }
        finally
        {
            _applyingCapabilities = false;
        }

        foreach (AiReferenceImageViewModel reference in ReferenceImages)
            reference.Dispose();
        ReferenceImages.Clear();
        _chosenReferencePaths.Clear();
        foreach (string path in referencePaths)
        {
            if (LoadReference(path) is not { } reference)
            {
                Error.Value = Strings.AiResultUnavailable;
                return false;
            }

            ReferenceImages.Add(reference);
            _chosenReferencePaths.Add(path);
        }

        // Re-read the durable source bytes to ensure the path verification and
        // the preview cannot drift before the request is sent.
        foreach (AiRequestRecoverySource source in attempt.EffectiveSources)
        {
            try
            {
                _ = _requestKey.ReadSourceBytes(source);
            }
            catch (InvalidDataException ex)
            {
                _logger.LogWarning(ex, "Image-generation recovery source changed.");
                Error.Value = Strings.AiResultUnavailable;
                return false;
            }
        }

        ActivateRecovery(attempt);
        SelectRecoveredModel();
        ApplyModelCapabilities(ModelPicker.Selected.Value?.Model);
        UpdateReferenceImageState();
        _recoveryRevision.Value++;
        return true;
    }

    internal void AbandonPendingAttempt(AiPendingAttempt attempt)
    {
        try
        {
            _requestKey.Abandon(attempt);
            if (_selectedRecovery is { } selected
                && selected.AccountId == attempt.AccountId
                && selected.Operation == attempt.Operation
                && selected.Fingerprint == attempt.Fingerprint)
            {
                ClearActiveRecovery();
                ModelPicker.ReconcileRecoveryModels();
                ApplyModelCapabilities(ModelPicker.Selected.Value?.Model);
                UpdateReferenceImageState();
            }

            _recoveryRevision.Value++;
        }
        catch (Exception ex) when (ex is InvalidDataException or AuthenticationRequiredException)
        {
            _logger.LogWarning(ex, "Failed to abandon image-generation recovery attempt.");
            Error.Value = Strings.AiResultUnavailable;
        }
    }

    private AiModelId? ModelForRequest(AiModelId? selected)
        => _selectedRecovery is { } attempt
            ? attempt.Model is { } model ? new AiModelId(model) : null
            : selected;

    private void ActivateRecovery(AiPendingAttempt attempt)
    {
        _selectedRecovery = attempt;
        SelectedRecoveryAttempt.Value = attempt;
        ModelPicker.IsSelectionEnabled.Value = false;
    }

    private void ClearActiveRecovery()
    {
        _selectedRecovery = null;
        SelectedRecoveryAttempt.Value = null;
        ModelPicker.IsSelectionEnabled.Value = true;
    }

    private void SelectRecoveredModel()
    {
        if (_selectedRecovery is not { } recovery)
            return;
        if (recovery.Model is not { } model)
        {
            ModelPicker.Selected.Value = null;
            return;
        }
        AiModelId id = new(model);
        ModelPicker.Selected.Value = ModelPicker.Options.FirstOrDefault(option => option.Id == id);
    }

    // The model a request should carry: the one the outstanding name was built
    // from while there is one, and the picker's otherwise.
    //
    // Only this one request is settled. Retiring the whole run instead would
    // throw away the name of anything else still waiting to be collected.
    private void RetireRequestName(AiRequestName name)
    {
        _requestKey.Retire(name);
        if (_selectedRecovery is { } selected
            && string.Equals(selected.Key, name.Key, StringComparison.Ordinal))
        {
            ClearActiveRecovery();
            ModelPicker.ReconcileRecoveryModels();
            ApplyModelCapabilities(ModelPicker.Selected.Value?.Model);
            UpdateReferenceImageState();
            _recoveryRevision.Value++;
        }
        // Reloads were held back while that name was outstanding, so this is
        // where an operator's change to the model list finally lands.
        _ = RefreshModelsAsync();
    }

    // What the pictures of one request may come to together is published by the
    // server and can be lowered. Anything over the new total is dropped here
    // rather than refused once the request has been built — which is only ever
    // reached while no name is outstanding, so this cannot rewrite a request
    // waiting to be collected.
    private void TrimReferenceImagesToLimit()
    {
        // 脇に置いてあるぶんも含めて切る。表示されているものだけを数えると、
        // 上限が下がったあとに広いモデルへ戻したとき、上限を超えた組が戻って
        // くる。
        string[] within = WithinTotalLimit(_chosenReferencePaths);
        if (within.Length == _chosenReferencePaths.Count)
            return;

        _chosenReferencePaths.Clear();
        _chosenReferencePaths.AddRange(within);
        ShowChosenReferenceImages(MaxReferenceImages.Value);
        UpdateReferenceImageState();
    }

    // What the pictures may come to together is published by the server. The
    // ones that fit, in the order they were picked.
    private string[] WithinTotalLimit(IEnumerable<string> paths)
    {
        long limit = _selectedRecovery?.Form?.MaxReferenceTotalBytes
            ?? ModelPicker.ImageReferenceLimits.MaxTotalBytes;
        long total = 0;
        var within = new List<string>();
        foreach (string path in paths)
        {
            long size = SizeOf(path);
            if (total + size > limit)
                break;
            within.Add(path);
            total += size;
        }

        return within.ToArray();
    }

    // A name the server never made a job under. Withdrawing it lets the picker
    // move again and puts the balance check back in front of the next attempt.
    private void WithdrawRequestName(AiRequestName name)
    {
        _requestKey.WithdrawAfterNoReservation(name);
        if (_selectedRecovery is { } selected
            && string.Equals(selected.Key, name.Key, StringComparison.Ordinal))
        {
            ClearActiveRecovery();
            ModelPicker.ReconcileRecoveryModels();
            ApplyModelCapabilities(ModelPicker.Selected.Value?.Model);
            UpdateReferenceImageState();
            _recoveryRevision.Value++;
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
                SelectRecoveredModel();
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
            string aspectRatio = SelectedAspectRatio.Value.Value;
            string[] referencePaths = ReferenceImages.Select(reference => reference.Path).ToArray();
            string background = SelectedBackground.Value.Value;
            // Every picture is part of what makes this request the request it
            // is, so each one is named in the key: the same prompt guided by
            // different pictures is a different run and costs its own. The
            // model's place is left empty until it is known, because which
            // model this request carries depends on whether it is the request a
            // name is already outstanding for.
            AiImageReferenceLimits referenceLimits = _selectedRecovery?.Form?.MaxReferenceTotalBytes
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
                    _selectedRecovery?.EffectiveSources);
            string?[] requestParts =
            [
                prompt,
                aspectRatio,
                background,
                Seed.Value?.ToString(CultureInfo.InvariantCulture),
                null,
                .. referenceStamps,
            ];
            AiModelId? model = ModelForRequest(ModelPicker.SelectedModel);
            requestParts[ModelPartIndex] = model?.Value;
            AiRequestFormSnapshot form = new(
                Prompt: Prompt.Value,
                Style: Style.Value,
                Composition: Composition.Value,
                Exclusions: Exclusions.Value,
                AspectRatio: aspectRatio,
                Background: background,
                Seed: Seed.Value,
                MaxReferenceImages: MaxReferenceImages.Value,
                MaxReferenceTotalBytes: referenceLimits.MaxTotalBytes,
                SupportsReferenceImage: SupportsReferenceImage.Value,
                SupportsSeed: SupportsSeed.Value,
                HasBackgroundChoice: HasBackgroundChoice.Value);
            if (_selectedRecovery is { } selected
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
            if (_requestKey.HasDurableRecovery && claim is null)
            {
                operation.TryPublish(() => Error.Value = Strings.AiResultUnavailable);
                return;
            }

            // Before it goes out. A name that ends here reached nothing.
            try
            {
                // Not for a repeat: the server looks up the job this name
                // already made before it looks at the balance, so refusing here
                // would refuse to collect a picture already paid for — which is
                // exactly what the request that emptied the balance is.
                if (!name.IsRepeat
                    && !await _availability.CheckAsync(
                        new AiOperationAvailabilityRequest.Fixed(
                            AiOperations.ImageGeneration,
                            model),
                        operation.CancellationToken))
                {
                    throw new AiUsageLimitExceededException();
                }
            }
            catch (Exception ex) when (AiRequestOutcome.ReservedNothing(ex))
            {
                WithdrawRequestName(name);
                throw;
            }

            AiImageResult response;
            try
            {
                _requestKey.MarkClaimDispatched(claim);
                response = await _images.GenerateAsync(
                    new AiImageGenerationRequest(
                        prompt,
                        new AiImageAspectRatioId(aspectRatio),
                        new AiImageBackgroundId(background),
                        seed: Seed.Value,
                        references: references,
                        model: model,
                        idempotencyKey: name.Key,
                        referenceLimits: referenceLimits),
                    new Progress<AiImagePreview>(preview => ShowPreview(preview, operation)),
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
                    PromptLibrary.Record(prompt);
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

        try
        {
            TimeSpan start = _editViewModel.Player.CurrentFrame.Value;
            int layer = _editViewModel.Scene.Children
                .Where(item => item.Start <= start && start < item.Range.End)
                .Select(item => item.ZIndex)
                .DefaultIfEmpty(-1)
                .Max() + 1;
            AiResultImportOptions options = new(
                start,
                TimeSpan.FromSeconds(5),
                layer,
                Strings.AiImageGeneration);
            ElementAddResult result;
            if (ResultImporter is { } importer)
            {
                result = await importer(bitmap, options, operation.CancellationToken);
            }
            else
            {
                var defaultImporter = new AiResultImporter(
                    _editViewModel.Scene,
                    _editViewModel.GetRequiredService<IElementAdder>());
                result = await defaultImporter.ImportImageAsync(
                    bitmap,
                    options,
                    operation.CancellationToken);
            }

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
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
        if (operation is null)
            return;
        using Ref<Bitmap>? resultImage = AiResultImageLease.Acquire(ResultImage.Value);
        if (resultImage?.Value is not { } bitmap)
            return;

        AiSaveFileDestination? destination;
        if (SaveFilePicker is { } picker)
        {
            destination = await picker(operation.CancellationToken);
        }
        else
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime
                { MainWindow: { } window }
                || TopLevel.GetTopLevel(window)?.StorageProvider is not { } storage)
                return;
            FilePickerSaveOptions options = SharedFilePickerOptions.SaveImage();
            options.SuggestedFileName = $"AI Image {DateTime.Now:yyyy-MM-dd HHmmss}";
            options.SuggestedStartLocation = await storage.TryGetWellKnownFolderAsync(WellKnownFolder.Pictures);
            options.DefaultExtension = "png";
            IStorageFile? file = await storage.SaveFilePickerAsync(options);
            destination = file is null
                ? null
                : new AiSaveFileDestination(
                    file.Path.LocalPath,
                    _ => file.OpenWriteAsync());
        }

        if (destination is null || !operation.IsCurrent)
            return;

        try
        {
            using Stream stream = await destination.OpenWriteAsync(operation.CancellationToken);
            if (!operation.TryPublish(() =>
                {
                    operation.CancellationToken.ThrowIfCancellationRequested();
                    stream.SetLength(0);
                    bitmap.Save(stream, EncodedImageFormat.Png);
                }))
                return;
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
        using IdentityOperationLifetime.Operation? operation = TryEnterIdentityOperation();
        if (operation is null)
            return;
        IReadOnlyList<string> paths;
        if (ReferenceImagePicker is { } picker)
        {
            paths = await picker(operation.CancellationToken);
        }
        else
        {
            if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime
                { MainWindow: { } window }
                || TopLevel.GetTopLevel(window)?.StorageProvider is not { } storage)
                return;
            FilePickerOpenOptions options = SharedFilePickerOptions.OpenAiInputImage();
            options.AllowMultiple = true;
            IReadOnlyList<IStorageFile> files = await storage.OpenFilePickerAsync(options);
            paths = files.Select(file => file.Path.LocalPath).ToArray();
        }
        if (paths.Count == 0)
            return;

        operation.TryPublish(() => AddReferenceImages(paths));
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
            if (total + size > ModelPicker.ImageReferenceLimits.MaxTotalBytes)
            {
                Error.Value = Strings.AiFileTooLarge;
                break;
            }

            if (LoadReference(path) is { } reference)
            {
                ReferenceImages.Add(reference);
                _chosenReferencePaths.Add(path);
                total += size;
            }
        }

        UpdateReferenceImageState();
    }

    // 送るものと、名前に使うものを、同じ一度の読み取りから作る。読み直すと、
    // 名前を付けた中身と実際に送る中身が食い違い、答えは別のものを指す名前で
    // 記録される。
    private async Task<(
        AiUploadSource[] References,
        string[] Stamps,
        AiRequestRecoverySource[] Sources)>
        ReadReferencesAsync(
            string[] paths,
            long totalLimit,
            CancellationToken cancellationToken,
            IReadOnlyList<AiRequestRecoverySource>? recoveredSources = null)
    {
        var sources = new AiUploadSource[paths.Length];
        var stamps = new string[paths.Length];
        var recoverySources = new AiRequestRecoverySource[paths.Length];
        // どの一枚も上限があるが、まとめて送れる量にも上限がある。一枚ずつしか
        // 見ないと、全部読み終えて写しまで作ってから断ることになる——残りの分
        // だけを読めば、断るときにはもう抱えていない。
        long remaining = totalLimit;
        for (int index = 0; index < paths.Length; index++)
        {
            AiRequestRecoverySource? recovered = recoveredSources is { Count: > 0 }
                ? recoveredSources.FirstOrDefault(source => source.Role == $"reference-{index.ToString(CultureInfo.InvariantCulture)}")
                : null;
            if (recovered is not null
                && (recovered.DurableFile is null
                    ? !string.Equals(
                        Path.GetFullPath(recovered.Path ?? string.Empty),
                        Path.GetFullPath(paths[index]),
                        StringComparison.Ordinal)
                    : !string.Equals(
                        Path.GetFileName(paths[index]),
                        recovered.DurableFile,
                        StringComparison.Ordinal)))
            {
                // A user-selected locator may be replaced after recovery. Read
                // the current file and let the fingerprint check decide whether
                // it is still the same request.
                recovered = null;
            }
            string fileName = recovered?.Name ?? Path.GetFileName(paths[index]);
            byte[] bytes = recovered is not null
                ? await ReadRecoveredSourceAsync(recovered, cancellationToken)
                : await AiUploadBytes.ReadWithinAsync(
                    paths[index],
                    Math.Min(remaining, AiRequestLimits.MaxImageUploadBytes),
                    cancellationToken);
            remaining -= bytes.LongLength;
            stamps[index] = AiRequestKey.FileStamp(fileName, bytes);
            sources[index] = AiUploadSource.FromBytes(fileName, bytes);
            recoverySources[index] = recovered ?? FileAiRequestRecoveryStore.CreateExternalSource(
                $"reference-{index.ToString(CultureInfo.InvariantCulture)}",
                paths[index],
                fileName,
                bytes);
        }

        return (sources, stamps, recoverySources);
    }

    private async Task<byte[]> ReadRecoveredSourceAsync(
        AiRequestRecoverySource source,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (source.Path is null && source.DurableFile is null)
            throw new InvalidDataException($"AI recovery source '{source.Role}' is unavailable.");
        return _requestKey.ReadSourceBytes(source);
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
internal sealed class AiReferenceImageViewModel : IDisposable
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
