using Beutl.Configuration;
using Reactive.Bindings;

namespace Beutl.ViewModels.SettingsPages;

public sealed class VersionControlSettingsPageViewModel : IDisposable
{
    private readonly CompositeDisposable _disposables = [];

    public VersionControlSettingsPageViewModel(VersionControlConfig config)
    {
        EnableVersionControlForNewProjects = SettingsPageBinding.Bind(config.EnableForNewProjects, _disposables);
        AutoCommitOnSave = SettingsPageBinding.Bind(config.AutoCommitOnSave, _disposables);
        AutoCommitOnClose = SettingsPageBinding.Bind(config.AutoCommitOnClose, _disposables);
        UseLfsWhenAvailable = SettingsPageBinding.Bind(config.UseLfsWhenAvailable, _disposables);

        GitExecutablePath = config.GitExecutablePath.GetObservable()
            .Select(static value => value ?? string.Empty)
            .ToReactiveProperty(initialValue: string.Empty)
            .DisposeWith(_disposables);
        GitExecutablePath.Subscribe(value =>
            {
                string? normalized = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                if (config.GitExecutablePath.Value != normalized)
                {
                    config.GitExecutablePath.Value = normalized;
                }
            })
            .DisposeWith(_disposables);

        LargeMediaWarningThresholdMb = config.LargeMediaWarningThresholdMb.GetObservable()
            .ToReactiveProperty()
            .DisposeWith(_disposables);
        LargeMediaWarningThresholdMb.Subscribe(value =>
            {
                int normalized = Math.Max(1, value);
                if (config.LargeMediaWarningThresholdMb.Value != normalized)
                {
                    config.LargeMediaWarningThresholdMb.Value = normalized;
                }

                if (LargeMediaWarningThresholdMb.Value != normalized)
                {
                    LargeMediaWarningThresholdMb.Value = normalized;
                }
            })
            .DisposeWith(_disposables);
    }

    public ReactiveProperty<bool> EnableVersionControlForNewProjects { get; }

    public ReactiveProperty<bool> AutoCommitOnSave { get; }

    public ReactiveProperty<bool> AutoCommitOnClose { get; }

    public ReactiveProperty<string> GitExecutablePath { get; }

    public ReactiveProperty<bool> UseLfsWhenAvailable { get; }

    public ReactiveProperty<int> LargeMediaWarningThresholdMb { get; }

    public void Dispose() => _disposables.Dispose();
}
