// ColorAnimationProperty.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.Serialization;
using System.Text.Json;

using BEditor.Command;
using BEditor.Data.Property.Easing;
using BEditor.Drawing;
using BEditor.Media;
using BEditor.Resources;

namespace BEditor.Data.Property
{
    /// <summary>
    /// Represents a property that eases the value of a <see cref="Color"/> type.
    /// </summary>
    [DebuggerDisplay("Count = {Value.Count}, Easing = {EasingData.Name}")]
    public class ColorAnimationProperty : PropertyElement<ColorAnimationPropertyMetadata>, IKeyframeProperty<Color>
    {
        private static readonly PropertyChangedEventArgs _easingFuncArgs = new(nameof(EasingType));
        private static readonly PropertyChangedEventArgs _easingDataArgs = new(nameof(EasingData));
        private EasingFunc? _easingTypeProperty;
        private EasingMetadata? _easingData;

        /// <summary>
        /// Initializes a new instance of the <see cref="ColorAnimationProperty"/> class.
        /// </summary>
        /// <param name="metadata">Metadata of this property.</param>
        /// <exception cref="ArgumentNullException"><paramref name="metadata"/> is <see langword="null"/>.</exception>
        public ColorAnimationProperty(ColorAnimationPropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            var color = metadata.DefaultColor;

            Pairs = new() { new(0, color), new(1, color) };
            EasingType = metadata.DefaultEase.CreateFunc();
        }

        /// <inheritdoc/>
        public event Action<float, int>? Added;

        /// <inheritdoc/>
        public event Action<int>? Removed;

        /// <inheritdoc/>
        public event Action<int, int>? Moved;

        /// <inheritdoc/>
        public ObservableCollection<KeyValuePair<float, Color>> Pairs { get; private set; }

        /// <summary>
        /// Gets or sets the current <see cref="EasingFunc"/>.
        /// </summary>
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
        internal Frame Length => Parent?.Parent?.Length ?? default;

        /// <summary>
        /// Gets an eased value.
        /// </summary>
        /// <param name="frame">The frame of the value to get.</param>
        public Color this[Frame frame] => GetValue(frame);

        /// <summary>
        /// Gets an eased value.
        /// </summary>
        /// <param name="frame">The frame of the value to get.</param>
        /// <returns>Returns an eased value.</returns>
        public Color GetValue(Frame frame)
        {
            (KeyValuePair<float, Color>, KeyValuePair<float, Color>) GetFrame(Frame frame)
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
            var (stval, edval) = (startPair.Value, endPair.Value);

            // 相対的な現在フレーム
            int now = frame - start;

            var red = EasingType.EaseFunc(now, end - start, stval.R, edval.R);
            var green = EasingType.EaseFunc(now, end - start, stval.G, edval.G);
            var blue = EasingType.EaseFunc(now, end - start, stval.B, edval.B);
            var alpha = EasingType.EaseFunc(now, end - start, stval.A, edval.A);

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
        /// <param name="value">Value to be added.</param>
        /// <returns>Index of the added <see cref="Pairs"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="frame"/> is outside the scope of the parent element.</exception>
        public int InsertKeyframe(float frame, Color value)
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
        public int RemoveKeyframe(float frame, out Color value)
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
                writer.WriteStringValue($"{item.Key},{item.Value:#argb}");
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
                    .Select(i => i.GetString()))
                    .Select(i => new KeyValuePair<float, Color>(i.First, Color.FromHTML(i.Second))));
            }
            else
            {
                Pairs = new(element.GetProperty(nameof(Pairs))
                    .EnumerateArray()
                    .Select(i => i.GetString()
                    ?.Split(',') ?? new string[2])
                    .Select(i => new KeyValuePair<float, Color>(float.Parse(i[0]), Color.FromHTML(i[1]))));
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
        /// <param name="color">New Color.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand ChangeColor(int index, Color color)
        {
            return new ChangeColorCommand(this, index, color);
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
            EasingType.Parent = this;
            EasingType.Load();
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

        private sealed class ChangeColorCommand : IRecordCommand
        {
            private readonly WeakReference<ColorAnimationProperty> _property;
            private readonly int _index;
            private readonly Color _new;
            private readonly Color _old;

            public ChangeColorCommand(ColorAnimationProperty property, int index, Color color)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));
                _index = (index < 0 || index >= property.Pairs.Count) ? throw new IndexOutOfRangeException($"{nameof(index)} is out of range of {nameof(EaseProperty.Pairs)}") : index;

                _new = color;
                _old = property.Pairs[index].Value;
            }

            public string Name => Strings.ChangeColor;

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
            private readonly WeakReference<ColorAnimationProperty> _property;
            private readonly EasingFunc _new;
            private readonly EasingFunc _old;

            public ChangeEaseCommand(ColorAnimationProperty property, string type)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));

                var data = EasingMetadata.LoadedEasingFunc.Find(x => x.Name == type)!;
                _new = data.CreateFunc();
                _new.Parent = property;
                _old = property.EasingType;
            }

            public ChangeEaseCommand(ColorAnimationProperty property, EasingMetadata metadata)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));

                _new = metadata.CreateFunc();
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
            private readonly WeakReference<ColorAnimationProperty> _property;
            private readonly float _frame;

            public AddCommand(ColorAnimationProperty property, float frame)
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
            private readonly WeakReference<ColorAnimationProperty> _property;
            private readonly float _frame;
            private Color _value;

            public RemoveCommand(ColorAnimationProperty property, float frame)
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
            private readonly WeakReference<ColorAnimationProperty> _property;
            private readonly float _oldFrame;
            private readonly float _newFrame;
            private int _fromIndex;
            private int _toIndex;

            public MoveCommand(ColorAnimationProperty property, int fromIndex, float to)
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