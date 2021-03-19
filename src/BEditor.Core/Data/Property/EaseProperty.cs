using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Reactive.Subjects;
using System.Runtime.Serialization;

using BEditor.Command;
using BEditor.Data.Property;
using BEditor.Data.Property.Easing;
using BEditor.Media;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents the property that eases the value of a <see cref="float"/> type.
    /// </summary>
    [DataContract]
    [DebuggerDisplay("Count = {Value.Count}, Easing = {EasingData.Name}")]
    public partial class EaseProperty : PropertyElement<EasePropertyMetadata>, IKeyFrameProperty
    {
        #region Fields
        private static readonly PropertyChangedEventArgs _easingFuncArgs = new(nameof(EasingType));
        private static readonly PropertyChangedEventArgs _easingDataArgs = new(nameof(EasingData));
        private EasingFunc? _easingTypeProperty;
        private EasingMetadata? _easingData;
        private Subject<(Frame frame, int index)>? _addKeyFrameSubject;
        private Subject<int>? _deleteKeyFrameSubject;
        private Subject<(int fromindex, int toindex)>? _moveKeyFrameSubject;
        #endregion


        /// <summary>
        /// Initializes a new instance of the <see cref="EaseProperty"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public EaseProperty(EasePropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));

            Value = new ObservableCollection<float> { metadata.DefaultValue, metadata.DefaultValue };
            Time = new();
            EasingType = metadata.DefaultEase.CreateFunc();
        }


        /// <summary>
        /// Get the <see cref="ObservableCollection{Single}"/> of the <see cref="float"/> type value corresponding to <see cref="Frame"/>.
        /// </summary>
        [DataMember]
        public ObservableCollection<float> Value { get; private set; }

        /// <summary>
        /// Get the <see cref="List{Frame}"/> of the frame number corresponding to <see cref="Value"/>.
        /// </summary>
        [DataMember]
        public List<Frame> Time { get; private set; }

        /// <summary>
        /// Get or set the current <see cref="EasingFunc"/>.
        /// </summary>
        [DataMember]
        public EasingFunc EasingType
        {
            get
            {
                if (_easingTypeProperty == null || EasingData.Type != _easingTypeProperty.GetType())
                {
                    _easingTypeProperty = EasingData.CreateFunc();
                    _easingTypeProperty.Parent = this;
                }

                return _easingTypeProperty;
            }
            set
            {
                SetValue(value, ref _easingTypeProperty, _easingDataArgs);

                EasingData = EasingMetadata.LoadedEasingFunc.Find(x => x.Type == value.GetType())!;
            }
        }

        /// <summary>
        /// Get or set an optional value.
        /// </summary>
        public float Optional { get; set; }

        /// <summary>
        /// Get or set the metadata for <see cref="EasingType"/>
        /// </summary>
        public EasingMetadata EasingData
        {
            get => _easingData ?? EasingMetadata.LoadedEasingFunc[0];
            set => SetValue(value, ref _easingData, _easingDataArgs);
        }

        internal Frame Length => this.GetParent2()?.Length ?? default;

        /// <inheritdoc/>
        public override EffectElement Parent
        {
            get => base.Parent;
            set
            {
                base.Parent = value;
                EasingType.Parent = this;
            }
        }

        /// <summary>
        /// Occurs when requesting to add a keyframe to the UI.
        /// </summary>
        public IObservable<(Frame frame, int index)> AddKeyFrameEvent => _addKeyFrameSubject ??= new();

        /// <summary>
        /// Occurs when requesting the UI to delete a keyframe.
        /// </summary>
        public IObservable<int> RemoveKeyFrameEvent => _deleteKeyFrameSubject ??= new();

        /// <summary>
        /// Occurs when the UI requires a keyframe to be moved.
        /// </summary>
        public IObservable<(int fromindex, int toindex)> MoveKeyFrameEvent => _moveKeyFrameSubject ??= new();


        /// <summary>
        /// Get an eased value.
        /// </summary>
        public float this[Frame frame] => GetValue(frame);



        #region Methods

        /// <summary>
        /// Get an eased value.
        /// </summary>
        public float GetValue(Frame frame)
        {
            static (int, int) GetFrame(EaseProperty property, int frame)
            {
                if (property.Time.Count == 0)
                {
                    return (0, property.Length);
                }
                else if (0 <= frame && frame <= property.Time[0])
                {
                    return (0, property.Time[0]);
                }
                else if (property.Time[^1] <= frame && frame <= property.Length)
                {
                    return (property.Time[^1], property.Length);
                }
                else
                {
                    int index = 0;
                    for (int f = 0; f < property.Time.Count - 1; f++)
                    {
                        if (property.Time[f] <= frame && frame <= property.Time[f + 1])
                        {
                            index = f;
                        }
                    }

                    return (property.Time[index], property.Time[index + 1]);
                }

                throw new Exception();
            }
            static (float, float) GetValues(EaseProperty property, int frame)
            {
                if (property.Value.Count == 2)
                {
                    return (property.Value[0], property.Value[1]);
                }
                else if (0 <= frame && frame <= property.Time[0])
                {
                    return (property.Value[0], property.Value[1]);
                }
                else if (property.Time[^1] <= frame && frame <= property.Length)
                {
                    return (property.Value[^2], property.Value[^1]);
                }
                else
                {
                    int index = 0;
                    for (int f = 0; f < property.Time.Count - 1; f++)
                    {
                        if (property.Time[f] <= frame && frame <= property.Time[f + 1])
                        {
                            index = f + 1;
                        }
                    }

                    return (property.Value[index], property.Value[index + 1]);
                }

                throw new Exception();
            }

            frame -= this.GetParent2()?.Start ?? default;

            var (start, end) = GetFrame(this, frame);

            var (stval, edval) = GetValues(this, frame);

            int now = frame - start;//相対的な現在フレーム

            float out_ = EasingType.EaseFunc(now, end - start, stval, edval);

            if (PropertyMetadata?.UseOptional ?? false)
            {
                return Clamp(out_ + Optional);
            }

            return Clamp(out_);
        }

        /// <summary>
        /// Returns <paramref name="value"/> clamped to the inclusive range of <see cref="EasePropertyMetadata.Min"/> and <see cref="EasePropertyMetadata.Max"/>.
        /// </summary>
        /// <param name="value">The value to be clamped.</param>
        /// <returns>value if min ≤ value ≤ max. -or- min if value &lt; min. -or- max if max &lt; value.</returns>
        public float Clamp(float value)
        {
            var meta = PropertyMetadata;
            var max = meta?.Max ?? float.NaN;
            var min = meta?.Min ?? float.NaN;

            if (!float.IsNaN(min) && value <= min)
            {
                return min;
            }
            else if (!float.IsNaN(max) && max <= value)
            {
                return max;
            }

            return value;
        }

        /// <summary>
        /// Insert a keyframe at a specific frame.
        /// </summary>
        /// <param name="frame">Frame to be added.</param>
        /// <param name="value">Value to be added</param>
        /// <returns>Index of the added <see cref="Value"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="frame"/> is outside the scope of the parent element.</exception>
        public int InsertKeyframe(Frame frame, float value)
        {
            if (Frame.Zero >= frame || frame >= this.GetParent2()!.Length) throw new ArgumentOutOfRangeException(nameof(frame));

            Time.Add(frame);

            var tmp = new List<Frame>(Time);
            tmp.Sort((a, b) => a - b);


            for (int i = 0; i < Time.Count; i++)
            {
                Time[i] = tmp[i];
            }

            var stindex = Time.IndexOf(frame) + 1;

            Value.Insert(stindex, value);

            return stindex;
        }

        /// <summary>
        /// Remove a keyframe of a specific frame.
        /// </summary>
        /// <param name="frame">Frame to be removed.</param>
        /// <param name="value">Removed value.</param>
        /// <returns>Index of the removed <see cref="Value"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="frame"/> is outside the scope of the parent element.</exception>
        public int RemoveKeyframe(Frame frame, out float value)
        {
            if (Frame.Zero >= frame || frame >= this.GetParent2()!.Length) throw new ArgumentOutOfRangeException(nameof(frame));

            //値基準のindex
            var index = Time.IndexOf(frame) + 1;

            value = Value[index];

            if (Time.Remove(frame))
            {
                Value.RemoveAt(index);
            }

            return index;
        }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            //Todo: ここに範囲外の場合の処理を書く

            EasingType.Load();
            EasingType.Parent = this;
        }

        /// <inheritdoc/>
        protected override void OnUnload()
        {
            EasingType.Unload();
        }

        /// <summary>
        /// Create a command to change the color of this <see cref="Value"/>.
        /// </summary>
        /// <param name="index">Index of colors to be changed.</param>
        /// <param name="value">New Value.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand ChangeValue(int index, float value) => new ChangeValueCommand(this, index, value);

        /// <summary>
        /// Create a command to change the easing function.
        /// </summary>
        /// <param name="metadata">New easing function metadata.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand ChangeEase(EasingMetadata metadata) => new ChangeEaseCommand(this, metadata);

        /// <summary>
        /// Create a command to add a keyframe.
        /// </summary>
        /// <param name="frame">Frame to be added</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand AddFrame(Frame frame) => new AddCommand(this, frame);

        /// <summary>
        /// Create a command to remove a keyframe.
        /// </summary>
        /// <param name="frame">Frame to be removed.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand RemoveFrame(Frame frame) => new RemoveCommand(this, frame);

        /// <summary>
        /// Create a command to move a keyframe
        /// </summary>
        /// <param name="fromIndex">Index of the frame to be moved from.</param>
        /// <param name="toFrame">Destination frame.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand MoveFrame(int fromIndex, Frame toFrame) => new MoveCommand(this, fromIndex, toFrame);

        #endregion


        #region Commands

        private sealed class ChangeValueCommand : IRecordCommand
        {
            private readonly WeakReference<EaseProperty> _property;
            private readonly int _index;
            private readonly float _new;
            private readonly float _old;

            public ChangeValueCommand(EaseProperty property, int index, float newvalue)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));
                _index = (index < 0 || index >= property.Value.Count) ? throw new IndexOutOfRangeException($"{nameof(index)} is out of range of {nameof(Value)}") : index;

                _new = property.Clamp(newvalue);
                _old = property.Value[index];
            }

            public string Name => CommandName.ChangeValue;

            public void Do()
            {
                if (_property.TryGetTarget(out var target))
                {
                    target.Value[_index] = _new;
                }
            }
            public void Redo()
            {
                Do();
            }
            public void Undo()
            {
                if (_property.TryGetTarget(out var target))
                {
                    target.Value[_index] = _old;
                }
            }
        }

        private sealed class ChangeEaseCommand : IRecordCommand
        {
            private readonly WeakReference<EaseProperty> _property;
            private readonly EasingFunc _new;
            private readonly EasingFunc _old;

            public ChangeEaseCommand(EaseProperty property, EasingMetadata metadata)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));

                _new = metadata.CreateFunc();
                _new.Parent = property;
                _old = property.EasingType;
            }
            public ChangeEaseCommand(EaseProperty property, string type)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));
                var easingFunc = EasingMetadata.LoadedEasingFunc.Find(x => x.Name == type) ?? throw new KeyNotFoundException($"No easing function named {type} was found");

                _new = easingFunc.CreateFunc();
                _new.Parent = property;
                _old = property.EasingType;
            }

            public string Name => CommandName.ChangeEasing;

            public void Do()
            {
                if (_property.TryGetTarget(out var target))
                {
                    target.EasingType = _new;
                }
            }
            public void Redo()
            {
                Do();
            }
            public void Undo()
            {
                if (_property.TryGetTarget(out var target))
                {
                    target.EasingType = _old;
                }
            }
        }

        private sealed class AddCommand : IRecordCommand
        {
            private readonly WeakReference<EaseProperty> _property;
            private readonly Frame _frame;

            public AddCommand(EaseProperty property, Frame frame)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));

                _frame = (Frame.Zero >= frame || frame >= property.GetParent2()!.Length) ? throw new ArgumentOutOfRangeException(nameof(frame)) : frame;
            }

            public string Name => CommandName.AddKeyFrame;

            public void Do()
            {
                if (_property.TryGetTarget(out var target))
                {
                    int index = target.InsertKeyframe(_frame, target.GetValue(_frame + target.GetParent2()?.Start ?? 0));

                    (target.AddKeyFrameEvent as Subject<(Frame, int)>)?.OnNext((_frame, index - 1));
                }
            }
            public void Redo()
            {
                Do();
            }
            public void Undo()
            {
                if (_property.TryGetTarget(out var target))
                {
                    int index = target.RemoveKeyframe(_frame, out _);

                    (target.RemoveKeyFrameEvent as Subject<int>)?.OnNext(index - 1);
                }
            }
        }

        private sealed class RemoveCommand : IRecordCommand
        {
            private readonly WeakReference<EaseProperty> _property;
            private readonly Frame _frame;
            private float _value;

            public RemoveCommand(EaseProperty property, Frame frame)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));

                _frame = (frame <= Frame.Zero || property.GetParent2()!.Length <= frame) ? throw new ArgumentOutOfRangeException(nameof(frame)) : frame;
            }

            public string Name => CommandName.RemoveKeyFrame;

            public void Do()
            {
                if (_property.TryGetTarget(out var target))
                {
                    int index = target.RemoveKeyframe(_frame, out _value);

                    (target.RemoveKeyFrameEvent as Subject<int>)?.OnNext(index - 1);
                }
            }
            public void Redo()
            {
                Do();
            }
            public void Undo()
            {
                if (_property.TryGetTarget(out var target))
                {
                    int index = target.InsertKeyframe(_frame, _value);
                    (target.AddKeyFrameEvent as Subject<(Frame, int)>)?.OnNext((_frame, index - 1));
                }
            }
        }

        private sealed class MoveCommand : IRecordCommand
        {
            private readonly WeakReference<EaseProperty> _property;
            private readonly int _fromIndex;
            private int _toIndex;
            private readonly Frame _toFrame;

            public MoveCommand(EaseProperty property, int fromIndex, Frame to)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));

                _fromIndex = (0 > fromIndex || fromIndex > property.Value.Count) ? throw new IndexOutOfRangeException() : fromIndex;

                _toFrame = (to <= Frame.Zero || property.GetParent2()!.Length <= to) ? throw new ArgumentOutOfRangeException(nameof(to)) : to;
            }

            public string Name => CommandName.MoveKeyFrame;

            public void Do()
            {
                if (_property.TryGetTarget(out var target))
                {
                    target.Time[_fromIndex] = _toFrame;
                    target.Time.Sort((a_, b_) => a_ - b_);

                    // 新しいindex
                    _toIndex = target.Time.FindIndex(x => x == _toFrame);

                    // 値のIndexを合わせる
                    target.Value.Move(_fromIndex + 1, _toIndex + 1);

                    (target.MoveKeyFrameEvent as Subject<(int, int)>)?.OnNext((_fromIndex, _toIndex));
                }
            }
            public void Redo()
            {
                Do();
            }
            public void Undo()
            {
                if (_property.TryGetTarget(out var target))
                {
                    int frame = target.Time[_toIndex];

                    target.Time.RemoveAt(_toIndex);
                    target.Time.Insert(_fromIndex, frame);

                    target.Value.Move(_toIndex + 1, _fromIndex + 1);


                    (target.MoveKeyFrameEvent as Subject<(int, int)>)?.OnNext((_toIndex, _fromIndex));
                }
            }
        }

        #endregion
    }

    /// <summary>
    /// The metadata of <see cref="EaseProperty"/>.
    /// </summary>
    /// <param name="Name">The string displayed in the property header.</param>
    /// <param name="DefaultEase">The default easing function.</param>
    /// <param name="DefaultValue">The default value.</param>
    /// <param name="Max">The maximum value.</param>
    /// <param name="Min">The minimum value.</param>
    /// <param name="UseOptional">The bool of whether to use the Optional value.</param>
    public record EasePropertyMetadata(string Name, EasingMetadata DefaultEase, float DefaultValue = 0, float Max = float.NaN, float Min = float.NaN, bool UseOptional = false)
        : PropertyElementMetadata(Name), IPropertyBuilder<EaseProperty>
    {
        /// <summary>
        /// The metadata of <see cref="EaseProperty"/>.
        /// </summary>
        /// <param name="Name">The string displayed in the property header.</param>
        /// <param name="DefaultValue">The default value.</param>
        /// <param name="Max">The maximum value.</param>
        /// <param name="Min">The minimum value.</param>
        /// <param name="UseOptional">The bool of whether to use the Optional value.</param>
        public EasePropertyMetadata(string Name, float DefaultValue = 0, float Max = float.NaN, float Min = float.NaN, bool UseOptional = false)
            : this(Name, EasingMetadata.LoadedEasingFunc[0], DefaultValue, Max, Min, UseOptional) { }

        /// <inheritdoc/>
        public EaseProperty Build()
        {
            return new(this);
        }
    }
}
