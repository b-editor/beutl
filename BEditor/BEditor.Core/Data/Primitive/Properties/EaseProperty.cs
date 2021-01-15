using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.Serialization;

using BEditor.Core.Command;
using BEditor.Core.Data.Property;
using BEditor.Core.Data.Property.EasingProperty;
using BEditor.Core.Extensions;
using BEditor.Media;

namespace BEditor.Core.Data.Primitive.Properties
{
    /// <summary>
    /// <see cref="float"/> 型の値をイージングするプロパティを表します
    /// </summary>
    [DataContract]
    public partial class EaseProperty : PropertyElement<EasePropertyMetadata>, IKeyFrameProperty
    {
        #region Fields

        private static readonly PropertyChangedEventArgs easingFuncArgs = new(nameof(EasingType));
        private static readonly PropertyChangedEventArgs easingDataArgs = new(nameof(EasingData));
        private EffectElement parent;
        private EasingFunc easingTypeProperty;
        private EasingData easingData;

        #endregion


        /// <summary>
        /// <see cref="EaseProperty"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="metadata">このプロパティの <see cref="EasePropertyMetadata"/></param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> が <see langword="null"/> です</exception>
        public EaseProperty(EasePropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));

            Value = new ObservableCollection<float> { metadata.DefaultValue, metadata.DefaultValue };
            Time = new();
            EasingType = (EasingFunc)Activator.CreateInstance(metadata.DefaultEase.Type);
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
        public event EventHandler<(Frame frame, int index)> AddKeyFrameEvent;
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
        /// <see cref="Time"/> に対応する <see langword="float"/> 型の値の <see cref="ObservableCollection{T}"/> を取得します
        /// </summary>
        /// <remarks>値の追加を行う場合は <see cref="InsertKeyframe(Frame, float)"/> 、削除は <see cref="RemoveKeyframe(Frame, out float)"/> を使用してください</remarks>
        [DataMember]
        public ObservableCollection<float> Value { get; private set; }
        /// <summary>
        /// <see cref="Value"/> に対応するフレーム番号の <see cref="List{T}"/> を取得します
        /// </summary>
        /// <remarks>値の追加を行う場合は <see cref="InsertKeyframe(Frame, float)"/> 、削除は <see cref="RemoveKeyframe(Frame, out float)"/> を使用してください</remarks>
        [DataMember]
        public List<Frame> Time { get; private set; }
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
                SetValue(value, ref easingTypeProperty, easingDataArgs);

