using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Beutl.Controls.PropertyEditors;
using Beutl.Graphics.Effects;
using Beutl.Language;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public sealed class PosterizeTimeEditorTests
{
    [AvaloniaTest]
    [TestCase("en", "Posterize Time", "Rate (fps)")]
    [TestCase("ja", "時間のポスタリゼーション", "レート (fps)")]
    public void FractionalFrameRateCanBeEditedAtNarrowWidth(string language, string effectName, string propertyName)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        try
        {
            var effect = new PosterizeTime();
            using var history = new PropertyEditorTestHost.HistoryScope(effect);
            var (model, control) = PropertyEditorTestHost.CreateControl(PropertyAdapterFactory.CreateAdapter(effect.FrameRate, effect));
            using var propertyModel = model;
            model.Accept(PropertyEditorTestHost.ServiceVisitor.For(history.History));
            var editor = (NumberEditor<float>)control;
            var title = new TextBlock { Text = GraphicsStrings.PosterizeTime, Focusable = true };
            var panel = new StackPanel { Children = { title, editor } };
            using var host = new ControlHost(panel, width: 280, height: 120);
            HeadlessTestHelpers.Render(3);
            Assert.That(editor.Value, Is.EqualTo(12f));

            TextBox? field = editor.FindDescendantOfType<TextBox>(false, box => box.Name == "PART_InnerTextBox");
            Assert.That(field, Is.Not.Null);
            Assert.That(field!.Focus(), Is.True);
            field.SelectAll();
            string input = 0.01f.ToString(CultureInfo.CurrentCulture);
            host.Window.KeyTextInput(input);
            Assert.That(title.Focus(), Is.True);
            HeadlessTestHelpers.Render(3);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(title.Text, Is.EqualTo(effectName));
                Assert.That(model.Header, Is.EqualTo(propertyName));
                Assert.That(model.Description.Value, Is.Not.Empty);
                Assert.That(editor.Bounds.Right, Is.LessThanOrEqualTo(panel.Bounds.Width));
                Assert.That(field.Text, Is.EqualTo(input));
                Assert.That(effect.FrameRate.Value, Is.EqualTo(0.01f));
            }

            if (Environment.GetEnvironmentVariable("BEUTL_POSTERIZE_TIME_CAPTURE") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var image = host.Window.CaptureRenderedFrame();
                Assert.That(image, Is.Not.Null);
                image!.Save(Path.Combine(directory, $"posterize-time-{language}.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
