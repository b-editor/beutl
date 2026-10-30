using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.PropertyEditors;
using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.Language;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public sealed class MotionBlurEditorTests
{
    [AvaloniaTest]
    [TestCase("en")]
    [TestCase("ja")]
    public void SettingsAreUsableAtNarrowWidth(string language)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var models = new List<PropertyEditorViewModel>();
        var panel = new StackPanel();
        var window = new Window { Width = 320, Height = 280, Content = panel };
        try
        {
            var blur = new MotionBlur
            {
                ShutterAngle = { Value = 720 },
                ShutterPosition = { Value = ShutterPosition.End },
                SourceMix = { Value = 35 },
            };
            panel.Children.Add(new TextBlock { Text = GraphicsStrings.MotionBlur });
            Add(blur, blur.ShutterAngle);
            Add(blur, blur.ShutterPosition);
            Add(blur, blur.Samples);
            Add(blur, blur.SourceMix);
            window.Show();
            HeadlessTestHelpers.Render(3);
            Assert.Multiple(() =>
            {
                Assert.That(panel.Children, Has.All.Matches<Control>(c => c is { Bounds.Width: > 0, Bounds.Height: > 0 }));
                Assert.That(models[0].Header, Is.EqualTo(language == "ja" ? "シャッター角 (°)" : "Shutter angle (°)"));
                Assert.That(models[2].Header, Is.EqualTo(language == "ja" ? "サンプル数" : "Samples"));
                Assert.That(models[3].Header, Is.EqualTo(language == "ja" ? "元画像ミックス (%)" : "Source mix (%)"));
                Assert.That(((NumberEditor<float>)panel.Children[1]).Value, Is.EqualTo(720));
                Assert.That(((NumberEditor<float>)panel.Children[4]).Value, Is.EqualTo(35));
                Assert.That(models.Select(m => m.Description.Value), Has.All.Not.Null.And.Not.Empty);
            });
            if (Environment.GetEnvironmentVariable("BEUTL_MOTION_BLUR_CAPTURE") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var image = window.CaptureRenderedFrame();
                Assert.That(image, Is.Not.Null);
                image!.Save(Path.Combine(directory, $"motion-blur-{language}.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            window.Close();
            foreach (PropertyEditorViewModel model in models) model.Dispose();
            CultureInfo.CurrentUICulture = previous;
        }

        void Add(EngineObject effect, IProperty property)
        {
            var (model, control) = PropertyEditorTestHost.CreateControl(PropertyAdapterFactory.CreateAdapter(property, effect));
            models.Add(model);
            panel.Children.Add(control);
        }
    }
}
