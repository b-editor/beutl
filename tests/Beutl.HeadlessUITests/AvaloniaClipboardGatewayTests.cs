using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Input.Platform;
using Beutl.Editor;
using Beutl.Editor.Components;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Components.Services;
using Beutl.Editor.Services;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Media;
using Beutl.PropertyAdapters;

namespace Beutl.HeadlessUITests;

[TestFixture, NonParallelizable]
public class AvaloniaClipboardGatewayTests
{
    // The gateway drops a format it does not map, with no error, on the way to the clipboard.
    [AvaloniaTest]
    public async Task The_groups_of_copied_clips_reach_the_clipboard_and_come_back()
    {
        var window = new Window();
        window.Show();
        ClipboardHelper.OverrideClipboard = window.Clipboard;
        try
        {
            var gateway = new AvaloniaClipboardGateway();

            await gateway.SetAsync(
            [
                new ClipboardEntry(BeutlClipboardFormats.Elements, "[]"),
                new ClipboardEntry(BeutlClipboardFormats.ElementGroups, "[[]]"),
            ]);

            Assert.That(await gateway.GetFormatsAsync(), Does.Contain(BeutlClipboardFormats.ElementGroups));
            Assert.That(await gateway.TryGetStringAsync(BeutlClipboardFormats.ElementGroups), Is.EqualTo("[[]]"));
        }
        finally
        {
            ClipboardHelper.OverrideClipboard = null;
            window.Close();
        }
    }

    [AvaloniaTest]
    public async Task A_brush_copied_in_one_property_editor_can_be_pasted_in_another()
    {
        var window = new Window();
        window.Show();
        try
        {
            var brush = new SolidColorBrush(Colors.Red);
            var source = new RectShape { Fill = { Value = brush } };
            var destination = new RectShape();
            using var history = new PropertyEditorTestHost.HistoryScope(destination);
            using var copy = new BrushEditorViewModel(new SimplePropertyAdapter<Brush?>((SimpleProperty<Brush?>)source.Fill, source));
            using var paste = new BrushEditorViewModel(new SimplePropertyAdapter<Brush?>((SimpleProperty<Brush?>)destination.Fill, destination));
            IClipboard clipboard = window.Clipboard!;
            var services = new PropertyEditorTestHost.ServiceVisitor(
                (typeof(IClipboardGateway), new AvaloniaClipboardGateway(clipboard)),
                (typeof(HistoryManager), history.History));
            copy.Accept(services);
            paste.Accept(services);

            Assert.That(await copy.CopyAsync(), Is.True);
            string? json = await clipboard.TryGetValueAsync(BeutlDataFormats.Brush);
            Assert.That(json, Is.Not.Null);
            Assert.That(await clipboard.TryGetTextAsync(), Is.EqualTo(json));

            await paste.RefreshCanPasteAsync();
            Assert.That(((IObjectEditorCommands)paste).CanPaste.Value, Is.True);
            Assert.That(await paste.PasteAsync(), Is.True);
            Assert.That(destination.Fill.Value, Is.TypeOf<SolidColorBrush>().And.Not.SameAs(brush));
            Assert.That(((SolidColorBrush)destination.Fill.Value!).Color.Value, Is.EqualTo(Colors.Red));

            await clipboard.SetTextAsync(json);
            Assert.That(await paste.PasteAsync(), Is.False);
        }
        finally
        {
            window.Close();
        }
    }
}
