using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Beutl.Api.Services;
using Beutl.Audio;
using Beutl.Audio.Effects;
using Beutl.Editor;
using Beutl.Editor.Components;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics.Effects;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.Language;
using Beutl.PropertyAdapters;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;
using static Beutl.HeadlessUITests.PropertyEditorTestHost;

namespace Beutl.HeadlessUITests;

// What the transform, filter effect and audio effect editors share: adding keeps what is there, and replacing or
// clearing the whole value is offered only for a repair.
[TestFixture]
public sealed class EffectEditorAddTests
{
    private static ServiceVisitor WithExtensions(HistoryScope history) => new(
        (typeof(HistoryManager), history.History),
        (typeof(IPropertyEditorFactory), new PropertyEditorFactoryAdapter(TestShell.Extensions)),
        (typeof(ExtensionProvider), TestShell.Extensions));

    private static TransformEditorViewModel TransformViewModel(RectShape shape, HistoryScope history)
    {
        var viewModel = new TransformEditorViewModel(
            new SimplePropertyAdapter<Transform?>((SimpleProperty<Transform?>)shape.Transform, shape));
        viewModel.Accept(WithExtensions(history));
        return viewModel;
    }

    private static FilterEffectEditorViewModel FilterViewModel(RectShape shape, HistoryScope history)
    {
        var viewModel = new FilterEffectEditorViewModel(
            new SimplePropertyAdapter<FilterEffect?>((SimpleProperty<FilterEffect?>)shape.FilterEffect, shape));
        viewModel.Accept(WithExtensions(history));
        return viewModel;
    }

    private static AudioEffectEditorViewModel AudioViewModel(SourceSound sound, HistoryScope history)
    {
        var viewModel = new AudioEffectEditorViewModel(new EnginePropertyAdapter<AudioEffect?>(sound.Effect, sound));
        viewModel.Accept(WithExtensions(history));
        return viewModel;
    }

    private static Window Show(Control editor)
    {
        var window = new Window { Content = editor, Width = 480, Height = 600 };
        window.Show();
        HeadlessTestHelpers.Render(3);
        return window;
    }

    // Drops on the editor through the window, as the platform does.
    private static void Drop(Window window, Control editor, DataFormat<string> format, Type type)
    {
        var transfer = new DataTransfer();
        transfer.Add(DataTransferItem.Create(format, TypeFormat.ToString(type)));
        Avalonia.Point point = HeadlessTestHelpers.Center(editor.FindControl<ToggleButton>("expandToggle")!, window);
        window.DragDrop(point, RawDragEventType.DragEnter, transfer, DragDropEffects.Copy);
        window.DragDrop(point, RawDragEventType.DragOver, transfer, DragDropEffects.Copy);
        window.DragDrop(point, RawDragEventType.Drop, transfer, DragDropEffects.Copy);
        HeadlessTestHelpers.Render();
    }

    // Whether the items that replace or clear the whole value show once the right-click menu opens.
    private static bool OffersRepair(Control editor)
    {
        ToggleButton toggle = editor.FindControl<ToggleButton>("expandToggle")!;
        var menu = (FAMenuFlyout)toggle.ContextFlyout!;
        menu.ShowAt(toggle);
        HeadlessTestHelpers.Render();
        bool change = menu.Items.OfType<FAMenuFlyoutItem>().Single(item => item.Text == Strings.Change).IsVisible;
        bool clear = menu.Items.OfType<FAMenuFlyoutItem>().Single(item => item.Text == Strings.Null).IsVisible;
        menu.Hide();
        HeadlessTestHelpers.Render();
        Assert.That(clear, Is.EqualTo(change));
        return change;
    }

