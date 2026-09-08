using System.Text.Json.Serialization;
using Beutl.Configuration;

namespace Beutl.Graphics.Rendering.Cache;

[JsonSerializable(typeof(RenderCacheOptions))]
public record RenderCacheOptions(bool IsEnabled, RenderCacheRules Rules)
{
    public static readonly RenderCacheOptions Disabled = new(false, RenderCacheRules.Default);
    public static readonly RenderCacheOptions Enabled = new(true, RenderCacheRules.Default);
    public static readonly RenderCacheOptions Default = Disabled;

    public static RenderCacheOptions CreateFromGlobalConfiguration()
    {
        EditorConfig config = GlobalConfiguration.Instance.EditorConfig;
        return new RenderCacheOptions(
            config.IsNodeCacheEnabled,
            RenderCacheRules.Create(config.NodeCacheMaxPixels, config.NodeCacheMinPixels));
    }
}
