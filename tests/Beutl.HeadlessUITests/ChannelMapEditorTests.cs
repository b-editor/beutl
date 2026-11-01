using System.Globalization;
using System.Text.Json.Nodes;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.NUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Audio;
using Beutl.Controls;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.ChannelMapProperties.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Engine.Expressions;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.Media.Wave;
using Beutl.PropertyAdapters;
using Beutl.Services.StartupTasks;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ChannelMapEditorTests
{
    [AvaloniaTest]
    public void HeaderUsesPropertyNameAndMenuAndFormatRowUsesStandardGrid()
    {
        var (vm, control) = CreateEditor(new SourceSound());
        using (vm)
        {
            var window = Show(control, 480, 200);
            try
            {
                var format = control.FindControl<EnumEditor>("FormatEditor")!;
                var toggle = control.FindControl<ToggleButton>("expandToggle")!;
                var title = (TextBlock)toggle.Content!;
                var header = format.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Name == "PART_HeaderTextBlock");
                Assert.That(title.Text, Is.EqualTo(vm.Header));
                Assert.That(title.FontWeight, Is.EqualTo(FontWeight.Normal));
                Assert.That(header.Text, Is.EqualTo(AudioStrings.ChannelMap_Format));
                Assert.That(header.FontWeight, Is.EqualTo(FontWeight.Normal));
                Assert.That(format.GetVisualDescendants().OfType<PropertyEditorGrid>().Count(), Is.EqualTo(1));
                Assert.That(format.GetVisualDescendants().OfType<GridSplitter>().Count(), Is.EqualTo(1));
                Assert.That(toggle.Tag, Is.TypeOf<PropertyEditorMenu>());
                Assert.That(format.GetVisualDescendants().OfType<PropertyEditorMenu>(), Is.Empty);
                var menu = (PropertyEditorMenu)toggle.Tag!;
                Assert.That(menu.DataContext, Is.SameAs(vm));
                Assert.That(menu.GetVisualDescendants().OfType<Button>().Single().IsEffectivelyVisible, Is.True);
                Assert.That(menu.IsEffectivelyEnabled, Is.True);
            }
            finally { window.Close(); }
        }
    }

    [AvaloniaTest]
    public async Task CollapsedByDefaultAndToggleExpandsAndCollapsesContent()
    {
        await WithFourChannelSource(async source =>
        {
            var sound = new SourceSound
            {
                Source = { Value = source },
                ChannelMap = { Value = AudioChannelMap.CreateDefault(AudioChannelFormat.Stereo, AudioChannelLayout.GetDefault(4)) }
            };
            using var history = new PropertyEditorTestHost.HistoryScope(sound);
            var (vm, control) = CreateEditor(sound, expand: false);
            using (vm)
            {
                vm.Accept(PropertyEditorTestHost.ServiceVisitor.For(history.History));
                var window = Show(control, 480, 300);
                try
                {
                    var toggle = control.FindControl<ToggleButton>("expandToggle")!;
                    var content = control.FindControl<Panel>("content")!;
                    Assert.That(vm.IsExpanded.Value, Is.False);
                    Assert.That(toggle.IsChecked, Is.False);
                    await WaitForExpansionState(control, false);
                    Assert.That(content.IsEffectivelyVisible, Is.False);
                    Assert.That(((PropertyEditorMenu)toggle.Tag!).IsEffectivelyVisible, Is.True);
                    HeadlessTestHelpers.Click(toggle);
                    await WaitForExpansionState(control);
                    var left = GetChannelBox(control, "L");
                    Assert.That(vm.IsExpanded.Value, Is.True);
                    Assert.That(toggle.IsChecked, Is.True);
                    Assert.That(left.IsEffectivelyVisible, Is.True);
                    HeadlessTestHelpers.Click(toggle);
                    Assert.That(vm.IsExpanded.Value, Is.False);
                    await WaitForExpansionState(control, false);
                    Assert.That(left.IsEffectivelyVisible, Is.False);
                    Assert.That(((PropertyEditorMenu)toggle.Tag!).IsEffectivelyVisible, Is.True);
                    HeadlessTestHelpers.Click(toggle);
                    await WaitForExpansionState(control);
                    Assert.That(vm.IsExpanded.Value, Is.True);
                    Assert.That(left.IsEffectivelyVisible, Is.True);
                    Assert.That(GetChannelBox(control, "L"), Is.SameAs(left));
                    Assert.That(history.History.UndoCount, Is.Zero);
                }
                finally { window.Close(); }
            }
        });
    }

    [AvaloniaTest]
    public void IsExpandedRoundTripsThroughJson([Values] bool expanded)
    {
        var sound = new SourceSound();
        var adapter = new EnginePropertyAdapter<AudioChannelMap>(sound.ChannelMap, sound);
        using var original = new ChannelMapEditorViewModel(adapter);
        original.IsExpanded.Value = expanded;
        var json = new JsonObject();
        original.WriteToJson(json);
        using var restored = new ChannelMapEditorViewModel(adapter);
        restored.IsExpanded.Value = !expanded;
        restored.ReadFromJson(JsonNode.Parse(json.ToJsonString())!.AsObject());
        Assert.That((bool)json[nameof(ChannelMapEditorViewModel.IsExpanded)]!, Is.EqualTo(expanded));
        Assert.That(restored.IsExpanded.Value, Is.EqualTo(expanded));
    }

    [AvaloniaTest]
    public void MissingOrInvalidExpansionStateIsIgnored([Values] bool expanded)
    {
        var sound = new SourceSound();
        using var vm = new ChannelMapEditorViewModel(new EnginePropertyAdapter<AudioChannelMap>(sound.ChannelMap, sound));
        vm.IsExpanded.Value = expanded;
        foreach (string json in new[] { "{}", "{\"IsExpanded\":null}", "{\"IsExpanded\":1}", "{\"IsExpanded\":\"false\"}", "{\"IsExpanded\":{}}" })
        {
            vm.ReadFromJson(JsonNode.Parse(json)!.AsObject());
            Assert.That(vm.IsExpanded.Value, Is.EqualTo(expanded), json);
        }
    }

    [AvaloniaTest]
    [TestCase("en-US", 400)]
    [TestCase("ja-JP", 280)]
    [TestCase("ja-JP", 480)]
    [TestCase("ja-JP", 760)]
    [TestCase("ko-KR", 320)]
    [TestCase("es-ES", 320)]
    [TestCase("zh-CN", 320)]
    public async Task StereoAssignments_EditThePropertyAndCommitOneUndoStepPerSelection(string culture, int width)
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            await WithFourChannelSource(async source =>
            {
                var sound = new SourceSound { Source = { Value = source } };
                using var history = new PropertyEditorTestHost.HistoryScope(sound);
                var (vm, control) = CreateEditor(sound);
                using (vm)
                {
                    vm.Accept(PropertyEditorTestHost.ServiceVisitor.For(history.History));
                    var window = Show(control, width, 310);
                    try
                    {
                        ComboBox format = GetFormatBox(control);
                        Assert.That(format.Items.Count, Is.EqualTo(5));
                        Assert.That(history.History.UndoCount, Is.Zero);
                        format.SelectedIndex = (int)AudioChannelFormat.Stereo;
                        HeadlessTestHelpers.Render();
                        Assert.That(sound.ChannelMap.Value.Sources, Is.EqualTo(new[] { 0, 1 }));
                        Assert.That(history.History.UndoCount, Is.EqualTo(1));

                        var rows = vm.Channels.ToArray();
                        ComboBox left = GetChannelBox(control, "L");
                        ComboBox right = GetChannelBox(control, "R");
                        left.SelectedIndex = 3;
                        HeadlessTestHelpers.Render();
                        AssertRowsRetained();
                        Assert.That(history.History.UndoCount, Is.EqualTo(2));
                        right.SelectedIndex = 4;
                        HeadlessTestHelpers.Render();
                        AssertRowsRetained();
                        Assert.That(sound.ChannelMap.Value.Format, Is.EqualTo(AudioChannelFormat.Stereo));
                        Assert.That(sound.ChannelMap.Value.Sources, Is.EqualTo(new[] { 2, 3 }));
                        Assert.That(sound.ChannelMap.Value.SourceLayout, Is.EqualTo(AudioChannelLayout.GetDefault(4)));
                        Assert.That(history.History.UndoCount, Is.EqualTo(3));
                        Assert.That(history.History.HasPendingOperations, Is.False);
                        Assert.That(vm.Channels[0].SourceChannels.Select(item => item.DisplayName), Is.EqualTo(new[] { AudioStrings.ChannelMap_None, "1 (L)", "2 (R)", "3 (Ls)", "4 (Rs)" }));
                        Assert.That(control.GetVisualDescendants().OfType<PropertyEditorMenu>().Count(), Is.EqualTo(1));
                        Assert.That(left.GetVisualAncestors().OfType<TreeLineDecorator>().Any(), Is.True);
                        Assert.That(control.Bounds.Width, Is.LessThanOrEqualTo(width));
                        AssertComboBoxesAligned(control);
                        if (HeadlessCapture.IsRequested)
                            await WaitForExpansionState(control);
                        HeadlessCapture.SaveIfRequested(window, nameof(ChannelMapEditorTests), $"stereo-{culture}-{width}");

                        history.History.Undo();
                        HeadlessTestHelpers.Render();
                        Assert.That(sound.ChannelMap.Value.Sources, Is.EqualTo(new[] { 2, 1 }));
                        Assert.That(GetChannelBox(control, "R").SelectedIndex, Is.EqualTo(2));
                        AssertRowsRetained();
                        history.History.Redo();
                        HeadlessTestHelpers.Render();
                        AssertRowsRetained();
                        Assert.That(sound.ChannelMap.Value.Sources, Is.EqualTo(new[] { 2, 3 }));
                        GetChannelBox(control, "L").SelectedIndex = 0;
                        Assert.That(sound.ChannelMap.Value.Sources, Is.EqualTo(new[] { -1, 3 }));
                        Assert.That(history.History.UndoCount, Is.EqualTo(4));

                        if (culture == "ja-JP" && width == 280)
                        {
                            format.SelectedIndex = (int)AudioChannelFormat.Surround71;
                            window.Height = 520;
                            HeadlessTestHelpers.Render();
                            Assert.That(vm.Channels.Count, Is.EqualTo(8));
                            Assert.That(vm.Channels[0], Is.Not.SameAs(rows[0]));
                            Assert.That(sound.ChannelMap.Value.Sources, Is.EqualTo(new[] { 0, 1, -1, -1, 2, 3, -1, -1 }));
                            AssertComboBoxesAligned(control);
                            HeadlessCapture.SaveIfRequested(window, nameof(ChannelMapEditorTests), "surround71-ja-JP-280");
                        }

                        void AssertRowsRetained()
                        {
                            Assert.That(vm.Channels[0], Is.SameAs(rows[0]));
                            Assert.That(vm.Channels[1], Is.SameAs(rows[1]));
                            Assert.That(GetChannelBox(control, "L"), Is.SameAs(left));
                            Assert.That(GetChannelBox(control, "R"), Is.SameAs(right));
                        }
                    }
                    finally { window.Close(); }
                }
            });
        }
        finally { CultureInfo.CurrentUICulture = previousCulture; }
    }

    [AvaloniaTest]
    public async Task MissingUnreadableAndChangedSources_ShowAutoWithoutRewritingTheSavedMap()
    {
        await WithFourChannelSource(async source =>
        {
            var saved = new AudioChannelMap(AudioChannelFormat.Stereo, AudioChannelLayout.GetDefault(4), [2, 3]);
            var sound = new SourceSound { ChannelMap = { Value = saved } };
            using var history = new PropertyEditorTestHost.HistoryScope(sound);
            var (vm, control) = CreateEditor(sound);
            using (vm)
            {
                vm.Accept(PropertyEditorTestHost.ServiceVisitor.For(history.History));
                var window = Show(control, 280, 250);
                try
                {
                    AssertUnavailable();
                    if (HeadlessCapture.IsRequested)
                        await WaitForExpansionState(control);
                    HeadlessCapture.SaveIfRequested(window, nameof(ChannelMapEditorTests), "source-unavailable-280");
                    using (history.History.SuppressRecording())
                    {
                        sound.Source.Value = SoundSource.Open(Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.wav"));
                        HeadlessTestHelpers.Render();
                        AssertUnavailable();
                        sound.Source.Value = source;
                        HeadlessTestHelpers.Render();
                        Assert.That(vm.SelectedFormatIndex.Value, Is.EqualTo(2));
                        Assert.That(vm.Channels.Count, Is.EqualTo(2));
                        Assert.That(vm.CanEditMap.Value, Is.True);

                        var mismatchedMap = new AudioChannelMap(AudioChannelFormat.Mono, AudioChannelLayout.Discrete(4), [2]);
                        sound.ChannelMap.Value = mismatchedMap;
                        HeadlessTestHelpers.Render();
                        Assert.That(vm.SelectedFormatIndex.Value, Is.Zero);
                        Assert.That(vm.Formats.Count, Is.EqualTo(5));
                        Assert.That(vm.Channels, Is.Empty);
                        Assert.That(vm.CanEditMap.Value, Is.True);
                        Assert.That(GetFormatBox(control).SelectedIndex, Is.Zero);
                        Assert.That(sound.ChannelMap.Value, Is.EqualTo(mismatchedMap));
                        HeadlessCapture.SaveIfRequested(window, nameof(ChannelMapEditorTests), "source-changed-280");
                    }
                    Assert.That(history.History.UndoCount, Is.Zero);
                }
                finally { window.Close(); }

                void AssertUnavailable()
                {
                    Assert.That(vm.Formats.Select(item => item.DisplayName), Is.EqualTo(new[] { AudioStrings.ChannelMap_Auto }));
                    Assert.That(GetFormatBox(control).IsEffectivelyEnabled, Is.False);
                    Assert.That(vm.SelectedFormatIndex.Value, Is.Zero);
                    Assert.That(vm.Channels, Is.Empty);
                    Assert.That(vm.CanEditMap.Value, Is.False);
                    Assert.That(sound.ChannelMap.Value, Is.EqualTo(saved));
                }
            }
        });
    }

    [AvaloniaTest]
    public async Task ExpressionDisablesSelectionsAndRemovingItRestoresEditing()
    {
        await WithFourChannelSource(async source =>
        {
            var sound = new SourceSound
            {
                Source = { Value = source },
                ChannelMap = { Value = AudioChannelMap.CreateDefault(AudioChannelFormat.Stereo, AudioChannelLayout.GetDefault(4)) }
            };
            using var history = new PropertyEditorTestHost.HistoryScope(sound);
            var (vm, control) = CreateEditor(sound);
            using (vm)
            {
                vm.Accept(PropertyEditorTestHost.ServiceVisitor.For(history.History));
                var window = Show(control, 480, 300);
                try
                {
                    await WaitForExpansionState(control);
                    var rows = vm.Channels.ToArray();
                    using (history.History.SuppressRecording())
                        sound.ChannelMap.Expression = new StringExpression<AudioChannelMap>("default");
                    HeadlessTestHelpers.Render();
                    Assert.That(control.GetVisualDescendants().OfType<ComboBox>().All(box => !box.IsEffectivelyEnabled), Is.True);
                    var toggle = control.FindControl<ToggleButton>("expandToggle")!;
                    var menu = (PropertyEditorMenu)toggle.Tag!;
                    Assert.That(menu.IsEffectivelyEnabled, Is.True,
                        "The property menu remains available to remove the expression.");

                    var button = menu.GetVisualDescendants().OfType<Button>().Single();
                    HeadlessTestHelpers.Click(button, button: MouseButton.Right);
                    var flyout = (FAMenuFlyout)button.ContextFlyout!;
                    Assert.That(flyout.IsOpen, Is.True);
                    var remove = flyout.Items.OfType<FAMenuFlyoutItem>().Single(item => item.Name == "removeExpressionItem");
                    Assert.That(remove.IsEnabled, Is.True);
                    HeadlessTestHelpers.Click(remove);
                    HeadlessTestHelpers.Render();
                    Assert.That(sound.ChannelMap.Expression, Is.Null);
                    Assert.That(vm.IsExpanded.Value, Is.True, "The header menu must not toggle expansion.");
                    Assert.That(history.History.UndoCount, Is.EqualTo(1));
                    Assert.That(control.GetVisualDescendants().OfType<ComboBox>().All(box => box.IsEffectivelyEnabled), Is.True);
                    Assert.That(vm.Channels[0], Is.SameAs(rows[0]));
                    GetChannelBox(control, "L").SelectedIndex = 3;
                    Assert.That(sound.ChannelMap.Value.Sources, Is.EqualTo(new[] { 2, 1 }));
                    Assert.That(history.History.UndoCount, Is.EqualTo(2));
                }
                finally { window.Close(); }
            }
        });
    }

    private static (ChannelMapEditorViewModel Vm, Control Control) CreateEditor(SourceSound sound, bool expand = true)
    {
        var adapter = PropertyAdapterFactory.CreateAdapter(sound.ChannelMap, sound);
        var context = PropertyEditorResolver.CreateContext(adapter,
            LoadPrimitiveExtensionTask.PrimitiveExtensions.OfType<PropertyEditorExtension>().ToArray(), PropertyEditorSurface.Panel);
        Assert.That(context, Is.TypeOf<ChannelMapEditorViewModel>());
        var vm = (ChannelMapEditorViewModel)context!;
        if (expand)
            vm.IsExpanded.Value = true;
        Assert.That(PropertyEditorHost.TryCreateControl(context!, PropertyEditorSurface.Panel, out var control), Is.True);
        control!.DataContext = context;
        return (vm, control);
    }

    private static ComboBox GetChannelBox(Control control, string channel)
        => control.GetVisualDescendants().OfType<ComboBox>()
            .Single(box => box.DataContext is ChannelAssignmentViewModel row && row.Name == channel);

    private static ComboBox GetFormatBox(Control control)
        => control.FindControl<EnumEditor>("FormatEditor")!.GetVisualDescendants().OfType<ComboBox>().Single();

    private static void AssertComboBoxesAligned(Control control)
    {
        ComboBox[] boxes = control.GetVisualDescendants().OfType<ComboBox>().ToArray();
        ComboBox format = GetFormatBox(control);
        double x = format.TranslatePoint(default, control)!.Value.X;
        foreach (ComboBox box in boxes)
        {
            Assert.That(box.TranslatePoint(default, control)!.Value.X, Is.EqualTo(x).Within(0.01));
            Assert.That(box.Bounds.Width, Is.EqualTo(format.Bounds.Width).Within(0.01));
        }
    }

    private static Window Show(Control control, int width, int height)
    {
        PropertyEditorGrid.SetIsAlignmentScope(control, true);
        var window = new Window
        {
            Content = new Border { Padding = new Thickness(12), Child = control },
            Width = width,
            Height = height,
            RequestedThemeVariant = ThemeVariant.Dark
        };
        window.Show();
        HeadlessTestHelpers.Render();
        return window;
    }

    private static Task WaitForExpansionState(Control control, bool expanded = true)
    {
        var content = control.FindControl<Panel>("content")!;
        return HeadlessTestHelpers.WaitUntilAsync(() =>
        {
            HeadlessTestHelpers.Render();
            return expanded ? content.IsVisible && content.Opacity == 1 : !content.IsVisible;
        });
    }

    private static async Task WithFourChannelSource(Func<SoundSource, Task> body)
    {
        string path = Path.Combine(Path.GetTempPath(), $"beutl-channel-editor-{Guid.NewGuid():N}.wav");
        var decoder = new WaveDecoderInfo();
        DecoderRegistry.Register(decoder);
        try
        {
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                const int bytes = 128 * 4 * sizeof(float);
                writer.Write("RIFF"u8);
                writer.Write(60 + bytes);
                writer.Write("WAVEfmt "u8);
                writer.Write(40);
                writer.Write((ushort)0xFFFE);
                writer.Write((ushort)4);
                writer.Write(44100);
                writer.Write(44100 * 4 * sizeof(float));
                writer.Write((ushort)(4 * sizeof(float)));
                writer.Write((ushort)32);
                writer.Write((ushort)22);
                writer.Write((ushort)32);
                writer.Write((uint)AudioChannelLayout.GetDefault(4).Speakers);
                writer.Write(new Guid("00000003-0000-0010-8000-00aa00389b71").ToByteArray());
                writer.Write("data"u8);
                writer.Write(bytes);
                writer.Write(new byte[bytes]);
            }
            await body(SoundSource.Open(path));
        }
        finally
        {
            DecoderRegistry.Unregister(decoder);
            File.Delete(path);
        }
    }
}
