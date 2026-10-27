using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Beutl.Editor.PropertyEditors;
using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.Language;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public sealed class ShaderGraphEffectEditorTests
{
    [AvaloniaTest]
    [TestCase("en")]
    [TestCase("ja")]
    public void BuiltInPropertiesHaveEditorsAndLocalizedLabels(string language)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var models = new List<PropertyEditorViewModel>();
        var panel = new StackPanel();
        var window = new Window { Width = 320, Height = 510, Content = panel };
        try
        {
            var bloom = new Bloom();
            panel.Children.Add(new TextBlock { Text = GraphicsStrings.Bloom });
            Add(bloom, bloom.Radius);
            Add(bloom, bloom.Threshold);
            Add(bloom, bloom.SoftKnee);
            Add(bloom, bloom.Intensity);
            Add(bloom, bloom.PreserveAlpha);
            var refraction = new WaveRefraction();
            panel.Children.Add(new TextBlock { Text = GraphicsStrings.WaveRefraction });
            Add(refraction, refraction.Amount);
            Add(refraction, refraction.Wavelength);
            Add(refraction, refraction.Phase);
            window.Show();
            HeadlessTestHelpers.Render(3);
            Assert.That(panel.Children, Has.All.Matches<Control>(control => control is { Bounds.Width: > 0, Bounds.Height: > 0 }));
            Assert.That(GraphicsStrings.WaveRefraction, Is.EqualTo(language == "ja" ? "波紋屈折" : "Wave refraction"));
            Assert.That(models[1].Header, Is.EqualTo(language == "ja" ? "しきい値" : "Threshold"));
            Assert.That(models[2].Header, Is.EqualTo(language == "ja" ? "滑らかさ (%)" : "Softness (%)"));
            Assert.That(models[2].Description.Value, Is.Not.Null.And.Not.Empty);
            if (Environment.GetEnvironmentVariable("BEUTL_SHADER_GRAPH_CAPTURE") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var image = window.CaptureRenderedFrame();
                Assert.That(image, Is.Not.Null);
                image!.Save(Path.Combine(directory, $"effects-{language}.png"), PngBitmapEncoderOptions.Default);
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
