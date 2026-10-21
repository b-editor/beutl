using Beutl.Editor.Services.Captions;
using Beutl.Media;

namespace Beutl.Editor.Components.AiWorkspaceTab.Services;

internal static class CaptionPresentationDefaults
{
    internal const string FontFamilyName = "Noto Sans JP";

    internal static FontFamily FontFamily { get; } = new(FontFamilyName);

    internal static DefaultTextCaptionElementFactory ElementFactory { get; } = new(FontFamily);

    internal static CaptionTemplate CreateDefaultText(string name)
        => CaptionTemplateDefaults.CreateDefaultText(name, ElementFactory);
}
