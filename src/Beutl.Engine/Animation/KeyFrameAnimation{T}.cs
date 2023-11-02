using System.Text.Json.Nodes;

using Beutl.Serialization;

namespace Beutl.Animation;

public class KeyFrameAnimation<T> : KeyFrameAnimation, IAnimation<T>
{
    public KeyFrameAnimation(CoreProperty<T> property)
        : base(property)
    {
    }
    
    public KeyFrameAnimation()
    {
    }

    public new CoreProperty<T> Property
    {
        get => (CoreProperty<T>)base.Property;
        set => base.Property = value;
    }

    public override void ApplyAnimation(Animatable target, IClock clock)
    {
        if (UseGlobalClock)
        {
            target.SetValue(Property, Interpolate(clock.GlobalClock.CurrentTime));
        }
        else
        {
            target.SetValue(Property, Interpolate(clock.CurrentTime));
        }
    }

    public T? GetAnimatedValue(IClock clock)
    {
        return Interpolate(UseGlobalClock ? clock.GlobalClock.CurrentTime : clock.CurrentTime);
    }

    public T? Interpolate(TimeSpan timeSpan)
    {
        (IKeyFrame? prev, IKeyFrame? next) = GetPreviousAndNextKeyFrame(timeSpan);

        if (next is KeyFrame<T> next2)
        {
            T? nextValue = next2.Value;
            T? prevValue = prev is KeyFrame<T> prev2 ? prev2.Value : nextValue;
            TimeSpan prevTime = prev?.KeyTime ?? TimeSpan.Zero;
            TimeSpan nextTime = next.KeyTime;
            // Zero除算になるので
            if (nextTime == prevTime)
            {
                return nextValue;
            }

            float progress = (float)((timeSpan - prevTime) / (nextTime - prevTime));
            float ease = next.Easing.Ease(progress);
            // どちらかがnullの場合、片方を返す
            if (prevValue == null)
                return nextValue;
            else if (nextValue == null)
                return prevValue;

            T? value = KeyFrame<T>.s_animator.Interpolate(ease, prevValue, nextValue);

            return value;
        }
        else if (prev is KeyFrame<T> prev2)
        {
            return prev2.Value;
        }
        else
        {
            return KeyFrame<T>.s_animator.DefaultValue();
        }
    }

    [ObsoleteSerializationApi]
    public override void ReadFromJson(JsonObject json)
    {
        base.ReadFromJson(json);
        if (json.TryGetPropertyValue(nameof(KeyFrames), out JsonNode? keyframesNode)
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

    [ObsoleteSerializationApi]
    public override void WriteToJson(JsonObject json)
    {
        base.WriteToJson(json);

        var array = new JsonArray();

        foreach (IKeyFrame item in KeyFrames.GetMarshal().Value)
        {
            var itemJson = new JsonObject();
            item.WriteToJson(itemJson);

            array.Add(itemJson);
        }

        json[nameof(KeyFrames)] = array;
    }

    public override void Serialize(ICoreSerializationContext context)
    {
        base.Serialize(context);
        context.SetValue(nameof(KeyFrames), KeyFrames);
    }

    public override void Deserialize(ICoreSerializationContext context)
    {
        base.Deserialize(context);

        if (context.GetValue<KeyFrames>(nameof(KeyFrames)) is { } keyframes)
        {
            KeyFrames.Replace(keyframes);
        }
    }
}
