using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.VisualTree;
using Beutl.Controls;
using Beutl.Editor.Components.GraphEditorTab;
using Beutl.Language;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

// Labels the user reads come from the translated strings, not English literals.
[TestFixture]
public sealed class LocalizedLabelTests
{
    // The tab switcher shows the display name as the tooltip of an open tab.
    [Test]
    public void Graph_editor_tab_has_a_translated_display_name()
    {
        Assert.That(GraphEditorTabExtension.Instance.DisplayName, Is.EqualTo(Strings.GraphEditor));
    }

    [AvaloniaTest]
    public void Loop_button_has_a_translated_tooltip()
    {
        var player = new Player();
        var window = new Window { Content = player, Width = 640, Height = 360 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render();
            ToggleButton loop = player.GetVisualDescendants().OfType<ToggleButton>()
                .Single(button => button.Name == "PART_LoopButton");
            Assert.That(ToolTip.GetTip(loop), Is.EqualTo(Strings.ToggleLoop));
        }
        finally
        {
            window.Close();
        }
    }
}
