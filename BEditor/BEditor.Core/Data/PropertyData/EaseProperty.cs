using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Serialization;

using BEditor.Core.Data.EffectData;
using BEditor.Core.Data.ObjectData;
using BEditor.Core.Data.PropertyData.EasingSetting;

namespace BEditor.Core.Data.PropertyData
{
    /// <summary>
    /// <see cref="float"/> 型の値をイージングするプロパティを表します
    /// </summary>
    [DataContract(Namespace = "")]
    public class EaseProperty : PropertyElement, IKeyFrameProperty
    {
        private EffectElement parent;
        private EasingFunc easingTypeProperty;
        private EasingData easingData;


        /// <summary>
        /// <see cref="Time"/> に対応する <see langword="float"/> 型の値の <see cref="ObservableCollection{T}"/> を取得します
        /// </summary>
        /// <remarks>値の追加を行う場合は <see cref="InsertKeyframe(int, float)"/> 、削除は <see cref="RemoveKeyframe(int, out float)"/> を使用してください</remarks>
        [DataMember]
        public ObservableCollection<float> Value { get; private set; }
        /// <summary>
        /// <see cref="Value"/> に対応するフレーム番号の <see cref="List{T}"/> を取得します
        /// </summary>
        /// <remarks>値の追加を行う場合は <see cref="InsertKeyframe(int, float)"/> 、削除は <see cref="RemoveKeyframe(int, out float)"/> を使用してください</remarks>
        [DataMember]
        public List<int> Time { get; private set; }
        /// <summary>
        /// 現在のイージング関数を取得または設定します
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
                SetValue(value, ref easingTypeProperty, nameof(EasingFunc));

