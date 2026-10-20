using Beutl.Language;

namespace Beutl.Controls;

public sealed class SaveAsTemplateFlyout()
    : TextInputFlyout(Strings.SaveAsTemplate, 280, Strings.EnterTemplateName, selectAllOnOpening: true);
