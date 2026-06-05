using Beutl.Language;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

public sealed class ElementAttributeService : IElementAttributeService
{
    private readonly HistoryManager _historyManager;

    public ElementAttributeService(HistoryManager historyManager)
    {
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
    }

    public void SetEnabled(Element element, bool isEnabled)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.IsEnabled == isEnabled) return;

        element.IsEnabled = isEnabled;
        _historyManager.Commit(CommandNames.ChangeElementEnabled);
    }

    public void SetAccentColor(Element element, Color color)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.AccentColor == color) return;

        element.AccentColor = color;
        _historyManager.Commit(CommandNames.ChangeElementColor);
    }
}
