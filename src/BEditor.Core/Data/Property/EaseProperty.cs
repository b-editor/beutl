// EaseProperty.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json;

using BEditor.Command;
using BEditor.Data.Property.Easing;
using BEditor.Media;
using BEditor.Resources;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents the property that eases the value of a <see cref="float"/> type.
    /// </summary>
    [DebuggerDisplay("Count = {Pairs.Count}, Easing = {EasingData.Name}")]
    public class EaseProperty : PropertyElement<EasePropertyMetadata>, IKeyframeProperty<float>
    {
        private static readonly PropertyChangedEventArgs _easingDataArgs = new(nameof(EasingData));
        private EasingFunc? _easingTypeProperty;
        private EasingMetadata? _easingData;

        /// <summary>
        /// Initializes a new instance of the <see cref="EaseProperty"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public EaseProperty(EasePropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));

            Pairs = new()
            {
                new(0, metadata.DefaultValue),
                new(1, metadata.DefaultValue),
            };
            EasingType = metadata.DefaultEase.CreateFunc();
        }

        /// <inheritdoc/>
        public event Action<float, int>? Added;

        /// <inheritdoc/>
        public event Action<int>? Removed;

        /// <inheritdoc/>
        public event Action<int, int>? Moved;

        /// <inheritdoc/>
        public ObservableCollection<KeyValuePair<float, float>> Pairs { get; private set; }

        /// <inheritdoc/>
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
                SetValue(value, ref _easingTypeProperty, _easingDataArgs);

                EasingData = EasingMetadata.LoadedEasingFunc.Find(x => x.Type == value.GetType())!;
            }
        }

        /// <summary>
        /// Gets or sets an optional value.
        /// </summary>
        public float Optional { get; set; }

        /// <summary>
        /// Gets or sets the metadata for <see cref="EasingType"/>.
        /// </summary>
        public EasingMetadata EasingData
        {
            get => _easingData ?? EasingMetadata.LoadedEasingFunc[0];
            set => SetValue(value, ref _easingData, _easingDataArgs);
        }

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
        /// Gets the length of the clip.
        /// </summary>
        internal Frame Length => this.GetParent<ClipElement>()?.Length ?? default;

        /// <summary>
        /// Gets an eased value.
        /// </summary>
        /// <param name="frame">The frame of the value to get.</param>
        public float this[Frame frame] => GetValue(frame);

        /// <summary>
        /// Gets an eased value.
        /// </summary>
        /// <param name="frame">The frame of the value to get.</param>
        /// <returns>Returns an eased value.</returns>
        public float GetValue(Frame frame)
        {
            // frame: Relative
            // return: 前後のフレーム
            (KeyValuePair<float, float>, KeyValuePair<float, float>) GetFrame(Frame frame)
            {
                var time = frame / (float)Length;
                if (time >= 0 && time <= Pairs[1].Key)
                {
                    return (Pairs[0], Pairs[1]);
                }
                else if (Pairs[^2].Key <= time && time <= 1)
                {
                    return (Pairs[^2], Pairs[^1]);
                }
                else
                {
                    var index = 0;
                    for (var f = 0; f < Pairs.Count - 1; f++)
                    {
                        if (Pairs[f].Key <= time && time <= Pairs[f + 1].Key)
                        {
                            index = f;
                        }
                    }

                    return (Pairs[index], Pairs[index + 1]);
                }

                throw new Exception();
            }

            frame -= this.GetParent<ClipElement>()?.Start ?? default;

            var (startPair, endPair) = GetFrame(frame);
            var (start, end) = (GetRelFrame(startPair.Key), GetRelFrame(endPair.Key));

            // 相対的な現在フレーム
            int now = frame - start;

            var out_ = EasingType.EaseFunc(now, end - start, startPair.Value, endPair.Value);

            if (PropertyMetadata?.UseOptional ?? false)
            {
                return Clamp(out_ + Optional);
            }

            return Clamp(out_);
        }

        /// <summary>
        /// Returns <paramref name="value"/> clamped to the inclusive range of <see cref="EasePropertyMetadata.Min"/> and <see cref="EasePropertyMetadata.Max"/>.
        /// </summary>
        /// <param name="value">The value to be clamped.</param>
        /// <returns>value if min ≤ value ≤ max. -or- min if value &lt; min. -or- max if max &lt; value.</returns>
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
        /// Insert a keyframe at a specific frame.
        /// </summary>
        /// <param name="frame">Frame to be added.</param>
        /// <param name="value">Value to be added.</param>
        /// <returns>Index of the added <see cref="Pairs"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="frame"/> is outside the scope of the parent element.</exception>
        public int InsertKeyframe(float frame, float value)
        {
            if (frame <= 0 || frame >= 1) throw new ArgumentOutOfRangeException(nameof(frame));

            for (var i = 0; i < Pairs.Count - 1; i++)
            {
                var current = Pairs[i];
                var nextIdx = i + 1;
                var next = Pairs[nextIdx];
                if (current.Key <= frame && frame <= next.Key)
                {
                    Pairs.Insert(nextIdx, new(frame, value));
                    return nextIdx;
                }
            }

            Pairs.Add(new(frame, value));
            return Pairs.Count - 1;
        }

        /// <summary>
        /// Remove a keyframe of a specific frame.
        /// </summary>
        /// <param name="frame">Frame to be removed.</param>
        /// <param name="value">Removed value.</param>
        /// <returns>Index of the removed <see cref="Pairs"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="frame"/> is outside the scope of the parent element.</exception>
        public int RemoveKeyframe(float frame, out float value)
        {
            if (frame <= 0 || frame >= 1) throw new ArgumentOutOfRangeException(nameof(frame));

            var item = Pairs.First(i => i.Key == frame);
            value = item.Value;
            var index = Pairs.IndexOf(item);
            Pairs.RemoveAt(index);

            return index;
        }

        /// <inheritdoc/>
        public override void GetObjectData(Utf8JsonWriter writer)
        {
            base.GetObjectData(writer);

            // Pairs
            writer.WriteStartArray(nameof(Pairs));

            foreach (var item in Pairs)
            {
                writer.WriteStringValue($"{item.Key},{item.Value}");
            }

            writer.WriteEndArray();

            // Easing
            writer.WriteStartObject("Easing");

            var type = EasingType.GetType();
            writer.WriteString("_type", type.FullName + ", " + type.Assembly.GetName().Name);
            EasingType.GetObjectData(writer);

            writer.WriteEndObject();
        }

        /// <inheritdoc/>
        public override void SetObjectData(JsonElement element)
        {
            base.SetObjectData(element);

            // 古いバージョン
            if (element.TryGetProperty("Frames", out var frme))
            {
                var frames = new List<float> { 0 };
                foreach (var item in frme.EnumerateArray().Select(i => i.GetInt32()))
                {
                    frames.Add(item / (float)Length);
                }

                frames.Add(1);

                Pairs = new(frames.Zip(element
                    .GetProperty("Values")
                    .EnumerateArray()
                    .Select(i => i.GetSingle()))
                    .Select(i => new KeyValuePair<float, float>(i.First, i.Second)));
            }
            else
            {
                Pairs = new(element.GetProperty(nameof(Pairs))
                    .EnumerateArray()
                    .Select(i => i.GetString()
                    ?.Split(',') ?? new string[2])
                    .Select(i => new KeyValuePair<float, float>(float.Parse(i[0]), float.Parse(i[1]))));
            }

            var easing = element.GetProperty("Easing");
            var type = Type.GetType(easing.GetProperty("_type").GetString()!);
            if (type is null)
            {
                EasingType = EasingMetadata.LoadedEasingFunc[0].CreateFunc();
            }
            else
            {
                EasingType = (EasingFunc)FormatterServices.GetUninitializedObject(type);
                EasingType.SetObjectData(easing);
            }
        }

        /// <summary>
        /// Create a command to change the color of this <see cref="Pairs"/>.
        /// </summary>
        /// <param name="index">Index of colors to be changed.</param>
        /// <param name="value">New Value.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand ChangeValue(int index, float value)
        {
            return new ChangeValueCommand(this, index, value);
        }

        /// <summary>
        /// Create a command to change the easing function.
        /// </summary>
        /// <param name="metadata">New easing function metadata.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand ChangeEase(EasingMetadata metadata)
        {
            return new ChangeEaseCommand(this, metadata);
        }

        /// <inheritdoc/>
        [Pure]
        public IRecordCommand AddFrame(float frame)
        {
            return new AddCommand(this, frame);
        }

        /// <inheritdoc/>
        [Pure]
        public IRecordCommand RemoveFrame(float frame)
        {
            return new RemoveCommand(this, frame);
        }

        /// <inheritdoc/>
        [Pure]
        public IRecordCommand MoveFrame(int fromIndex, float toFrame)
        {
            return new MoveCommand(this, fromIndex, toFrame);
        }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            // Todo: ここに範囲外の場合の処理を書く
            EasingType.Load();
            EasingType.Parent = this;
        }

        /// <inheritdoc/>
        protected override void OnUnload()
        {
            EasingType.Unload();
        }

        private Frame GetRelFrame(float frame)
        {
            return (Frame)(Length * frame);
        }

        private sealed class ChangeValueCommand : IRecordCommand
        {
            private readonly WeakReference<EaseProperty> _property;
            private readonly int _index;
            private readonly float _new;
            private readonly float _old;

            public ChangeValueCommand(EaseProperty property, int index, float newvalue)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));
                _index = (index < 0 || index >= property.Pairs.Count) ? throw new IndexOutOfRangeException($"{nameof(index)} is out of range of {nameof(EaseProperty.Pairs)}") : index;

                _new = property.Clamp(newvalue);
                _old = property.Pairs[index].Value;
            }

            public string Name => Strings.ChangeValue;

            public void Do()
            {
                if (_property.TryGetTarget(out var target))
                {
                    var item = target.Pairs[_index];
                    target.Pairs[_index] = new(item.Key, _new);
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
                    var item = target.Pairs[_index];
                    target.Pairs[_index] = new(item.Key, _old);
                }
            }
        }

        private sealed class ChangeEaseCommand : IRecordCommand
        {
            private readonly WeakReference<EaseProperty> _property;
            private readonly EasingFunc _new;
            private readonly EasingFunc _old;

            public ChangeEaseCommand(EaseProperty property, EasingMetadata metadata)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));

                _new = metadata.CreateFunc();
                _new.Parent = property;
                _old = property.EasingType;
            }

            public ChangeEaseCommand(EaseProperty property, string type)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));
                var easingFunc = EasingMetadata.LoadedEasingFunc.Find(x => x.Name == type) ?? throw new KeyNotFoundException($"No easing function named {type} was found");

                _new = easingFunc.CreateFunc();
                _new.Parent = property;
                _old = property.EasingType;
            }

            public string Name => Strings.ChangeEasing;

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
            private readonly WeakReference<EaseProperty> _property;
            private readonly float _frame;

            public AddCommand(EaseProperty property, float frame)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));

                _frame = (frame <= 0 || frame >= 1) ? throw new ArgumentOutOfRangeException(nameof(frame)) : frame;
            }

            public string Name => Strings.AddKeyframe;

            public void Do()
            {
                if (_property.TryGetTarget(out var target))
                {
                    var clip = target.GetRequiredParent<ClipElement>();
                    var index = target.InsertKeyframe(_frame, target.GetValue((int)(_frame * clip.Length) + clip.Start));

                    target.Added?.Invoke(_frame, index);
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
                    var index = target.RemoveKeyframe(_frame, out _);

                    target.Removed?.Invoke(index);
                }
            }
        }

        private sealed class RemoveCommand : IRecordCommand
        {
            private readonly WeakReference<EaseProperty> _property;
            private readonly float _frame;
            private float _value;

            public RemoveCommand(EaseProperty property, float frame)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));

                _frame = (frame <= 0 || frame >= 1) ? throw new ArgumentOutOfRangeException(nameof(frame)) : frame;
            }

            public string Name => Strings.RemoveKeyframe;

            public void Do()
            {
                if (_property.TryGetTarget(out var target))
                {
                    var index = target.RemoveKeyframe(_frame, out _value);

                    target.Removed?.Invoke(index);
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
                    var index = target.InsertKeyframe(_frame, _value);

                    target.Added?.Invoke(_frame, index);
                }
            }
        }

        private sealed class MoveCommand : IRecordCommand
        {
            private readonly WeakReference<EaseProperty> _property;
            private readonly float _oldFrame;
            private readonly float _newFrame;
            private int _fromIndex;
            private int _toIndex;

            public MoveCommand(EaseProperty property, int fromIndex, float to)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));

                _fromIndex = (fromIndex < 0 || fromIndex > property.Pairs.Count) ? throw new IndexOutOfRangeException() : fromIndex;
                _oldFrame = property.Pairs[fromIndex].Key;
                _newFrame = (to <= 0 || to >= 1) ? throw new ArgumentOutOfRangeException(nameof(to)) : to;
            }

            public string Name => Strings.MoveKeyframe;

            public void Do()
            {
                if (_property.TryGetTarget(out var target))
                {
                    target.RemoveKeyframe(_oldFrame, out var value);
                    _toIndex = target.InsertKeyframe(_newFrame, value);

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
                    target.RemoveKeyframe(_newFrame, out var value);
                    _fromIndex = target.InsertKeyframe(_oldFrame, value);

                    target.Moved?.Invoke(_toIndex, _fromIndex);
                }
            }
        }
    }
}