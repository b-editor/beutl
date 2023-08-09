using System.ComponentModel;
using System.Text.Json.Nodes;

using Beutl.Media;

namespace Beutl.Animation;

public abstract class Animatable : CoreObject, IAnimatable
{
    public static readonly CoreProperty<Animations> AnimationsProperty;

    static Animatable()
    {
        AnimationsProperty = ConfigureProperty<Animations, Animatable>(nameof(Animations))
            .Accessor(o => o.Animations)
            .RegisterStatic();
    }

    protected Animatable()
    {
        Animations = new();
    }

    [NotAutoSerialized]
    [Browsable(false)]
    public Animations Animations { get; }

    public event EventHandler<RenderInvalidatedEventArgs> AnimationInvalidated
    {
        add => Animations.Invalidated += value;
        remove => Animations.Invalidated -= value;
    }

    // Todo: オブジェクトを作成したものがIClockを指定する
    public virtual void ApplyAnimations(IClock clock)
    {
        foreach (IAnimation? item in Animations.GetMarshal().Value)
        {
            item.ApplyAnimation(this, clock);
        }
    }

    public override void ReadFromJson(JsonObject json)
    {
        base.ReadFromJson(json);
        if (json.TryGetPropertyValue("animations", out JsonNode? animationsNode)
            && animationsNode is JsonObject animationsObj)
        {
            Animations.Clear();
            Animations.EnsureCapacity(animationsObj.Count);

            Type type = GetType();
            foreach ((string name, JsonNode? node) in animationsObj)
            {
                if (node?.ToAnimation(name, type) is IAnimation animation)
                {
                    Animations.Add(animation);
                }
            }
        }
    }

    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);
        var animations = new JsonObject();

        foreach (IAnimation item in Animations.GetMarshal().Value)
        {
            if (item.ToJson() is { } itemJson)
            {
                animations.Add(item.Property.Name, itemJson);
            }
        }

        if (animations.Count > 0)
        {
            json["animations"] = animations;
        }
    }
}
