using Beutl.Configuration;

namespace Beutl.ViewModels.SettingsPages;

public sealed class TerminalSettingsPageViewModel(TerminalConfig config)
{
    public TerminalConfig Config { get; } = config;
}
