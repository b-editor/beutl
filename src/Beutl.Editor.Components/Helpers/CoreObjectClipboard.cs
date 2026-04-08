using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Beutl.Editor.Services;
using Beutl.Serialization;

namespace Beutl.Editor.Components.Helpers;

public static class CoreObjectClipboard
{
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

            if (Activator.CreateInstance(actualType) is not T instance) return false;

            CoreSerializer.PopulateFromJsonObject(instance, actualType, jsonObj);
            ObjectRegenerator.Regenerate(instance, out string newJson);
            var newJsonNode = JsonNode.Parse(newJson) as JsonObject;
            if (newJsonNode == null) return false;

            var newInstance = (T)Activator.CreateInstance(actualType)!;
            CoreSerializer.PopulateFromJsonObject(newInstance, actualType, newJsonNode);

            result = newInstance;
            return true;
        }
        catch
        {
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
