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

    public bool SetLocked(Scene scene, int zIndex, bool isLocked)
        => SetLayerFlag(scene, zIndex, isLocked, l => l.IsLocked, (l, v) => l.IsLocked = v, CommandNames.ChangeLayerLocked);

    public bool SetAudioMuted(Scene scene, int zIndex, bool isMuted)
        => SetLayerFlag(scene, zIndex, isMuted, l => l.IsAudioMuted, (l, v) => l.IsAudioMuted = v, CommandNames.ChangeLayerAudioMuted);

    public bool SetVideoMuted(Scene scene, int zIndex, bool isMuted)
        => SetLayerFlag(scene, zIndex, isMuted, l => l.IsVideoMuted, (l, v) => l.IsVideoMuted = v, CommandNames.ChangeLayerVideoMuted);

    public bool SetSolo(Scene scene, int zIndex, bool isSolo)
        => SetLayerFlag(scene, zIndex, isSolo, l => l.IsSolo, (l, v) => l.IsSolo = v, CommandNames.ChangeLayerSolo);

    private bool SetLayerFlag(
        Scene scene, int zIndex, bool newValue,
        Func<TimelineLayer, bool> getter,
        Action<TimelineLayer, bool> setter,
        string commandName)
    {
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(commandName);

        TimelineLayer? layer = FindLayer(scene, zIndex);
        if (layer is null)
        {
            if (!newValue) return false;
            layer = new TimelineLayer { ZIndex = zIndex };
            scene.Layers.Add(layer);
        }
        else if (getter(layer) == newValue)
        {
            return false;
        }

        setter(layer, newValue);
        if (!newValue && IsEmptyLayerModel(layer))
        {
            scene.Layers.Remove(layer);
        }

        _historyManager.Commit(commandName);
        return true;
    }

    // A model holding only defaults is indistinguishable from having no model,
    // so prune it instead of letting toggled-off layers accumulate in the file.
    // Alpha 0 covers both default(Color) (#00000000) and Colors.Transparent
    // (#00FFFFFF) — either renders as "no tint" in the layer header.
    private static bool IsEmptyLayerModel(TimelineLayer layer)
        => layer is { IsLocked: false, IsAudioMuted: false, IsVideoMuted: false, IsSolo: false }
           && string.IsNullOrEmpty(layer.Name)
           && layer.Color.A == 0;

    private static TimelineLayer? FindLayer(Scene scene, int zIndex)
    {
        foreach (TimelineLayer layer in scene.Layers)
        {
            if (layer.ZIndex == zIndex) return layer;
        }

        return null;
    }
}
