using Beutl.Language;
using Beutl.Media;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

public sealed class LayerAttributeService : ILayerAttributeService
{
    private readonly HistoryManager _historyManager;

    public LayerAttributeService(HistoryManager historyManager)
    {
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
    }

    public bool SetEnabled(Scene scene, int zIndex, bool newEnabled)
    {
        ArgumentNullException.ThrowIfNull(scene);

        bool changed = false;
        foreach (Element element in scene.Children)
        {
            if (element.ZIndex != zIndex || element.IsEnabled == newEnabled) continue;
            element.IsEnabled = newEnabled;
            changed = true;
        }

        if (!changed) return false;

        _historyManager.Commit(CommandNames.ChangeLayerEnabled);
        return true;
    }

    public bool SetColor(Scene scene, int zIndex, Color color, string defaultName)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(defaultName);

        TimelineLayer? existing = null;
        foreach (TimelineLayer layer in scene.Layers)
        {
            if (layer.ZIndex == zIndex)
            {
                existing = layer;
                break;
            }
        }

        if (existing is null)
        {
            // No TimelineLayer for this zIndex yet — materialize one to persist
            // the color, else the picker no-ops on a fresh layer.
            var created = new TimelineLayer
            {
                Name = defaultName,
                Color = color,
                ZIndex = zIndex,
            };
            scene.Layers.Add(created);
            _historyManager.Commit(CommandNames.ChangeLayerColor);
            return true;
        }

        if (existing.Color == color) return false;

        existing.Color = color;
        _historyManager.Commit(CommandNames.ChangeLayerColor);
        return true;
    }
}
