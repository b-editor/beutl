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

public sealed class AiImageGenerationDialogViewModel : IToolContext, IAsyncDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly AsyncOperationLifetime _operations = new();
    private readonly object _disposeGate = new();
    private readonly ILogger _logger = Log.CreateLogger<AiImageGenerationDialogViewModel>();
    private readonly IAiEntitlementService _entitlements;
    private readonly IAiPlanCoordinator _aiPlanCoordinator;
    private readonly IAiImageGenerationService _images;
    private readonly IAuthenticatedContentService _content;
    private readonly EditViewModel? _editViewModel;
    private AiImageResultSnapshot? _resultSnapshot;
    private Task? _disposeTask;

    public AiImageGenerationDialogViewModel(
        IAiEntitlementService entitlements,
        IAiPlanCoordinator aiPlanCoordinator,
        IAiImageGenerationService images,
        IAuthenticatedContentService content,
        EditViewModel? editViewModel = null)
    {
        _entitlements = entitlements ?? throw new ArgumentNullException(nameof(entitlements));
        _aiPlanCoordinator = aiPlanCoordinator
            ?? throw new ArgumentNullException(nameof(aiPlanCoordinator));
        _images = images ?? throw new ArgumentNullException(nameof(images));
        _content = content ?? throw new ArgumentNullException(nameof(content));
        _editViewModel = editViewModel;
        Usage = new AiUsageViewModel(_entitlements.Entitlements).DisposeWith(_disposables);
        EstimatedUsage = new AiUsageEstimateViewModel(
                Usage,
                _entitlements.Entitlements.Select(value =>
                    value?.Availability.CanStart(AiOperations.ImageGeneration) ?? false))
            .DisposeWith(_disposables);
        PromptLibrary = new AiPromptLibraryViewModel(
                PromptTaskKind.Image,
                ComposePrompt,
                prompt => Prompt.Value = prompt)
            .DisposeWith(_disposables);

        SizeOptions =
        [
            new AiImageSizeOption("1024x1024"),
            new AiImageSizeOption("1024x1536"),
            new AiImageSizeOption("1536x1024"),
        ];
        SelectedSize = new ReactivePropertySlim<AiImageSizeOption>(
                GetSuggestedSize(SizeOptions, editViewModel?.Scene.FrameSize))
            .DisposeWith(_disposables);

        IsGenerating = new ReactivePropertySlim<bool>(false)
            .DisposeWith(_disposables);

        GenerateButtonText = IsGenerating
            .Select(x => x ? Strings.AiGenerating : Strings.AiGenerate)
            .ToReadOnlyReactivePropertySlim(Strings.AiGenerate)
            .DisposeWith(_disposables);

        CanGenerate = Prompt
            .CombineLatest(IsGenerating, (prompt, generating) =>
                !string.IsNullOrWhiteSpace(prompt) && !generating)
            .CombineLatest(EstimatedUsage.CanAfford, (canGenerate, canAfford) => canGenerate && canAfford)
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

        OpenAiPlan = new ReactiveCommand();
        OpenAiPlan.Subscribe(aiPlanCoordinator.OpenAiPlan).DisposeWith(_disposables);

        ShowJoinPro = Usage.HasSnapshot
            .CombineLatest(Usage.CanUseAi, (hasSnapshot, canUseAi) => hasSnapshot && !canUseAi)
            .ToReadOnlyReactivePropertySlim()
            .DisposeWith(_disposables);

        _ = LoadEntitlementsAsync();
    }

    public ToolTabExtension Extension => AiImageGenerationTabExtension.Instance;

    public IReactiveProperty<bool> IsSelected { get; } = new ReactivePropertySlim<bool>();

    public IReadOnlyReactiveProperty<string> Header { get; } = new ReactivePropertySlim<string>(Strings.AiImageGeneration);

    public IReadOnlyList<AiImageSizeOption> SizeOptions { get; }

    public ReactivePropertySlim<AiImageSizeOption> SelectedSize { get; }

    public ReactivePropertySlim<string> Prompt { get; } = new();

    public ReactivePropertySlim<string> Style { get; } = new();

    public ReactivePropertySlim<string> Composition { get; } = new();

    public ReactivePropertySlim<string> Exclusions { get; } = new();

    public ReactivePropertySlim<bool> IsGenerating { get; }

    public ReadOnlyReactivePropertySlim<string> GenerateButtonText { get; }

    public ReadOnlyReactivePropertySlim<bool> CanGenerate { get; }

    public AsyncReactiveCommand Generate { get; }

    public ReadOnlyReactivePropertySlim<bool> CanAddToScene { get; }

    public AsyncReactiveCommand AddToScene { get; }

    public AsyncReactiveCommand SaveToFile { get; }

    public ReactiveCommand OpenAiPlan { get; }

    internal IAiPlanCoordinator AiPlanCoordinator => _aiPlanCoordinator;

    public ReactivePropertySlim<Ref<Bitmap>?> ResultImage { get; } = new();

    internal AiUsageViewModel Usage { get; }

    internal AiUsageEstimateViewModel EstimatedUsage { get; }

    internal AiPromptLibraryViewModel PromptLibrary { get; }

    public ReadOnlyReactivePropertySlim<bool> ShowJoinPro { get; }

    public ReactivePropertySlim<string?> Error { get; } = new();

    public object? GetService(Type serviceType) => _editViewModel?.GetService(serviceType);

    public void ReadFromJson(JsonObject json)
    {
        // Prompts and generated content are intentionally kept out of the persisted dock layout.
    }

    public void WriteToJson(JsonObject json)
    {
        // Prompts and generated content are intentionally kept out of the persisted dock layout.
    }

    internal static AiImageSizeOption GetSuggestedSize(
        IReadOnlyList<AiImageSizeOption> options,
        Beutl.Media.PixelSize? frameSize)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Count == 0)
            throw new ArgumentException("At least one image size option is required.", nameof(options));

        string value = frameSize switch
        {
            { Width: > 0, Height: > 0 } size when size.Width >= size.Height * 1.15 => "1536x1024",
            { Width: > 0, Height: > 0 } size when size.Height >= size.Width * 1.15 => "1024x1536",
            _ => "1024x1024",
        };
        return options.FirstOrDefault(option => option.Value == value) ?? options[0];
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
        Prompt.Dispose();
        Style.Dispose();
        Composition.Dispose();
        Exclusions.Dispose();
        Error.Dispose();
        IsSelected.Dispose();
        _disposables.Dispose();
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

        try
        {
            string prompt = ComposePrompt();
            string size = SelectedSize.Value.Value;
            AiImageResult response = await _images.GenerateAsync(
                new AiImageGenerationRequest(
                    prompt,
                    new AiImageSizeId(size)),
                operation.CancellationToken);

            using var stream = new MemoryStream();
            await _content.CopyToAsync(response.ContentUri, stream, operation.CancellationToken);
            operation.CancellationToken.ThrowIfCancellationRequested();
            stream.Position = 0;
            var resultImage = Ref<Bitmap>.Create(Bitmap.FromStream(stream));
            var snapshot = new AiImageResultSnapshot(
                response.JobId,
                response.FileId,
                prompt,
                size,
                DateTimeOffset.UtcNow);
            if (!operation.TryPublish(() =>
                {
                    ResultImage.Value?.Dispose();
                    ResultImage.Value = resultImage;
                    _resultSnapshot = snapshot;
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
        catch (AiProviderErrorException)
        {
            operation.TryPublish(() => Error.Value = Strings.AiProviderError);
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
            operation.TryPublish(() => IsGenerating.Value = false);
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
            AiImageResultSnapshot snapshot = _resultSnapshot
                ?? new AiImageResultSnapshot(null, null, string.Empty, SelectedSize.Value.Value, DateTimeOffset.UtcNow);
            GenerationProvenance provenance = AiProvenanceFactory.ImageGeneration(
                snapshot.Size,
                snapshot.GeneratedAt);
            var importer = new AiResultImporter(
                _editViewModel.Scene,
                _editViewModel.GetRequiredService<IElementAdder>());
            ElementAddResult result = await importer.ImportImageAsync(
                bitmap,
                new AiResultImportOptions(
                    start,
                    TimeSpan.FromSeconds(5),
                    layer,
                    Strings.AiImageGeneration,
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

    private sealed record AiImageResultSnapshot(
        AiJobId? JobId,
        AiContentId? FileId,
        string Prompt,
        string Size,
        DateTimeOffset GeneratedAt);

}

public sealed record AiImageSizeOption(string Value)
{
    public override string ToString() => Value;
}
