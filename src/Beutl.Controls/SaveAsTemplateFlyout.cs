using Beutl.Language;

namespace Beutl.Controls;

internal sealed class SaveAsTemplateFlyout()
    : TextInputFlyout(Strings.SaveAsTemplate, 280, Strings.EnterTemplateName, selectAllOnOpening: true);
