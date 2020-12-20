using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization;

using BEditor.Core.Command;
using BEditor.Core.Data.Control;
using BEditor.Core.Data.Property;
using BEditor.Core.Data.Property.EasingProperty;
using BEditor.Core.Extensions;
using BEditor.Core.Media;
using BEditor.Media;

namespace BEditor.Core.Data.Primitive.Properties
{
    /// <summary>
    /// 
    /// </summary>
    [DataContract]
    public class ColorAnimationProperty : PropertyElement<ColorAnimationPropertyMetadata>, IKeyFrameProperty
    {
        #region Fields

        private static readonly PropertyChangedEventArgs easingFuncArgs = new(nameof(EasingType));
        private static readonly PropertyChangedEventArgs easingDataArgs = new(nameof(EasingData));
        private EffectElement parent;
        private EasingFunc easingTypeProperty;
        private EasingData easingData;

        #endregion


        /// <summary>
        /// 
        /// </summary>
        /// <param name="metadata"></param>
        public ColorAnimationProperty(ColorAnimationPropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            Color color = new(metadata.Red, metadata.Green, metadata.Blue, metadata.Alpha);

            Value = new() { color, color };
            Frame = new();
            EasingType = (EasingFunc)Activator.CreateInstance(metadata.DefaultEase.Type);
        }


        public event EventHandler<(Frame frame, int index)> AddKeyFrameEvent;
        public event EventHandler<int> DeleteKeyFrameEvent;
        public event EventHandler<(int fromindex, int toindex)> MoveKeyFrameEvent;


        /// <inheritdoc/>
        public override EffectElement Parent
        {
            get => parent;
            set
            {
                parent = value;
                EasingType.Parent = this;
            }
        }

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
                if (easingTypeProperty == null || EasingData.Type != easingTypeProperty.GetType())
                {
                    easingTypeProperty = (EasingFunc)Activator.CreateInstance(EasingData.Type);
                    easingTypeProperty.Parent = this;
                }

