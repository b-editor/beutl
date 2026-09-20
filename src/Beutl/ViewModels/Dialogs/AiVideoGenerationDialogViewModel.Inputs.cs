using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Beutl.Api.Services;
using Beutl.Language;
using Beutl.Media.Decoding;
using Beutl.Services.AI;
using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

internal sealed partial class AiVideoGenerationDialogViewModel
{
    internal AiSourceVideoMode? SourceMode { get; }
    private AiOperationId Operation => SourceMode switch
    {
        AiSourceVideoMode.Edit => AiOperations.VideoEditing,
        AiSourceVideoMode.Extend => AiOperations.VideoExtension,
        AiSourceVideoMode.Motion => AiOperations.VideoMotion,
        _ => AiOperations.VideoGeneration,
    };
    public bool IsGeneration => SourceMode is null;
    public bool IsSourceVideo => !IsGeneration;
    public bool IsMotionControl => SourceMode == AiSourceVideoMode.Motion;
    public bool CanChooseDuration => SourceMode != AiSourceVideoMode.Edit;
    public ObservableCollection<AiVideoInputGroup> ReferenceGroups { get; } = [];
    public ReactivePropertySlim<bool> HasReferenceControls { get; } = new();
    public ReactivePropertySlim<bool> ReferencesSuspended { get; } = new();
    public ReactivePropertySlim<string?> InputError { get; } = new();
    public ReactivePropertySlim<bool> HasRequiredInputs { get; } = new();
    public ReactivePropertySlim<int> MaxPromptLength { get; } = new(AiRequestLimits.MaxPromptLength);
    public ReactivePropertySlim<string?> SourceVideoPath { get; } = new();
    public ReactivePropertySlim<double?> SourceDuration { get; } = new();
    public ReactivePropertySlim<string?> CharacterImagePath { get; } = new();
    public AsyncReactiveCommand SelectSourceVideo { get; private set; } = null!;
    public AsyncReactiveCommand SelectCharacterImage { get; private set; } = null!;
    public IReadOnlyList<AiVideoInputOption> Orientations { get; } = [new("video", Strings.AiSourceVideo), new("image", Strings.AiCharacterImage)];
    public IReadOnlyList<AiVideoInputOption> Qualities { get; } = [new("standard", Strings.AiMotionStandard), new("pro", Strings.AiMotionPro)];
    public ReactivePropertySlim<AiVideoInputOption> Orientation { get; } = new();
    public ReactivePropertySlim<AiVideoInputOption> Quality { get; } = new();
    internal Func<string, CancellationToken, Task<IReadOnlyList<string>>>? InputPicker { get; set; }
    internal Func<string, TimeSpan>? VideoDurationReader { get; set; }

    private int RequestDuration => SourceMode == AiSourceVideoMode.Edit
        ? (int)Math.Clamp(Math.Ceiling(SourceDuration.Value ?? 1), 1, AiRequestLimits.MaxVideoDurationSeconds)
        : SelectedDuration.Value.Seconds;

    private void InitializeVideoInputs()
    {
        foreach (var property in new IDisposable[] { HasReferenceControls, ReferencesSuspended, InputError, HasRequiredInputs, MaxPromptLength,
            SourceVideoPath, SourceDuration, CharacterImagePath, Orientation, Quality })
            property.DisposeWith(_disposables);
        Orientation.Value = Orientations[0];
        Quality.Value = Qualities[0];
        foreach (var (kind, label) in new[] { ("image", Strings.AiReferenceImages), ("video", Strings.AiReferenceVideos), ("audio", Strings.AiReferenceAudio) })
        {
            var group = new AiVideoInputGroup(kind, label, IsGenerating, () => PickInputAsync(kind), RefreshVideoInputs);
            group.DisposeWith(_disposables);
            ReferenceGroups.Add(group);
        }
        SelectSourceVideo = new AsyncReactiveCommand(IsGenerating.Select(value => !value))
            .WithSubscribe(() => PickInputAsync("source")).DisposeWith(_disposables);
        SelectCharacterImage = new AsyncReactiveCommand(IsGenerating.Select(value => !value))
            .WithSubscribe(() => PickInputAsync("character")).DisposeWith(_disposables);
        SourceVideoPath.Subscribe(_ => RefreshVideoInputs()).DisposeWith(_disposables);
        SourceDuration.Subscribe(_ =>
        {
            RefreshVideoInputs();
            _availabilityTracker.Check(new AiOperationAvailabilityRequest.Video(Operation, RequestDuration, ModelPicker.SelectedModel));
        }).DisposeWith(_disposables);
        CharacterImagePath.Subscribe(_ => RefreshVideoInputs()).DisposeWith(_disposables);
        FirstFramePath.Subscribe(_ => RefreshVideoInputs()).DisposeWith(_disposables);
    }

