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

    // クリップボードにICoreSerializableオブジェクトをJSON化して保存する
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

    // クリップボードから指定フォーマットのJSON文字列を取得
    public static async ValueTask<string?> TryGetJsonAsync(IClipboard clipboard, DataFormat<string> format)
    {
        if (await clipboard.TryGetValueAsync(format) is not { } data) return null;
        return IsJsonData(data) ? data : null;
    }

    // JSON文字列から指定型のオブジェクトに復元し、Idを新規生成して返す
    public static bool TryDeserializeJson<T>(string? json, [NotNullWhen(true)] out T? result)
        where T : class, ICoreObject
    {
        result = null;
        if (string.IsNullOrEmpty(json)) return false;
        try
        {
            if (JsonNode.Parse(json) is not JsonObject jsonObj) return false;

            Type baseType = typeof(T);
            Type? actualType = baseType.IsSealed ? baseType : jsonObj.GetDiscriminator(baseType);
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

    // 文字列がJSON形式かどうかを判定する
    public static bool IsJsonData(string? data)
    {
        if (string.IsNullOrWhiteSpace(data)) return false;
        ReadOnlySpan<char> span = data.AsSpan().TrimStart();
        return span.Length > 0 && span[0] == '{';
    }
}