                EasingData = EasingFunc.LoadedEasingFunc.Find(x => x.Type == value.GetType());
            }
        }


        /// <summary>
        /// UIにキーフレームの追加を要求する場合に発生します
        /// </summary>
        /// <remarks>
        /// <list type="bullet">
        /// <item>
        /// <term>frame</term>
        /// <description>追加するフレーム番号</description>
        /// </item>
        /// <item>
        /// <term>index</term>
        /// <description><see cref="Time"/> のインデックス</description>
        /// </item>
        /// </list>
        /// </remarks>
        public event EventHandler<(int frame, int index)> AddKeyFrameEvent;
        /// <summary>
        /// UIにキーフレームの削除を要求する場合に発生します
        /// </summary>
        /// <remarks>
        /// <list type="bullet">
        /// <item>
        /// <term>e</term>
        /// <description><see cref="Value"/> の削除するインデックス</description>
        /// </item>
        /// </list>
        /// </remarks>
        public event EventHandler<int> DeleteKeyFrameEvent;
        /// <summary>
        /// UIにキーフレームの移動を要求する場合に発生します
        /// </summary>
        /// <remarks>
        /// <list type="bullet">
        /// <item>
        /// <term>toindex</term>
        /// <description><see cref="Time"/> の移動元のインデックス</description>
        /// </item>
        /// <item>
        /// <term>toindex</term>
        /// <description><see cref="Time"/> の移動先のインデックス</description>
        /// </item>
        /// </list>
        /// </remarks>
        public event EventHandler<(int fromindex, int toindex)> MoveKeyFrameEvent;

        /// <summary>
        /// 追加の値を取得または設定します
        /// </summary>
        public float Optional { get; set; }

        /// <summary>
        /// 現在のイージングのデータを取得または設定します
        /// </summary>
        public EasingData EasingData { get => easingData; set => SetValue(value, ref easingData, nameof(EasingData)); }

        internal int Length => ClipData.Length;


        /// <summary>
        /// <see cref="EaseProperty"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="metadata">このプロパティの <see cref="EasePropertyMetadata"/></param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> が <see langword="null"/> です</exception>
        public EaseProperty(EasePropertyMetadata metadata)
        {
            if (metadata is null) throw new ArgumentNullException(nameof(metadata));

            Value = new ObservableCollection<float> { metadata.DefaultValue, metadata.DefaultValue };
            Time = new List<int>();
            EasingType = (EasingFunc)Activator.CreateInstance(metadata.DefaultEase.Type);
            PropertyMetadata = metadata;
        }


        #region PropertyElementメンバー

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

        /// <inheritdoc/>
        public override void PropertyLoaded()
        {
            base.PropertyLoaded();
            EasingType.PropertyLoaded();
        }

        #endregion



        /// <summary>
        /// イージングをして、Optionalを追加します
        /// </summary>
        /// <param name="frame">タイムライン基準のフレーム</param>
        /// <returns></returns>
        public float GetValue(int frame)
        {

            (int, int) GetFrame(int frame)
            {
                if (Time.Count == 0)
                {
                    return (0, Length);
                }
                else if (0 <= frame && frame <= Time[0])
                {
                    return (0, Time[0]);
                }
                else if (Time[^1] <= frame && frame <= Length)
                {
                    return (Time[^1], Length);
                }
                else
                {
                    int index = 0;
                    for (int f = 0; f < Time.Count() - 1; f++)
                    {
                        if (Time[f] <= frame && frame <= Time[f + 1])
                        {
                            index = f;
                        }
                    }

                    return (Time[index], Time[index + 1]);
                }

                throw new Exception();
            }
            (float, float) GetValues(int frame)
            {
                if (Value.Count == 2)
                {
                    return (Value[0], Value[1]);
                }
                else if (0 <= frame && frame <= Time[0])
                {
                    return (Value[0], Value[1]);
                }
                else if (Time[^1] <= frame && frame <= Length)
                {
                    return (Value[^2], Value[^1]);
                }
                else
                {
                    int index = 0;
                    for (int f = 0; f < Time.Count() - 1; f++)
                    {
                        if (Time[f] <= frame && frame <= Time[f + 1])
                        {
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

            if ((PropertyMetadata as EasePropertyMetadata).UseOptional)
            {
                return InRange(out_ + Optional);
            }

            return InRange(out_);
        }
        /// <summary>
        /// <paramref name="value"/> が範囲外の場合その範囲に収まる値を、範囲内の場合 <paramref name="value"/> を返します
        /// </summary>
        /// <param name="value">対象の値</param>
        public float InRange(float value)
        {
            EasePropertyMetadata constant = (EasePropertyMetadata)PropertyMetadata;
            var max = constant.Max;
            var min = constant.Min;

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
        /// 特定のフレームにキーフレームを挿入します
        /// </summary>
        /// <param name="frame">追加するフレーム</param>
        /// <param name="value">追加する値</param>
        /// <returns>追加された <see cref="Value"/> のインデックス</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="frame"/> が 親要素の範囲外です</exception>
        public int InsertKeyframe(int frame, float value)
        {
            if (frame <= ClipData.Start || ClipData.End <= frame) throw new ArgumentOutOfRangeException(nameof(frame));

            Time.Add(frame);

            List<int> tmp = new List<int>(Time);
            tmp.Sort((a, b) => a - b);


            for (int i = 0; i < Time.Count; i++)
            {
                Time[i] = tmp[i];
            }

            int stindex = Time.IndexOf(frame) + 1;

            Value.Insert(stindex, value);

            return stindex;
        }
        /// <summary>
        /// 特定のフレームのキーフレームを削除します
        /// </summary>
        /// <param name="frame">削除するフレーム</param>
        /// <param name="value">削除された値</param>
        /// <returns>削除された <see cref="Value"/> のインデックス</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="frame"/> が 親要素の範囲外です</exception>
        public int RemoveKeyframe(int frame, out float value)
        {
            if (frame <= ClipData.Start || ClipData.End <= frame) throw new ArgumentOutOfRangeException(nameof(frame));

            var index = Time.IndexOf(frame) + 1;//値基準のindex

            value = Value[index];

            if (Time.Remove(frame))
            {
                Value.RemoveAt(index);
            }

            return index;
        }

        /// <inheritdoc/>
        public override string ToString() => $"(Count:{Value.Count} Easing:{EasingData?.Name} Name:{PropertyMetadata?.Name})";

        #region Commands

        /// <summary>
        /// 値を変更するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="UndoRedoManager.Do(IUndoRedoCommand)"/> と併用することでコマンドを記録できます</remarks>
        public sealed class ChangeValueCommand : IUndoRedoCommand
        {
            private readonly EaseProperty EaseSetting;
            private readonly int index;
            private readonly float newvalue;
            private readonly float oldvalue;

            /// <summary>
            /// <see cref="ChangeEaseCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="EaseProperty"/></param>
            /// <param name="index">変更する <see cref="Value"/> のインデックス</param>
            /// <param name="newvalue"><see cref="Value"/> の新しい値</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            /// <exception cref="IndexOutOfRangeException"><paramref name="index"/> が <see cref="Value"/> の範囲外です</exception>
            public ChangeValueCommand(EaseProperty property, int index, float newvalue)
            {
                EaseSetting = property ?? throw new ArgumentNullException(nameof(property));
                this.index = (index < 0 || index >= property.Value.Count) ?
                             throw new IndexOutOfRangeException($"{nameof(index)} is out of range of {nameof(Value)}")
                             : index;

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
        /// イージング関数を変更するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="UndoRedoManager.Do(IUndoRedoCommand)"/> と併用することでコマンドを記録できます</remarks>
        public sealed class ChangeEaseCommand : IUndoRedoCommand
        {
            private readonly EaseProperty EaseSetting;
            private readonly EasingFunc EasingNumber;
            private readonly EasingFunc OldEasingNumber;

            /// <summary>
            /// <see cref="ChangeEaseCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="EaseProperty"/></param>
            /// <param name="type">新しいイージング関数の名前</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            /// <exception cref="KeyNotFoundException"><paramref name="type"/> という名前のイージング関数が見つかりませんでした</exception>
            public ChangeEaseCommand(EaseProperty property, string type)
            {
                EaseSetting = property ?? throw new ArgumentNullException(nameof(property));
                var easingFunc = EasingFunc.LoadedEasingFunc.Find(x => x.Name == type) ?? throw new KeyNotFoundException($"No easing function named {type} was found");

                EasingNumber = (EasingFunc)Activator.CreateInstance(easingFunc.Type);
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
        /// キーフレームを追加するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="UndoRedoManager.Do(IUndoRedoCommand)"/> と併用することでコマンドを記録できます</remarks>
        public sealed class AddCommand : IUndoRedoCommand
        {
            private readonly EaseProperty EaseProperty;
            private readonly int frame;

            /// <summary>
            /// <see cref="AddCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="PropertyData.EaseProperty"/></param>
            /// <param name="frame">追加するフレーム</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="frame"/> が 親要素の範囲外です</exception>
            public AddCommand(EaseProperty property, int frame)
            {
                EaseProperty = property ?? throw new ArgumentNullException(nameof(property));

                this.frame = (frame <= 0 || property.ClipData.Length <= frame) ?
                             throw new ArgumentOutOfRangeException(nameof(frame))
                             : frame;
            }


            /// <inheritdoc/>
            public void Do()
            {
                int index = EaseProperty.InsertKeyframe(frame, EaseProperty.GetValue(frame + EaseProperty.ClipData.Start));
                EaseProperty.AddKeyFrameEvent?.Invoke(EaseProperty, (frame, index - 1));
            }

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo()
            {
                int index = EaseProperty.RemoveKeyframe(frame, out _);
                EaseProperty.DeleteKeyFrameEvent?.Invoke(EaseProperty, index - 1);
            }
        }

        /// <summary>
        /// キーフレームを削除するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="UndoRedoManager.Do(IUndoRedoCommand)"/> と併用することでコマンドを記録できます</remarks>
        public sealed class RemoveCommand : IUndoRedoCommand
        {
            private readonly EaseProperty EaseProperty;
            private readonly int frame;
            private float value;

            /// <summary>
            /// <see cref="RemoveCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="PropertyData.EaseProperty"/></param>
            /// <param name="frame">削除するフレーム</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="frame"/> が 親要素の範囲外です</exception>
            public RemoveCommand(EaseProperty property, int frame)
            {
                EaseProperty = property ?? throw new ArgumentNullException(nameof(property));

                this.frame = (frame <= 0 || property.ClipData.Length <= frame) ?
                             throw new ArgumentOutOfRangeException(nameof(frame))
                             : frame;
            }

            /// <inheritdoc/>
            public void Do()
            {
                int index = EaseProperty.RemoveKeyframe(frame, out value);

                EaseProperty.DeleteKeyFrameEvent?.Invoke(EaseProperty, index - 1);
            }

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo()
            {
                int index = EaseProperty.InsertKeyframe(frame, value);
                EaseProperty.AddKeyFrameEvent?.Invoke(EaseProperty, (frame, index - 1));
            }
        }

        /// <summary>
        /// キーフレームを移動するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="UndoRedoManager.Do(IUndoRedoCommand)"/> と併用することでコマンドを記録できます</remarks>
        public sealed class MoveCommand : IUndoRedoCommand
        {
            private readonly EaseProperty EaseProperty;
            private readonly int fromIndex;
            private int toIndex;
            private readonly int to;

            /// <summary>
            /// <see cref="MoveCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="PropertyData.EaseProperty"/></param>
            /// <param name="fromIndex">移動するキーフレームの <see cref="Value"/> のインデックス</param>
            /// <param name="to">移動先のフレーム番号</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            /// <exception cref="IndexOutOfRangeException"><paramref name="fromIndex"/> が <see cref="Value"/> の範囲外です</exception>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="to"/> が 親要素の範囲外です</exception>
            public MoveCommand(EaseProperty property, int fromIndex, int to)
            {
                EaseProperty = property ?? throw new ArgumentNullException(nameof(property));

                this.fromIndex = (0 > fromIndex || fromIndex > property.Value.Count) ?
                                 throw new IndexOutOfRangeException()
                                 : fromIndex;

                this.to = (to <= 0 || property.ClipData.Length <= to) ?
                          throw new ArgumentOutOfRangeException(nameof(to))
                          : to;
            }

            /// <inheritdoc/>
            public void Do()
            {
                EaseProperty.Time[fromIndex] = to;
                EaseProperty.Time.Sort((a_, b_) => a_ - b_);


                toIndex = EaseProperty.Time.FindIndex(x => x == to);//新しいindex

                EaseProperty.Value.Move(fromIndex + 1, toIndex + 1);

                EaseProperty.MoveKeyFrameEvent?.Invoke(EaseProperty, (fromIndex, toIndex));//GUIのIndexの正規化
            }

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo()
            {
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
    /// <see cref="EaseProperty"/> のメタデータを表します
    /// </summary>
    public record EasePropertyMetadata : PropertyElementMetadata
    {
        /// <summary>
        /// <see cref="EasePropertyMetadata"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        public EasePropertyMetadata(string name, float defaultvalue = 0, float max = float.NaN, float min = float.NaN, bool useoptional = false) : base(name)
        {
            DefaultValue = defaultvalue;
            DefaultEase = EasingFunc.LoadedEasingFunc[0];
            Max = max;
            Min = min;
            UseOptional = useoptional;
        }
        /// <summary>
        /// <see cref="EasePropertyMetadata"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        public EasePropertyMetadata(string name, float defaultvalue, EasingData easingType, float max = float.NaN, float min = float.NaN, bool useoptional = false) : base(name)
        {
            DefaultValue = defaultvalue;
            DefaultEase = easingType;
            Max = max;
            Min = min;
            UseOptional = useoptional;
        }

        /// <summary>
        /// デフォルトの値を取得します
        /// </summary>
        public float DefaultValue { get; init; }
        /// <summary>
        /// デフォルトのイージングデータを取得します
        /// </summary>
        public EasingData DefaultEase { get; init; }
        /// <summary>
        /// 最大の値を取得します
        /// </summary>
        public float Max { get; init; }
        /// <summary>
        /// 最小の値を取得します
        /// </summary>
        public float Min { get; init; }
        /// <summary>
        /// 追加の値を使用するかを取得します
        /// </summary>
        public bool UseOptional { get; init; }
    }
}
