using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.Serialization;

using BEditor.Core.Command;
using BEditor.Core.Data.Property;
using BEditor.Core.Data.Property.Easing;
using BEditor.Core.Extensions;
using BEditor.Drawing;
using BEditor.Media;

namespace BEditor.Core.Data.Property
{
    /// <summary>
    /// Represents a property that eases the value of a <see cref="Color"/> type.
    /// </summary>
    [DataContract]
    public class ColorAnimationProperty : PropertyElement<ColorAnimationPropertyMetadata>, IKeyFrameProperty
    {
        #region Fields
        private static readonly PropertyChangedEventArgs _EasingFuncArgs = new(nameof(EasingType));
        private static readonly PropertyChangedEventArgs _EasingDataArgs = new(nameof(EasingData));
        private EasingFunc? _EasingTypeProperty;
        private EasingMetadata? _EasingData;
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
                if (_EasingTypeProperty == null || EasingData.Type != _EasingTypeProperty.GetType())
                {
                    _EasingTypeProperty = EasingData.CreateFunc();
                    _EasingTypeProperty.Parent = this;
                }

                return _EasingTypeProperty;
            }
            set
            {
                SetValue(value, ref _EasingTypeProperty, _EasingFuncArgs);

                EasingData = EasingMetadata.LoadedEasingFunc.Find(x => x.Type == value.GetType())!;
            }
        }
        /// <summary>
        /// Get or set the metadata for <see cref="EasingType"/>
        /// </summary>
        public EasingMetadata EasingData
        {
            get => _EasingData ?? EasingMetadata.LoadedEasingFunc[0];
            set => SetValue(value, ref _EasingData, _EasingDataArgs);
        }
        internal Frame Length => Parent?.Parent?.Length ?? default;
        /// <summary>
        /// Get an eased value.
        /// </summary>
        public Color this[Frame frame] => GetValue(frame);

        /// <summary>
        /// Occurs when requesting to add a keyframe to the UI.
        /// </summary>
        public event EventHandler<(Frame frame, int index)>? AddKeyFrameEvent;
        /// <summary>
        /// Occurs when requesting the UI to delete a keyframe.
        /// </summary>
        public event EventHandler<int>? DeleteKeyFrameEvent;
        /// <summary>
        /// Occurs when the UI requires a keyframe to be moved.
        /// </summary>
        public event EventHandler<(int fromindex, int toindex)>? MoveKeyFrameEvent;


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

        #region キーフレーム操作
        /// <summary>
        /// Insert a keyframe at a specific frame.
        /// </summary>
        /// <param name="frame">Frame to be added.</param>
        /// <param name="value">Value to be added</param>
        /// <returns>Index of the added <see cref="Value"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="frame"/> is outside the scope of the parent element.</exception>
        public int InsertKeyframe(Frame frame, Color value)
        {
            if (frame <= this.GetParent2()!.Start || this.GetParent2()!.End <= frame) throw new ArgumentOutOfRangeException(nameof(frame));

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
            if (frame <= this.GetParent2()!.Start || this.GetParent2()!.End <= frame) throw new ArgumentOutOfRangeException(nameof(frame));

            //値基準のindex
            var index = Frame.IndexOf(frame) + 1;
            value = Value[index];

            if (Frame.Remove(frame))
            {
                Value.RemoveAt(index);
            }

            return index;
        }
        #endregion

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
            private readonly ColorAnimationProperty _Property;
            private readonly int _Index;
            private readonly Color _New;
            private readonly Color _Old;

            public ChangeColorCommand(ColorAnimationProperty property, int index, Color color)
            {
                _Property = property ?? throw new ArgumentNullException(nameof(property));
                _Index = index;

                _New = color;
                _Old = property.Value[index];
            }

            public string Name => CommandName.ChangeColor;

            public void Do() => _Property.Value[_Index] = _New;
            public void Redo() => Do();
            public void Undo() => _Property.Value[_Index] = _Old;
        }

        private sealed class ChangeEaseCommand : IRecordCommand
        {
            private readonly ColorAnimationProperty _Property;
            private readonly EasingFunc _New;
            private readonly EasingFunc _Old;

            public ChangeEaseCommand(ColorAnimationProperty property, string type)
            {
                _Property = property ?? throw new ArgumentNullException(nameof(property));

                var data = EasingMetadata.LoadedEasingFunc.Find(x => x.Name == type)!;
                _New = data.CreateFunc();
                _New.Parent = property;
                _Old = _Property.EasingType;
            }
            public ChangeEaseCommand(ColorAnimationProperty property, EasingMetadata metadata)
            {
                _Property = property ?? throw new ArgumentNullException(nameof(property));

                _New = metadata.CreateFunc();
                _New.Parent = property;
                _Old = _Property.EasingType;
            }

            public string Name => CommandName.ChangeEasing;

            public void Do() => _Property.EasingType = _New;
            public void Redo() => Do();
            public void Undo() => _Property.EasingType = _Old;
        }

        private sealed class AddCommand : IRecordCommand
        {
            private readonly ColorAnimationProperty _Property;
            private readonly Frame _Frame;

            public AddCommand(ColorAnimationProperty property, Frame frame)
            {
                _Property = property ?? throw new ArgumentNullException(nameof(property));
                _Frame = frame;
            }

            public string Name => CommandName.AddKeyFrame;

            public void Do()
            {
                int index = _Property.InsertKeyframe(_Frame, _Property.GetValue(_Frame + _Property.GetParent2()?.Start ?? 0));
                _Property.AddKeyFrameEvent?.Invoke(_Property, (_Frame, index - 1));
            }
            public void Redo() => Do();
            public void Undo()
            {
                int index = _Property.RemoveKeyframe(_Frame, out _);
                _Property.DeleteKeyFrameEvent?.Invoke(_Property, index - 1);
            }
        }

        private sealed class RemoveCommand : IRecordCommand
        {
            private readonly ColorAnimationProperty _Property;
            private readonly Frame _Frame;
            private Color _Value;

            public RemoveCommand(ColorAnimationProperty property, Frame frame)
            {
                _Property = property ?? throw new ArgumentNullException(nameof(property));
                _Frame = frame;
            }

            public string Name => CommandName.RemoveKeyFrame;

            public void Do()
            {
                int index = _Property.RemoveKeyframe(_Frame, out _Value);
                _Property.DeleteKeyFrameEvent?.Invoke(_Property, index - 1);
            }
            public void Redo() => Do();
            public void Undo()
            {
                int index = _Property.InsertKeyframe(_Frame, _Value);
                _Property.AddKeyFrameEvent?.Invoke(_Property, (_Frame, index - 1));
            }
        }

        private sealed class MoveCommand : IRecordCommand
        {
            private readonly ColorAnimationProperty _Property;
            private readonly int _FromIndex;
            private int _ToIndex;
            private readonly Frame _ToFrame;

            public MoveCommand(ColorAnimationProperty property, int fromIndex, Frame to)
            {
                _Property = property ?? throw new ArgumentNullException(nameof(property));
                _FromIndex = fromIndex;
                _ToFrame = to;
            }

            public string Name => CommandName.MoveKeyFrame;

            public void Do()
            {
                _Property.Frame[_FromIndex] = _ToFrame;
                _Property.Frame.Sort((a_, b_) => a_ - b_);


                _ToIndex = _Property.Frame.FindIndex(x => x == _ToFrame);//新しいindex

                //Indexの正規化
                _Property.Value.Move(_FromIndex + 1, _ToIndex + 1);

                _Property.MoveKeyFrameEvent?.Invoke(_Property, (_FromIndex, _ToIndex));//GUIのIndexの正規化 UIスレッドで動作
            }
            public void Redo() => Do();
            public void Undo()
            {
                int frame = _Property.Frame[_ToIndex];

                _Property.Frame.RemoveAt(_ToIndex);
                _Property.Frame.Insert(_FromIndex, frame);

                _Property.Value.Move(_ToIndex + 1, _FromIndex + 1);


                _Property.MoveKeyFrameEvent?.Invoke(_Property, (_ToIndex, _FromIndex));
            }
        }

        #endregion
    }

