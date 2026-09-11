using System.ComponentModel;
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

    public BrowserSettingsPageViewModel() : this(BrowserProfile.Default, CreateCookieClearAction) { }

    internal BrowserSettingsPageViewModel(BrowserProfile profile, Func<Func<Task>?> getClearCookies)
    {
        _profile = profile;
        _getClearCookies = getClearCookies;
        SelectedEngineIndex = new((int)profile.Engine);
        SuggestionsEnabled = new(profile.SuggestionsEnabled);
        RecordDownloads = new(profile.RecordDownloads);
        Feedback.Value = profile.Error;
        _subscriptions.Add(SelectedEngineIndex.Skip(1).Subscribe(_ => Save()));
        _subscriptions.Add(SuggestionsEnabled.Skip(1).Subscribe(_ => Save()));
        _subscriptions.Add(RecordDownloads.Skip(1).Subscribe(_ => Save()));
        profile.SettingsChanged += Reload;
        BrowserWebViewRegistry.Changed += RefreshCookieAvailability;
        RefreshCookieAvailability();
    }

    public string[] Engines { get; } = ["Google", "Bing"];
    public ReactivePropertySlim<int> SelectedEngineIndex { get; }
    public ReactivePropertySlim<bool> SuggestionsEnabled { get; }
    public ReactivePropertySlim<bool> RecordDownloads { get; }
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
            SuggestionsEnabled.Value, RecordDownloads.Value);
        if (!saved) Reload();
        Feedback.Value = saved ? null : string.Format(Strings.BrowserStorageError, _profile.Error);
    }

    private void Reload()
    {
        _updating = true;
        try
        {
            SelectedEngineIndex.Value = (int)_profile.Engine;
            SuggestionsEnabled.Value = _profile.SuggestionsEnabled;
            RecordDownloads.Value = _profile.RecordDownloads;
        }
        finally { _updating = false; }
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
        BrowserWebViewRegistry.Changed -= RefreshCookieAvailability;
        _profile.SettingsChanged -= Reload;
        _subscriptions.Dispose();
        SelectedEngineIndex.Dispose();
        SuggestionsEnabled.Dispose();
        RecordDownloads.Dispose();
        CookieDeletionConfirmed.Dispose();
        IsClearingCookies.Dispose();
        Feedback.Dispose();
    }
}
