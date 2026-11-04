using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.Services;
using Beutl.Editor.Services;

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
}
