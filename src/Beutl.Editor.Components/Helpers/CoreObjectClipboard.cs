using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Beutl.Editor.Services;
using Beutl.Logging;
using Beutl.Serialization;
using Microsoft.Extensions.Logging;

namespace Beutl.Editor.Components.Helpers;

public static class CoreObjectClipboard
{
    private static readonly ILogger s_logger = Log.CreateLogger(typeof(CoreObjectClipboard));

    public static async ValueTask<bool> CopyAsync(ICoreSerializable obj, DataFormat<string> format)
    {
        IClipboard? clipboard = ClipboardHelper.GetClipboard();
        if (clipboard == null) return false;

        string json = CoreSerializer.SerializeToJsonString(obj);
        var data = new DataTransfer();
        data.Add(DataTransferItem.CreateText(json));
        data.Add(DataTransferItem.Create(format, json));
        await clipboard.SetDataAsync(data);
        return true;
    }

    public static async ValueTask<string?> TryGetJsonAsync(IClipboard clipboard, DataFormat<string> format)
    {
        if (await clipboard.TryGetValueAsync(format) is not { } data) return null;
        return IsJsonData(data) ? data : null;
    }

    // 復元した複製の Id を振り直して返す
    public static bool TryDeserializeJson<T>(string? json, [NotNullWhen(true)] out T? result)
        where T : class, ICoreObject
    {
        result = null;
        if (string.IsNullOrEmpty(json)) return false;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject jsonObj) return false;

            Type baseType = typeof(T);
            Type? actualType = CoreSerializer.ResolveActualType(jsonObj, baseType);
            if (actualType == null || !actualType.IsAssignableTo(baseType)) return false;

            if (Activator.CreateInstance(actualType) is not ICoreSerializable tmp) return false;
            CoreSerializer.PopulateFromJsonObject(tmp, actualType, jsonObj);

            ObjectRegenerator.Regenerate(tmp, actualType, out ICoreSerializable newInstance);
            result = (T)newInstance;
            return true;
        }
        catch (Exception ex)
        {
            s_logger.LogWarning(ex, "Failed to deserialize clipboard JSON as {Type}", typeof(T).Name);
            return false;
        }
    }

    // 先頭が '{' かどうかだけを見る簡易判定
    public static bool IsJsonData(string? data)
    {
        if (string.IsNullOrWhiteSpace(data)) return false;
        ReadOnlySpan<char> span = data.AsSpan().TrimStart();
        return span[0] == '{';
    }
}
