using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.Serialization;

using BEditor.Core.Command;
using BEditor.Core.Data.Property;
using BEditor.Core.Data.Property.Easing;
using BEditor.Core.Extensions;
using BEditor.Media;

namespace BEditor.Core.Data.Property
{
    /// <summary>
    /// <see cref="float"/> 型の値をイージングするプロパティを表します
    /// </summary>
    [DataContract]
    public partial class EaseProperty : PropertyElement<EasePropertyMetadata>, IKeyFrameProperty
    {
        #region Fields

        private static readonly PropertyChangedEventArgs _EasingFuncArgs = new(nameof(EasingType));
        private static readonly PropertyChangedEventArgs _EasingDataArgs = new(nameof(EasingData));
        private EasingFunc? _EasingTypeProperty;
        private EasingMetadata? _EasingData;

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
            EasingType = metadata.DefaultEase.CreateFunc();
        }


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
                if (_EasingTypeProperty == null || EasingData.Type != _EasingTypeProperty.GetType())
                {
                    _EasingTypeProperty = EasingData.CreateFunc();
                    _EasingTypeProperty.Parent = this;
                }

                return _EasingTypeProperty;
            }
            set
            {
                SetValue(value, ref _EasingTypeProperty, _EasingDataArgs);

                EasingData = EasingMetadata.LoadedEasingFunc.Find(x => x.Type == value.GetType())!;
            }
        }
        /// <summary>
        /// 追加の値を取得または設定します
        /// </summary>
        public float Optional { get; set; }
        /// <summary>
        /// 現在のイージングのデータを取得または設定します
        /// </summary>
        public EasingMetadata EasingData
        {
            get => _EasingData ?? EasingMetadata.LoadedEasingFunc[0];
            set => SetValue(value, ref _EasingData, _EasingDataArgs);
        }
        internal Frame Length => this.GetParent2()?.Length ?? default;


        /// <summary>
        /// イージングをして、Optionalを追加します
        /// </summary>
        /// <param name="frame">タイムライン基準のフレーム</param>
        /// <returns></returns>
        public float this[Frame frame] => GetValue(frame);


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
        public event EventHandler<(Frame frame, int index)>? AddKeyFrameEvent;
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
        public event EventHandler<int>? DeleteKeyFrameEvent;
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
        public event EventHandler<(int fromindex, int toindex)>? MoveKeyFrameEvent;


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
        /// <paramref name="value"/> が範囲外の場合その範囲に収まる値を、範囲内の場合 <paramref name="value"/> を返します
        /// </summary>
        /// <param name="value">対象の値</param>
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
        /// 特定のフレームにキーフレームを挿入します
        /// </summary>
        /// <param name="frame">追加するフレーム</param>
        /// <param name="value">追加する値</param>
        /// <returns>追加された <see cref="Value"/> のインデックス</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="frame"/> が 親要素の範囲外です</exception>
        public int InsertKeyframe(Frame frame, float value)
        {
            if (frame <= this.GetParent2()!.Start || this.GetParent2()!.End <= frame) throw new ArgumentOutOfRangeException(nameof(frame));

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
            if (frame <= this.GetParent2()!.Start || this.GetParent2()!.End <= frame) throw new ArgumentOutOfRangeException(nameof(frame));

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

        [Pure]
        public IRecordCommand ChangeValue(int index, float value) => new ChangeValueCommand(this, index, value);
        [Pure]
        public IRecordCommand ChangeEase(string type) => new ChangeEaseCommand(this, type);
        [Pure]
        public IRecordCommand ChangeEase(EasingMetadata metadata) => new ChangeEaseCommand(this, metadata);
        [Pure]
        public IRecordCommand AddFrame(Frame frame) => new AddCommand(this, frame);
        [Pure]
        public IRecordCommand RemoveFrame(Frame frame) => new RemoveCommand(this, frame);
        [Pure]
        public IRecordCommand MoveFrame(int fromIndex, Frame toFrame) => new MoveCommand(this, fromIndex, toFrame);

        #endregion


        #region Commands

        /// <summary>
        /// 値を変更するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="CommandManager.Do(IRecordCommand)"/> と併用することでコマンドを記録できます</remarks>
        private sealed class ChangeValueCommand : IRecordCommand
        {
            private readonly EaseProperty _Property;
            private readonly int _Index;
            private readonly float _New;
            private readonly float _Old;

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
                _Property = property ?? throw new ArgumentNullException(nameof(property));
                _Index = (index < 0 || index >= property.Value.Count) ? throw new IndexOutOfRangeException($"{nameof(index)} is out of range of {nameof(Value)}") : index;

                _New = property.Clamp(newvalue);
                _Old = property.Value[index];
            }

            public string Name => CommandName.ChangeValue;

            /// <inheritdoc/>
            public void Do() => _Property.Value[_Index] = _New;

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo() => _Property.Value[_Index] = _Old;
        }

        /// <summary>
        /// イージング関数を変更するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="CommandManager.Do(IRecordCommand)"/> と併用することでコマンドを記録できます</remarks>
        private sealed class ChangeEaseCommand : IRecordCommand
        {
            private readonly EaseProperty _Property;
            private readonly EasingFunc _New;
            private readonly EasingFunc _Old;

            /// <summary>
            /// <see cref="ChangeEaseCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="EaseProperty"/></param>
            /// <param name="metadata">新しいイージング関数のメタデータ</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            public ChangeEaseCommand(EaseProperty property, EasingMetadata metadata)
            {
                _Property = property ?? throw new ArgumentNullException(nameof(property));

                _New = metadata.CreateFunc();
                _New.Parent = property;
                _Old = _Property.EasingType;
            }
            /// <summary>
            /// <see cref="ChangeEaseCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="EaseProperty"/></param>
            /// <param name="type">新しいイージング関数の名前</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            /// <exception cref="KeyNotFoundException"><paramref name="type"/> という名前のイージング関数が見つかりませんでした</exception>
            public ChangeEaseCommand(EaseProperty property, string type)
            {
                _Property = property ?? throw new ArgumentNullException(nameof(property));
                var easingFunc = EasingMetadata.LoadedEasingFunc.Find(x => x.Name == type) ?? throw new KeyNotFoundException($"No easing function named {type} was found");

                _New = easingFunc.CreateFunc();
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
        /// キーフレームを追加するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="CommandManager.Do(IRecordCommand)"/> と併用することでコマンドを記録できます</remarks>
        private sealed class AddCommand : IRecordCommand
        {
            private readonly EaseProperty _Property;
            private readonly Frame _Frame;

            /// <summary>
            /// <see cref="AddCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="EaseProperty"/></param>
            /// <param name="frame">追加するフレーム</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="frame"/> が 親要素の範囲外です</exception>
            public AddCommand(EaseProperty property, Frame frame)
            {
                _Property = property ?? throw new ArgumentNullException(nameof(property));

                _Frame = (frame <= Frame.Zero || property.GetParent2()!.Length <= frame) ? throw new ArgumentOutOfRangeException(nameof(frame)) : frame;
            }

            public string Name => CommandName.AddKeyFrame;

            /// <inheritdoc/>
            public void Do()
            {
                int index = _Property.InsertKeyframe(_Frame, _Property.GetValue(_Frame + _Property.GetParent2()!.Start));
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
        /// キーフレームを削除するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="CommandManager.Do(IRecordCommand)"/> と併用することでコマンドを記録できます</remarks>
        private sealed class RemoveCommand : IRecordCommand
        {
            private readonly EaseProperty _Property;
            private readonly Frame _Frame;
            private float _Value;

            /// <summary>
            /// <see cref="RemoveCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="EaseProperty"/></param>
            /// <param name="frame">削除するフレーム</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="frame"/> が 親要素の範囲外です</exception>
            public RemoveCommand(EaseProperty property, Frame frame)
            {
                _Property = property ?? throw new ArgumentNullException(nameof(property));

                _Frame = (frame <= Frame.Zero || property.GetParent2()!.Length <= frame) ? throw new ArgumentOutOfRangeException(nameof(frame)) : frame;
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
        /// キーフレームを移動するコマンド
        /// </summary>
        /// <remarks>このクラスは <see cref="CommandManager.Do(IRecordCommand)"/> と併用することでコマンドを記録できます</remarks>
        private sealed class MoveCommand : IRecordCommand
        {
            private readonly EaseProperty _Property;
            private readonly int _FromIndex;
            private int _ToIndex;
            private readonly Frame _ToFrame;

            /// <summary>
            /// <see cref="MoveCommand"/> クラスの新しいインスタンスを初期化します
            /// </summary>
            /// <param name="property">対象の <see cref="EaseProperty"/></param>
            /// <param name="fromIndex">移動するキーフレームの <see cref="Value"/> のインデックス</param>
            /// <param name="to">移動先のフレーム番号</param>
            /// <exception cref="ArgumentNullException"><paramref name="property"/> が <see langword="null"/> です</exception>
            /// <exception cref="IndexOutOfRangeException"><paramref name="fromIndex"/> が <see cref="Value"/> の範囲外です</exception>
            /// <exception cref="ArgumentOutOfRangeException"><paramref name="to"/> が 親要素の範囲外です</exception>
            public MoveCommand(EaseProperty property, int fromIndex, Frame to)
            {
                _Property = property ?? throw new ArgumentNullException(nameof(property));

                _FromIndex = (0 > fromIndex || fromIndex > property.Value.Count) ? throw new IndexOutOfRangeException() : fromIndex;

                _ToFrame = (to <= Frame.Zero || property.GetParent2()!.Length <= to) ? throw new ArgumentOutOfRangeException(nameof(to)) : to;
            }

            public string Name => CommandName.MoveKeyFrame;

            /// <inheritdoc/>
            public void Do()
            {
                _Property.Time[_FromIndex] = _ToFrame;
                _Property.Time.Sort((a_, b_) => a_ - b_);


                _ToIndex = _Property.Time.FindIndex(x => x == _ToFrame);//新しいindex

                _Property.Value.Move(_FromIndex + 1, _ToIndex + 1);

                _Property.MoveKeyFrameEvent?.Invoke(_Property, (_FromIndex, _ToIndex));//GUIのIndexの正規化
            }

            /// <inheritdoc/>
            public void Redo() => Do();

            /// <inheritdoc/>
            public void Undo()
            {
                int frame = _Property.Time[_ToIndex];

                _Property.Time.RemoveAt(_ToIndex);
                _Property.Time.Insert(_FromIndex, frame);


                _Property.Value.Move(_ToIndex + 1, _FromIndex + 1);

                _Property.MoveKeyFrameEvent?.Invoke(_Property, (_ToIndex, _FromIndex));
            }
        }

        #endregion
    }

    /// <summary>
    /// <see cref="EaseProperty"/> のメタデータを表します
    /// </summary>
    public record EasePropertyMetadata(string Name, EasingMetadata DefaultEase, float DefaultValue = 0, float Max = float.NaN, float Min = float.NaN, bool UseOptional = false) : PropertyElementMetadata(Name)
    {
        /// <summary>
        /// <see cref="EasePropertyMetadata"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        public EasePropertyMetadata(string Name, float DefaultValue = 0, float Max = float.NaN, float Min = float.NaN, bool UseOptional = false)
            : this(Name, EasingMetadata.LoadedEasingFunc[0], DefaultValue, Max, Min, UseOptional) { }
    }
}
