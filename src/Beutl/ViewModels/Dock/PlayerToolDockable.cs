using Dock.Model.Inpc.Controls;

namespace Beutl.ViewModels.Dock;

public class PlayerToolDockable : Tool
{
    public PlayerToolDockable(PlayerViewModel player, string title)
    {
        Id = "Player";
        Title = title;
        Context = player;
        Player = player;
        CanClose = false;
        CanPin = false;
        CanFloat = true;
    }

    public PlayerViewModel Player { get; }
}
