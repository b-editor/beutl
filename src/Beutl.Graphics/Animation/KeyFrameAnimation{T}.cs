using System.Text.Json.Nodes;

namespace Beutl.Animation;

public class KeyFrameAnimation<T> : KeyFrameAnimation, IAnimation<T>
{
    public KeyFrameAnimation(CoreProperty<T> property)
        : base(property)
    {
    }

    public new CoreProperty<T> Property
    {
        get => (CoreProperty<T>)base.Property;
        set => base.Property = value;
    }

    public override void ApplyAnimation(Animatable target, IClock clock)
    {
        target.SetValue(Property, Interpolate(clock.CurrentTime));
    }

    public T Interpolate(TimeSpan timeSpan)
    {
        (IKeyFrame? prev, IKeyFrame? next) = GetPreviousAndNextKeyFrame(timeSpan);
        T defaultValue = KeyFrame<T>.s_animator.DefaultValue();

        if (next is KeyFrame<T> next2)
        {
            T prevValue = prev is KeyFrame<T> prev2 ? prev2.Value : defaultValue;
            T nextValue = next2.Value;
            TimeSpan prevTime = prev?.KeyTime ?? TimeSpan.Zero;
            TimeSpan nextTime = next.KeyTime;

            float progress = (float)((timeSpan - prevTime) / (nextTime - prevTime));
            float ease = next.Easing.Ease(progress);
            T value = KeyFrame<T>.s_animator.Interpolate(ease, prevValue, nextValue);

            return value;
        }
        else if (prev is KeyFrame<T> prev2)
        {
            return prev2.Value;
        }
        else
        {
            return defaultValue;
        }
    }

    public override void ReadFromJson(JsonNode json)
    {
        base.ReadFromJson(json);
        if (json is JsonObject obj)
        {
            if (obj.TryGetPropertyValue(nameof(KeyFrames), out JsonNode? keyframesNode)
                && keyframesNode is JsonArray keyframesArray)
            {
                KeyFrames.Clear();
                KeyFrames.EnsureCapacity(keyframesArray.Count);

                foreach (JsonObject childJson in keyframesArray.OfType<JsonObject>())
                {
                    var item = new KeyFrame<T>();
                    item.ReadFromJson(childJson);
                    KeyFrames.Add(item);
                }
            }
        }
    }

    public override void WriteToJson(ref JsonNode json)
    {
        base.WriteToJson(ref json);

        var array = new JsonArray();

        foreach (KeyFrame<T> item in KeyFrames.GetMarshal().Value)
        {
            JsonNode node = new JsonObject();
            item.WriteToJson(ref node);

            array.Add(node);
        }

        json[nameof(KeyFrames)] = array;
    }
}
