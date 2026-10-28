using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Editor;
using Beutl.Editor.Components.TimelineTab.ViewModels;
using Beutl.Editor.Components.TimelineTab.Views;
using Beutl.Editor.Observers;
using Beutl.Engine;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class SilenceSplitDialogTests
{
    [AvaloniaTest]
    public void ToolsMenu_ExposesSilenceSplit()
    {
        var main = TestShell.MainViewModel;
        var tools = main.MenuItems.Single(item => item.Header == Strings.Tools);
        Assert.That(tools.Items!.Single(item => item.Header == Strings.AutoSplitBySilence).Command,
            Is.SameAs(main.MenuBar.AutoSplitBySilence));
    }

    [AvaloniaTest]
    [TestCase(true)]
    [TestCase(false)]
    public async Task DialogButtons_ApplyOneUndoEntry_OrCancelWithoutEdits(bool apply)
    {
        var scene = new Scene(100, 100, "silence") { Uri = new Uri(Path.Combine(Path.GetTempPath(), "silence-ui.scene")) };
        var element = new Element { Length = { Value = MediaTime.FromSeconds(10) } };
        scene.Children.Add(element);
        var sequence = new OperationSequenceGenerator();
        using var history = new HistoryManager(sequence);
        using var observer = new CoreObjectOperationObserver(null, scene, sequence);
        history.Subscribe(observer);
        using var model = new SilenceSplitViewModel(scene, element, history);
        var dialog = new SilenceSplitDialog(model);
        var window = new Window { Width = 800, Height = 780 };
        window.Show();
        Task closed = dialog.ShowAsync(window);
        try
        {
            HeadlessTestHelpers.Render(30);
            await HeadlessTestHelpers.WaitUntilAsync(() => !model.IsBusy.Value);
            model.SetWaveform(Enumerable.Range(0, 500).Select(i => new WaveformChunk(i, 0, i is >= 100 and < 250 ? 0 : 0.5f)).ToArray());
            model.ModeIndex.Value = 2;
            HeadlessTestHelpers.Render();
            var button = dialog.GetVisualDescendants().OfType<Button>()
                .Single(b => Equals(b.Content, apply ? Strings.Apply : Strings.Cancel));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await closed;
            Assert.That(history.UndoCount, Is.EqualTo(apply ? 1 : 0));
            Assert.That(scene.Children.Count, Is.EqualTo(apply ? 2 : 1));
            if (apply) history.Undo();
            Assert.That(scene.Children, Is.EqualTo(new[] { element }));
            Assert.That(element.Length.Value, Is.EqualTo(MediaTime.FromSeconds(10)));
        }
        finally
        {
            if (!closed.IsCompleted) { dialog.Hide(); await closed; }
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase("en", 800, false)]
    [TestCase("ja", 440, true)]
    public async Task Preview_ReactsToParameters_AndDialogFits(string language, int width, bool light)
    {
        CultureInfo previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(language);
        var scene = new Scene(100, 100, "silence");
        var element = new Element { Length = { Value = MediaTime.FromSeconds(10) } };
        var linked = new Element { Length = { Value = MediaTime.FromSeconds(10) }, ZIndex = { Value = 1 } };
        scene.Children.Add(element);
        scene.Children.Add(linked);
        scene.Groups.Add([element.Id.Value, linked.Id.Value]);
        using var history = new HistoryManager(new OperationSequenceGenerator());
        using var model = new SilenceSplitViewModel(scene, element, history);
        var dialog = new SilenceSplitDialog(model);
        var window = new Window { Width = width, Height = 780, RequestedThemeVariant = light ? ThemeVariant.Light : ThemeVariant.Dark };
        window.Show();
        Task closed = dialog.ShowAsync(window);
        try
        {
            HeadlessTestHelpers.Render(30);
            await HeadlessTestHelpers.WaitUntilAsync(() => !model.IsBusy.Value);
            model.SetWaveform(Enumerable.Range(0, 500).Select(i => new WaveformChunk(i,
                i is >= 100 and < 250 ? -0.005f : -0.55f,
                i is >= 100 and < 250 ? 0.005f : 0.65f)).ToArray());
            HeadlessTestHelpers.Render();
            Assert.That(model.Ranges, Has.Count.EqualTo(1));
            Assert.That(dialog.IsPrimaryButtonEnabled, Is.True);
            dialog.FindControl<NumericUpDown>("Threshold")!.Value = null;
            HeadlessTestHelpers.Render();
            Assert.That(dialog.IsPrimaryButtonEnabled, Is.False, "an empty threshold must not apply the previous value");
            dialog.FindControl<NumericUpDown>("Threshold")!.Value = -50;
            HeadlessTestHelpers.Render();
            Assert.That(model.Ranges, Is.Empty);
            Assert.That(dialog.IsPrimaryButtonEnabled, Is.False);
            dialog.FindControl<NumericUpDown>("Threshold")!.Value = -40;
            dialog.FindControl<ComboBox>("Mode")!.SelectedIndex = 2;
            dialog.FindControl<CheckBox>("Linked")!.IsChecked = false;
            HeadlessTestHelpers.Render();
            Assert.That(model.ModeIndex.Value, Is.EqualTo(2));
            Assert.That(model.IncludeLinked.Value, Is.False);
            Assert.That(model.Ranges, Has.Count.EqualTo(1));
            Assert.That(model.Ranges[0].Duration.ToSeconds(), Is.EqualTo(2.8).Within(0.001));
            foreach (var control in dialog.GetVisualDescendants().OfType<NumericUpDown>())
            {
                var point = control.TranslatePoint(default, window)!.Value;
                Assert.That(point.X, Is.GreaterThanOrEqualTo(0));
                Assert.That(point.X + control.Bounds.Width, Is.LessThanOrEqualTo(width));
            }
            HeadlessCapture.SaveIfRequested(dialog, nameof(SilenceSplitDialogTests), $"{language}-{width}");
            element.Length.Value = MediaTime.FromSeconds(12);
            Assert.That(model.CanApply.Value, Is.False, "a stale preview cannot edit the changed clip");
        }
        finally
        {
            dialog.Hide();
            await closed;
            window.Close();
            CultureInfo.CurrentUICulture = previous;
        }
    }
}
