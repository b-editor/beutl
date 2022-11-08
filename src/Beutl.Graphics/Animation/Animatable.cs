using System.Text.Json.Nodes;

namespace Beutl.Animation;

public abstract class Animatable : CoreObject, IAnimatable
{
    protected Animatable()
    {
        Animations = new();
    }

    public Animations Animations { get; }

    public event EventHandler AnimationInvalidated
    {
        add => Animations.Invalidated += value;
        remove => Animations.Invalidated -= value;
    }

    public virtual void ApplyAnimations(IClock clock)
    {
        foreach (IAnimation? item in Animations.GetMarshal().Value)
        {
            item.ApplyTo(this, clock.CurrentTime);
        }
    }

    public override void ReadFromJson(JsonNode json)
    {
        base.ReadFromJson(json);
        if (json is JsonObject jobject)
        {
            if (jobject.TryGetPropertyValue("animations", out JsonNode? animationsNode)
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
    }

    public override void WriteToJson(ref JsonNode json)
    {
        base.WriteToJson(ref json);
        if (json is JsonObject jobject)
        {
            Type type = GetType();
            var animations = new JsonObject();

            foreach (IAnimation item in Animations.GetMarshal().Value)
            {
                if (item.ToJson(type) is (string name, JsonNode node))
                {
                    animations.Add(name, node);
                }
            }

            if (animations.Count > 0)
            {
                jobject["animations"] = animations;
            }
        }
    }
}
