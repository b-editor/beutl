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
using BEditor.LangResources;
using BEditor.Media;

using Microsoft.Extensions.DependencyInjection;

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
            if (metadata.DefaultEase.CreateFunc is null)
                throw new DataException("Invalid easing.");

            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            var color = metadata.DefaultColor;

            Pairs = new() { new(0, color, PositionType.Percentage), new(1, color, PositionType.Percentage) };
            EasingType = metadata.DefaultEase.CreateFunc();
        }

        /// <inheritdoc/>
        public event Action<PositionInfo>? Added;

        /// <inheritdoc/>
        public event Action<PositionInfo>? Removed;

        /// <inheritdoc/>
        public event Action<int, int>? Moved;

        /// <inheritdoc/>
        public ObservableCollection<KeyFramePair<Color>> Pairs { get; private set; }

        /// <summary>
        /// Gets or sets the current <see cref="EasingFunc"/>.
        /// </summary>
        public EasingFunc EasingType
        {
            get
            {
                if (_easingTypeProperty == null || EasingData.Type != _easingTypeProperty.GetType())
                {
                    _easingTypeProperty = EasingData.CreateFunc!();
                    _easingTypeProperty.Parent = this;
                }

                return _easingTypeProperty;
            }
            set
            {
                var type = value.GetType();
                EasingData = EasingMetadata.Find(type);

                SetAndRaise(value, ref _easingTypeProperty, _easingFuncArgs);
            }
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
        /// Gets or sets the metadata for <see cref="EasingType"/>.
        /// </summary>
        public EasingMetadata EasingData
        {
            get => _easingData ?? EasingMetadata.GetDefault();
            set => SetAndRaise(value, ref _easingData, _easingDataArgs);
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
            (KeyFramePair<Color>, KeyFramePair<Color>) GetFrame(Frame frame)
            {
                var time = frame / (float)Length;
                if (time >= 0 && time <= Pairs[1].Position.GetPercentagePosition(Length))
                {
                    return (Pairs[0], Pairs[1]);
                }
                else if (Pairs[^2].Position.GetPercentagePosition(Length) <= time && time <= 1)
                {
                    return (Pairs[^2], Pairs[^1]);
                }
                else
                {
                    var index = 0;
                    for (var f = 0; f < Pairs.Count - 1; f++)
                    {
                        if (Pairs[f].Position.GetPercentagePosition(Length) <= time && time <= Pairs[f + 1].Position.GetPercentagePosition(Length))
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
            var (start, end) = ((Frame)startPair.Position.GetAbsolutePosition(Length), (Frame)endPair.Position.GetAbsolutePosition(Length));
            var (stval, edval) = (startPair.Value, endPair.Value);

            // 相対的な現在フレーム
            int now = frame - start;

            var red = EasingType.EaseFunc(now, end - start, stval.R, edval.R);
            var green = EasingType.EaseFunc(now, end - start, stval.G, edval.G);
            var blue = EasingType.EaseFunc(now, end - start, stval.B, edval.B);
            var alpha = EasingType.EaseFunc(now, end - start, stval.A, edval.A);

            return Color.FromArgb(
                (byte)alpha,
                (byte)red,
                (byte)green,
                (byte)blue);
        }

        /// <summary>
        /// Insert a keyframe at a specific frame.
        /// </summary>
        /// <param name="item">Item to be added.</param>
        /// <returns>Index of the added <see cref="Pairs"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="item"/> is outside the scope of the parent element.</exception>
        public int InsertKeyframe(KeyFramePair<Color> item)
        {
            var ps = item.Position.GetPercentagePosition(Length);
            if (ps <= 0 || ps >= 1) throw new ArgumentOutOfRangeException(nameof(item));

            for (var i = 0; i < Pairs.Count - 1; i++)
            {
                var current = Pairs[i];
                var nextIdx = i + 1;
                var next = Pairs[nextIdx];
                if (current.Position.GetPercentagePosition(Length) <= ps && ps <= next.Position.GetPercentagePosition(Length))
                {
                    Pairs.Insert(nextIdx, item);
                    return nextIdx;
                }
            }

            Pairs.Add(item);
            return Pairs.Count - 1;
        }

        /// <summary>
        /// Insert a keyframe at a specific frame.
        /// </summary>
        /// <param name="item">Item to be added.</param>
        /// <param name="index">The index.</param>
        /// <returns>Index of the added <see cref="Pairs"/>.</returns>
        public int InsertKeyframe(KeyFramePair<Color> item, int index)
        {
            Pairs.Insert(index, item);
            return index;
        }

        /// <summary>
        /// Remove a keyframe of a specific frame.
        /// </summary>
        /// <param name="item">Item to be removed.</param>
        /// <returns>Index of the removed <see cref="Pairs"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="item"/> is outside the scope of the parent element.</exception>
        public Color RemoveKeyframe(PositionInfo item)
        {
            var per = item.GetPercentagePosition(Length);
            if (per <= 0 || per >= 1) throw new ArgumentOutOfRangeException(nameof(item));

            var pair = Pairs.First(i => i.Position == item);
            Pairs.Remove(pair);

            return pair.Value;
        }

        /// <inheritdoc/>
        public override void GetObjectData(Utf8JsonWriter writer)
        {
            base.GetObjectData(writer);

            // Pairs
            writer.WriteStartArray(nameof(Pairs));

            foreach (var item in Pairs)
            {
                writer.WriteStringValue(item.ToString());
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
        public override void SetObjectData(DeserializeContext context)
        {
            base.SetObjectData(context);
            var element = context.Element;

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
                    .Select(i => new KeyFramePair<Color>(i.First, Color.Parse(i.Second), PositionType.Percentage)));
            }
            else
            {
                Pairs = new(element.GetProperty(nameof(Pairs))
                    .EnumerateArray()
                    .Select(i => (KeyFramePair<Color>?)(KeyFramePair<Color>.TryParse(i.GetString() ?? string.Empty, out var pair) ? pair : null))
                    .Where(i => i.HasValue)
                    .Select(i => (KeyFramePair<Color>)i!));
            }

            var easing = element.GetProperty("Easing");
            var type = Type.GetType(easing.GetProperty("_type").GetString()!);
            if (type is null)
            {
                EasingType = EasingMetadata.GetDefault().CreateFunc!();
                EasingType.Parent = this;
            }
            else
            {
                EasingType = (EasingFunc)FormatterServices.GetUninitializedObject(type);
                EasingType.SetObjectData(context.WithElement(easing).WithParent(this));
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
        public IRecordCommand AddFrame(PositionInfo position)
        {
            return new AddCommand(this, position);
        }

        /// <inheritdoc/>
        [Pure]
        public IRecordCommand RemoveFrame(PositionInfo position)
        {
            return new RemoveCommand(this, position);
        }

        /// <inheritdoc/>
        [Pure]
        public IRecordCommand MoveFrame(int fromIndex, PositionInfo toFrame)
        {
            return new MoveCommand(this, fromIndex, toFrame);
        }

        /// <inheritdoc/>
        [Pure]
        public IRecordCommand UpdatePositionInfo(int index, PositionInfo position)
        {
            return new UpdatePositionInfoCommand(this, index, position);
        }

        /// <inheritdoc/>
        public int IndexOf(PositionInfo position)
        {
            var item = Pairs.First(i => i.Position == position);
            return Pairs.IndexOf(item);
        }

        /// <inheritdoc/>
        public IEnumerable<PositionInfo> Enumerate()
        {
            return Pairs.Select(i => i.Position);
        }

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            EasingType.Parent = this;
            EasingType.Load();

            var clip = this.GetParent<ClipElement>();
            if (clip != null)
                clip.LengthChanging += Clip_LengthChanging;
        }

        /// <inheritdoc/>
        protected override void OnUnload()
        {
            EasingType.Unload();

            var clip = this.GetParent<ClipElement>();
            if (clip != null)
                clip.LengthChanging -= Clip_LengthChanging;
        }

        private void Clip_LengthChanging(object? sender, ClipLengthChangingEventArgs e)
        {
            var msg = ServicesLocator.Current.Provider.GetRequiredService<IMessage>();
            if (e.Anchor == ClipLengthChangeAnchor.End && sender is ClipElement clip)
            {
                var oldStart = clip.End - e.OldLength;
                var newStart = clip.End - e.NewLength;
                for (var i = 0; i < Pairs.Count; i++)
                {
                    var item = Pairs[i];
                    if (item.Position.Type == PositionType.Absolute)
                    {
                        // タイムラインベースのフレーム
                        var abs = item.Position.Value + oldStart;
                        var rel = abs - newStart;
                        var newItem = item.WithPosition(rel);

                        Pairs[i] = newItem;

                        Removed?.Invoke(item.Position);
                        Added?.Invoke(newItem.Position);

                        if (rel < 0)
                        {
                            msg.Snackbar(
                                string.Format(Strings.KeyframeHasBeenMovedOutOfRange, item.Position.ToString(), newItem.Position.ToString()),
                                string.Format(Strings.MessageBy, PropertyMetadata?.Name ?? Id.ToString()),
                                IMessage.IconType.Warning,
                                actionName: Strings.RemoveTarget,
                                parameter: (this, newItem.Position),
                                action: obj =>
                                {
                                    if (obj is (EaseProperty ease, PositionInfo pos))
                                    {
                                        ease.RemoveFrame(pos).Execute();
                                    }
                                });
                        }
                    }
                }
            }
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
                    target.Pairs[_index] = item.WithValue(_new);
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
                    target.Pairs[_index] = item.WithValue(_old);
                }
            }
        }

        private sealed class ChangeEaseCommand : IRecordCommand
        {
            private readonly WeakReference<ColorAnimationProperty> _property;
            private readonly EasingFunc _new;
            private readonly EasingFunc _old;

            public ChangeEaseCommand(ColorAnimationProperty property, EasingMetadata metadata)
            {
                if (metadata.CreateFunc is null) throw new DataException("Invalid easing.");

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
                    _old.Unload();
                    _new.Load();
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
                    _old.Load();
                    _new.Unload();
                    target.EasingType = _old;
                }
            }
        }

        private sealed class AddCommand : IRecordCommand
        {
            private readonly WeakReference<ColorAnimationProperty> _property;
            private readonly KeyFramePair<Color> _item;

            public AddCommand(ColorAnimationProperty property, PositionInfo position)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));
                var clip = property.GetRequiredParent<ClipElement>();

                _item = new(
                    position,
                    property.GetValue(((int)position.GetAbsolutePosition(clip.Length)) + clip.Start));
            }

            public string Name => Strings.AddKeyframe;

            public void Do()
            {
                if (_property.TryGetTarget(out var target))
                {
                    target.InsertKeyframe(_item);

                    target.Added?.Invoke(_item.Position);
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
                    target.RemoveKeyframe(_item.Position);

                    target.Removed?.Invoke(_item.Position);
                }
            }
        }

        private sealed class RemoveCommand : IRecordCommand
        {
            private readonly WeakReference<ColorAnimationProperty> _property;
            private readonly PositionInfo _item;
            private Color _value;

            public RemoveCommand(ColorAnimationProperty property, PositionInfo item)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));
                _item = item;
            }

            public string Name => Strings.RemoveKeyframe;

            public void Do()
            {
                if (_property.TryGetTarget(out var target))
                {
                    _value = target.RemoveKeyframe(_item);

                    target.Removed?.Invoke(_item);
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
                    target.InsertKeyframe(new(_item, _value));

                    target.Added?.Invoke(_item);
                }
            }
        }

        private sealed class MoveCommand : IRecordCommand
        {
            private readonly WeakReference<ColorAnimationProperty> _property;
            private readonly PositionInfo _oldFrame;
            private readonly PositionInfo _newFrame;
            private int _fromIndex;
            private int _toIndex;

            public MoveCommand(ColorAnimationProperty property, int fromIndex, PositionInfo to)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));

                _fromIndex = (fromIndex < 0 || fromIndex > property.Pairs.Count) ? throw new IndexOutOfRangeException() : fromIndex;
                _oldFrame = property.Pairs[fromIndex].Position;
                _newFrame = to;
            }

            public string Name => Strings.MoveKeyframe;

            public void Do()
            {
                if (_property.TryGetTarget(out var target))
                {
                    var value = target.RemoveKeyframe(_oldFrame);
                    _toIndex = target.InsertKeyframe(new(_newFrame, value));

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
                    var value = target.RemoveKeyframe(_newFrame);
                    _fromIndex = target.InsertKeyframe(new(_oldFrame, value));

                    target.Moved?.Invoke(_toIndex, _fromIndex);
                }
            }
        }

        private sealed class UpdatePositionInfoCommand : IRecordCommand
        {
            private readonly WeakReference<ColorAnimationProperty> _property;
            private readonly PositionInfo _oldValue;
            private readonly PositionInfo _newValue;
            private readonly int _index;

            public UpdatePositionInfoCommand(ColorAnimationProperty property, int index, PositionInfo value)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));

                _index = (index < 0 || index > property.Pairs.Count) ? throw new IndexOutOfRangeException() : index;
                _oldValue = property.Pairs[index].Position;
                _newValue = value;
            }

            public string Name => Strings.UpdatePosition;

            public void Do()
            {
                if (_property.TryGetTarget(out var target))
                {
                    target.Pairs[_index] = target.Pairs[_index]
                        .WithPosition(_newValue.Value)
                        .WithType(_newValue.Type);
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
                    target.Pairs[_index] = target.Pairs[_index]
                        .WithPosition(_oldValue.Value)
                        .WithType(_oldValue.Type);
                }
            }
        }
    }
}