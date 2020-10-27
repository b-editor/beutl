using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Serialization;

using BEditorCore.Data.EffectData;
using BEditorCore.Data.PropertyData.EasingSetting;
using BEditorCore.Media;

namespace BEditorCore.Data.PropertyData {
    /// <summary>
    /// 
    /// </summary>
    [DataContract(Namespace = "")]
    public class ColorAnimationProperty : PropertyElement, IKeyFrameProperty {

        /// <summary>
        /// 
        /// </summary>
        /// <param name="metadata"></param>
        public ColorAnimationProperty(ColorAnimationPropertyMetadata metadata) {
            Color4 color = Color4.FromRgba(metadata.Red, metadata.Green, metadata.Blue, metadata.Alpha);

            Value = new ObservableCollection<Color4> { color, color };
            Frame = new List<int>();
            EasingType = (EasingFunc)Activator.CreateInstance(metadata.DefaultEase.Type);
            PropertyMetadata = metadata;
        }

        #region PropertyElement

        /// <inheritdoc/>
        public override EffectElement Parent {
            get => parent;
            set {
                parent = value;
                EasingType.Parent = this;
            }
        }

        /// <inheritdoc/>
        public override void PropertyLoaded() {
            base.PropertyLoaded();
            EasingType.PropertyLoaded();
        }

        #endregion


        private EffectElement parent;
        private EasingFunc easingTypeProperty;
        private EasingData easingData;
        
        /// <summary>
        /// 
        /// </summary>
        [DataMember]
        public ObservableCollection<Color4> Value { get; set; }
        /// <summary>
        /// 
        /// </summary>
        [DataMember]
        public List<int> Frame { get; set; }
        /// <summary>
        /// 
        /// </summary>
        [DataMember]
        public EasingFunc EasingType {
            get {
                if (easingTypeProperty == null || EasingData.Type != easingTypeProperty.GetType()) {
                    easingTypeProperty = (EasingFunc)Activator.CreateInstance(EasingData.Type);
                    easingTypeProperty.Parent = this;
                }

                return easingTypeProperty;
            }
            set {
                SetValue(value, ref easingTypeProperty, nameof(EasingFunc));

                EasingData = EasingFunc.LoadedEasingFunc.Find(x => x.Type == value.GetType());
            }
        }

        public event EventHandler<(int frame, int index)> AddKeyFrameEvent;
        public event EventHandler<int> DeleteKeyFrameEvent;
        public event EventHandler<(int fromindex, int toindex)> MoveKeyFrameEvent;

        /// <summary>
        /// 
        /// </summary>
        public EasingData EasingData { get => easingData; set => SetValue(value, ref easingData, nameof(EasingData)); }
        internal int Length => ClipData.Length;


        /// <summary>
        /// イージングします
        /// </summary>
        /// <param name="frame">タイムライン基準のフレーム</param>
        /// <returns></returns>
        public Color4 GetValue(int frame) {

            (int, int) GetFrame(int frame) {
                if (Frame.Count == 0) {
                    return (0, Length);
                }
                else if (0 <= frame && frame <= Frame[0]) {
                    return (0, Frame[0]);
                }
                else if (Frame[^1] <= frame && frame <= Length) {
                    return (Frame[^1], Length);
                }
                else {
                    int index = 0;
                    for (int f = 0; f < Frame.Count() - 1; f++) {
                        if (Frame[f] <= frame && frame <= Frame[f + 1]) {
                            index = f;
                        }
                    }

                    return (Frame[index], Frame[index + 1]);
                }

                throw new Exception();
            }
            (Color4, Color4) GetValues(int frame) {
                if (Value.Count == 2) {
                    return (Value[0], Value[1]);
                }
                else if (0 <= frame && frame <= Frame[0]) {
                    return (Value[0], Value[1]);
                }
                else if (Frame[^1] <= frame && frame <= Length) {
                    return (Value[^2], Value[^1]);
                }
                else {
                    int index = 0;
                    for (int f = 0; f < Frame.Count() - 1; f++) {
                        if (Frame[f] <= frame && frame <= Frame[f + 1]) {
                            index = f + 1;
                        }
                    }

                    return (Value[index], Value[index + 1]);
                }

                throw new Exception();
            }

            frame -= ClipData.Start;

            var (start, end) = GetFrame(frame);

            var (stval, edval) = GetValues(frame);

            int now = frame - start;//相対的な現在フレーム



            byte red = (byte)EasingType.EaseFunc(now, end - start, stval.R, edval.R);
            byte green = (byte)EasingType.EaseFunc(now, end - start, stval.G, edval.G);
            byte blue = (byte)EasingType.EaseFunc(now, end - start, stval.B, edval.B);
            byte alpha = (byte)EasingType.EaseFunc(now, end - start, stval.A, edval.A);

            return Color4.FromRgba(red, green, blue, alpha);
        }

        #region キーフレーム操作

        private int InsertKeyframe(int frame, Color4 value) {
            Frame.Add(frame);


            List<int> tmp = new List<int>(Frame);
            tmp.Sort((a, b) => a - b);


            for (int i = 0; i < Frame.Count; i++) {
                Frame[i] = tmp[i];
            }

            int stindex = Frame.IndexOf(frame) + 1;

            Value.Insert(stindex, value);

            return stindex;
        }

        private int RemoveKeyframe(int frame, out Color4 value) {
            var index = Frame.IndexOf(frame) + 1;//値基準のindex
            value = Value[index];

            if (Frame.Remove(frame)) {
                Value.RemoveAt(index);
            }

            return index;
        }
        #endregion


