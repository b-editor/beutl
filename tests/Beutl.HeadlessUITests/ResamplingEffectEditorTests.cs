using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Beutl.Audio.Effects;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.PropertyEditors;
using Beutl.Language;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public sealed class ResamplingEffectEditorTests
{
    [AvaloniaTest]
    [TestCase("en")]
    [TestCase("ja")]
    public void MethodIsUsableAtNarrowWidth(string language)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        PropertyEditorViewModel? model = null;
        var panel = new StackPanel();
        // Tall enough for the open list to show every method.
        var window = new Window { Width = 320, Height = 280, Content = panel };
        try
        {
            var effect = new ResamplingEffect { Method = { Value = ResamplingMethod.Sinc64 } };
            panel.Children.Add(new TextBlock { Text = AudioStrings.ResamplingEffect });
            var (editor, control) = PropertyEditorTestHost.CreateControl(PropertyAdapterFactory.CreateAdapter(effect.Method, effect));
            model = editor;
            panel.Children.Add(control);
            window.Show();
            HeadlessTestHelpers.Render(3);
            var methods = (EnumEditor)control;
            bool ja = language == "ja";
            Assert.Multiple(() =>
            {
                Assert.That(panel.Children, Has.All.Matches<Control>(c => c is { Bounds.Width: > 0, Bounds.Height: > 0 }));
                Assert.That(editor.Header, Is.EqualTo(ja ? "方式" : "Method"));
                Assert.That(editor.Description.Value, Is.Not.Null.And.Not.Empty);
                Assert.That(methods.SelectedIndex, Is.EqualTo((int)ResamplingMethod.Sinc64));
                // The names themselves are checked in ResamplingEffectTests: the editor caches each one for the rest of
                // the process, in whichever language a test resolved it first.
                Assert.That(methods.Items!.Select(item => item.DisplayName), Has.Exactly(6).Items.And.All.Not.Empty);
            });
            if (Environment.GetEnvironmentVariable("BEUTL_RESAMPLING_CAPTURE") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var image = window.CaptureRenderedFrame();
                Assert.That(image, Is.Not.Null);
                image!.Save(Path.Combine(directory, $"resampling-{language}.png"), PngBitmapEncoderOptions.Default);
                methods.GetVisualDescendants().OfType<ComboBox>().Single().IsDropDownOpen = true;
                HeadlessTestHelpers.Render(3);
                using var popup = TopLevel.GetTopLevel(window.OpenedPopups.Last().Child!)!.CaptureRenderedFrame();
                popup!.Save(Path.Combine(directory, $"resampling-methods-{language}.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            window.Close();
            model?.Dispose();
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
