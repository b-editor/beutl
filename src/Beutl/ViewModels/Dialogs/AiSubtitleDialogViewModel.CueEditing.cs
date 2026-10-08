using System.ComponentModel;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using Beutl.Api.Services;
using Beutl.Editor.Services.Captions;
using Beutl.Services;
using Beutl.Services.AI;
using FluentAvalonia.UI.Controls;
using Microsoft.Extensions.Logging;
using Reactive.Bindings;

namespace Beutl.ViewModels.Dialogs;

public sealed partial class AiSubtitleDialogViewModel
{
    private int _captionImportRevision;

    private Task ImportCaptionsCore() => ImportCaptionsCore(null);

    internal bool CanImportCaptionFile(string name)
        => !_disposed && _captionCodecs.TryGetByFileName(name, out CaptionCodecInfo? codec) && codec.CanDecode;

    internal async Task ImportCaptionsCore(string? droppedPath, Func<Stream>? openDroppedFile = null)
    {
        using AsyncOperationLifetime.Operation? operationLifetime = _operations.TryEnter();
        if (operationLifetime is null)
            return;
        IReadOnlyList<IStorageFile> files = [];
        if (droppedPath is null)
        {
            IStorageProvider? storage = AiDialogStorage.MainWindowStorage();
            if (storage is null)
                return;

            files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                AllowMultiple = false,
                FileTypeFilter = [CreateCaptionFileType(canDecode: true)],
            });
            if (files.Count == 0)
                return;
        }
        using IDisposable fileOwnership = SharedFilePickerOptions.OwnStorageFiles(files);
        int importRevision = Interlocked.Increment(ref _captionImportRevision);

        try
        {
            if (!_captionCodecs.TryGetByFileName(
                    droppedPath is null ? files[0].Name : Path.GetFileName(droppedPath),
                    out CaptionCodecInfo? codec)
                || !codec.CanDecode)
            {
                throw new NotSupportedException("No caption codec is registered for this file extension.");
            }
            await using Stream stream = droppedPath is null
                ? await files[0].OpenReadAsync()
                : openDroppedFile?.Invoke() ?? File.OpenRead(droppedPath);
            using var memory = new SizeLimitedMemoryStream(AiCaptionHistoryResultParser.MaximumResultBytes);
            await stream.CopyToAsync(memory, operationLifetime.CancellationToken);
            operationLifetime.TryPublish(() =>
            {
                if (importRevision == Volatile.Read(ref _captionImportRevision))
                    ImportCaptionBytes(memory.ToArray(), codec.Format);
            });
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import captions.");
            operationLifetime.TryPublish(() =>
            {
                if (importRevision == Volatile.Read(ref _captionImportRevision))
                    Error.Value = Strings.AiSubtitle_ImportFailed;
            });
        }
    }

    private async Task ExportCaptionsCore()
    {
        using AsyncOperationLifetime.Operation? operationLifetime = _operations.TryEnter();
        if (operationLifetime is null)
            return;
        if (!TryBuildCaptionDocument(out CaptionDocument? document, out _) || document is null)
            return;

        IStorageProvider? storage = AiDialogStorage.MainWindowStorage();
        if (storage is null)
            return;

        using IStorageFile? file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "subtitles.srt",
            DefaultExtension = "srt",
            FileTypeChoices = [CreateCaptionFileType(canEncode: true)],
        });
        if (file is null)
            return;

        try
        {
            if (!_captionCodecs.TryGetByFileName(
                    file.Name,
                    out CaptionCodecInfo? codec)
                || !codec.CanEncode)
            {
                throw new NotSupportedException("No caption codec is registered for this file extension.");
            }
            bool exported = await TryWriteCaptionExportAsync(
                document,
                codec.Format,
                _captionSerializer,
                ConfirmLossySrtExportAsync,
                (bytes, cancellationToken) => CaptionExportStorage.WriteAsync(
                    file,
                    bytes,
                    cancellationToken),
                operationLifetime.CancellationToken);
            if (!exported)
                return;

            operationLifetime.TryPublish(() => NotificationService.ShowSuccess(Strings.AiSubtitle, Strings.AiSubtitle_Exported));
        }
        catch (OperationCanceledException) when (_lifetimeCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export captions.");
            operationLifetime.TryPublish(() => Error.Value = Strings.AiSubtitle_ExportFailed);
        }
    }

    private void AddCueCore()
    {
        TimeSpan start = _editableCues.LastOrDefault()?.TryCreateCue(out CaptionCue? last) == true
            ? last!.End
            : _editViewModel?.Player.CurrentFrame.Value ?? TimeSpan.Zero;
        var cue = new CaptionCue(start, start + TimeSpan.FromSeconds(2), string.Empty);
        var item = new EditableCaptionCueViewModel(_editableCues.Count + 1, cue);
        AttachCue(item);
        _editableCues.Add(item);
        MarkCaptionDocumentChanged();
        SelectedCue.Value = item;
        RefreshCaptionState();
    }

    internal bool ImportCaptionBytes(ReadOnlySpan<byte> bytes, CaptionFormatId format)
    {
        CaptionImportResult result = _captionSerializer.Import(bytes, format);
        if (result.Document is null)
        {
            Error.Value = result.Diagnostics.FirstOrDefault()?.Message ?? Strings.AiSubtitle_ImportFailed;
            return false;
        }

        if (!TryParkCurrentCaptionRecovery())
        {
            Error.Value = Strings.AiSubtitle_RunCannotBeRecorded;
            return false;
        }
        if (_retainedCaptionRecoveries.Count == 0)
            ChangeCaptionDraftJob(null, deleteCurrent: true);
        else
            PersistRetainedCaptionRecoveries();
        _lastCaptionLanguage = null;
        DetectedLanguageText.Value = null;
        ReplaceCues(result.Document);
        Error.Value = result.Diagnostics.FirstOrDefault()?.Message;
        return true;
    }

    internal byte[] ExportCaptionBytes(CaptionFormatId format)
    {
        if (!TryBuildCaptionDocument(out CaptionDocument? document, out string? error)
            || document is null)
        {
            throw new CaptionExportException(null, error ?? Strings.AiSubtitle_ExportFailed);
        }
        return _captionSerializer.Export(document, format);
    }

    internal static async Task<bool> TryWriteCaptionExportAsync(
        CaptionDocument document,
        CaptionFormatId format,
        CaptionDocumentSerializer serializer,
        Func<CancellationToken, Task<bool>> confirmLossySrtExport,
        Func<byte[], CancellationToken, Task> write,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(serializer);
        ArgumentNullException.ThrowIfNull(confirmLossySrtExport);
        ArgumentNullException.ThrowIfNull(write);
        cancellationToken.ThrowIfCancellationRequested();

        CaptionDocument exportDocument = document;
        if (format == CaptionFormats.Srt && HasUnsupportedSrtMetadata(document))
        {
            if (!await confirmLossySrtExport(cancellationToken))
                return false;

            cancellationToken.ThrowIfCancellationRequested();
            exportDocument = new CaptionDocument(document.Cues.Select(cue => new CaptionCue(
                cue.Start,
                cue.End,
                cue.Text)));
        }

        byte[] bytes = serializer.Export(exportDocument, format);
        await write(bytes, cancellationToken);
        return true;
    }

    private static bool HasUnsupportedSrtMetadata(CaptionDocument document)
        => document.Cues.Any(cue =>
            cue.Speaker is not null
            || cue.Language is not null
            || cue.Metadata.Count > 0);

    private static async Task<bool> ConfirmLossySrtExportAsync(
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var dialog = new FAContentDialog
        {
            Title = Strings.AiSubtitle_LossySrtExportTitle,
            Content = Strings.AiSubtitle_LossySrtExportMessage,
            PrimaryButtonText = Strings.AiSubtitle_Export,
            CloseButtonText = Strings.Cancel,
            DefaultButton = FAContentDialogButton.Close,
        };
        var showDialog = dialog.ShowAsync();
        using CancellationTokenRegistration cancellationRegistration =
            cancellationToken.Register(static state =>
            {
                var contentDialog = (FAContentDialog)state!;
                Dispatcher.UIThread.Post(contentDialog.Hide);
            }, dialog);
        FAContentDialogResult result = await showDialog;
        cancellationToken.ThrowIfCancellationRequested();
        return result == FAContentDialogResult.Primary;
    }

    private void DeleteCueCore()
    {
        if (SelectedCue.Value is not { } selected)
            return;

        int index = _editableCues.IndexOf(selected);
        if (index < 0)
            return;

        selected.PropertyChanged -= OnCuePropertyChanged;
        _editableCues.RemoveAt(index);
        RenumberCues();
        MarkCaptionDocumentChanged();
        SelectedCue.Value = _editableCues.Count == 0
            ? null
            : _editableCues[Math.Min(index, _editableCues.Count - 1)];
        RefreshCaptionState();
    }

    private void SplitCueCore()
    {
        if (SelectedCue.Value is not { } selected
            || !TryBuildCaptionDocumentCore(out CaptionDocument? document, out _)
            || document is null)
        {
            return;
        }

        int index = _editableCues.IndexOf(selected);
        if (index < 0)
            return;

        CaptionCue cue = document[index];
        int textOffset = selected.CaretIndex;
        if (!TryGetCueSplitTime(cue, textOffset, out TimeSpan splitTime))
            return;

        document.SplitCue(index, splitTime, textOffset);
        ReplaceCues(document);
        SelectedCue.Value = _editableCues[index + 1];
    }

    private void MergeCueCore()
    {
        if (SelectedCue.Value is not { } selected
            || !TryBuildCaptionDocumentCore(out CaptionDocument? document, out _)
            || document is null)
        {
            return;
        }

        int index = _editableCues.IndexOf(selected);
        if (index < 0 || index >= document.Count - 1)
            return;

        document.MergeWithNext(index);
        ReplaceCues(document);
        SelectedCue.Value = _editableCues[index];
    }

    private void WrapCuesCore()
    {
        CaptionTextConstraints constraints = CreateTextConstraints();
        foreach (EditableCaptionCueViewModel cue in _editableCues)
        {
            cue.Text = CaptionTextWrapper.Wrap(cue.Text, constraints);
        }
        RefreshCaptionState();
    }

    private void ApplyTranscriptionSegments(AiTranscriptionSegment[]? segments)
    {
        if (_disposed)
            return;

        if (segments is not { Length: > 0 })
        {
            ReplaceCues(new CaptionDocument());
            return;
        }

        ReplaceCues(new CaptionDocument(segments.Select(segment => new CaptionCue(
            TimeSpan.FromSeconds(segment.Start),
            TimeSpan.FromSeconds(segment.End),
            segment.Text,
            language: _lastCaptionLanguage))));
    }

    private void ReplaceCues(CaptionDocument document)
    {
        if (_disposed)
            return;

        foreach (EditableCaptionCueViewModel cue in _editableCues)
        {
            cue.PropertyChanged -= OnCuePropertyChanged;
        }
        _editableCues.Clear();
        for (int index = 0; index < document.Count; index++)
        {
            var cue = new EditableCaptionCueViewModel(index + 1, document[index]);
            AttachCue(cue);
            _editableCues.Add(cue);
        }
        MarkCaptionDocumentChanged();
        SelectedCue.Value = _editableCues.FirstOrDefault();
        if (document.Count > 0)
            SelectedSubtitlePageIndex.Value = EditPageIndex;
        RefreshCaptionState();
    }

    private void AttachCue(EditableCaptionCueViewModel cue)
        => cue.PropertyChanged += OnCuePropertyChanged;

    private void OnCuePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(EditableCaptionCueViewModel.CaretIndex))
        {
            RefreshCueCommandStates();
            return;
        }

        if (e.PropertyName != nameof(EditableCaptionCueViewModel.Number))
        {
            MarkCaptionDocumentChanged();
        }
        RefreshCueCommandStates();
        RefreshCaptionState();
    }

    private void MarkCaptionDocumentChanged()
        => Interlocked.Increment(ref _captionDocumentRevision);

    private void RenumberCues()
    {
        for (int index = 0; index < _editableCues.Count; index++)
        {
            _editableCues[index].Number = index + 1;
        }
    }

    private void RefreshCaptionState()
    {
        bool timingValid = TryBuildCaptionDocumentCore(out CaptionDocument? document, out string? parseError);
        HasTimingValidCues.Value = timingValid && document is { Count: > 0 };
        if (!timingValid || document is null)
        {
            HasValidCues.Value = false;
            CaptionValidationMessage.Value = parseError;
        }
        else
        {
            IReadOnlyList<CaptionValidationIssue> issues = CaptionDocumentValidator.Validate(
                document,
                CreateTextConstraints());
            CaptionValidationIssue[] blockingIssues = GetBlockingIssues(issues);
            HasValidCues.Value = document.Count > 0 && blockingIssues.Length == 0;
            CaptionValidationMessage.Value = blockingIssues.Length > 0
                ? string.Format(Strings.AiSubtitle_ValidationIssues, blockingIssues.Length)
                : issues.Any(issue => issue.Kind == CaptionValidationIssueKind.Overlap)
                    ? Strings.AiSubtitle_OverlapWarning
                    : null;
        }

        RefreshTranslationEstimate();
        RefreshTemplatePreview();
    }

    private void RefreshCueCommandStates()
    {
        EditableCaptionCueViewModel? selected = SelectedCue.Value;
        int index = selected is null ? -1 : _editableCues.IndexOf(selected);
        _canDeleteCue.Value = index >= 0;
        _canMergeCue.Value = index >= 0
            && index < _editableCues.Count - 1
            && TryBuildCaptionDocumentCore(out _, out _);
        _canSplitCue.Value = index >= 0
            && TryBuildCaptionDocumentCore(out CaptionDocument? document, out _)
            && document is not null
            && TryGetCueSplitTime(document[index], selected!.CaretIndex, out _);
    }

    private static bool TryGetCueSplitTime(
        CaptionCue cue,
        int textOffset,
        out TimeSpan splitTime)
    {
        splitTime = default;
        if (textOffset <= 0 || textOffset >= cue.Text.Length)
            return false;

        int[] boundaries = StringInfo.ParseCombiningCharacters(cue.Text);
        int boundaryIndex = Array.BinarySearch(boundaries, textOffset);
        long durationTicks = (cue.End - cue.Start).Ticks;
        if (boundaryIndex <= 0 || durationTicks <= 1)
            return false;

        long offsetTicks = (long)Math.Round(
            durationTicks * (boundaryIndex / (double)boundaries.Length),
            MidpointRounding.AwayFromZero);
        offsetTicks = Math.Clamp(offsetTicks, 1, durationTicks - 1);
        splitTime = cue.Start + TimeSpan.FromTicks(offsetTicks);
        return true;
    }

    internal bool TryBuildCaptionDocument(out CaptionDocument? document, out string? error)
    {
        if (!TryBuildCaptionDocumentCore(out document, out error) || document is null)
            return false;

        IReadOnlyList<CaptionValidationIssue> issues = CaptionDocumentValidator.Validate(
            document,
            CreateTextConstraints());
        CaptionValidationIssue[] blockingIssues = GetBlockingIssues(issues);
        if (blockingIssues.Length > 0)
        {
            error = string.Format(Strings.AiSubtitle_ValidationIssues, blockingIssues.Length);
            return false;
        }
        return document.Count > 0;
    }

    private bool TryBuildCaptionDocumentCore(out CaptionDocument? document, out string? error)
    {
        var cues = new List<CaptionCue>(_editableCues.Count);
        foreach (EditableCaptionCueViewModel item in _editableCues)
        {
            if (!item.TryCreateCue(out CaptionCue? cue) || cue is null)
            {
                document = null;
                error = Strings.AiSubtitle_InvalidTiming;
                return false;
            }
            cues.Add(cue);
        }
        document = new CaptionDocument(cues);
        error = null;
        return true;
    }

    private CaptionTextConstraints CreateTextConstraints()
        => new(Math.Max(MaximumLineLength.Value, 1), Math.Max(MaximumLineCount.Value, 1));

    private static CaptionValidationIssue[] GetBlockingIssues(
        IReadOnlyList<CaptionValidationIssue> issues)
        => issues.Where(issue => issue.Kind != CaptionValidationIssueKind.Overlap).ToArray();

    private FilePickerFileType CreateCaptionFileType(
        bool canDecode = false,
        bool canEncode = false)
        => new(Strings.AiSubtitle_CaptionFiles)
        {
            Patterns = _captionCodecs.Codecs
                .Where(codec => (!canDecode || codec.CanDecode)
                    && (!canEncode || codec.CanEncode))
                .SelectMany(codec => codec.FileExtensions)
                .Select(extension => $"*{extension}")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(pattern => pattern, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            MimeTypes = ["text/plain", "application/octet-stream"],
        };
}
