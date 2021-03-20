using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Runtime.Serialization;

using BEditor.Command;
using BEditor.Data.Property;
using BEditor.Data.Property.Easing;
using BEditor.Drawing;
using BEditor.Media;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a property that eases the value of a <see cref="Color"/> type.
    /// </summary>
    [DataContract]
    [DebuggerDisplay("Count = {Value.Count}, Easing = {EasingData.Name}")]
    public class ColorAnimationProperty : PropertyElement<ColorAnimationPropertyMetadata>, IKeyFrameProperty
    {
        #region Fields
        private static readonly PropertyChangedEventArgs _easingFuncArgs = new(nameof(EasingType));
        private static readonly PropertyChangedEventArgs _easingDataArgs = new(nameof(EasingData));
        private EasingFunc? _easingTypeProperty;
        private EasingMetadata? _easingData;
        #endregion


        /// <summary>
        /// Initializes a new instance of the <see cref="ColorAnimationProperty"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public ColorAnimationProperty(ColorAnimationPropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Color color = metadata.DefaultColor;

            Value = new() { color, color };
            Frames = new();
            EasingType = metadata.DefaultEase.CreateFunc();
        }


        /// <summary>
        /// Get the <see cref="ObservableCollection{Color}"/> of the <see cref="Color"/> type value corresponding to <see cref="Frames"/>.
        /// </summary>
        [DataMember]
        public ObservableCollection<Color> Value { get; set; }

        /// <summary>
        /// Get the <see cref="List{Frame}"/> of the frame number corresponding to <see cref="Value"/>.
        /// </summary>
        [DataMember]
        public List<Frame> Frames { get; set; }

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
                SetValue(value, ref _easingTypeProperty, _easingFuncArgs);

                EasingData = EasingMetadata.LoadedEasingFunc.Find(x => x.Type == value.GetType())!;
            }
        }

        /// <summary>
        /// Get or set the metadata for <see cref="EasingType"/>
        /// </summary>
        /// 
        public EasingMetadata EasingData
        {
            get => _easingData ?? EasingMetadata.LoadedEasingFunc[0];
            set => SetValue(value, ref _easingData, _easingDataArgs);
        }

        internal Frame Length => Parent?.Parent?.Length ?? default;


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

        /// <inheritdoc/>
        public event Action<Frame, int>? Added;

        /// <inheritdoc/>
        public event Action<int>? Removed;

        /// <inheritdoc/>
        public event Action<int, int>? Moved;


        /// <summary>
        /// Get an eased value.
        /// </summary>
        public Color this[Frame frame] => GetValue(frame);


        #region Methods

        /// <summary>
        /// Get an eased value.
        /// </summary>
        public Color GetValue(Frame frame)
        {

            static (int, int) GetFrame(ColorAnimationProperty property, Frame frame)
            {
                if (property.Frames.Count == 0)
                {
                    return (0, property.Length);
                }
                else if (0 <= frame && frame <= property.Frames[0])
                {
                    return (0, property.Frames[0]);
                }
                else if (property.Frames[^1] <= frame && frame <= property.Length)
                {
                    return (property.Frames[^1], property.Length);
                }
                else
                {
                    int index = 0;
                    for (int f = 0; f < property.Frames.Count - 1; f++)
                    {
                        if (property.Frames[f] <= frame && frame <= property.Frames[f + 1])
                        {
                            index = f;
                        }
                    }

                    return (property.Frames[index], property.Frames[index + 1]);
                }

                throw new Exception();
            }
            static (Color, Color) GetValues(ColorAnimationProperty property, Frame frame)
            {
                if (property.Value.Count == 2)
                {
                    return (property.Value[0], property.Value[1]);
                }
                else if (0 <= frame && frame <= property.Frames[0])
                {
                    return (property.Value[0], property.Value[1]);
                }
                else if (property.Frames[^1] <= frame && frame <= property.Length)
                {
                    return (property.Value[^2], property.Value[^1]);
                }
                else
                {
                    int index = 0;
                    for (int f = 0; f < property.Frames.Count - 1; f++)
                    {
                        if (property.Frames[f] <= frame && frame <= property.Frames[f + 1])
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



            float red = EasingType.EaseFunc(now, end - start, stval.R, edval.R);
            float green = EasingType.EaseFunc(now, end - start, stval.G, edval.G);
            float blue = EasingType.EaseFunc(now, end - start, stval.B, edval.B);
            float alpha = EasingType.EaseFunc(now, end - start, stval.A, edval.A);

            return Color.FromARGB(
                (byte)alpha,
                (byte)red,
                (byte)green,
                (byte)blue);
        }

        /// <summary>
        /// Insert a keyframe at a specific frame.
        /// </summary>
        /// <param name="frame">Frame to be added.</param>
        /// <param name="value">Value to be added</param>
        /// <returns>Index of the added <see cref="Value"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="frame"/> is outside the scope of the parent element.</exception>
        public int InsertKeyframe(Frame frame, Color value)
        {
            if (Media.Frame.Zero >= frame || frame >= this.GetParent2()!.Length) throw new ArgumentOutOfRangeException(nameof(frame));

            Frames.Add(frame);


            var tmp = new List<Frame>(Frames);
            tmp.Sort((a, b) => a - b);


            for (int i = 0; i < Frames.Count; i++)
            {
                Frames[i] = tmp[i];
            }

            int stindex = Frames.IndexOf(frame) + 1;

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
        public int RemoveKeyframe(Frame frame, out Color value)
        {
            if (Media.Frame.Zero >= frame || frame >= this.GetParent2()!.Length) throw new ArgumentOutOfRangeException(nameof(frame));

            //値基準のindex
            var index = Frames.IndexOf(frame) + 1;
            value = Value[index];

            if (Frames.Remove(frame))
            {
                Value.RemoveAt(index);
            }

            return index;
        }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            EasingType.Parent = this;
            EasingType.Load();
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
        /// <param name="color">New Color.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand ChangeColor(int index, Color color) => new ChangeColorCommand(this, index, color);

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

        private sealed class ChangeColorCommand : IRecordCommand
        {
            private readonly WeakReference<ColorAnimationProperty> _property;
            private readonly int _index;
            private readonly Color _new;
            private readonly Color _old;

            public ChangeColorCommand(ColorAnimationProperty property, int index, Color color)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));
                _index = (index < 0 || index >= property.Value.Count) ? throw new IndexOutOfRangeException($"{nameof(index)} is out of range of {nameof(Value)}") : index;

                _new = color;
                _old = property.Value[index];
            }

            public string Name => CommandName.ChangeColor;

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
            private readonly WeakReference<ColorAnimationProperty> _property;
            private readonly EasingFunc _new;
            private readonly EasingFunc _old;

            public ChangeEaseCommand(ColorAnimationProperty property, string type)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));

                var data = EasingMetadata.LoadedEasingFunc.Find(x => x.Name == type)!;
                _new = data.CreateFunc();
                _new.Parent = property;
                _old = property.EasingType;
            }
            public ChangeEaseCommand(ColorAnimationProperty property, EasingMetadata metadata)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));

                _new = metadata.CreateFunc();
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
            private readonly WeakReference<ColorAnimationProperty> _property;
            private readonly Frame _frame;

            public AddCommand(ColorAnimationProperty property, Frame frame)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));

                _frame = (Media.Frame.Zero >= frame || frame >= property.GetParent2()!.Length) ? throw new ArgumentOutOfRangeException(nameof(frame)) : frame;
            }

            public string Name => CommandName.AddKeyFrame;

            public void Do()
            {
                if (_property.TryGetTarget(out var target))
                {
                    int index = target.InsertKeyframe(_frame, target.GetValue(_frame + target.GetParent2()?.Start ?? 0));

                    target.Added?.Invoke(_frame, index - 1);
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

                    target.Removed?.Invoke(index - 1);
                }
            }
        }

        private sealed class RemoveCommand : IRecordCommand
        {
            private readonly WeakReference<ColorAnimationProperty> _property;
            private readonly Frame _frame;
            private Color _value;

            public RemoveCommand(ColorAnimationProperty property, Frame frame)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));

                _frame = (frame <= Media.Frame.Zero || property.GetParent2()!.Length <= frame) ? throw new ArgumentOutOfRangeException(nameof(frame)) : frame;
            }

            public string Name => CommandName.RemoveKeyFrame;

            public void Do()
            {
                if (_property.TryGetTarget(out var target))
                {
                    int index = target.RemoveKeyframe(_frame, out _value);

                    target.Removed?.Invoke(index - 1);
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

                    target.Added?.Invoke(_frame, index - 1);
                }
            }
        }

        private sealed class MoveCommand : IRecordCommand
        {
            private readonly WeakReference<ColorAnimationProperty> _property;
            private readonly int _fromIndex;
            private int _toIndex;
            private readonly Frame _toFrame;

            public MoveCommand(ColorAnimationProperty property, int fromIndex, Frame to)
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
                    target.Frames[_fromIndex] = _toFrame;
                    target.Frames.Sort((a_, b_) => a_ - b_);

                    // 新しいindex
                    _toIndex = target.Frames.FindIndex(x => x == _toFrame);

                    // 値のIndexを合わせる
                    target.Value.Move(_fromIndex + 1, _toIndex + 1);

                    target.Moved?.Invoke(_fromIndex, _toIndex);
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
                    int frame = target.Frames[_toIndex];

                    target.Frames.RemoveAt(_toIndex);
                    target.Frames.Insert(_fromIndex, frame);

                    target.Value.Move(_toIndex + 1, _fromIndex + 1);

                    target.Moved?.Invoke(_toIndex, _fromIndex);
                }
            }
        }

        #endregion
    }

    /// <summary>
    /// The metadata of <see cref="ColorAnimationProperty"/>.
    /// </summary>
    /// <param name="Name">The string displayed in the property header.</param>
    /// <param name="DefaultColor">The default color.</param>
    /// <param name="DefaultEase">The default easing function.</param>
    /// <param name="UseAlpha">The value of whether to use alpha components or not.</param>
    public record ColorAnimationPropertyMetadata(string Name, Color DefaultColor, EasingMetadata DefaultEase, bool UseAlpha = false) : PropertyElementMetadata(Name), IPropertyBuilder<ColorAnimationProperty>
    {
        /// <summary>
        /// The metadata of <see cref="ColorAnimationProperty"/>.
        /// </summary>
        /// <param name="Name">The string displayed in the property header.</param>
        public ColorAnimationPropertyMetadata(string Name) : this(Name, default, EasingMetadata.LoadedEasingFunc[0])
        {

        }
        /// <summary>
        /// The metadata of <see cref="ColorAnimationProperty"/>.
        /// </summary>
        /// <param name="Name">The string displayed in the property header.</param>
        /// <param name="DefaultColor">The default color.</param>
        /// <param name="UseAlpha">The value of whether to use alpha components or not.</param>
        public ColorAnimationPropertyMetadata(string Name, Color DefaultColor, bool UseAlpha = false)
            : this(Name, DefaultColor, EasingMetadata.LoadedEasingFunc[0], UseAlpha) { }

        /// <inheritdoc/>
        public ColorAnimationProperty Build()
        {
            return new(this);
        }
    }
}
