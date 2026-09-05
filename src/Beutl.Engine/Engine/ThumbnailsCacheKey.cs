using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Beutl.Engine;

internal static class ThumbnailsCacheKey
{
    // Insertion order fixes the hash: reordering these writes invalidates every stored cache entry.
    public static string Compute(JsonObject fullJson, ReadOnlySpan<string> targetProps)
    {
        var cacheJson = new JsonObject();

        foreach (var prop in targetProps)
        {
            if (fullJson.TryGetPropertyValue(prop, out var node))
                cacheJson[prop] = node?.DeepClone();
        }

        CopyTargeted(fullJson, cacheJson, "Animations", targetProps);
        CopyTargeted(fullJson, cacheJson, "Expressions", targetProps);

        var jsonStr = cacheJson.ToJsonString();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(jsonStr));
        return Convert.ToHexString(hash);
    }

    private static void CopyTargeted(
        JsonObject fullJson, JsonObject cacheJson, string section, ReadOnlySpan<string> targetProps)
    {
        if (!fullJson.TryGetPropertyValue(section, out var node) || node is not JsonObject sectionObj)
            return;

        var filtered = new JsonObject();
        foreach (var prop in targetProps)
        {
            if (sectionObj.TryGetPropertyValue(prop, out var n))
                filtered[prop] = n?.DeepClone();
        }

        if (filtered.Count > 0)
            cacheJson[section] = filtered;
    }
}
