using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace BeUtl.Animation;

internal static class AnimationSerializer
{
    public static (string?, JsonNode?) ToJson(this IAnimation animation, Type targetType)
    {
        if (animation.Children.Count > 0)
        {
            var animations = new JsonArray();

            foreach (IAnimationSpan item in animation.Children)
            {
                JsonNode anmNode = new JsonObject();
                item.WriteToJson(ref anmNode);
                animations.Add(anmNode);
            }

            return (animation.Property.GetMetadata<CorePropertyMetadata>(targetType).SerializeName ?? animation.Property.Name, animations);
        }
        else
        {
            return default;
        }
    }

    public static IAnimation? ToAnimation(this JsonNode json, string name, Type targetType)
    {
        if (json is JsonArray jsonArray)
        {
            CoreProperty? property
            = PropertyRegistry.GetRegistered(targetType).FirstOrDefault(
                x => x.GetMetadata<CorePropertyMetadata>(targetType).SerializeName == name || x.Name == name);

            if (property == null)
                return null;

            var helper = (IGenericHelper)typeof(GenericHelper<>)
                .MakeGenericType(property.PropertyType)
                .GetField("Instance")!
                .GetValue(null)!;

            var animations = new List<AnimationSpan>();

            if (animations.Capacity < jsonArray.Count)
            {
                animations.Capacity = jsonArray.Count;
            }
            foreach (JsonNode? item in jsonArray)
            {
                if (item is JsonObject animationObj)
                {
                    animations.Add(helper.DeserializeAnimation(animationObj));
                }
            }

            return helper.InitializeAnimation(property, animations);
        }
        else
        {
            return null;
        }
    }

    private interface IGenericHelper
    {
        IAnimation InitializeAnimation(CoreProperty property, IEnumerable<AnimationSpan> animations);

        AnimationSpan DeserializeAnimation(JsonObject json);
    }

    private sealed class GenericHelper<T> : IGenericHelper
    {
        public static readonly GenericHelper<T> Instance = new();

        public IAnimation InitializeAnimation(CoreProperty property, IEnumerable<AnimationSpan> animations)
        {
            var animation = new Animation<T>((CoreProperty<T>)property);
            animation.Children.AddRange(animations.OfType<AnimationSpan<T>>());

            return animation;
        }

        public AnimationSpan DeserializeAnimation(JsonObject json)
        {
            var anm = new AnimationSpan<T>();
            anm.ReadFromJson(json);
            return anm;
        }
    }
}
