using Beutl.Media.Proxy;
using Reactive.Bindings;

namespace Beutl.Editor.Components.ProxiesTab.ViewModels;

public sealed class ProxyClipViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];
    private readonly ProxiesTabViewModel _owner;

    private bool _applyingDefault;

    public ProxyClipViewModel(
        ProxiesTabViewModel owner,
        string path,
        ProxyFingerprint source,
        ProxyPreset preset,
        ProxyEntry? entry,
        bool followingDefault,
        bool explicitlyChosen)
    {
        _owner = owner;
        Path = path;
        Source = source;
        IsFollowingDefault = followingDefault;
        IsExplicitlyChosen = explicitlyChosen;
        Preset = new ReactiveProperty<ProxyPreset>(preset)
            .DisposeWith(_disposables);
        State = new ReactiveProperty<string>()
            .DisposeWith(_disposables);
        ProxyInfoText = new ReactiveProperty<string>()
            .DisposeWith(_disposables);
        LastUsedText = new ReactiveProperty<string>()
            .DisposeWith(_disposables);
        FailureReason = new ReactiveProperty<string?>()
            .DisposeWith(_disposables);
        IsReady = new ReactiveProperty<bool>()
            .DisposeWith(_disposables);
        IsStale = new ReactiveProperty<bool>()
            .DisposeWith(_disposables);
        IsFailed = new ReactiveProperty<bool>()
            .DisposeWith(_disposables);
        IsMissing = new ReactiveProperty<bool>()
            .DisposeWith(_disposables);
        SourceInfoText = string.Format(
            CultureInfo.CurrentCulture,
            Strings.ProxySourceInfoFormat,
            ProxiesTabViewModel.FormatBytes(source.FileSizeBytes),
            source.MtimeUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture));
        HasJob = new ReactiveProperty<bool>()
            .DisposeWith(_disposables);
        JobStatus = new ReactiveProperty<string>()
            .DisposeWith(_disposables);
        JobProgressValue = new ReactiveProperty<double>()
            .DisposeWith(_disposables);
        JobProgressText = new ReactiveProperty<string>()
            .DisposeWith(_disposables);
        IsSelected = new ReactiveProperty<bool>()
            .DisposeWith(_disposables);
        IsSelected.Subscribe(_ => _owner.UpdateClipSummary())
            .DisposeWith(_disposables);
        GenerateCommand = new AsyncReactiveCommand()
            .WithSubscribe(() => _owner.GenerateAsync(this))
            .DisposeWith(_disposables);
        RegenerateCommand = new AsyncReactiveCommand()
            .WithSubscribe(() => _owner.RegenerateAsync(this))
            .DisposeWith(_disposables);
        DeleteCommand = new ReactiveCommand()
            .WithSubscribe(() => _owner.Delete(this))
            .DisposeWith(_disposables);
        CancelJobCommand = new ReactiveCommand()
            .WithSubscribe(() => _owner.CancelJob(this))
            .DisposeWith(_disposables);

        bool initialized = false;
        ProxyPreset previousPreset = preset;
        Preset.Subscribe(newPreset =>
            {
                if (initialized)
                {
                    // A change the user drove from the dropdown is an explicit choice, so the row stops
                    // tracking the global default and records the choice so a later Refresh preserves it.
                    // A programmatic ApplyDefaultPreset write (guarded by _applyingDefault) keeps the row
                    // following.
                    if (!_applyingDefault)
                    {
                        IsFollowingDefault = false;
                        IsExplicitlyChosen = true;
                    }

                    _owner.OnPresetChanged(this, previousPreset, newPreset);
                }

                previousPreset = newPreset;
            })
            .DisposeWith(_disposables);
        UpdateEntry(entry);
        initialized = true;
    }

    // Whether this row still tracks the global default preset. A row starts following when its preset
    // came from the default with no pre-existing proxy pinning it; it stops the moment the user picks a
    // preset from the dropdown, even one that equals the current default.
    public bool IsFollowingDefault { get; private set; }

    // Set once the user picks a preset from the dropdown. Unlike IsFollowingDefault (which a pinned proxy
    // also clears), this records only a deliberate user choice, so Refresh preserves it across a rebuild
    // while a merely-pinned row re-derives its follow state — a proxy that later disappears reverts the
    // row to following the default instead of leaving it stuck on the stale preset.
    public bool IsExplicitlyChosen { get; private set; }

    // Push a global-default change onto a following row without clearing IsFollowingDefault. Distinct
    // from a user edit so a row whose explicit choice happens to equal the old default is not swept along.
    internal void ApplyDefaultPreset(ProxyPreset newPreset)
    {
        if (Preset.Value == newPreset)
            return;

        _applyingDefault = true;
        try
        {
            Preset.Value = newPreset;
        }
        finally
        {
            _applyingDefault = false;
        }
    }

    public string FileName => System.IO.Path.GetFileName(Path);

    public string Path { get; }

    public ProxyFingerprint Source { get; }

    public ProxyFingerprint? EntrySource { get; private set; }

    public ReactiveProperty<ProxyPreset> Preset { get; }

    public ReactiveProperty<string> State { get; }

    public string SourceInfoText { get; }

    public ReactiveProperty<string> ProxyInfoText { get; }

    public ReactiveProperty<string> LastUsedText { get; }

    public ReactiveProperty<string?> FailureReason { get; }

    public ReactiveProperty<bool> IsReady { get; }

    public ReactiveProperty<bool> IsStale { get; }

    public ReactiveProperty<bool> IsFailed { get; }

    public ReactiveProperty<bool> IsMissing { get; }

    internal Guid? JobId { get; private set; }

    public ReactiveProperty<bool> HasJob { get; }

    public ReactiveProperty<string> JobStatus { get; }

    public ReactiveProperty<double> JobProgressValue { get; }

    public ReactiveProperty<string> JobProgressText { get; }

    public ReactiveProperty<bool> IsSelected { get; }

    public AsyncReactiveCommand GenerateCommand { get; }

    public AsyncReactiveCommand RegenerateCommand { get; }

    public ReactiveCommand DeleteCommand { get; }

    public ReactiveCommand CancelJobCommand { get; }

    internal void UpdateEntry(ProxyEntry? entry)
    {
        EntrySource = entry?.Source;
        ProxyState state = entry == null
            ? ProxyState.None
            : entry.Source == Source
                ? entry.State
                : ProxyState.Stale;
        FailureReason.Value = state == ProxyState.Failed ? entry?.FailureReason : null;
        State.Value = ProxiesTabViewModel.GetProxyStateText(state);
        IsReady.Value = state == ProxyState.Ready;
        IsStale.Value = state == ProxyState.Stale;
        IsFailed.Value = state == ProxyState.Failed;
        IsMissing.Value = state == ProxyState.None;
        ProxyInfoText.Value = entry == null
            ? Strings.ProxyMissingForSelectedPreset
            : string.Format(
                CultureInfo.CurrentCulture,
                Strings.ProxyInfoFormat,
                ProxiesTabViewModel.FormatSize(entry.OriginalLogicalFrameSize),
                ProxiesTabViewModel.FormatSize(entry.ProxyDecodedFrameSize),
                ProxiesTabViewModel.FormatBytes(entry.ProxyFileSizeBytes));
        LastUsedText.Value = entry == null
            ? string.Empty
            : string.Format(
                CultureInfo.CurrentCulture,
                Strings.ProxyLastUsedFormat,
                entry.LastUsedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture));
    }

    internal void UpdateJob(ProxyJob? job)
    {
        JobId = job?.JobId;
        HasJob.Value = job != null;
        JobStatus.Value = job == null
            ? string.Empty
            : ProxiesTabViewModel.GetJobStatusText(job);
        JobProgressValue.Value = job?.LatestProgress?.FractionComplete ?? 0;
        JobProgressText.Value = job?.LatestProgress is { } progress
            ? ProxiesTabViewModel.FormatProgress(progress.FractionComplete)
            : string.Empty;
    }

    public void ToggleSelection()
    {
        IsSelected.Value = !IsSelected.Value;
    }

    public void Dispose()
    {
        _disposables.Dispose();
    }
}
