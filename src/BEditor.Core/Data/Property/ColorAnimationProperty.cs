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
        private Subject<(Frame frame, int index)>? _addKeyFrameSubject;
        private Subject<int>? _deleteKeyFrameSubject;
        private Subject<(int fromindex, int toindex)>? _moveKeyFrameSubject;
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
            Frame = new();
            EasingType = metadata.DefaultEase.CreateFunc();
        }


        /// <summary>
        /// Get the <see cref="ObservableCollection{Color}"/> of the <see cref="Color"/> type value corresponding to <see cref="Frame"/>.
        /// </summary>
        [DataMember]
        public ObservableCollection<Color> Value { get; set; }

        /// <summary>
        /// Get the <see cref="List{Frame}"/> of the frame number corresponding to <see cref="Value"/>.
        /// </summary>
        [DataMember]
        public List<Frame> Frame { get; set; }

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
        public Color this[Frame frame] => GetValue(frame);


        #region Methods

        /// <summary>
        /// Get an eased value.
        /// </summary>
        public Color GetValue(Frame frame)
        {

            static (int, int) GetFrame(ColorAnimationProperty property, Frame frame)
            {
                if (property.Frame.Count == 0)
                {
                    return (0, property.Length);
                }
                else if (0 <= frame && frame <= property.Frame[0])
                {
                    return (0, property.Frame[0]);
                }
                else if (property.Frame[^1] <= frame && frame <= property.Length)
                {
                    return (property.Frame[^1], property.Length);
                }
                else
                {
                    int index = 0;
                    for (int f = 0; f < property.Frame.Count - 1; f++)
                    {
                        if (property.Frame[f] <= frame && frame <= property.Frame[f + 1])
                        {
                            index = f;
                        }
                    }

                    return (property.Frame[index], property.Frame[index + 1]);
                }

                throw new Exception();
            }
            static (Color, Color) GetValues(ColorAnimationProperty property, Frame frame)
            {
                if (property.Value.Count == 2)
                {
                    return (property.Value[0], property.Value[1]);
                }
                else if (0 <= frame && frame <= property.Frame[0])
                {
                    return (property.Value[0], property.Value[1]);
                }
                else if (property.Frame[^1] <= frame && frame <= property.Length)
                {
                    return (property.Value[^2], property.Value[^1]);
                }
                else
                {
                    int index = 0;
                    for (int f = 0; f < property.Frame.Count - 1; f++)
                    {
                        if (property.Frame[f] <= frame && frame <= property.Frame[f + 1])
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

            Frame.Add(frame);


            var tmp = new List<Frame>(Frame);
            tmp.Sort((a, b) => a - b);


            for (int i = 0; i < Frame.Count; i++)
            {
                Frame[i] = tmp[i];
            }

            int stindex = Frame.IndexOf(frame) + 1;

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
            var index = Frame.IndexOf(frame) + 1;
            value = Value[index];

            if (Frame.Remove(frame))
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
            private readonly ColorAnimationProperty _property;
            private readonly int _index;
            private readonly Color _new;
            private readonly Color _old;

            public ChangeColorCommand(ColorAnimationProperty property, int index, Color color)
            {
                _property = property ?? throw new ArgumentNullException(nameof(property));
                _index = index;

                _new = color;
                _old = property.Value[index];
            }

            public string Name => CommandName.ChangeColor;

            public void Do() => _property.Value[_index] = _new;
            public void Redo() => Do();
            public void Undo() => _property.Value[_index] = _old;
        }

        private sealed class ChangeEaseCommand : IRecordCommand
        {
            private readonly ColorAnimationProperty _property;
            private readonly EasingFunc _new;
            private readonly EasingFunc _old;

            public ChangeEaseCommand(ColorAnimationProperty property, string type)
            {
                _property = property ?? throw new ArgumentNullException(nameof(property));

                var data = EasingMetadata.LoadedEasingFunc.Find(x => x.Name == type)!;
                _new = data.CreateFunc();
                _new.Parent = property;
                _old = _property.EasingType;
            }
            public ChangeEaseCommand(ColorAnimationProperty property, EasingMetadata metadata)
            {
                _property = property ?? throw new ArgumentNullException(nameof(property));

                _new = metadata.CreateFunc();
                _new.Parent = property;
                _old = _property.EasingType;
            }

            public string Name => CommandName.ChangeEasing;

            public void Do() => _property.EasingType = _new;
            public void Redo() => Do();
            public void Undo() => _property.EasingType = _old;
        }

        private sealed class AddCommand : IRecordCommand
        {
            private readonly ColorAnimationProperty _property;
            private readonly Frame _frame;

            public AddCommand(ColorAnimationProperty property, Frame frame)
            {
                _property = property ?? throw new ArgumentNullException(nameof(property));
                _frame = frame;
            }

            public string Name => CommandName.AddKeyFrame;

            public void Do()
            {
                int index = _property.InsertKeyframe(_frame, _property.GetValue(_frame + _property.GetParent2()?.Start ?? 0));
                (_property.AddKeyFrameEvent as Subject<(Frame, int)>)?.OnNext((_frame, index - 1));
            }
            public void Redo() => Do();
            public void Undo()
            {
                int index = _property.RemoveKeyframe(_frame, out _);
                (_property.RemoveKeyFrameEvent as Subject<int>)?.OnNext(index - 1);
            }
        }

        private sealed class RemoveCommand : IRecordCommand
        {
            private readonly ColorAnimationProperty _property;
            private readonly Frame _frame;
            private Color _value;

            public RemoveCommand(ColorAnimationProperty property, Frame frame)
            {
                _property = property ?? throw new ArgumentNullException(nameof(property));
                _frame = frame;
            }

            public string Name => CommandName.RemoveKeyFrame;

            public void Do()
            {
                int index = _property.RemoveKeyframe(_frame, out _value);
                (_property.RemoveKeyFrameEvent as Subject<int>)?.OnNext(index - 1);
            }
            public void Redo() => Do();
            public void Undo()
            {
                int index = _property.InsertKeyframe(_frame, _value);
                (_property.AddKeyFrameEvent as Subject<(Frame, int)>)?.OnNext((_frame, index - 1));
            }
        }

        private sealed class MoveCommand : IRecordCommand
        {
            private readonly ColorAnimationProperty _property;
            private readonly int _fromIndex;
            private int _toIndex;
            private readonly Frame _toFrame;

            public MoveCommand(ColorAnimationProperty property, int fromIndex, Frame to)
            {
                _property = property ?? throw new ArgumentNullException(nameof(property));
                _fromIndex = fromIndex;
                _toFrame = to;
            }

            public string Name => CommandName.MoveKeyFrame;

            public void Do()
            {
                _property.Frame[_fromIndex] = _toFrame;
                _property.Frame.Sort((a_, b_) => a_ - b_);


                _toIndex = _property.Frame.FindIndex(x => x == _toFrame);//新しいindex

                //Indexの正規化
                _property.Value.Move(_fromIndex + 1, _toIndex + 1);

                (_property.MoveKeyFrameEvent as Subject<(int, int)>)?.OnNext((_fromIndex, _toIndex));//GUIのIndexの正規化 UIスレッドで動作
            }
            public void Redo() => Do();
            public void Undo()
            {
                int frame = _property.Frame[_toIndex];

                _property.Frame.RemoveAt(_toIndex);
                _property.Frame.Insert(_fromIndex, frame);

                _property.Value.Move(_toIndex + 1, _fromIndex + 1);


                (_property.MoveKeyFrameEvent as Subject<(int, int)>)?.OnNext((_toIndex, _fromIndex));
            }
        }

        #endregion
    }

    /// <summary>
    /// Represents the metadata of a <see cref="ColorAnimationProperty"/>.
    /// </summary>
    public record ColorAnimationPropertyMetadata : PropertyElementMetadata, IPropertyBuilder<ColorAnimationProperty>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ColorAnimationPropertyMetadata"/> class.
        /// </summary>
        /// <param name="Name">The string displayed in the property header.</param>
        /// <param name="DefaultColor">Default color</param>
        /// <param name="DefaultEase">Default easing function</param>
        /// <param name="UseAlpha">Value if the alpha component should be used or not</param>
        public ColorAnimationPropertyMetadata(string Name, Color DefaultColor, EasingMetadata DefaultEase, bool UseAlpha = false) : base(Name)
        {
            this.DefaultColor = DefaultColor;
            this.UseAlpha = UseAlpha;
            this.DefaultEase = DefaultEase;
        }
        /// <summary>
        /// Initializes a new instance of the <see cref="ColorAnimationPropertyMetadata"/> class.
        /// </summary>
        /// <param name="Name">The string displayed in the property header.</param>
        public ColorAnimationPropertyMetadata(string Name) : this(Name, default, EasingMetadata.LoadedEasingFunc[0])
        {

        }
        /// <summary>
        /// Initializes a new instance of the <see cref="ColorAnimationPropertyMetadata"/> class.
        /// </summary>
        /// <param name="Name">The string displayed in the property header.</param>
        /// <param name="DefaultColor">Default color</param>
        /// <param name="UseAlpha">Value if the alpha component should be used or not</param>
        public ColorAnimationPropertyMetadata(string Name, Color DefaultColor, bool UseAlpha = false)
            : this(Name, DefaultColor, EasingMetadata.LoadedEasingFunc[0], UseAlpha) { }

        /// <summary>
        /// Gets the default color.
        /// </summary>
        public Color DefaultColor { get; init; }
        /// <summary>
        /// Gets a <see cref="bool"/> indicating whether or not to use the alpha component.
        /// </summary>
        public bool UseAlpha { get; init; }
        /// <summary>
        /// Gets the default easing function.
        /// </summary>
        public EasingMetadata DefaultEase { get; init; }

        /// <inheritdoc/>
        public ColorAnimationProperty Build()
        {
            return new(this);
        }
    }
}
