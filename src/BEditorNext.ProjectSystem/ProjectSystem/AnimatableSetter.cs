using System.Text.Json;
using System.Text.Json.Nodes;

using BEditorNext.Animation;
using BEditorNext.Collections;

namespace BEditorNext.ProjectSystem;

public interface IAnimatableSetter : ISetter
{
    public IReadOnlyList<IAnimation> Children { get; }

    public void SetProperty(Element element, TimeSpan progress);
}

public class AnimatableSetter<T> : Setter<T>, IAnimatableSetter
    where T : struct
{
    private readonly ObservableList<Animation<T>> _children = new();

    public AnimatableSetter()
    {
    }

    public AnimatableSetter(PropertyDefine<T> property)
        : base(property)
    {
    }

    public IObservableList<Animation<T>> Children => _children;

    IReadOnlyList<IAnimation> IAnimatableSetter.Children => _children;

    public void SetProperty(Element element, TimeSpan progress)
    {
        if (_children.Count < 1)
        {
            element.SetValue(Property, Value);
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
                    // イージングする
                    float ease = item.Easing.Ease((float)(time / item.Duration));
                    // 値を補間する
                    T value = item.Animator.Interpolate(ease, item.Previous, item.Next);
                    // 値をセット
                    element.SetValue(Property, value);
                }
                else
                {
                    cur = next;
                }
            }
        }
    }

    public void AddChild(Animation<T> animation, CommandRecorder? recorder = null)
    {
        ArgumentNullException.ThrowIfNull(animation);

        if (recorder == null)
        {
            Children.Add(animation);
        }
        else
        {
            recorder.DoAndPush(new AddCommand(this, animation, Children.Count));
        }
    }

    public void RemoveChild(Animation<T> animation, CommandRecorder? recorder = null)
    {
        ArgumentNullException.ThrowIfNull(animation);

        if (recorder == null)
        {
            Children.Remove(animation);
        }
        else
        {
            recorder.DoAndPush(new RemoveCommand(this, animation));
        }
    }

    public void InsertChild(int index, Animation<T> animation, CommandRecorder? recorder = null)
    {
        ArgumentNullException.ThrowIfNull(animation);

        if (recorder == null)
        {
            Children.Insert(index, animation);
        }
        else
        {
            recorder.DoAndPush(new AddCommand(this, animation, index));
        }
    }

    public override void FromJson(JsonNode json)
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
                        anm.FromJson(jobj);
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
    }

    public override JsonNode ToJson()
    {
        var jsonObj = new JsonObject();
        var jsonArray = new JsonArray();
        foreach (Animation<T> item in _children)
        {
            jsonArray.Add(item.ToJson());
        }

        jsonObj["value"] = JsonSerializer.SerializeToNode(Value, JsonHelper.SerializerOptions);
        jsonObj["children"] = jsonArray;

        return jsonObj;
    }

    private sealed class AddCommand : IRecordableCommand
    {
        private readonly AnimatableSetter<T> _setter;
        private readonly Animation<T> _animation;
        private readonly int _index;

        public AddCommand(AnimatableSetter<T> setter, Animation<T> animation, int index)
        {
            _setter = setter;
            _animation = animation;
            _index = index;
        }

        public void Do()
        {
            _setter.Children.Insert(_index, _animation);
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            _setter.Children.Remove(_animation);
        }
    }

    private sealed class RemoveCommand : IRecordableCommand
    {
        private readonly AnimatableSetter<T> _setter;
        private readonly Animation<T> _animation;
        private int _index;

        public RemoveCommand(AnimatableSetter<T> setter, Animation<T> animation)
        {
            _setter = setter;
            _animation = animation;
        }

        public void Do()
        {
            _index = _setter.Children.IndexOf(_animation);
            _setter.Children.Remove(_animation);
        }

        public void Redo()
        {
            Do();
        }

        public void Undo()
        {
            _setter.Children.Insert(_index, _animation);
        }
    }
}
