using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Serialization;

using BEditorCore.Data.EffectData;
using BEditorCore.Data.ObjectData;
using BEditorCore.Data.PropertyData.EasingSetting;

namespace BEditorCore.Data.PropertyData {
    /// <summary>
    /// 
    /// </summary>
    [DataContract(Namespace = "")]
    public class EaseProperty : PropertyElement, IKeyFrameProperty {
        private EffectElement parent;
        private EasingFunc easingTypeProperty;
        private EasingData easingData;


        /// <summary>
        /// 
        /// </summary>
        [DataMember]
        public ObservableCollection<float> Value { get; set; }
        /// <summary>
        /// 
        /// </summary>
        [DataMember]
        public List<int> Time { get; set; }
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
        public float Optional { get; set; }

        /// <summary>
        /// 
        /// </summary>
        public EasingData EasingData { get => easingData; set => SetValue(value, ref easingData, nameof(EasingData)); }

        internal int Length => ClipData.Length;


        /// <summary>
        /// 
        /// </summary>
        /// <param name="metadata"></param>
        public EaseProperty(EasePropertyMetadata metadata) {
            Value = new ObservableCollection<float> { metadata.DefaultValue, metadata.DefaultValue };
            Time = new List<int>();
            EasingType = (EasingFunc)Activator.CreateInstance(metadata.DefaultEase.Type);
            PropertyMetadata = metadata;
        }


        #region PropertyElementメンバー

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



