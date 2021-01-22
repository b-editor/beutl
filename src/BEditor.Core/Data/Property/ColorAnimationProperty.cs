using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
    /// 
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
        /// 
        /// </summary>
        [DataMember]
        public ObservableCollection<Color> Value { get; set; }
        /// <summary>
        /// 
        /// </summary>
        [DataMember]
        public List<Frame> Frame { get; set; }
        /// <summary>
        /// 
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
        /// 
        /// </summary>
        public EasingMetadata EasingData
        {
            get => _EasingData ?? EasingMetadata.LoadedEasingFunc[0];
            set => SetValue(value, ref _EasingData, _EasingDataArgs);
        }
        internal Frame Length => Parent?.Parent?.Length ?? default;
        /// <summary>
        /// イージングします
        /// </summary>
        /// <param name="frame">タイムライン基準のフレーム</param>
        /// <returns></returns>
        public Color this[Frame frame] => GetValue(frame);


        public event EventHandler<(Frame frame, int index)>? AddKeyFrameEvent;
        public event EventHandler<int>? DeleteKeyFrameEvent;
        public event EventHandler<(int fromindex, int toindex)>? MoveKeyFrameEvent;


        /// <summary>
        /// 
        /// </summary>
        /// <param name="metadata"></param>
        public ColorAnimationProperty(ColorAnimationPropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Color color = metadata.DefaultColor;

            Value = new() { color, color };
            Frame = new();
            EasingType = metadata.DefaultEase.CreateFunc();
        }


        #region Methods

        /// <summary>
        /// イージングします
        /// </summary>
        /// <param name="frame">タイムライン基準のフレーム</param>
        /// <returns></returns>
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

        public int InsertKeyframe(Frame frame, Color value)
        {
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

        public int RemoveKeyframe(Frame frame, out Color value)
        {
            var index = Frame.IndexOf(frame) + 1;//値基準のindex
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

        #endregion


        #region Commands

        /// <summary>
        /// 
        /// </summary>
        public sealed class ChangeColorCommand : IRecordCommand
        {
            private readonly ColorAnimationProperty _Property;
            private readonly int _Index;
            private readonly Color _New;
            private readonly Color _Old;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="property"></param>
            /// <param name="index"></param>
            /// <param name="color"></param>
            public ChangeColorCommand(ColorAnimationProperty property, int index, in Color color)
            {
                _Property = property ?? throw new ArgumentNullException(nameof(property));
                _Index = index;

                _New = color;
                _Old = property.Value[index];
            }

            public string Name => CommandName.ChangeColor;

            /// <inheritdoc/>
            public void Do() => _Property.Value[_Index] = _New;

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => _Property.Value[_Index] = _Old;
        }
        /// <summary>
        /// 
        /// </summary>
        public sealed class ChangeEaseCommand : IRecordCommand
        {
            private readonly ColorAnimationProperty _Property;
            private readonly EasingFunc _New;
            private readonly EasingFunc _Old;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="property"></param>
            /// <param name="type"></param>
            public ChangeEaseCommand(ColorAnimationProperty property, string type)
            {
                _Property = property ?? throw new ArgumentNullException(nameof(property));

                var data = EasingMetadata.LoadedEasingFunc.Find(x => x.Name == type)!;
                _New = data.CreateFunc();
                _New.Parent = property;
                _Old = _Property.EasingType;
            }

            public string Name => CommandName.ChangeEasing;

            /// <inheritdoc/>
            public void Do() => _Property.EasingType = _New;

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => _Property.EasingType = _Old;
        }


        /// <summary>
        /// 
        /// </summary>
        public sealed class AddCommand : IRecordCommand
        {
            private readonly ColorAnimationProperty _Property;
            private readonly Frame _Frame;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="property"></param>
            /// <param name="frame"></param>
            public AddCommand(ColorAnimationProperty property, Frame frame)
            {
                _Property = property ?? throw new ArgumentNullException(nameof(property));
                _Frame = frame;
            }

            public string Name => CommandName.AddKeyFrame;

            /// <inheritdoc/>
            public void Do()
            {
                int index = _Property.InsertKeyframe(_Frame, _Property.GetValue(_Frame + _Property.GetParent2()?.Start ?? 0));
                _Property.AddKeyFrameEvent?.Invoke(_Property, (_Frame, index - 1));
            }

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo()
            {
                int index = _Property.RemoveKeyframe(_Frame, out _);
                _Property.DeleteKeyFrameEvent?.Invoke(_Property, index - 1);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public sealed class RemoveCommand : IRecordCommand
        {
            private readonly ColorAnimationProperty _Property;
            private readonly Frame _Frame;
            private Color _Value;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="property"></param>
            /// <param name="frame"></param>
            public RemoveCommand(ColorAnimationProperty property, Frame frame)
            {
                _Property = property ?? throw new ArgumentNullException(nameof(property));
                _Frame = frame;
            }

            public string Name => CommandName.RemoveKeyFrame;

            /// <inheritdoc/>
            public void Do()
            {
                int index = _Property.RemoveKeyframe(_Frame, out _Value);
                _Property.DeleteKeyFrameEvent?.Invoke(_Property, index - 1);
            }

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo()
            {
                int index = _Property.InsertKeyframe(_Frame, _Value);
                _Property.AddKeyFrameEvent?.Invoke(_Property, (_Frame, index - 1));
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public sealed class MoveCommand : IRecordCommand
        {
            private readonly ColorAnimationProperty _Property;
            private readonly int _FromIndex;
            private int _ToIndex;
            private readonly Frame _ToFrame;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="property"></param>
            /// <param name="fromIndex"></param>
            /// <param name="to"></param>
            public MoveCommand(ColorAnimationProperty property, int fromIndex, Frame to)
            {
                _Property = property ?? throw new ArgumentNullException(nameof(property));
                _FromIndex = fromIndex;
                _ToFrame = to;
            }

            public string Name => CommandName.MoveKeyFrame;

            /// <inheritdoc/>
            public void Do()
            {
                _Property.Frame[_FromIndex] = _ToFrame;
                _Property.Frame.Sort((a_, b_) => a_ - b_);


                _ToIndex = _Property.Frame.FindIndex(x => x == _ToFrame);//新しいindex

                //Indexの正規化
                _Property.Value.Move(_FromIndex + 1, _ToIndex + 1);

                _Property.MoveKeyFrameEvent?.Invoke(_Property, (_FromIndex, _ToIndex));//GUIのIndexの正規化 UIスレッドで動作
            }

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
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

    /// <summary>
    /// 
    /// </summary>
    public record ColorAnimationPropertyMetadata(string Name, in Color DefaultColor, EasingMetadata DefaultEase, bool UseAlpha = false) : ColorPropertyMetadata(Name, DefaultColor, UseAlpha)
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="Name"></param>
        public ColorAnimationPropertyMetadata(string Name) : this(Name, default, EasingMetadata.LoadedEasingFunc[0])
        {

        }
        public ColorAnimationPropertyMetadata(string Name, in Color DefaultColor, bool UseAlpha = false)
            : this(Name, DefaultColor, EasingMetadata.LoadedEasingFunc[0], UseAlpha) { }
    }
}
