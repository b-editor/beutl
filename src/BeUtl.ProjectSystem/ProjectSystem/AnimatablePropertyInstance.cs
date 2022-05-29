using System.Text.Json;
using System.Text.Json.Nodes;

using BeUtl.Animation;
using BeUtl.Collections;
using BeUtl.Commands;

namespace BeUtl.ProjectSystem;

public interface IAnimatablePropertyInstance : IPropertyInstance, ILogicalElement
{
    public IReadOnlyList<IAnimation> Children { get; }

    public void SetProperty(TimeSpan progress);

    public IRecordableCommand AddChild(IAnimation animation);

    public IRecordableCommand RemoveChild(IAnimation animation);

    public IRecordableCommand InsertChild(int index, IAnimation animation);
}

public class AnimatablePropertyInstance<T> : PropertyInstance<T>, IAnimatablePropertyInstance
{
    private readonly CoreList<Animation<T>> _children;

    public AnimatablePropertyInstance()
    {
        _children = new CoreList<Animation<T>>();
    }

    public AnimatablePropertyInstance(CoreProperty<T> property)
        : base(property)
    {
        _children = new CoreList<Animation<T>>();
    }

    public IObservableList<Animation<T>> Children => _children;

    IReadOnlyList<IAnimation> IAnimatablePropertyInstance.Children => _children;

    public void SetProperty(TimeSpan progress)
    {
        void EaseAndSet(Animation<T> animation, float progress)
        {
            // イージングする
            float ease = animation.Easing.Ease(progress);
            // 値を補間する
            T value = animation.Animator.Interpolate(ease, animation.Previous, animation.Next);
            // 値をセット
            Parent.SetValue(Property, value);
        }

        if (_children.Count < 1)
        {
            Parent.SetValue(Property, Value);
        }
        else
        {
            TimeSpan cur = TimeSpan.Zero;
            for (int i = 0; i < _children.Count; i++)
            {
                Animation<T> item = _children[i];

                TimeSpan next = cur + item.Duration;
                if (cur <= progress && progress < next)
                {
                    // 相対的なTimeSpan
                    TimeSpan time = progress - cur;
                    EaseAndSet(item, (float)(time / item.Duration));
                    return;
                }
                else
                {
                    cur = next;
                }
            }

            EaseAndSet(_children[^1], 1);
        }
    }

    public IRecordableCommand AddChild(Animation<T> animation)
    {
        ArgumentNullException.ThrowIfNull(animation);

        return new AddCommand<Animation<T>>(_children, animation, Children.Count);
    }

    public IRecordableCommand RemoveChild(Animation<T> animation)
    {
        ArgumentNullException.ThrowIfNull(animation);

        return new RemoveCommand<Animation<T>>(_children, animation);
    }

    public IRecordableCommand InsertChild(int index, Animation<T> animation)
    {
        ArgumentNullException.ThrowIfNull(animation);

        return new AddCommand<Animation<T>>(_children, animation, index);
    }

    public override void ReadFromJson(JsonNode json)
    {
        _children.Clear();
        if (json is JsonObject jsonobj)
        {
            if (jsonobj.TryGetPropertyValue("children", out JsonNode? childrenNode) &&
                childrenNode is JsonArray jsonArray)
            {
                foreach (JsonNode? item in jsonArray)
                {
                    if (item is JsonObject jobj)
                    {
                        var anm = new Animation<T>();
                        anm.ReadFromJson(jobj);
                        _children.Add(anm);
                    }
                }
            }

            if (jsonobj.TryGetPropertyValue("value", out JsonNode? valueNode))
            {
                T? value = JsonSerializer.Deserialize<T>(valueNode, JsonHelper.SerializerOptions);
                if (value != null)
                    Value = (T)value;
            }
        }
        else if (json is JsonValue jsonValue)
        {
            T? value = JsonSerializer.Deserialize<T>(jsonValue, JsonHelper.SerializerOptions);
            if (value != null)
                Value = (T)value;
        }
    }

    public override void WriteToJson(ref JsonNode node)
    {
        if (Children.Count == 0)
        {
            node = JsonSerializer.SerializeToNode(Value, JsonHelper.SerializerOptions)!;
        }
        else
        {
            var jsonObj = new JsonObject();
            var jsonArray = new JsonArray();
            foreach (Animation<T> item in _children)
            {
                JsonNode json = new JsonObject();
                item.WriteToJson(ref json);
                jsonArray.Add(json);
            }

            jsonObj["value"] = JsonSerializer.SerializeToNode(Value, JsonHelper.SerializerOptions);
            jsonObj["children"] = jsonArray;

            node = jsonObj;
        }
    }

    IRecordableCommand IAnimatablePropertyInstance.AddChild(IAnimation animation)
    {
        return AddChild((Animation<T>)animation);
    }

    IRecordableCommand IAnimatablePropertyInstance.RemoveChild(IAnimation animation)
    {
        return RemoveChild((Animation<T>)animation);
    }

    IRecordableCommand IAnimatablePropertyInstance.InsertChild(int index, IAnimation animation)
    {
        return InsertChild(index, (Animation<T>)animation);
    }
}