    [AvaloniaTest]
    public void A_transform_dropped_on_a_single_transform_goes_beside_it()
    {
        var existing = new ScaleTransform(150, 150);
        var shape = new RectShape { Transform = { Value = existing } };
        using var history = new HistoryScope(shape);
        using TransformEditorViewModel viewModel = TransformViewModel(shape, history);
        var editor = new TransformEditor { DataContext = viewModel };
        Window window = Show(editor);
        try
        {
            Drop(window, editor, BeutlDataFormats.Transform, typeof(TranslateTransform));

            var group = (TransformGroup)shape.Transform.Value!;
            Assert.Multiple(() =>
            {
                Assert.That(group.Children[0], Is.TypeOf<TranslateTransform>());
                Assert.That(group.Children[1], Is.SameAs(existing));
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void A_filter_effect_dropped_on_a_single_filter_effect_goes_beside_it()
    {
        var existing = new Brightness();
        var shape = new RectShape { FilterEffect = { Value = existing } };
        using var history = new HistoryScope(shape);
        using FilterEffectEditorViewModel viewModel = FilterViewModel(shape, history);
        var editor = new FilterEffectEditor { DataContext = viewModel };
        Window window = Show(editor);
        try
        {
            Drop(window, editor, BeutlDataFormats.FilterEffect, typeof(Blur));

            var group = (FilterEffectGroup)shape.FilterEffect.Value!;
            Assert.Multiple(() =>
            {
                Assert.That(group.Children[0], Is.SameAs(existing));
                Assert.That(group.Children[1], Is.TypeOf<Blur>());
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void An_audio_effect_dropped_on_a_single_audio_effect_goes_beside_it()
    {
        var existing = new DelayEffect();
        var sound = new SourceSound { Effect = { Value = existing } };
        using var history = new HistoryScope(sound);
        using AudioEffectEditorViewModel viewModel = AudioViewModel(sound, history);
        var editor = new AudioEffectEditor { DataContext = viewModel };
        Window window = Show(editor);
        try
        {
            Drop(window, editor, BeutlDataFormats.AudioEffect, typeof(CompressorEffect));

            var group = (AudioEffectGroup)sound.Effect.Value!;
            Assert.Multiple(() =>
            {
                Assert.That(group.Children[0], Is.SameAs(existing));
                Assert.That(group.Children[1], Is.TypeOf<CompressorEffect>());
            });
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    public void A_reference_added_to_a_single_filter_effect_goes_beside_it()
    {
        var existing = new Brightness();
        var shape = new RectShape { FilterEffect = { Value = existing } };
        using var history = new HistoryScope(shape);
        using FilterEffectEditorViewModel viewModel = FilterViewModel(shape, history);

        viewModel.AddTarget(typeof(FilterEffectPresenter), new Blur());

        var group = (FilterEffectGroup)shape.FilterEffect.Value!;
        Assert.Multiple(() =>
        {
            Assert.That(group.Children[0], Is.SameAs(existing));
            Assert.That(group.Children[1], Is.TypeOf<FilterEffectPresenter>());
        });
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void The_filter_effect_menu_offers_changing_and_clearing_only_for_a_repair(bool unreadable)
    {
        var shape = new RectShape();
        if (unreadable)
        {
            shape.FilterEffect.Value = new FallbackFilterEffect();
        }

        using var history = new HistoryScope(shape);
        using FilterEffectEditorViewModel viewModel = FilterViewModel(shape, history);
        var editor = new FilterEffectEditor { DataContext = viewModel };
        Window window = Show(editor);
        try
        {
            Assert.That(OffersRepair(editor), Is.EqualTo(unreadable));
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTest]
    [TestCase(false)]
    [TestCase(true)]
    public void The_audio_effect_menu_offers_changing_and_clearing_only_for_a_repair(bool unreadable)
    {
        var sound = new SourceSound { Effect = { Value = unreadable ? new FallbackAudioEffect() : new AudioEffectGroup() } };
        using var history = new HistoryScope(sound);
        using AudioEffectEditorViewModel viewModel = AudioViewModel(sound, history);
        var editor = new AudioEffectEditor { DataContext = viewModel };
        Window window = Show(editor);
        try
        {
            Assert.That(OffersRepair(editor), Is.EqualTo(unreadable));
        }
        finally
        {
            window.Close();
        }
    }
}
