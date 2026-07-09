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
        // Backstop for a locked clip whose color edit slips past a UI guard (e.g. a color picker
        // confirmed after the clip was locked). Layer-lock is enforced by the caller's IsEditable,
        // which has the scene the element alone lacks.
        if (element.IsLocked) return;
        if (element.AccentColor == color) return;

        element.AccentColor = color;
        _historyManager.Commit(CommandNames.ChangeElementColor);
    }

    public void SetLocked(Element element, bool isLocked)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (element.IsLocked == isLocked) return;

        element.IsLocked = isLocked;
        _historyManager.Commit(CommandNames.ChangeElementLocked);
    }
}
