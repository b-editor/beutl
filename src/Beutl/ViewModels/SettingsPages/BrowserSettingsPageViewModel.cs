using System.ComponentModel;
using Avalonia.Threading;
using Beutl.Editor.Components.WebBrowserTab;
using Reactive.Bindings;

namespace Beutl.ViewModels.SettingsPages;

public sealed class BrowserSettingsPageViewModel : IDisposable, INotifyPropertyChanged
{
    private readonly BrowserProfile _profile;
    private bool _updating;
    private bool _disposed;
    private readonly CompositeDisposable _subscriptions = [];
    private readonly Func<Func<Task>?> _getClearCookies;
    private bool _canClearCookies;
    private readonly CancellationTokenSource _lifetime = new();

    public BrowserSettingsPageViewModel() : this(BrowserProfile.Default, CreateCookieClearAction) { }

    internal BrowserSettingsPageViewModel(BrowserProfile profile, Func<Func<Task>?> getClearCookies)
    {
        _profile = profile;
        _getClearCookies = getClearCookies;
        SelectedEngineIndex = new((int)profile.Engine);
        SuggestionsEnabled = new(profile.SuggestionsEnabled);
        RecordDownloads = new(profile.RecordDownloads);
        BlockAds = new(profile.BlockAds);
        FilterListUrls = new(string.Join(Environment.NewLine, profile.AdBlockListUrls));
        Feedback.Value = profile.Error;
        _subscriptions.Add(SelectedEngineIndex.Skip(1).Subscribe(_ => Save()));
        _subscriptions.Add(SuggestionsEnabled.Skip(1).Subscribe(_ => Save()));
        _subscriptions.Add(RecordDownloads.Skip(1).Subscribe(_ => Save()));
        _subscriptions.Add(BlockAds.Skip(1).Subscribe(_ => Save()));
        profile.SettingsChanged += Reload;
        profile.AdBlockFilters.Changed += OnFiltersChanged;
        RefreshFilterStatus();
        BrowserWebViewRegistry.Changed += RefreshCookieAvailability;
        RefreshCookieAvailability();
        if (profile.BlockAds) _ = LoadFiltersAsync();
    }

    public string[] Engines { get; } = ["Google", "Bing"];
    public ReactivePropertySlim<int> SelectedEngineIndex { get; }
    public ReactivePropertySlim<bool> SuggestionsEnabled { get; }
    public ReactivePropertySlim<bool> RecordDownloads { get; }
    public ReactivePropertySlim<bool> BlockAds { get; }
    public ReactivePropertySlim<string> FilterListUrls { get; }
    public ReactivePropertySlim<bool> IsUpdatingFilters { get; } = new();
    public ReactivePropertySlim<string?> FilterStatus { get; } = new();
    public ReactivePropertySlim<string?> FilterFeedback { get; } = new();
    public ReactivePropertySlim<bool> CookieDeletionConfirmed { get; } = new();
    public ReactivePropertySlim<bool> IsClearingCookies { get; } = new();
    public ReactivePropertySlim<string?> Feedback { get; } = new();
    public bool CanClearCookies => _canClearCookies;
    public event PropertyChangedEventHandler? PropertyChanged;