        /// <summary>
        /// イージングをして、Optionalを追加します
        /// </summary>
        /// <param name="frame">タイムライン基準のフレーム</param>
        /// <returns></returns>
        public float GetValue(int frame) {

            (int, int) GetFrame(int frame) {
                if (Time.Count == 0) {
                    return (0, Length);
                }
                else if (0 <= frame && frame <= Time[0]) {
                    return (0, Time[0]);
                }
                else if (Time[^1] <= frame && frame <= Length) {
                    return (Time[^1], Length);
                }
                else {
                    int index = 0;
                    for (int f = 0; f < Time.Count() - 1; f++) {
                        if (Time[f] <= frame && frame <= Time[f + 1]) {
                            index = f;
                        }
                    }

                    return (Time[index], Time[index + 1]);
                }

                throw new Exception();
            }
            (float, float) GetValues(int frame) {
                if (Value.Count == 2) {
                    return (Value[0], Value[1]);
                }
                else if (0 <= frame && frame <= Time[0]) {
                    return (Value[0], Value[1]);
                }
                else if (Time[^1] <= frame && frame <= Length) {
                    return (Value[^2], Value[^1]);
                }
                else {
                    int index = 0;
                    for (int f = 0; f < Time.Count() - 1; f++) {
                        if (Time[f] <= frame && frame <= Time[f + 1]) {
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



            float out_ = EasingType.EaseFunc(now, end - start, stval, edval);

            if ((PropertyMetadata as EasePropertyMetadata).UseOptional) {
                return InRange(out_ + Optional);
            }

            return InRange(out_);
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="value"></param>
        /// <returns></returns>
        public float InRange(float value) {
            EasePropertyMetadata constant = (EasePropertyMetadata)PropertyMetadata;
            var max = constant.Max;
            var min = constant.Min;

            if (!float.IsNaN(min) && value <= min) {
                return min;
            }
            else if (!float.IsNaN(max) && max <= value) {
                return max;
            }

            return value;
        }

        private int InsertKeyframe(int frame, float value) {
            Time.Add(frame);


            List<int> tmp = new List<int>(Time);
            tmp.Sort((a, b) => a - b);


            for (int i = 0; i < Time.Count; i++) {
                Time[i] = tmp[i];
            }

            int stindex = Time.IndexOf(frame) + 1;

            Value.Insert(stindex, value);

            return stindex;
        }
        private int RemoveKeyframe(int frame, out float value) {
            var index = Time.IndexOf(frame) + 1;//値基準のindex

            value = Value[index];

            if (Time.Remove(frame)) {
                Value.RemoveAt(index);
            }

            return index;
        }

        public override string ToString() => $"(Count:{Value.Count} Easing:{EasingData?.Name} Name:{PropertyMetadata?.Name})";

        #region Commands

        /// <summary>
        /// 
        /// </summary>
        public class ChangeValue : IUndoRedoCommand {
            private readonly EaseProperty EaseSetting;
            private readonly int index;
            private readonly float newvalue;
            private readonly float oldvalue;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="property"></param>
            /// <param name="index"></param>
            /// <param name="newvalue"></param>
            public ChangeValue(EaseProperty property, int index, float newvalue) {
                EaseSetting = property;
                this.index = index;
                this.newvalue = property.InRange(newvalue);
                oldvalue = property.Value[index];
            }


            /// <inheritdoc/>
            public void Do() => EaseSetting.Value[index] = newvalue;

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => EaseSetting.Value[index] = oldvalue;
        }

        /// <summary>
        /// 
        /// </summary>
        public class ChangeEase : IUndoRedoCommand {
            private readonly EaseProperty EaseSetting;
            private readonly EasingFunc EasingNumber;
            private readonly EasingFunc OldEasingNumber;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="property"></param>
            /// <param name="type"></param>
            public ChangeEase(EaseProperty property, string type) {
                EaseSetting = property;
                EasingNumber = (EasingFunc)Activator.CreateInstance(EasingFunc.LoadedEasingFunc.Find(x => x.Name == type).Type);
                EasingNumber.Parent = property;
                OldEasingNumber = EaseSetting.EasingType;
            }


            /// <inheritdoc/>
            public void Do() => EaseSetting.EasingType = EasingNumber;

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => EaseSetting.EasingType = OldEasingNumber;
        }


        /// <summary>
        /// 
        /// </summary>
        public class Add : IUndoRedoCommand {
            private readonly EaseProperty EaseProperty;
            private readonly int frame;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="easeProperty"></param>
            /// <param name="frame"></param>
            public Add(EaseProperty easeProperty, int frame) {
                EaseProperty = easeProperty;
                this.frame = frame;
            }


            /// <inheritdoc/>
            public void Do() {
                int index = EaseProperty.InsertKeyframe(frame, EaseProperty.GetValue(frame + EaseProperty.ClipData.Start));
                EaseProperty.AddKeyFrameEvent?.Invoke(EaseProperty, (frame, index - 1));
            }

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() {
                int index = EaseProperty.RemoveKeyframe(frame, out _);
                EaseProperty.DeleteKeyFrameEvent?.Invoke(EaseProperty, index - 1);
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public class Remove : IUndoRedoCommand {
            private readonly EaseProperty EaseProperty;
            private readonly int frame;
            private float value;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="easeProperty"></param>
            /// <param name="frame"></param>
            public Remove(EaseProperty easeProperty, int frame) {
                EaseProperty = easeProperty;
                this.frame = frame;
            }

            /// <inheritdoc/>
            public void Do() {
                int index = EaseProperty.RemoveKeyframe(frame, out value);

                EaseProperty.DeleteKeyFrameEvent?.Invoke(EaseProperty, index - 1);
            }

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() {
                int index = EaseProperty.InsertKeyframe(frame, value);
                EaseProperty.AddKeyFrameEvent?.Invoke(EaseProperty, (frame, index - 1));
            }
        }

        /// <summary>
        /// 
        /// </summary>
        public class Move : IUndoRedoCommand {
            private readonly EaseProperty EaseProperty;
            private readonly int fromIndex;
            private int toIndex;
            private readonly int to;

            /// <summary>
            /// 
            /// </summary>
            /// <param name="easeProperty"></param>
            /// <param name="fromIndex"></param>
            /// <param name="to"></param>
            public Move(EaseProperty easeProperty, int fromIndex, int to) {
                EaseProperty = easeProperty;
                this.fromIndex = fromIndex;
                this.to = to;
            }

            /// <inheritdoc/>
            public void Do() {
                EaseProperty.Time[fromIndex] = to;
                EaseProperty.Time.Sort((a_, b_) => a_ - b_);


                toIndex = EaseProperty.Time.FindIndex(x => x == to);//新しいindex

                EaseProperty.Value.Move(fromIndex + 1, toIndex + 1);

                EaseProperty.MoveKeyFrameEvent?.Invoke(EaseProperty, (fromIndex, toIndex));//GUIのIndexの正規化
            }

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() {
                int frame = EaseProperty.Time[toIndex];

                EaseProperty.Time.RemoveAt(toIndex);
                EaseProperty.Time.Insert(fromIndex, frame);


                EaseProperty.Value.Move(toIndex + 1, fromIndex + 1);

                EaseProperty.MoveKeyFrameEvent?.Invoke(EaseProperty, (toIndex, fromIndex));
            }
        }

        #endregion
    }

    /// <summary>
    /// 
    /// </summary>
    public class EasePropertyMetadata : PropertyElementMetadata {
        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <param name="defaultvalue"></param>
        /// <param name="max"></param>
        /// <param name="min"></param>
        /// <param name="useoptional"></param>
        public EasePropertyMetadata(string name, float defaultvalue = 0, float max = float.NaN, float min = float.NaN, bool useoptional = false) : base(name) {
            DefaultValue = defaultvalue;
            DefaultEase = EasingFunc.LoadedEasingFunc[0];
            Max = max;
            Min = min;
            UseOptional = useoptional;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="name"></param>
        /// <param name="defaultvalue"></param>
        /// <param name="easingType"></param>
        /// <param name="max"></param>
        /// <param name="min"></param>
        /// <param name="useoptional"></param>
        public EasePropertyMetadata(string name, float defaultvalue, EasingData easingType, float max = float.NaN, float min = float.NaN, bool useoptional = false) : base(name) {
            DefaultValue = defaultvalue;
            DefaultEase = easingType;
            Max = max;
            Min = min;
            UseOptional = useoptional;
        }

        /// <summary>
        /// 
        /// </summary>
        public float DefaultValue { get; }
        /// <summary>
        /// 
        /// </summary>
        public EasingData DefaultEase { get; }
        /// <summary>
        /// 
        /// </summary>
        public float Max { get; }
        /// <summary>
        /// 
        /// </summary>
        public float Min { get; }
        /// <summary>
        /// 
        /// </summary>
        public bool UseOptional { get; }
    }
}
