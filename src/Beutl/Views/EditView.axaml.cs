using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Beutl.Views;

public sealed partial class EditView : UserControl
{
    public EditView()
    {
        InitializeComponent();

        this.GetObservable(IsKeyboardFocusWithinProperty)
            .Subscribe(v =>
            {
                var playerView = this.GetVisualDescendants().OfType<PlayerView>().FirstOrDefault();
                playerView?.Player.SetSeekBarOpacity(v ? 1 : 0.8);
            });
    }
}
