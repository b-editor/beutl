
using Beutl.Api;
using Beutl.Api.Objects;

using BeUtl.Configuration;

using Reactive.Bindings;

namespace BeUtl.ViewModels.SettingsPages;

public sealed class StorageSettingsPageViewModel
{
    private readonly BackupConfig _config;
    private readonly IReadOnlyReactiveProperty<AuthorizedUser?> _user;
    private readonly ReactivePropertySlim<StorageUsageResponse?> _storageUsageResponse = new();

    public StorageSettingsPageViewModel(IReadOnlyReactiveProperty<AuthorizedUser?> user)
    {
        _config = GlobalConfiguration.Instance.BackupConfig;
        BackupSettings = _config.GetObservable(BackupConfig.BackupSettingsProperty).ToReactiveProperty();
        BackupSettings.Subscribe(b => _config.BackupSettings = b);

        Percent = _storageUsageResponse.Where(x => x != null)
            .Select(x => x!.Size / (double)x.Max_size)
            .ToReadOnlyReactivePropertySlim();
        
        MaxSize = _storageUsageResponse.Where(x => x != null)
            .Select(x => ToHumanReadableSize(x!.Max_size))
            .ToReadOnlyReactivePropertySlim()!;
        
        Using = _storageUsageResponse.Where(x => x != null)
            .Select(x => ToHumanReadableSize(x!.Size))
            .ToReadOnlyReactivePropertySlim()!;

        Remaining = _storageUsageResponse.Where(x => x != null)
            .Select(x => ToHumanReadableSize(x!.Max_size - x.Size))
            .ToReadOnlyReactivePropertySlim()!;

        _user = user;
        _user.Skip(1).Subscribe(async _ =>
        {
            if (Refresh.CanExecute())
            {
                await Refresh.ExecuteAsync();
            }
        });
        Refresh.Subscribe(async () =>
        {
            if (_user.Value == null)
                return;

            try
            {
                IsBusy.Value = true;
                await _user.Value.RefreshAsync();
                _storageUsageResponse.Value = await _user.Value.StorageUsageAsync();
            }
            catch
            {
                // Todo
            }
            finally
            {
                IsBusy.Value = false;
            }
        });

        _user.Where(x => x != null)
            .Timeout(TimeSpan.FromSeconds(10))
            .Subscribe(
                _ => Refresh.Execute(),
                ex => { },
                () => { });
    }

    public ReactiveProperty<bool> BackupSettings { get; }

    public ReadOnlyReactivePropertySlim<double> Percent { get; }

    public ReadOnlyReactivePropertySlim<string> MaxSize { get; }

    public ReadOnlyReactivePropertySlim<string> Using { get; }
    
    public ReadOnlyReactivePropertySlim<string> Remaining { get; }

    public ReactivePropertySlim<bool> IsBusy { get; } = new();

    public AsyncReactiveCommand Refresh { get; } = new();

    // https://teratail.com/questions/136799#reply-207332
    private static string ToHumanReadableSize(double size, int scale = 0, int standard = 1024)
    {
        string[] unit = new[] { "B", "KB", "MB", "GB" };
        if (scale == unit.Length - 1 || size <= standard) { return $"{size:F} {unit[scale]}"; }
        return ToHumanReadableSize(size / standard, scale + 1, standard);
    }
}