    private sealed class VideoInputException(string message) : Exception(message);

    private void RefreshVideoInputs()
    {
        var limits = ModelPicker.Selected.Value?.Model.Video ?? AiVideoModelCapabilities.Unrestricted;
        // Legacy pending requests used the global limit before per-model limits were saved.
        MaxPromptLength.Value = _selectedRecovery is { } recovery
            ? recovery.Form?.VideoPromptLimit ?? AiRequestLimits.MaxPromptLength
            : limits.MaxPromptLength;
        foreach (var group in ReferenceGroups)
        {
            (int count, long bytes) = group.Kind switch
            {
                "image" => (limits.MaxInputReferences, limits.MaxInputReferenceBytes),
                "video" => (limits.MaxVideoReferences, limits.MaxVideoReferenceBytes),
                _ => (limits.MaxAudioReferences, limits.MaxAudioReferenceBytes),
            };
            group.SetLimits(IsGeneration && limits.SupportsInputReferences, count, bytes);
        }
        HasReferenceControls.Value = ReferenceGroups.Any(group => group.IsVisible.Value);
        ReferencesSuspended.Value = IsGeneration && FirstFramePath.Value is not null && ReferenceGroups.Any(group => group.Files.Count > 0);
        HasRequiredInputs.Value = IsGeneration || (SourceVideoPath.Value is not null && (!IsMotionControl || CharacterImagePath.Value is not null));
        InputError.Value = null;
        if (!HasRequiredInputs.Value) return;
        try
        {
            if (IsSourceVideo)
            {
                ValidateSourceInputs(Describe(SourceVideoPath.Value!),
                    IsMotionControl && CharacterImagePath.Value is { } character ? Describe(character) : null,
                    SourceDuration.Value, limits);
            }
            else if (FirstFramePath.Value is null)
            {
                var references = ReferenceGroups.SelectMany(group => group.Files).Select(file => Describe(file.Path, file.Name)).ToArray();
                AiVideoInputLimits.ValidateReferences(references);
                if (_selectedRecovery is null)
                {
                    if (references.Length == 0 && !limits.SupportsPromptToVideo) throw new VideoInputException(Strings.AiChooseVideoInput);
                    foreach (var group in ReferenceGroups.Where(group => group.Files.Count > 0))
                    {
                        if (!group.IsSupported.Value || group.Files.Count > group.MaximumCount
                            || group.Files.Any(file => new FileInfo(file.Path).Length > group.MaximumBytes))
                            throw new VideoInputException(Strings.AiModelDoesNotSupportRequest);
                    }
                }
            }
            InputError.Value = null;
        }
        catch (AiFileTooLargeException) { InputError.Value = Strings.AiFileTooLarge; }
        catch (VideoInputException ex) { InputError.Value = ex.Message; }
        catch (ArgumentException) { InputError.Value = Strings.AiVideoInputUnavailable; }
        catch (IOException) { InputError.Value = Strings.AiVideoInputUnavailable; }
        catch (UnauthorizedAccessException) { InputError.Value = Strings.AiVideoInputUnavailable; }
    }

    private void ValidateSourceInputs(AiUploadSource source, AiUploadSource? character, double? duration, AiVideoModelCapabilities limits)
    {
        AiVideoInputLimits.Validate(source, "video", _selectedRecovery is null ? limits.MaxSourceVideoBytes : AiVideoInputLimits.MaxSourceBytes);
        if (duration is not { } seconds || !double.IsFinite(seconds) || seconds <= 0 || seconds > 60
            || (_selectedRecovery is null && (seconds < (limits.MinSourceVideoSeconds ?? 0) || seconds > (limits.MaxSourceVideoSeconds ?? 60))))
            throw new VideoInputException(Strings.AiModelDoesNotSupportRequest);
        if (IsMotionControl)
        {
            if (character is null) throw new VideoInputException(Strings.AiChooseCharacterImage);
            AiVideoInputLimits.Validate(character, "image", AiRequestLimits.MaxFrameUploadBytes);
        }
    }