                return easingTypeProperty;
            }
            set
            {
                SetValue(value, ref easingTypeProperty, easingFuncArgs);

                EasingData = EasingFunc.LoadedEasingFunc.Find(x => x.Type == value.GetType());
            }
        }
        /// <summary>
        /// 
        /// </summary>
        public EasingData EasingData
        {
            get => easingData;
            set => SetValue(value, ref easingData, easingDataArgs);
        }
        internal Frame Length => Parent.Parent.Length;


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

            frame -= this.GetParent2().Start;

            var (start, end) = GetFrame(this, frame);

            var (stval, edval) = GetValues(this, frame);

            int now = frame - start;//相対的な現在フレーム



            float red = EasingType.EaseFunc(now, end - start, stval.ScR, edval.ScR);
            float green = EasingType.EaseFunc(now, end - start, stval.ScG, edval.ScG);
            float blue = EasingType.EaseFunc(now, end - start, stval.ScB, edval.ScB);
            float alpha = EasingType.EaseFunc(now, end - start, stval.ScA, edval.ScA);

            return new Color(red, green, blue, alpha);
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
        public override void PropertyLoaded()
        {
            base.PropertyLoaded();
            EasingType.PropertyLoaded();
        }

        #endregion


        #region Commands

        /// <summary>
        /// 
        /// </summary>
        public sealed class ChangeColorCommand : IRecordCommand
        {
            private readonly ColorAnimationProperty property;
            private readonly int index;
            private readonly ReadOnlyColor @new;
            private readonly ReadOnlyColor old;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="property"></param>
            /// <param name="index"></param>
            /// <param name="color"></param>
            public ChangeColorCommand(ColorAnimationProperty property, int index, in ReadOnlyColor color)
            {
                this.property = property ?? throw new ArgumentNullException(nameof(property));
                this.index = index;

                this.@new = color;
                old = property.Value[index];
            }


            /// <inheritdoc/>
            public void Do() => property.Value[index] = @new;

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => property.Value[index] = old;
        }
        /// <summary>
        /// 
        /// </summary>
        public sealed class ChangeEaseCommand : IRecordCommand
        {
            private readonly ColorAnimationProperty property;
            private readonly EasingFunc @new;
            private readonly EasingFunc old;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="property"></param>
            /// <param name="type"></param>
            public ChangeEaseCommand(ColorAnimationProperty property, string type)
            {
                this.property = property ?? throw new ArgumentNullException(nameof(property));

                var data = EasingFunc.LoadedEasingFunc.Find(x => x.Name == type);
                @new = data.CreateFunc?.Invoke() ?? (EasingFunc)Activator.CreateInstance(data.Type);
                @new.Parent = property;
                old = this.property.EasingType;
            }


            /// <inheritdoc/>
            public void Do() => property.EasingType = @new;

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => property.EasingType = old;
        }


        /// <summary>
        /// 
        /// </summary>
        public sealed class AddCommand : IRecordCommand
        {
            private readonly ColorAnimationProperty property;
            private readonly Frame frame;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="property"></param>
            /// <param name="frame"></param>
            public AddCommand(ColorAnimationProperty property, Frame frame)
            {
                this.property = property ?? throw new ArgumentNullException(nameof(property));
                this.frame = frame;
            }


            /// <inheritdoc/>
            public void Do()
            {
                int index = property.InsertKeyframe(frame, property.GetValue(frame + property.GetParent2().Start));
                property.AddKeyFrameEvent?.Invoke(property, (frame, index - 1));
            }

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo()
            {
                int index = property.RemoveKeyframe(frame, out _);
                property.DeleteKeyFrameEvent?.Invoke(property, index - 1);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public sealed class RemoveCommand : IRecordCommand
        {
            private readonly ColorAnimationProperty property;
            private readonly Frame frame;
            private Color value;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="property"></param>
            /// <param name="frame"></param>
            public RemoveCommand(ColorAnimationProperty property, Frame frame)
            {
                this.property = property ?? throw new ArgumentNullException(nameof(property));
                this.frame = frame;
            }


            /// <inheritdoc/>
            public void Do()
            {
                int index = property.RemoveKeyframe(frame, out value);
                property.DeleteKeyFrameEvent?.Invoke(property, index - 1);
            }

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo()
            {
                int index = property.InsertKeyframe(frame, value);
                property.AddKeyFrameEvent?.Invoke(property, (frame, index - 1));
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public sealed class MoveCommand : IRecordCommand
        {
            private readonly ColorAnimationProperty property;
            private readonly int fromIndex;
            private int toIndex;
            private readonly Frame to;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="property"></param>
            /// <param name="fromIndex"></param>
            /// <param name="to"></param>
            public MoveCommand(ColorAnimationProperty property, int fromIndex, Frame to)
            {
                this.property = property ?? throw new ArgumentNullException(nameof(property));
                this.fromIndex = fromIndex;
                this.to = to;
            }


            /// <inheritdoc/>
            public void Do()
            {
                property.Frame[fromIndex] = to;
                property.Frame.Sort((a_, b_) => a_ - b_);


                toIndex = property.Frame.FindIndex(x => x == to);//新しいindex

                //Indexの正規化
                property.Value.Move(fromIndex + 1, toIndex + 1);

                property.MoveKeyFrameEvent?.Invoke(property, (fromIndex, toIndex));//GUIのIndexの正規化 UIスレッドで動作
            }

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo()
            {
                int frame = property.Frame[toIndex];

                property.Frame.RemoveAt(toIndex);
                property.Frame.Insert(fromIndex, frame);

                property.Value.Move(toIndex + 1, fromIndex + 1);


                property.MoveKeyFrameEvent?.Invoke(property, (toIndex, fromIndex));
            }
        }

        #endregion
    }

    /// <summary>
    /// 
    /// </summary>
    public record ColorAnimationPropertyMetadata : ColorPropertyMetadata
    {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        public ColorAnimationPropertyMetadata(string name) : base(name, 255, 255, 255, 255) => DefaultEase = EasingFunc.LoadedEasingFunc[0];
        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <param name="r"></param>
        /// <param name="g"></param>
        /// <param name="b"></param>
        /// <param name="a"></param>
        /// <param name="usealpha"></param>
        public ColorAnimationPropertyMetadata(string name, byte r, byte g, byte b, byte a = 255, bool usealpha = false) : base(name, r, g, b, a, usealpha) => DefaultEase = EasingFunc.LoadedEasingFunc[0];
        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <param name="r"></param>
        /// <param name="g"></param>
        /// <param name="b"></param>
        /// <param name="easingType"></param>
        /// <param name="a"></param>
        /// <param name="usealpha"></param>
        public ColorAnimationPropertyMetadata(string name, byte r, byte g, byte b, EasingData easingType, byte a = 255, bool usealpha = false) : base(name, r, g, b, a, usealpha) => DefaultEase = easingType;

        /// <summary>
        /// 
        /// </summary>
        public EasingData DefaultEase { get; init; }
    }
}
