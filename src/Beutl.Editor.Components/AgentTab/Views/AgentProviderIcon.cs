using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Beutl.Agents;

namespace Beutl.Editor.Components.AgentTab.Views;

internal sealed class AgentProviderIcon : PathIcon
{
    private static readonly SolidColorBrush s_claudeBrandBrush = new(Color.FromRgb(0xD9, 0x77, 0x57));

    public static readonly StyledProperty<AgentProviderKind> KindProperty =
        AvaloniaProperty.Register<AgentProviderIcon, AgentProviderKind>(nameof(Kind), AgentProviderKind.Acp);

    public AgentProviderIcon()
    {
        Classes.Add("agent-provider-glyph");
        Resources["AgentClaudeBrandBrush"] = s_claudeBrandBrush;
    }

    public AgentProviderKind Kind
    {
        get => GetValue(KindProperty);
        set => SetValue(KindProperty, value);
    }

    protected override Type StyleKeyOverride => typeof(PathIcon);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == KindProperty)
        {
            PseudoClasses.Set(":codex", Kind == AgentProviderKind.Codex);
            PseudoClasses.Set(":claude", Kind == AgentProviderKind.ClaudeCode);
        }
    }
}