    private void RefreshCookieAvailability()
    {
        if (_disposed) return;
        bool available = _getClearCookies() != null;
        if (_canClearCookies == available) return;
        _canClearCookies = available;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanClearCookies)));
    }

    private void Save()
    {
        if (_updating || SelectedEngineIndex.Value is < 0 or > 1) return;
        bool saved = _profile.UpdateSettings((BrowserSearchEngine)SelectedEngineIndex.Value,
            SuggestionsEnabled.Value, RecordDownloads.Value, BlockAds.Value);
        if (!saved) Reload();
        Feedback.Value = saved ? null : string.Format(Strings.BrowserStorageError, _profile.Error);
        if (saved && BlockAds.Value) _ = LoadFiltersAsync();
    }

    private void Reload()
    {
        _updating = true;
        try
        {
            SelectedEngineIndex.Value = (int)_profile.Engine;
            SuggestionsEnabled.Value = _profile.SuggestionsEnabled;
            RecordDownloads.Value = _profile.RecordDownloads;
            BlockAds.Value = _profile.BlockAds;
        }
        finally { _updating = false; }
    }

    private void OnFiltersChanged() => Dispatcher.UIThread.Post(RefreshFilterStatus);

    private void RefreshFilterStatus()
    {
        if (_disposed) return;
        var store = _profile.AdBlockFilters;
        FilterStatus.Value = store.Current is { } rules
            ? string.Format(Strings.BrowserAdBlockFilterStatus, rules.SupportedCount, rules.UnsupportedCount, store.UpdatedAt?.ToLocalTime().ToString("g"))
            : Strings.BrowserAdBlockNotDownloaded;
    }

    public async Task UpdateFiltersAsync()
    {
        if (_disposed || IsUpdatingFilters.Value) return;
        IsUpdatingFilters.Value = true;
        FilterFeedback.Value = Strings.BrowserAdBlockLoading;
        try
        {
            string[] urls = BrowserAdBlockFilterStore.ParseUrls(FilterListUrls.Value);
            if (!_profile.UpdateAdBlockListUrls(urls))
            {
                FilterFeedback.Value = string.Format(Strings.BrowserStorageError, _profile.Error);
                return;
            }
            await _profile.AdBlockFilters.UpdateAsync(urls, _lifetime.Token);
            if (!_disposed)
            {
                FilterListUrls.Value = string.Join(Environment.NewLine, urls);
                RefreshFilterStatus();
                FilterFeedback.Value = Strings.BrowserAdBlockUpdated;
            }
        }
        catch (OperationCanceledException) when (_disposed) { }
        catch (Exception ex)
        {
            if (!_disposed) FilterFeedback.Value = string.Format(Strings.BrowserAdBlockUpdateFailed, ex.Message);
        }
        finally { if (!_disposed) IsUpdatingFilters.Value = false; }
    }

    private async Task LoadFiltersAsync()
    {
        if (_disposed || IsUpdatingFilters.Value || _profile.AdBlockFilters.Current != null) return;
        IsUpdatingFilters.Value = true;
        FilterStatus.Value = Strings.BrowserAdBlockLoading;
        try
        {
            await _profile.AdBlockFilters.GetAsync(_profile.AdBlockListUrls);
            if (!_disposed) RefreshFilterStatus();
        }
        catch (Exception ex)
        {
            if (!_disposed)
            {
                RefreshFilterStatus();
                FilterFeedback.Value = string.Format(Strings.BrowserAdBlockUpdateFailed, ex.Message);
            }
        }
        finally { if (!_disposed) IsUpdatingFilters.Value = false; }
    }

    public void ClearHistory()
    {
        Feedback.Value = _profile.ClearHistory() ? Strings.BrowserHistoryCleared
            : string.Format(Strings.BrowserStorageError, _profile.Error);
    }

    public async Task ClearCookiesAsync()
    {
        if (_disposed || IsClearingCookies.Value) return;
        RefreshCookieAvailability();
        Func<Task>? clearCookies = _getClearCookies();
        if (clearCookies == null) { Feedback.Value = Strings.BrowserCookiesUnsupported; return; }
        if (!CookieDeletionConfirmed.Value) { Feedback.Value = Strings.BrowserCookieWarning; return; }
        IsClearingCookies.Value = true;
        try
        {
            await clearCookies();
            if (_disposed) return;
            Feedback.Value = Strings.BrowserCookiesCleared;
            CookieDeletionConfirmed.Value = false;
        }
        catch (Exception ex) { if (!_disposed) Feedback.Value = ex.Message; }
        finally { if (!_disposed) IsClearingCookies.Value = false; }
    }

    private static Func<Task>? CreateCookieClearAction()
    {
        if (BrowserWebViewRegistry.GetCookieManager() == null) return null;
        return async () =>
        {
            var manager = BrowserWebViewRegistry.GetCookieManager()
                ?? throw new InvalidOperationException(Strings.BrowserCookiesUnsupported);
            foreach (var cookie in await manager.GetCookiesAsync())
                manager.DeleteCookie(cookie);
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _lifetime.Dispose();
        _profile.AdBlockFilters.Changed -= OnFiltersChanged;
        BrowserWebViewRegistry.Changed -= RefreshCookieAvailability;
        _profile.SettingsChanged -= Reload;
        _subscriptions.Dispose();
        SelectedEngineIndex.Dispose();
        SuggestionsEnabled.Dispose();
        RecordDownloads.Dispose();
        BlockAds.Dispose();
        FilterListUrls.Dispose();
        IsUpdatingFilters.Dispose();
        FilterStatus.Dispose();
        FilterFeedback.Dispose();
        CookieDeletionConfirmed.Dispose();
        IsClearingCookies.Dispose();
        Feedback.Dispose();
    }
}
