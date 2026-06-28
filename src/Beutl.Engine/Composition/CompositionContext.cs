using Beutl.Engine;
using Beutl.Media.Proxy;

namespace Beutl.Composition;

public class CompositionContext(TimeSpan time)
{
    public static CompositionContext Default { get; } = new(TimeSpan.Zero);

    public IList<EngineObject.Resource>? Flow { get; set; }

    public TimeSpan Time { get; set; } = time;

    public bool DisableResourceShare { get; set; }

    public bool ForceOriginalSource { get; set; }

    public bool PreferProxy { get; set; }

    public ProxyPreset PreferredProxyPreset { get; set; } = ProxyPreset.Quarter;

    public virtual T Get<T>(IProperty<T> property)
    {
        if (property == null)
            throw new ArgumentNullException(nameof(property));
        return property.GetValue(this);
    }
}
