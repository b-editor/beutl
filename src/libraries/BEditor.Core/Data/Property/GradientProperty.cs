using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using BEditor.Command;
using BEditor.Data.Property.Easing;
using BEditor.Drawing;

namespace BEditor.Data.Property
{
    public struct GradientKeyPoint
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="GradientKeyPoint"/> struct.
        /// </summary>
        /// <param name="color">The color.</param>
        /// <param name="position">The position.</param>
        public GradientKeyPoint(Color color, float position)
        {
            Color = color;
            Position = Math.Clamp(position, 0, 1);
        }

        /// <summary>
        /// Gets the color.
        /// </summary>
        public Color Color { get; }

        /// <summary>
        /// Gets the position.
        /// </summary>
        public float Position { get; }

        /// <summary>
        /// Parses a <see cref="GradientKeyPoint"/> string.
        /// </summary>
        /// <param name="s">The string.</param>
        /// <returns>The parsed <see cref="GradientKeyPoint"/>.</returns>
        public static GradientKeyPoint Parse(string s)
        {
            var array = s.Split(',');
            if (array.Length != 2)
            {
                throw new Exception($"\"{s}\" could not be parsed.");
            }

            var pos = float.Parse(array[0]);
            var color = Color.Parse(array[1]);

            return new GradientKeyPoint(color, pos);
        }

        /// <summary>
        /// Parses a <see cref="GradientKeyPoint"/> string.
        /// A return value indicates whether the conversion succeeded or failed.
        /// </summary>
        /// <param name="s">The string.</param>
        /// <param name="result">The parsed <see cref="GradientKeyPoint"/>.</param>
        /// <returns>true if s was parsed successfully; otherwise, false.</returns>
        public static bool TryParse(string? s, out GradientKeyPoint result)
        {
            if (s == null)
            {
                result = default;
                return false;
            }

            var array = s.Split(',');
            if (array.Length != 2)
            {
                result = default;
                return false;
            }

            if (float.TryParse(array[0], out var pos))
            {
                var color = Color.Parse(array[1]);

                result = new GradientKeyPoint(color, pos);
                return true;
            }
            else
            {
                result = default;
                return false;
            }
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            return $"{Position},{Color:#argb}";
        }
    }

    public sealed class GradientProperty : PropertyElement<GradientPropertyMetadata>
    {
        public GradientProperty(GradientPropertyMetadata metadata)
        {
            PropertyMetadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
            KeyPoints = new()
            {
                new(metadata.Color1, 0),
                new(metadata.Color2, 1),
            };
        }

        public ObservableCollection<GradientKeyPoint> KeyPoints { get; private set; }

        public IRecordCommand AddPoint(GradientKeyPoint keyPoint)
        {
            return new AddPointCommand(this, keyPoint);
        }

        public IRecordCommand RemovePoint(GradientKeyPoint keyPoint)
        {
            return new RemovePointCommand(this, keyPoint);
        }

        public IRecordCommand UpdatePoint(int index, GradientKeyPoint keyPoint)
        {
            return new UpdatePointCommand(this, index, keyPoint);
        }

        /// <inheritdoc/>
        public override void GetObjectData(Utf8JsonWriter writer)
        {
            base.GetObjectData(writer);
            writer.WriteStartArray(nameof(KeyPoints));
            foreach (var item in KeyPoints)
            {
                writer.WriteStringValue(item.ToString());
            }

            writer.WriteEndArray();
        }

        /// <inheritdoc/>
        public override void SetObjectData(DeserializeContext context)
        {
            base.SetObjectData(context);
            var element = context.Element;
            KeyPoints = new(element.GetProperty(nameof(KeyPoints))
                .EnumerateArray()
                .Select<JsonElement, GradientKeyPoint?>(i => GradientKeyPoint.TryParse(i.GetString(), out var value) ? value : null)
                .Where(i => i != null)
                .Select(i => (GradientKeyPoint)i!));
        }

        private sealed class AddPointCommand : IRecordCommand
        {
            private readonly WeakReference<GradientProperty> _property;
            private readonly GradientKeyPoint _keyPoint;
            private readonly int _index;

            public AddPointCommand(GradientProperty property, GradientKeyPoint keyPoint)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));
                _keyPoint = keyPoint;

                for (var i = 0; i < property.KeyPoints.Count - 1; i++)
                {
                    var current = property.KeyPoints[i];
                    var nextIdx = i + 1;
                    var next = property.KeyPoints[nextIdx];
                    var curPs = current.Position;
                    var nextPs = next.Position;

                    if (curPs <= keyPoint.Position && keyPoint.Position <= nextPs)
                    {
                        _index = nextIdx;
                        return;
                    }
                }

                _index = property.KeyPoints.Count - 1;
            }

            public string Name => "グラデーションを追加";

            public void Do()
            {
                if (_property.TryGetTarget(out var target))
                {
                    target.KeyPoints.Insert(_index, _keyPoint);
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
                    target.KeyPoints.RemoveAt(_index);
                }
            }
        }

        private sealed class RemovePointCommand : IRecordCommand
        {
            private readonly WeakReference<GradientProperty> _property;
            private readonly GradientKeyPoint _keyPoint;
            private readonly int _index;

            public RemovePointCommand(GradientProperty property, GradientKeyPoint keyPoint)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));
                _keyPoint = keyPoint;
                _index = property.KeyPoints.IndexOf(keyPoint);
            }

            public string Name => "グラデーションを削除";

            public void Do()
            {
                if (_property.TryGetTarget(out var target))
                {
                    target.KeyPoints.RemoveAt(_index);
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
                    target.KeyPoints.Insert(_index, _keyPoint);
                }
            }
        }

        private sealed class UpdatePointCommand : IRecordCommand
        {
            private readonly WeakReference<GradientProperty> _property;
            private readonly GradientKeyPoint _newKeyPoint;
            private readonly GradientKeyPoint _oldKeyPoint;
            private readonly int _index;

            public UpdatePointCommand(GradientProperty property, int index, GradientKeyPoint keyPoint)
            {
                _property = new(property ?? throw new ArgumentNullException(nameof(property)));
                _newKeyPoint = keyPoint;
                _oldKeyPoint = property.KeyPoints[index];
                _index = index;
            }

            public string Name => "グラデーションを変更";

            public void Do()
            {
                if (_property.TryGetTarget(out var target))
                {
                    target.KeyPoints[_index] = _newKeyPoint;
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
                    target.KeyPoints[_index] = _oldKeyPoint;
                }
            }
        }
    }
}
