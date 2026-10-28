using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Media;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using static Beutl.HeadlessUITests.PropertyEditorTestHost;
using TextBlock = Beutl.Graphics.Shapes.TextBlock;

namespace Beutl.HeadlessUITests;

public class VerticalTextEditorTests
{
    [AvaloniaTest]
    [TestCase(300)]
    [TestCase(440)]
    public void WritingModeEditor_RendersAndSupportsUndo(int width)
    {
        // Display names are cached for the process lifetime, matching the app's restart-on-language-change behavior.
        string culture = Environment.GetEnvironmentVariable("BEUTL_VERTICAL_TEST_CULTURE") ?? "ja";
        CultureInfo previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        var block = new TextBlock();
        var (vm, control) = CreateControl(new EnginePropertyAdapter<WritingMode>(block.WritingMode, block));
        using var lifetime = vm;
        using var history = new HistoryScope(block);
        vm.Accept(ServiceVisitor.For(history.History));
        var editor = (EnumEditor)control;
        var scope = new StackPanel { Children = { control } };
        PropertyEditorGrid.SetIsAlignmentScope(scope, true);
        var window = new Window { Content = scope, Width = width, Height = 100 };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            editor.RaiseEvent(new PropertyEditorValueChangedEventArgs<int>(1, 0, PropertyEditor.ValueConfirmedEvent));
            HeadlessTestHelpers.Render(3);
            Assert.That(block.WritingMode.Value, Is.EqualTo(WritingMode.VerticalRl));
            Assert.That(editor.Items![1].DisplayName, Does.Contain(culture == "ja" ? "縦書き" : "Vertical"));
            if (Environment.GetEnvironmentVariable("BEUTL_VERTICAL_CAPTURE_DIR") is { } directory)
            {
                using var frame = window.CaptureRenderedFrame();
                Assert.That(frame, Is.Not.Null);
                frame!.Save(Path.Combine(directory, $"vertical-editor-{culture}-{width}.png"), PngBitmapEncoderOptions.Default);
            }
            Assert.That(history.History.Undo(), Is.True);
            Assert.That(block.WritingMode.Value, Is.EqualTo(WritingMode.HorizontalTb));
            Assert.That(history.History.Redo(), Is.True);
            Assert.That(block.WritingMode.Value, Is.EqualTo(WritingMode.VerticalRl));
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
