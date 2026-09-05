using Beutl.Editor.Services.Captions;
using Beutl.Media;

namespace Beutl.Services.AI;

internal static class CaptionPresentationDefaults
{
    internal const string FontFamilyName = "Noto Sans JP";

    internal static FontFamily FontFamily { get; } = new(FontFamilyName);

    internal static DefaultTextCaptionElementFactory ElementFactory { get; } = new(FontFamily);

    internal static CaptionTemplateContribution CreateDefaultText(string name)
        => CaptionTemplateDefaults.CreateDefaultText(name, ElementFactory);
}
