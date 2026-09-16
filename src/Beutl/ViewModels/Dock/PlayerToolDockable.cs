using Dock.Model.Inpc.Controls;
using FluentAvalonia.UI.Controls;
using FluentIconSource = FluentIcons.Avalonia.Fluent.FluentIconSource;

namespace Beutl.ViewModels.Dock;

public class PlayerToolDockable : Tool
{
    public PlayerToolDockable(PlayerViewModel player, string title)
    {
        Id = "Player";
        Title = title;
        // Fully qualified: the usual `Icon` alias for the enum would be shadowed by this type's
        // own Icon property.
        Icon = new FluentIconSource { Icon = FluentIcons.Common.Icon.Play };
        Context = player;
        Player = player;
        CanClose = false;
        CanPin = false;
        CanFloat = true;
        CanDockAsDocument = false;
    }

    public PlayerViewModel Player { get; }

    /// <summary>Gets the icon the tab strip shows left of the title.</summary>
    public FAIconSource Icon { get; }
}
