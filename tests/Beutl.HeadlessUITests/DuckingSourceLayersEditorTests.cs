using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Api.Services;
using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.Services;
using Beutl.Language;
using Beutl.ProjectSystem;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class DuckingSourceLayersEditorTests
{
    [AvaloniaTest]
    public async Task Checklist_excludes_the_own_layer_and_supports_multiple_sources_and_undo()
    {
        Assert.That(TestShell.Library.FindItem(typeof(DuckingEffect)), Is.Not.Null);
        var scene = new Scene(320, 180, "Ducking");
        var musicLayer = AddLayer(0, "Background music");
        var narration = AddLayer(1, "Narration");
        var interview = AddLayer(2, "Interview");
        AddLayer(4, "Ambience");

        var effect = new DuckingEffect();
        var sound = new SourceSound { Effect = { Value = effect } };
        var clip = new Element { ZIndex = { Value = 0 } };
        clip.Objects.Add(sound);
        scene.Children.Add(clip);
        scene.Children.Add(new Element { ZIndex = { Value = 3 } });

        using var history = new PropertyEditorTestHost.HistoryScope(scene);
        var (context, control) = PropertyEditorTestHost.CreateControl(PropertyAdapterFactory.CreateAdapter(sound.Effect, sound));
        using var vm = (AudioEffectEditorViewModel)context;
        vm.Accept(new PropertyEditorTestHost.ServiceVisitor(
            (typeof(ExtensionProvider), TestShell.Extensions),
            (typeof(HistoryManager), history.History),
            (typeof(Scene), scene),
            (typeof(Element), clip)));
        vm.IsExpanded.Value = true;
        PropertyEditorGrid.SetIsAlignmentScope(control, true);
        var window = new Window
        {
            Width = 360,
            Height = 400,
            RequestedThemeVariant = ThemeVariant.Dark,
            Content = new Border { Padding = new Thickness(12), Child = control },
        };

        try
        {
            window.Show();
            var content = control.FindControl<Panel>("content")!;
            await HeadlessTestHelpers.WaitUntilAsync(() =>
            {
                HeadlessTestHelpers.Render();
                return content.IsVisible && content.Opacity == 1 && Choices().Length == 4;
            });

            CheckBox[] choices = Choices();
            var checklist = control.GetVisualDescendants().OfType<DuckingSourceLayersEditor>().Single();
            var emptyMessage = checklist.FindControl<TextBlock>("emptyMessage")!;
            Assert.That(emptyMessage.IsVisible, Is.False);
            Assert.That(choices.Select(AutomationProperties.GetName),
                Is.EqualTo(new[] { narration.Name.Value, interview.Name.Value, string.Format(AudioStrings.DuckingEffect_LayerNumber, 3), "Ambience" }));
            Assert.That(choices.Select(AutomationProperties.GetName), Does.Not.Contain(musicLayer.Name.Value));

            Click(choices[0]);
            Click(choices[1]);
            Assert.That(effect.SourceLayers.Select(source => source.Id),
                Is.EqualTo(new[] { narration.Id.Value, interview.Id.Value }));
            Assert.That(choices.Take(2).Select(choice => choice.IsChecked), Is.All.True);

            HeadlessCapture.SaveIfRequested(window, nameof(DuckingSourceLayersEditorTests), "ducking-dark");
            if (HeadlessCapture.IsRequested)
            {
                window.RequestedThemeVariant = ThemeVariant.Light;
                HeadlessTestHelpers.Render(3);
                HeadlessCapture.SaveIfRequested(window, nameof(DuckingSourceLayersEditorTests), "ducking-light");
            }

            Click(choices[0]);
            Assert.That(effect.SourceLayers.Select(source => source.Id), Is.EqualTo(new[] { interview.Id.Value }));
            Assert.That(history.History.Undo(), Is.True);
            HeadlessTestHelpers.Render();
            Assert.That(choices[0].IsChecked, Is.True);

            Click(choices[2]);
            TimelineLayer unnamed = scene.Layers.Single(layer => layer.ZIndex.Value == 3);
            Assert.That(effect.SourceLayers.Any(source => source.Id == unnamed.Id.Value), Is.True);
            Assert.That(history.History.Undo(), Is.True);
            HeadlessTestHelpers.Render();
            Assert.That(choices[2].IsChecked, Is.False);
            Assert.That(scene.Layers, Does.Not.Contain(unnamed));
            Assert.That(history.History.Redo(), Is.True);
            HeadlessTestHelpers.Render();
            Assert.That(choices[2].IsChecked, Is.True);

            var layerAttributes = new LayerAttributeService(history.History);
            layerAttributes.SetAudioMuted(scene, 3, true);
            layerAttributes.SetAudioMuted(scene, 3, false);
            Assert.That(scene.Layers.Single(layer => layer.ZIndex.Value == 3).Id.Value, Is.EqualTo(unnamed.Id.Value));
            Assert.That(choices[2].IsChecked, Is.True);

            var threshold = PropertyEditorTestHost.FindNumberEditor(vm.Properties.Value!.Properties, effect.Threshold);
            float originalThreshold = effect.Threshold.Value;
            threshold.SetValue(-14f);
            Assert.That(effect.Threshold.Value, Is.EqualTo(-14f));
            Assert.That(history.History.Undo(), Is.True);
            Assert.That(effect.Threshold.Value, Is.EqualTo(originalThreshold));

            using (history.History.SuppressRecording())
            {
                scene.Children.Remove(scene.Children.Last());
                foreach (TimelineLayer layer in scene.Layers.Where(layer => layer != musicLayer).ToArray())
                    scene.Layers.Remove(layer);
            }
            HeadlessTestHelpers.Render();
            Assert.That(Choices(), Is.Empty);
            Assert.That(emptyMessage.IsVisible, Is.True);
        }
        finally
        {
            window.Close();
        }

        TimelineLayer AddLayer(int index, string name)
        {
            var layer = new TimelineLayer { ZIndex = { Value = index }, Name = { Value = name } };
            scene.Layers.Add(layer);
            return layer;
        }

        CheckBox[] Choices() => [.. control.GetVisualDescendants()
            .OfType<DuckingSourceLayersEditor>()
            .SelectMany(editor => editor.GetVisualDescendants().OfType<CheckBox>())];

        static void Click(CheckBox choice)
        {
            HeadlessTestHelpers.Click(choice);
            HeadlessTestHelpers.Render();
        }
    }
}
