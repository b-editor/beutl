using System.Text.Json;
using System.Text.Json.Nodes;

using BeUtl.Animation;
using BeUtl.Collections;
using BeUtl.Commands;

namespace BeUtl.ProjectSystem;

public interface IAnimatablePropertyInstance : IPropertyInstance, ILogicalElement
{
    public IReadOnlyList<IAnimationSpan> Children { get; }

    public void SetProperty(TimeSpan progress);

    public IRecordableCommand AddChild(IAnimationSpan animation);

    public IRecordableCommand RemoveChild(IAnimationSpan animation);

    public IRecordableCommand InsertChild(int index, IAnimationSpan animation);
}

public class AnimatablePropertyInstance<T> : PropertyInstance<T>, IAnimatablePropertyInstance
{
    private readonly Animation<T> _animation;

    public AnimatablePropertyInstance(CoreProperty<T> property)
        : base(property)
    {
        _animation = new Animation<T>(property);
    }

    public IObservableList<AnimationSpan<T>> Children => _animation.Children;

    IReadOnlyList<IAnimationSpan> IAnimatablePropertyInstance.Children => _animation.Children;

    public void SetProperty(TimeSpan progress)
    {
        if (Children.Count < 1)
        {
            Parent.SetValue(Property, Value);
        }
        else
        {
            Parent.SetValue(Property, _animation.Interpolate(progress));
        }
    }

    public IRecordableCommand AddChild(AnimationSpan<T> animation)
    {
        ArgumentNullException.ThrowIfNull(animation);

        return new AddCommand<AnimationSpan<T>>(_animation.Children, animation, Children.Count);
    }

    public IRecordableCommand RemoveChild(AnimationSpan<T> animation)
    {
        ArgumentNullException.ThrowIfNull(animation);

        return new RemoveCommand<AnimationSpan<T>>(_animation.Children, animation);
    }

    public IRecordableCommand InsertChild(int index, AnimationSpan<T> animation)
    {
        ArgumentNullException.ThrowIfNull(animation);

        return new AddCommand<AnimationSpan<T>>(_animation.Children, animation, index);
    }

    public override void ReadFromJson(JsonNode json)
    {
        _animation.Children.Clear();
        if (json is JsonObject jsonobj)
        {
            if (jsonobj.TryGetPropertyValue("children", out JsonNode? childrenNode) &&
                childrenNode is JsonArray jsonArray)
            {
                foreach (JsonNode? item in jsonArray)
                {
                    if (item is JsonObject jobj)
                    {
                        var anm = new AnimationSpan<T>();
                        anm.ReadFromJson(jobj);
                        _animation.Children.Add(anm);
                    }
                }
            }

            if (jsonobj.TryGetPropertyValue("value", out JsonNode? valueNode))
            {
                base.ReadFromJson(valueNode!);
            }
        }
        else
        {
            base.ReadFromJson(json);
        }
    }

    public override void WriteToJson(ref JsonNode node)
    {
        if (Children.Count == 0)
        {
            base.WriteToJson(ref node);
        }
        else
        {
            var jsonObj = new JsonObject();
            var jsonArray = new JsonArray();
            foreach (AnimationSpan<T> item in _animation.Children)
            {
                JsonNode json = new JsonObject();
                item.WriteToJson(ref json);
                jsonArray.Add(json);
            }

            JsonNode valueNode = new JsonObject();
            base.WriteToJson(ref valueNode);
            jsonObj["value"] = valueNode;
            jsonObj["children"] = jsonArray;

            node = jsonObj;
        }
    }

    IRecordableCommand IAnimatablePropertyInstance.AddChild(IAnimationSpan animation)
    {
        return AddChild((AnimationSpan<T>)animation);
    }

    IRecordableCommand IAnimatablePropertyInstance.RemoveChild(IAnimationSpan animation)
    {
        return RemoveChild((AnimationSpan<T>)animation);
    }

    IRecordableCommand IAnimatablePropertyInstance.InsertChild(int index, IAnimationSpan animation)
    {
        return InsertChild(index, (AnimationSpan<T>)animation);
    }
}
