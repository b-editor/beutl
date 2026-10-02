using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor;
using Beutl.Editor.Observers;
using Beutl.Extensibility;
using Beutl.Graphics.Particles;
using Beutl.Language;
using Beutl.Testing.Headless;
using Beutl.ViewModels.Editors;
using Beutl.Views.Editors;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class ParticlePrewarmEditorTests
{
    [AvaloniaTest]
    [TestCase(320, false, "ja", "2")]
    [TestCase(480, true, "en", "500.00:00:00")]
    public void PrewarmDuration_CanBeEditedUndoneAndRedone(int width, bool light, string language, string unsupportedInput)
    {
        var originalCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var emitter = new ParticleEmitter();
        var sequence = new OperationSequenceGenerator();
        using var history = new HistoryManager(emitter, sequence);
        using var observer = new CoreObjectOperationObserver(null, emitter, sequence);
        using var subscription = history.Subscribe(observer);
        using var model = new PropertiesEditorViewModel(emitter, TestShell.Extensions, property =>
            property.Name is nameof(ParticleEmitter.PrewarmDuration) or nameof(ParticleEmitter.EmissionRate)
                or nameof(ParticleEmitter.Lifetime) or nameof(ParticleEmitter.LifetimeRandom));
        foreach (var context in model.Properties)
            context.Accept(new Services(history));

        var view = new PropertiesEditor { DataContext = model };
        var done = new Button { Content = "OK", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right };
        var window = new Window
        {
            Content = new StackPanel { Margin = new Thickness(12), Spacing = 12, Children = { view, done } },
            Width = width,
            Height = 240,
            RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark,
        };
        try
        {
            window.Show();
            HeadlessTestHelpers.Render(3);
            var editor = view.GetVisualDescendants().OfType<TimeSpanEditor>().Single();
            var input = editor.GetVisualDescendants().OfType<TextBox>().Single();
            Assert.That(editor.Header, Is.EqualTo(GraphicsStrings.ParticleEmitter_PrewarmDuration));
            Assert.That(editor.HoverInfo, Does.Contain("00:01:00"));
            Assert.That(editor.Value, Is.EqualTo(TimeSpan.Zero));
            Assert.That(input.Focus(), Is.True);
            input.Clear();
            window.KeyTextInput("00:00:02.2500000");
            Assert.That(done.Focus(), Is.True);
            HeadlessTestHelpers.Render(3);

            Assert.That(emitter.PrewarmDuration.CurrentValue, Is.EqualTo(TimeSpan.FromSeconds(2.25)));
            Assert.That(history.UndoCount, Is.EqualTo(1));
            Assert.That(history.Undo(), Is.True);
            HeadlessTestHelpers.Render(3);
            Assert.That(editor.Value, Is.EqualTo(TimeSpan.Zero));
            Assert.That(history.Redo(), Is.True);
            HeadlessTestHelpers.Render(3);
            Assert.That(editor.Value, Is.EqualTo(TimeSpan.FromSeconds(2.25)));
            Assert.That(input.Bounds.Width, Is.GreaterThan(0));
            Assert.That(input.TranslatePoint(default, view)!.Value.X + input.Bounds.Width,
                Is.LessThanOrEqualTo(view.Bounds.Width + 1));

            Assert.That(input.Focus(), Is.True);
            input.Clear();
            window.KeyTextInput(unsupportedInput);
            Assert.That(done.Focus(), Is.True);
            HeadlessTestHelpers.Render(3);
            Assert.That(emitter.PrewarmDuration.CurrentValue, Is.EqualTo(TimeSpan.Zero));
            Assert.That(editor.Value, Is.EqualTo(TimeSpan.Zero));
            Assert.That(input.Text, Is.EqualTo(editor.Value.ToString()));
            Assert.That(history.Undo(), Is.True);
            HeadlessTestHelpers.Render(3);
            Assert.That(editor.Value, Is.EqualTo(TimeSpan.FromSeconds(2.25)));
            Assert.That(input.Text, Is.EqualTo(editor.Value.ToString()));

            if (Environment.GetEnvironmentVariable("BEUTL_PARTICLE_PREWARM_CAPTURE") is { Length: > 0 } directory)
            {
                Directory.CreateDirectory(directory);
                using var image = window.CaptureRenderedFrame();
                Assert.That(image, Is.Not.Null);
                image!.Save(Path.Combine(directory, $"prewarm-{language}-{width}.png"), PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            window.Close();
            CultureInfo.CurrentUICulture = originalCulture;
        }
    }

    private sealed record Services(HistoryManager History) : IServiceProvider, IPropertyEditorContextVisitor
    {
        public object? GetService(Type serviceType) => serviceType == typeof(HistoryManager) ? History : null;
        public void Visit(IPropertyEditorContext context) { }
    }
}
