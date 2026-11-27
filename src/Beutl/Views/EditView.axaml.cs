using Avalonia.Controls;
using Avalonia.Input;
using Beutl.Controls;
using Beutl.ViewModels;

namespace Beutl.Views;

public sealed partial class EditView : UserControl
{
    public EditView()
    {
        InitializeComponent();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (DataContext is EditViewModel editor)
            editor.Player.HandleEffectComparisonEscape(e);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        // The editor's controls show the fonts of its scene's fork, including its project's bundled fonts.
        if (DataContext is EditViewModel editor)
            ControlFonts.SetFonts(this, editor.GetService(typeof(Beutl.Media.FontManager)) as Beutl.Media.FontManager);
        else
            ClearValue(ControlFonts.FontsProperty);
    }
}
