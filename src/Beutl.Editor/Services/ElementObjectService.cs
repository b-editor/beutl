using System.Text.Json.Nodes;
using Beutl.Engine;
using Beutl.Language;
using Beutl.Logging;
using Beutl.ProjectSystem;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Services;

public sealed class ElementObjectService : IElementObjectService
{
    private static readonly ILogger s_logger = Log.CreateLogger<ElementObjectService>();

    private readonly HistoryManager _historyManager;

    public ElementObjectService(HistoryManager historyManager)
    {
        _historyManager = historyManager ?? throw new ArgumentNullException(nameof(historyManager));
    }

    public void Add(Element element, EngineObject obj)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(obj);

        element.AddObject(obj);
        _historyManager.Commit(CommandNames.AddObject);
    }

    public void InsertAt(Element element, int index, EngineObject obj)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(obj);

        int clamped = Math.Clamp(index, 0, element.Objects.Count);
        element.InsertObject(clamped, obj);
        _historyManager.Commit(CommandNames.AddObject);
    }

    public bool Remove(Element element, EngineObject obj)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(obj);
        if (!element.Objects.Contains(obj)) return false;

        element.RemoveObject(obj);
        _historyManager.Commit(CommandNames.RemoveObject);
        return true;
    }

    public bool Move(Element element, int oldIndex, int newIndex)
    {
        ArgumentNullException.ThrowIfNull(element);
        if (oldIndex == newIndex) return false;
        if (oldIndex < 0 || oldIndex >= element.Objects.Count) return false;
        if (newIndex < 0 || newIndex >= element.Objects.Count) return false;

        element.Objects.Move(oldIndex, newIndex);
        _historyManager.Commit(CommandNames.MoveObject);
        return true;
    }

    public ObjectPasteOutcome PasteOver(Element element, int index, string json)
    {
        ArgumentNullException.ThrowIfNull(element);
        ArgumentNullException.ThrowIfNull(json);
        if (index < 0 || index >= element.Objects.Count) return ObjectPasteOutcome.InvalidJson;

        if (JsonNode.Parse(json) is not JsonObject newJson)
        {
            return ObjectPasteOutcome.InvalidJson;
        }

        Type? type = newJson.GetDiscriminator();
        if (type is null || !type.IsAssignableTo(typeof(EngineObject)))
        {
            return ObjectPasteOutcome.MissingType;
        }

        try
        {
            EngineObject? obj = Activator.CreateInstance(type) as EngineObject;
            if (obj is null) return ObjectPasteOutcome.MissingType;

            CoreSerializer.PopulateFromJsonObject(obj, type, newJson);
            element.Objects[index] = obj;
            _historyManager.Commit(CommandNames.PasteObject);
            return ObjectPasteOutcome.Pasted;
        }
        catch (Exception ex)
        {
            s_logger.LogError(ex, "PasteOver failed while materializing EngineObject of type {Type}.", type);
            return ObjectPasteOutcome.UnexpectedError;
        }
    }

    public bool SetEnabled(EngineObject obj, bool isEnabled)
    {
        ArgumentNullException.ThrowIfNull(obj);
        if (obj.IsEnabled == isEnabled) return false;

        obj.IsEnabled = isEnabled;
        _historyManager.Commit(CommandNames.ChangeObjectEnabled);
        return true;
    }
}