                EasingData = EasingFunc.LoadedEasingFunc.Find(x => x.Type == value.GetType());
            }
        }
        /// <summary>
        /// 追加の値を取得または設定します
        /// </summary>
        public float Optional { get; set; }
        /// <summary>
        /// 現在のイージングのデータを取得または設定します
        /// </summary>
        public EasingData EasingData
        {
            get => easingData;
            set => SetValue(value, ref easingData, easingDataArgs);
        }
        internal Frame Length => this.GetParent2().Length;

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


        #region Methods

        /// <summary>
        /// イージングをして、Optionalを追加します
        /// </summary>
        /// <param name="frame">タイムライン基準のフレーム</param>
        /// <returns></returns>
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

            frame -= this.GetParent2().Start;

            var (start, end) = GetFrame(this, frame);

            var (stval, edval) = GetValues(this, frame);

            int now = frame - start;//相対的な現在フレーム

            float out_ = EasingType.EaseFunc(now, end - start, stval, edval);

            if (PropertyMetadata.UseOptional)
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
            EasePropertyMetadata constant = PropertyMetadata;
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
        public int InsertKeyframe(Frame frame, float value)
        {
            if (frame <= this.GetParent2().Start || this.GetParent2().End <= frame) throw new ArgumentOutOfRangeException(nameof(frame));

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
        /// 特定のフレームのキーフレームを削除します
        /// </summary>
        /// <param name="frame">削除するフレーム</param>
        /// <param name="value">削除された値</param>
        /// <returns>削除された <see cref="Value"/> のインデックス</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="frame"/> が 親要素の範囲外です</exception>
        public int RemoveKeyframe(Frame frame, out float value)
        {
            if (frame <= this.GetParent2().Start || this.GetParent2().End <= frame) throw new ArgumentOutOfRangeException(nameof(frame));

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

        /// <inheritdoc/>
        public override void Loaded()
        {
            if (IsLoaded) return;

            EasingType.Loaded();
            base.Loaded();
        }
        /// <inheritdoc/>
        public override void Unloaded()
        {
            if (!IsLoaded) return;

            EasingType.Unloaded();
            base.Unloaded();
        }

        #endregion


        #region Commands

        /// <summary>
        /// 値を変更するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="CommandManager.Do(IRecordCommand)"/> と併用することでコマンドを記録できます</remarks>
        public sealed class ChangeValueCommand : IRecordCommand
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
        /// <remarks>このクラスは <see cref="CommandManager.Do(IRecordCommand)"/> と併用することでコマンドを記録できます</remarks>
        public sealed class ChangeEaseCommand : IRecordCommand
        {
            private readonly EaseProperty property;
            private readonly EasingFunc @new;
            private readonly EasingFunc old;

            /// <summary>
            /// <see cref="ChangeEaseCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="EaseProperty"/></param>
            /// <param name="type">新しいイージング関数の名前</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            /// <exception cref="KeyNotFoundException"><paramref name="type"/> という名前のイージング関数が見つかりませんでした</exception>
            public ChangeEaseCommand(EaseProperty property, string type)
            {
                this.property = property ?? throw new ArgumentNullException(nameof(property));
                var easingFunc = EasingFunc.LoadedEasingFunc.Find(x => x.Name == type) ?? throw new KeyNotFoundException($"No easing function named {type} was found");

                @new = easingFunc.CreateFunc?.Invoke() ?? (EasingFunc)Activator.CreateInstance(easingFunc.Type);
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
        /// キーフレームを追加するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="CommandManager.Do(IRecordCommand)"/> と併用することでコマンドを記録できます</remarks>
        public sealed class AddCommand : IRecordCommand
        {
            private readonly EaseProperty property;
            private readonly Frame frame;

            /// <summary>
            /// <see cref="AddCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="Properties.EaseProperty"/></param>
            /// <param name="frame">追加するフレーム</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="frame"/> が 親要素の範囲外です</exception>
            public AddCommand(EaseProperty property, Frame frame)
            {
                this.property = property ?? throw new ArgumentNullException(nameof(property));

                this.frame = (frame <= Frame.Zero || property.GetParent2().Length <= frame) ?
                             throw new ArgumentOutOfRangeException(nameof(frame))
                             : frame;
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
        /// キーフレームを削除するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="CommandManager.Do(IRecordCommand)"/> と併用することでコマンドを記録できます</remarks>
        public sealed class RemoveCommand : IRecordCommand
        {
            private readonly EaseProperty property;
            private readonly Frame frame;
            private float value;

            /// <summary>
            /// <see cref="RemoveCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="Properties.EaseProperty"/></param>
            /// <param name="frame">削除するフレーム</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="frame"/> が 親要素の範囲外です</exception>
            public RemoveCommand(EaseProperty property, Frame frame)
            {
                this.property = property ?? throw new ArgumentNullException(nameof(property));

                this.frame = (frame <= Frame.Zero || property.GetParent2().Length <= frame) ?
                             throw new ArgumentOutOfRangeException(nameof(frame))
                             : frame;
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
        /// キーフレームを移動するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="CommandManager.Do(IRecordCommand)"/> と併用することでコマンドを記録できます</remarks>
        public sealed class MoveCommand : IRecordCommand
        {
            private readonly EaseProperty property;
            private readonly int fromIndex;
            private int toIndex;
            private readonly Frame to;

            /// <summary>
            /// <see cref="MoveCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="Properties.EaseProperty"/></param>
            /// <param name="fromIndex">移動するキーフレームの <see cref="Value"/> のインデックス</param>
            /// <param name="to">移動先のフレーム番号</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            /// <exception cref="IndexOutOfRangeException"><paramref name="fromIndex"/> が <see cref="Value"/> の範囲外です</exception>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="to"/> が 親要素の範囲外です</exception>
            public MoveCommand(EaseProperty property, int fromIndex, Frame to)
            {
                this.property = property ?? throw new ArgumentNullException(nameof(property));

                this.fromIndex = (0 > fromIndex || fromIndex > property.Value.Count) ?
                                 throw new IndexOutOfRangeException()
                                 : fromIndex;

                this.to = (to <= Frame.Zero || property.GetParent2().Length <= to) ?
                          throw new ArgumentOutOfRangeException(nameof(to))
                          : to;
            }

            /// <inheritdoc/>
            public void Do()
            {
                property.Time[fromIndex] = to;
                property.Time.Sort((a_, b_) => a_ - b_);


                toIndex = property.Time.FindIndex(x => x == to);//新しいindex

                property.Value.Move(fromIndex + 1, toIndex + 1);

                property.MoveKeyFrameEvent?.Invoke(property, (fromIndex, toIndex));//GUIのIndexの正規化
            }

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo()
            {
                int frame = property.Time[toIndex];

                property.Time.RemoveAt(toIndex);
                property.Time.Insert(fromIndex, frame);


                property.Value.Move(toIndex + 1, fromIndex + 1);

                property.MoveKeyFrameEvent?.Invoke(property, (toIndex, fromIndex));
            }
        }

        #endregion
    }

    /// <summary>
    /// <see cref="EaseProperty"/> のメタデータを表します
    /// </summary>
    public record EasePropertyMetadata(string Name, EasingData DefaultEase, float DefaultValue = 0, float Max = float.NaN, float Min = float.NaN, bool UseOptional = false) : PropertyElementMetadata(Name)
    {
        /// <summary>
        /// <see cref="EasePropertyMetadata"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        public EasePropertyMetadata(string Name, float DefaultValue = 0, float Max = float.NaN, float Min = float.NaN, bool UseOptional = false)
            : this(Name, EasingFunc.LoadedEasingFunc[0], DefaultValue, Max, Min, UseOptional) { }
    }
}
