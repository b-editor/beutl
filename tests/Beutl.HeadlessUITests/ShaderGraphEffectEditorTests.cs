using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Beutl.Editor.PropertyEditors;
using Beutl.Engine;
using Beutl.Graphics;
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
    [TestCase("en", false)]
    [TestCase("ja", false)]
    [TestCase("en", true)]
    [TestCase("ja", true)]
    public void BuiltInPropertiesHaveEditorsAndLocalizedLabels(string language, bool lightRays)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var models = new List<PropertyEditorViewModel>();
        var panel = new StackPanel();
        var window = new Window { Width = 320, Height = 510, Content = panel };
        try
        {
            if (lightRays)
            {
                var rays = new LightRays { Center = { Value = new(-0.25f, 0.5f, RelativeUnit.Relative) } };
                panel.Children.Add(new TextBlock { Text = GraphicsStrings.LightRays });
                Add(rays, rays.Center);
                Add(rays, rays.Length);
                Add(rays, rays.Decay);
                Add(rays, rays.Intensity);
                Add(rays, rays.Threshold);
                Add(rays, rays.SoftKnee);
            }
            else
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
            }
            window.Show();
            HeadlessTestHelpers.Render(3);
            Assert.That(panel.Children, Has.All.Matches<Control>(control => control is { Bounds.Width: > 0, Bounds.Height: > 0 }));
            if (lightRays)
            {
                Assert.That(GraphicsStrings.LightRays, Is.EqualTo(language == "ja" ? "光線" : "Light rays"));
                Assert.That(models[1].Header, Is.EqualTo(language == "ja" ? "長さ (%)" : "Length (%)"));
            }
            else
            {
                Assert.That(GraphicsStrings.WaveRefraction, Is.EqualTo(language == "ja" ? "波紋屈折" : "Wave refraction"));
                Assert.That(models[1].Header, Is.EqualTo(language == "ja" ? "しきい値" : "Threshold"));
            }
            PropertyEditorViewModel softness = models[lightRays ? 5 : 2];
            Assert.That(softness.Header, Is.EqualTo(language == "ja" ? "滑らかさ (%)" : "Softness (%)"));
            Assert.That(softness.Description.Value, Is.Not.Null.And.Not.Empty);
            if (Environment.GetEnvironmentVariable("BEUTL_SHADER_GRAPH_CAPTURE") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var image = window.CaptureRenderedFrame();
                Assert.That(image, Is.Not.Null);
                image!.Save(Path.Combine(directory, $"effects{(lightRays ? "-rays" : "")}-{language}.png"), PngBitmapEncoderOptions.Default);
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