#pragma warning disable CS1591
#pragma warning disable CS1573
#pragma warning disable CS1572

    /// <summary>
    /// <see cref="ColorAnimationPropertyMetadata"/> Initialize a new instance of the class.
    /// </summary>
    /// <param name="Name">Gets or sets the string to be displayed in the property header.</param>
    /// <param name="DefaultColor">Gets or sets the default color.</param>
    /// <param name="DefaultEase">Gets or sets the default easing function.</param>
    /// <param name="UseAlpha">Gets or sets a <see cref="bool"/> indicating whether or not to use the alpha component.</param>
    public record ColorAnimationPropertyMetadata(string Name, Color DefaultColor, EasingMetadata DefaultEase, bool UseAlpha = false) : ColorPropertyMetadata(Name, DefaultColor, UseAlpha)
    {
        /// <summary>
        /// <see cref="ColorAnimationPropertyMetadata"/> Initialize a new instance of the class.
        /// </summary>
        /// <param name="Name">Gets or sets the string to be displayed in the property header.</param>
        public ColorAnimationPropertyMetadata(string Name) : this(Name, default, EasingMetadata.LoadedEasingFunc[0])
        {

        }
        /// <summary>
        /// <see cref="ColorAnimationPropertyMetadata"/> Initialize a new instance of the class.
        /// </summary>
        /// <param name="Name">Gets or sets the string to be displayed in the property header.</param>
        /// <param name="DefaultColor">Gets or sets the default color.</param>
        /// <param name="UseAlpha">Gets or sets a <see cref="bool"/> indicating whether or not to use the alpha component.</param>
        public ColorAnimationPropertyMetadata(string Name, Color DefaultColor, bool UseAlpha = false)
            : this(Name, DefaultColor, EasingMetadata.LoadedEasingFunc[0], UseAlpha) { }
    }

#pragma warning restore CS1573
#pragma warning restore CS1591
#pragma warning restore CS1572
}
