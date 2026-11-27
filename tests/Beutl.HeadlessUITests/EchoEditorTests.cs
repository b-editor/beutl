using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.PropertyEditors;
using Beutl.Graphics.Effects;
using Beutl.Language;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
[NonParallelizable]
public sealed class EchoEditorTests
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
        var window = new Window { Width = 320, Height = 320, Content = panel };
        try
        {
            var echo = new Echo
            {
                Interval = { Value = -0.25f },
                Count = { Value = 7 },
                StartingIntensity = { Value = 80 },
                Decay = { Value = 65 },
                BlendMode = { Value = EchoBlendMode.NewestOnTop },
            };
            var title = new TextBlock { Text = GraphicsStrings.Echo };
            panel.Children.Add(title);
            var interval = (NumberEditor<float>)Add(echo.Interval);
            var count = (NumberEditor<int>)Add(echo.Count);
            var intensity = (NumberEditor<float>)Add(echo.StartingIntensity);
            var decay = (NumberEditor<float>)Add(echo.Decay);
            var blendMode = (EnumEditor)Add(echo.BlendMode);
            window.Show();
            HeadlessTestHelpers.Render(3);

            bool ja = language == "ja";
            string[] headers = ja
                ? ["間隔 (秒)", "個数", "初期強度 (%)", "減衰 (%)", "ブレンドモード"]
                : ["Interval (s)", "Count", "Start Intensity (%)", "Decay (%)", "Blend Mode"];
            using (Assert.EnterMultipleScope())
            {
                Assert.That(title.Text, Is.EqualTo(ja ? "エコー" : "Echo"));
                Assert.That(panel.Children, Has.All.Matches<Control>(child =>
                    child is { Bounds.Width: > 0, Bounds.Height: > 0 }
                    && child.Bounds.Right <= panel.Bounds.Width && child.Bounds.Bottom <= panel.Bounds.Height));
                Assert.That(models.Select(model => model.Header), Is.EqualTo(headers));
                Assert.That(models.Select(model => model.Description.Value), Has.All.Not.Null.And.Not.Empty);
                Assert.That(interval.Value, Is.EqualTo(-0.25f));
                Assert.That(count.Value, Is.EqualTo(7));
                Assert.That(intensity.Value, Is.EqualTo(80));
                Assert.That(decay.Value, Is.EqualTo(65));
                Assert.That(blendMode.SelectedIndex, Is.EqualTo((int)EchoBlendMode.NewestOnTop));
                Assert.That(blendMode.Items!.Select(item => item.DisplayName), Has.Exactly(2).Items.And.All.Not.Empty);
            }

            if (Environment.GetEnvironmentVariable("BEUTL_ECHO_CAPTURE") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var image = window.CaptureRenderedFrame();
                Assert.That(image, Is.Not.Null);
                image!.Save(Path.Combine(directory, $"echo-{language}.png"), PngBitmapEncoderOptions.Default);
            }

            Control Add(IProperty property)
            {
                var (model, control) = PropertyEditorTestHost.CreateControl(PropertyAdapterFactory.CreateAdapter(property, echo));
                models.Add(model);
                panel.Children.Add(control);
                return control;
            }
        }
        finally
        {
            window.Close();
            foreach (PropertyEditorViewModel model in models) model.Dispose();
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
