using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using Beutl.Audio;
using Beutl.Editor.Components.ChannelMapProperties.ViewModels;
using Beutl.Editor.Components.PropertyEditors.Services;
using Beutl.Extensibility;
using Beutl.Language;
using Beutl.Media.Decoding;
using Beutl.Media.Source;
using Beutl.Media.Wave;
using Beutl.PropertyAdapters;
using Beutl.Services.StartupTasks;
using Beutl.Testing.Headless;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class ChannelMapEditorTests
{
    [AvaloniaTest]
    [TestCase("en-US", 400)]
    [TestCase("ja-JP", 280)]
    [TestCase("ko-KR", 320)]
    [TestCase("es-ES", 320)]
    [TestCase("zh-CN", 320)]
    public void StereoAssignments_EditThePropertyAndCommitOneUndoStepPerSelection(string culture, int width)
    {
        var previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
        try
        {
            WithFourChannelSource(source =>
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
                        ComboBox format = control.GetVisualDescendants().OfType<ComboBox>().Single();
                        Assert.That(format.Items.Count, Is.EqualTo(5));
                        Assert.That(history.History.UndoCount, Is.Zero);
                        format.SelectedIndex = (int)AudioChannelFormat.Stereo;
                        HeadlessTestHelpers.Render();
                        Assert.That(sound.ChannelMap.Value.Sources, Is.EqualTo(new[] { 0, 1 }));
                        Assert.That(history.History.UndoCount, Is.EqualTo(1));

                        GetChannelBox(control, "L").SelectedIndex = 3;
                        HeadlessTestHelpers.Render();
                        Assert.That(history.History.UndoCount, Is.EqualTo(2));
                        GetChannelBox(control, "R").SelectedIndex = 4;
                        HeadlessTestHelpers.Render();
                        Assert.That(sound.ChannelMap.Value.Format, Is.EqualTo(AudioChannelFormat.Stereo));
                        Assert.That(sound.ChannelMap.Value.Sources, Is.EqualTo(new[] { 2, 3 }));
                        Assert.That(sound.ChannelMap.Value.SourceLayout, Is.EqualTo(AudioChannelLayout.GetDefault(4)));
                        Assert.That(history.History.UndoCount, Is.EqualTo(3));
                        Assert.That(history.History.HasPendingOperations, Is.False);
                        Assert.That(vm.Channels[0].SourceChannels, Is.EqualTo(new[] { AudioStrings.ChannelMap_None, "1 (L)", "2 (R)", "3 (Ls)", "4 (Rs)" }));
                        Assert.That(control.Bounds.Width, Is.LessThanOrEqualTo(width));
                        HeadlessCapture.SaveIfRequested(window, nameof(ChannelMapEditorTests), $"stereo-{culture}-{width}");

                        history.History.Undo();
                        HeadlessTestHelpers.Render();
                        Assert.That(sound.ChannelMap.Value.Sources, Is.EqualTo(new[] { 2, 1 }));
                        Assert.That(GetChannelBox(control, "R").SelectedIndex, Is.EqualTo(2));
                        history.History.Redo();
                        HeadlessTestHelpers.Render();
                        Assert.That(sound.ChannelMap.Value.Sources, Is.EqualTo(new[] { 2, 3 }));
                        GetChannelBox(control, "L").SelectedIndex = 0;
                        Assert.That(sound.ChannelMap.Value.Sources, Is.EqualTo(new[] { -1, 3 }));
                        Assert.That(history.History.UndoCount, Is.EqualTo(4));

                        if (culture == "ja-JP")
                        {
                            format.SelectedIndex = (int)AudioChannelFormat.Surround71;
                            window.Height = 520;
                            HeadlessTestHelpers.Render();
                            Assert.That(vm.Channels.Count, Is.EqualTo(8));
                            HeadlessCapture.SaveIfRequested(window, nameof(ChannelMapEditorTests), "surround71-ja-JP-280");
                        }
                    }
                    finally { window.Close(); }
                }
            });
        }
        finally { CultureInfo.CurrentUICulture = previousCulture; }
    }

    [AvaloniaTest]
    public void MissingUnreadableAndChangedSources_ShowAutoWithoutRewritingTheSavedMap()
    {
        WithFourChannelSource(source =>
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
                        Assert.That(vm.Message.Value, Is.Null);

                        sound.ChannelMap.Value = new AudioChannelMap(AudioChannelFormat.Mono, AudioChannelLayout.Discrete(4), [2]);
                        HeadlessTestHelpers.Render();
                        Assert.That(vm.SelectedFormatIndex.Value, Is.Zero);
                        Assert.That(vm.Formats.Count, Is.EqualTo(5));
                        Assert.That(vm.Channels, Is.Empty);
                        Assert.That(vm.Message.Value, Is.EqualTo(AudioStrings.ChannelMap_SourceChanged));
                        Assert.That(sound.ChannelMap.Value.Format, Is.EqualTo(AudioChannelFormat.Mono));
                    }
                    Assert.That(history.History.UndoCount, Is.Zero);
                }
                finally { window.Close(); }

                void AssertUnavailable()
                {
                    Assert.That(vm.Formats, Is.EqualTo(new[] { AudioStrings.ChannelMap_Auto }));
                    Assert.That(vm.SelectedFormatIndex.Value, Is.Zero);
                    Assert.That(vm.Channels, Is.Empty);
                    Assert.That(vm.Message.Value, Is.EqualTo(AudioStrings.ChannelMap_SourceUnavailable));
                    Assert.That(sound.ChannelMap.Value, Is.EqualTo(saved));
                }
            }
        });
    }

    private static (ChannelMapEditorViewModel Vm, Control Control) CreateEditor(SourceSound sound)
    {
        var adapter = new EnginePropertyAdapter<AudioChannelMap>(sound.ChannelMap, sound);
        var context = PropertyEditorResolver.CreateContext(adapter,
            LoadPrimitiveExtensionTask.PrimitiveExtensions.OfType<PropertyEditorExtension>().ToArray(), PropertyEditorSurface.Panel);
        Assert.That(context, Is.TypeOf<ChannelMapEditorViewModel>());
        Assert.That(PropertyEditorHost.TryCreateControl(context!, PropertyEditorSurface.Panel, out var control), Is.True);
        control!.DataContext = context;
        return ((ChannelMapEditorViewModel)context!, control);
    }

    private static ComboBox GetChannelBox(Control control, string channel)
        => control.GetVisualDescendants().OfType<ComboBox>()
            .Single(box => box.DataContext is ChannelAssignmentViewModel row && row.Name == channel);

    private static Window Show(Control control, int width, int height)
    {
        var window = new Window
        {
            Content = new Border { Padding = new Thickness(12), Child = control }, Width = width, Height = height,
            RequestedThemeVariant = ThemeVariant.Dark
        };
        window.Show();
        HeadlessTestHelpers.Render();
        return window;
    }

    private static void WithFourChannelSource(Action<SoundSource> body)
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
            body(SoundSource.Open(path));
        }
        finally
        {
            DecoderRegistry.Unregister(decoder);
            File.Delete(path);
        }
    }
}
