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

        TimelineLayer? existing = FindLayer(scene, zIndex);
        if (existing is null)
        {
            // No TimelineLayer for this zIndex yet — materialize one to persist
            // the color, else the picker no-ops on a fresh layer.
            var created = new TimelineLayer
            {
                Name = { Value = defaultName },
                Color = { Value = color },
                ZIndex = { Value = zIndex },
            };
            scene.Layers.Add(created);
            _historyManager.Commit(CommandNames.ChangeLayerColor);
            return true;
        }

        if (existing.Color.Value == color) return false;

        existing.Color.Value = color;
        _historyManager.Commit(CommandNames.ChangeLayerColor);
        return true;
    }

    public bool SetLocked(Scene scene, int zIndex, bool isLocked)
        => SetLayerFlag(scene, zIndex, isLocked, l => l.IsLocked.Value, (l, v) => l.IsLocked.Value = v, CommandNames.ChangeLayerLocked);

    public bool SetAudioMuted(Scene scene, int zIndex, bool isMuted)
        => SetLayerFlag(scene, zIndex, isMuted, l => l.IsAudioMuted.Value, (l, v) => l.IsAudioMuted.Value = v, CommandNames.ChangeLayerAudioMuted);

    public bool SetVideoMuted(Scene scene, int zIndex, bool isMuted)
        => SetLayerFlag(scene, zIndex, isMuted, l => l.IsVideoMuted.Value, (l, v) => l.IsVideoMuted.Value = v, CommandNames.ChangeLayerVideoMuted);

    public bool SetSolo(Scene scene, int zIndex, bool isSolo)
        => SetLayerFlag(scene, zIndex, isSolo, l => l.IsSolo.Value, (l, v) => l.IsSolo.Value = v, CommandNames.ChangeLayerSolo);

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
            layer = new TimelineLayer { ZIndex = { Value = zIndex } };
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
        => layer is { IsLocked.Value: false, IsAudioMuted.Value: false, IsVideoMuted.Value: false, IsSolo.Value: false }
           && string.IsNullOrEmpty(layer.Name.Value)
           && layer.Color.Value.A == 0;

    private static TimelineLayer? FindLayer(Scene scene, int zIndex)
    {
        foreach (TimelineLayer layer in scene.Layers)
        {
            if (layer.ZIndex.Value == zIndex) return layer;
        }

        return null;
    }
}