        #region Commands

        /// <summary>
        /// 
        /// </summary>
        public class ChangeColor : IUndoRedoCommand {
            private readonly ColorAnimationProperty Color;
            private readonly int index;
            private readonly byte r, g, b, a;
            private readonly byte or, og, ob, oa;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="color"></param>
            /// <param name="index"></param>
            /// <param name="r"></param>
            /// <param name="g"></param>
            /// <param name="b"></param>
            /// <param name="a"></param>
            public ChangeColor(ColorAnimationProperty color, int index, byte r, byte g, byte b, byte a) {
                Color = color;
                this.index = index;

                (this.r, this.g, this.b, this.a) = (r, g, b, a);
                (or, og, ob, oa) = ((byte)color.Value[index].R, (byte)color.Value[index].G, (byte)color.Value[index].B, (byte)color.Value[index].A);
            }


            /// <inheritdoc/>
            public void Do() => Color.Value[index] = Color4.FromRgba(r, g, b, a);

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => Color.Value[index] = Color4.FromRgba(or, og, ob, oa);
        }
        /// <summary>
        /// 
        /// </summary>
        public class ChangeEase : IUndoRedoCommand {
            private readonly ColorAnimationProperty ColorProperty;
            private readonly EasingFunc EasingNumber;
            private readonly EasingFunc OldEasingNumber;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="easingList"></param>
            /// <param name="type"></param>
            public ChangeEase(ColorAnimationProperty easingList, string type) {
                ColorProperty = easingList;
                EasingNumber = (EasingFunc)Activator.CreateInstance(EasingFunc.LoadedEasingFunc.Find(x => x.Name == type).Type);
                EasingNumber.Parent = easingList;
                OldEasingNumber = ColorProperty.EasingType;
            }


            /// <inheritdoc/>
            public void Do() => ColorProperty.EasingType = EasingNumber;

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => ColorProperty.EasingType = OldEasingNumber;
        }


        /// <summary>
        /// 
        /// </summary>
        public class Add : IUndoRedoCommand {
            private readonly ColorAnimationProperty ColorProperty;
            private readonly int frame;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="colorProperty"></param>
            /// <param name="frame"></param>
            public Add(ColorAnimationProperty colorProperty, int frame) {
                ColorProperty = colorProperty;
                this.frame = frame;
            }


            /// <inheritdoc/>
            public void Do() {
                int index = ColorProperty.InsertKeyframe(frame, ColorProperty.GetValue(frame + ColorProperty.ClipData.Start));
                ColorProperty.AddKeyFrameEvent?.Invoke(ColorProperty, (frame, index - 1));
            }

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() {
                int index = ColorProperty.RemoveKeyframe(frame, out _);
                ColorProperty.DeleteKeyFrameEvent?.Invoke(ColorProperty, index - 1);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public class Remove : IUndoRedoCommand {
            private readonly ColorAnimationProperty ColorProperty;
            private readonly int frame;
            private Color4 value;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="colorProperty"></param>
            /// <param name="frame"></param>
            public Remove(ColorAnimationProperty colorProperty, int frame) {
                ColorProperty = colorProperty;
                this.frame = frame;
            }


            /// <inheritdoc/>
            public void Do() {
                int index = ColorProperty.RemoveKeyframe(frame, out value);
                ColorProperty.DeleteKeyFrameEvent?.Invoke(ColorProperty, index - 1);
            }

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() {
                int index = ColorProperty.InsertKeyframe(frame, value);
                ColorProperty.AddKeyFrameEvent?.Invoke(ColorProperty, (frame, index - 1));
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public class Move : IUndoRedoCommand {
            private readonly ColorAnimationProperty ColorProperty;
            private readonly int fromIndex;
            private int toIndex;
            private readonly int to;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="colorProperty"></param>
            /// <param name="fromIndex"></param>
            /// <param name="to"></param>
            public Move(ColorAnimationProperty colorProperty, int fromIndex, int to) {
                ColorProperty = colorProperty;
                this.fromIndex = fromIndex;
                this.to = to;
            }


            /// <inheritdoc/>
            public void Do() {
                ColorProperty.Frame[fromIndex] = to;
                ColorProperty.Frame.Sort((a_, b_) => a_ - b_);


                toIndex = ColorProperty.Frame.FindIndex(x => x == to);//新しいindex

                //Indexの正規化
                ColorProperty.Value.Move(fromIndex + 1, toIndex + 1);

                ColorProperty.MoveKeyFrameEvent?.Invoke(ColorProperty, (fromIndex, toIndex));//GUIのIndexの正規化 UIスレッドで動作
            }

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() {
                int frame = ColorProperty.Frame[toIndex];

                ColorProperty.Frame.RemoveAt(toIndex);
                ColorProperty.Frame.Insert(fromIndex, frame);

                ColorProperty.Value.Move(toIndex + 1, fromIndex + 1);


                ColorProperty.MoveKeyFrameEvent?.Invoke(ColorProperty, (toIndex, fromIndex));
            }
        }

        #endregion
    }

    /// <summary>
    /// 
    /// </summary>
    public class ColorAnimationPropertyMetadata : ColorPropertyMetadata {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        public ColorAnimationPropertyMetadata(string name) : base(name) => DefaultEase = EasingFunc.LoadedEasingFunc[0];
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
        public EasingData DefaultEase { get; private set; }
    }
}