    private static string MediaType(string name) => Path.GetExtension(name).ToLowerInvariant() switch
    {
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".mp4" => "video/mp4",
        ".webm" => "video/webm",
        ".wav" or ".wave" => "audio/wav",
        ".mp3" => "audio/mpeg",
        _ => throw new VideoInputException(Strings.AiVideoInputUnavailable),
    };
    private static AiUploadSource Describe(string path, string? name = null)
        => new(name ?? Path.GetFileName(path), MediaType(name ?? path),
            token => { token.ThrowIfCancellationRequested(); return ValueTask.FromResult<Stream>(File.OpenRead(path)); },
            new FileInfo(path).Length);

    private async Task PickInputAsync(string role)
    {
        using var operation = TryEnterIdentityOperation();
        if (operation is null) return;
        try
        {
            IReadOnlyList<string> paths;
            if (InputPicker is { } picker) paths = await picker(role, operation.CancellationToken);
            else
            {
                if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime { MainWindow: { } window }) return;
                var patterns = role switch { "source" or "video" => new[] { "*.mp4", "*.webm" }, "audio" => ["*.wav", "*.wave", "*.mp3"], _ => ["*.png", "*.jpg", "*.jpeg", "*.webp"] };
                var files = await window.StorageProvider.OpenFilePickerAsync(new()
                {
                    AllowMultiple = role is "image" or "video" or "audio",
                    FileTypeFilter = [new FilePickerFileType(Strings.AiVideoInput) { Patterns = patterns }],
                });
                paths = files.Select(file => file.TryGetLocalPath()).OfType<string>().ToArray();
                foreach (var file in files) file.Dispose();
            }
            if (paths.Count == 0) return;
            string kind = role is "source" or "video" ? "video" : role == "audio" ? "audio" : "image";
            foreach (string path in paths) AiVideoInputLimits.Validate(Describe(path), kind,
                kind == "image" ? AiRequestLimits.MaxFrameUploadBytes : kind == "audio" ? AiVideoInputLimits.MaxAudioBytes : AiVideoInputLimits.MaxSourceBytes);
            double? duration = role == "source" ? await ReadVideoDurationAsync(paths[0], operation.CancellationToken) : null;
            operation.TryPublish(() =>
            {
                if (role == "source") { SourceVideoPath.Value = paths[0]; SourceDuration.Value = duration; }
                else if (role == "character") CharacterImagePath.Value = paths[0];
                else
                {
                    var group = ReferenceGroups.Single(group => group.Kind == kind);
                    foreach (string path in paths) group.Add(path);
                }
                Error.Value = null;
                RefreshVideoInputs();
            });
        }
        catch (OperationCanceledException) when (operation.CancellationToken.IsCancellationRequested) { }
        catch (Exception ex) { operation.TryPublish(() => Error.Value = ex is AiFileTooLargeException ? Strings.AiFileTooLarge : Strings.AiVideoInputUnavailable); }
    }

    private sealed record InputSnapshot(string Role, string Path, string Name, byte[] Bytes, AiUploadSource Upload);

    private Task<double> ReadVideoDurationAsync(string path, CancellationToken token) => Task.Run(() =>
    {
        if (VideoDurationReader is { } reader) return reader(path).TotalSeconds;
        using var video = MediaReader.Open(path, new MediaOptions(MediaMode.Video));
        return video.VideoInfo.Duration.ToDouble();
    }, token);

    private async Task<double> ReadSourceSnapshotDurationAsync(InputSnapshot source, CancellationToken token)
    {
        // Probe the upload bytes: the selected path can be overwritten while this request is prepared.
        (string path, FileStream stream) = AiTemporaryFileStore.Create("inputs", "source-video", Path.GetExtension(source.Name));
        try
        {
            await using (stream)
                await stream.WriteAsync(source.Bytes, token);
            return await ReadVideoDurationAsync(path, token);
        }
        finally
        {
            DeleteTemporaryFile(path);
        }
    }

    private async Task<InputSnapshot[]> ReadVideoInputsAsync(CancellationToken token, bool references)
    {
        var paths = new List<(string Role, string Path, string Name)>();
        if (IsSourceVideo && SourceVideoPath.Value is { } source) paths.Add(("source-video", source, Path.GetFileName(source)));
        if (IsMotionControl && CharacterImagePath.Value is { } image) paths.Add(("character-image", image, Path.GetFileName(image)));
        if (IsGeneration && references)
            foreach (var group in ReferenceGroups)
                for (int i = 0; i < group.Files.Count; i++)
                    paths.Add(($"reference-{group.Kind}-{i}", group.Files[i].Path, group.Files[i].Name));
        var result = new List<InputSnapshot>();
        var limits = ModelPicker.Selected.Value?.Model.Video ?? AiVideoModelCapabilities.Unrestricted;
        long totalBytes = 0;
        foreach (var (role, path, name) in paths)
        {
            var recovery = _selectedRecovery?.EffectiveSources.FirstOrDefault(item => item.Role == role && RecoverySourceMatchesPath(item, path));
            string fileName = recovery?.Name ?? name;
            long maximum = role switch
            {
                "character-image" => AiRequestLimits.MaxFrameUploadBytes,
                "source-video" => _selectedRecovery is null ? limits.MaxSourceVideoBytes : AiVideoInputLimits.MaxSourceBytes,
                _ when role.StartsWith("reference-image-", StringComparison.Ordinal) => _selectedRecovery is null ? limits.MaxInputReferenceBytes : AiRequestLimits.MaxFrameUploadBytes,
                _ when role.StartsWith("reference-audio-", StringComparison.Ordinal) => _selectedRecovery is null ? limits.MaxAudioReferenceBytes : AiVideoInputLimits.MaxAudioBytes,
                _ => _selectedRecovery is null ? limits.MaxVideoReferenceBytes : AiVideoInputLimits.MaxSourceBytes,
            };
            byte[] bytes = recovery is null ? await AiUploadBytes.ReadWithinAsync(path, maximum, token) : _requestKey.ReadSourceBytes(recovery);
            totalBytes += bytes.LongLength;
            if (bytes.LongLength > maximum || totalBytes > AiVideoInputLimits.MaxSourceBytes + (IsMotionControl ? AiRequestLimits.MaxFrameUploadBytes : 0))
                throw new AiFileTooLargeException();
            result.Add(new(role, path, fileName, bytes, AiUploadSource.FromBytes(fileName, MediaType(fileName), bytes)));
        }
        if (IsGeneration) AiVideoInputLimits.ValidateReferences(result.Select(item => item.Upload).ToArray());
        return result.ToArray();
    }

    private void RestoreVideoInputs(AiPendingAttempt attempt, IReadOnlyList<string> paths)
    {
        foreach (var group in ReferenceGroups) group.Files.Clear();
        SourceVideoPath.Value = null;
        CharacterImagePath.Value = null;
        for (int i = 0; i < paths.Count; i++)
        {
            var source = attempt.EffectiveSources[i];
            if (source.Role == "source-video") SourceVideoPath.Value = paths[i];
            else if (source.Role == "character-image") CharacterImagePath.Value = paths[i];
            else if (source.Role.StartsWith("reference-", StringComparison.Ordinal))
                ReferenceGroups.Single(group => source.Role.StartsWith($"reference-{group.Kind}-", StringComparison.Ordinal)).Add(paths[i], source.Name);
        }
        var form = attempt.Form!;
        SourceDuration.Value = form.SourceVideoSeconds;
        Orientation.Value = Orientations.FirstOrDefault(item => item.Value == form.VideoOrientation) ?? Orientations[0];
        Quality.Value = Qualities.FirstOrDefault(item => item.Value == form.VideoQuality) ?? Qualities[0];
    }

    internal void CopySourceIntent(AiVideoGenerationDialogViewModel source)
    {
        if (_selectedRecovery is not null || _requestKey.HasOutstandingName.Value || IsGenerating.Value || source.IsGenerating.Value) return;
        using var origin = source.TryEnterIdentityOperation();
        using var target = TryEnterIdentityOperation();
        if (origin is null || target is null) return;
        origin.TryPublish(() => target.TryPublish(() =>
        {
            SourceVideoPath.Value = source.SourceVideoPath.Value;
            SourceDuration.Value = source.SourceDuration.Value;
            Prompt.Value = source.Prompt.Value;
            Style.Value = source.Style.Value;
            Composition.Value = source.Composition.Value;
            Motion.Value = source.Motion.Value;
            Exclusions.Value = source.Exclusions.Value;
        }));
    }

    private void ClearVideoInputs()
    {
        foreach (var group in ReferenceGroups) group.Files.Clear();
        SourceVideoPath.Value = null;
        CharacterImagePath.Value = null;
        SourceDuration.Value = null;
        Orientation.Value = Orientations[0];
        Quality.Value = Qualities[0];
    }
}

internal sealed record AiVideoInputOption(string Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}
