using System;
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Globalization;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using BEditor.Command;
using BEditor.Media;

namespace BEditor.Data
{
    /// <summary>
    /// Represents a data of a clip to be placed in the timeline.
    /// </summary>
    public partial class ClipElement : ICloneable, IFormattable, IJsonObject, IElementObject
    {
        /// <summary>
        /// Gets the clip from its full name.
        /// </summary>
        /// <param name="name">The <see cref="string"/> that can be retrieved by <see cref="ToString(string?)"/> of the <see cref="ClipElement"/> to be searched.</param>
        /// <param name="project">The <see cref="Project"/> contains the <see cref="ClipElement"/> to be retrieved.</param>
        /// <returns>The <see cref="ClipElement"/> in the <paramref name="project"/> that match the <paramref name="name"/>.</returns>
        public static ClipElement? FromFullName(string name, Project? project)
        {
            if (project is null) return null;

            var reg = new Regex(@"^([\da-zA-Z亜-熙ぁ-んァ-ヶ]+)\.([\da-zA-Z]+)\z");

            if (reg.IsMatch(name))
            {
                var match = reg.Match(name);

                var scene = project.Find(match.Groups[1].Value);
                var clip = scene?.Find(match.Groups[2].Value);

                return clip;
            }

            return null;
        }

        #region ICloneable

        /// <inheritdoc/>
        object ICloneable.Clone()
        {
            return Clone();
        }

        /// <inheritdoc cref="ICloneable.Clone"/>
        public ClipElement Clone()
        {
            var clip = this.DeepClone();

            clip!.Parent = Parent;
            clip.Load();

            return clip;
        }
        #endregion

        #region IJsonObject

        /// <inheritdoc/>
        public override void GetObjectData(Utf8JsonWriter writer)
        {
            base.GetObjectData(writer);
            writer.WriteNumber(nameof(Id), Id);
            writer.WriteNumber(nameof(Start), Start);
            writer.WriteNumber(nameof(End), End);
            writer.WriteNumber(nameof(Layer), Layer);
            writer.WriteString("Text", LabelText);
            writer.WriteStartArray("Effects");
            {
                foreach (var effect in Effect)
                {
                    writer.WriteStartObject();
                    {
                        var type = effect.GetType();
                        writer.WriteString("_type", type.FullName + ", " + type.Assembly.GetName().Name);
                        effect.GetObjectData(writer);
                    }
                    writer.WriteEndObject();
                }
            }
            writer.WriteEndArray();
        }

        /// <inheritdoc/>
        public override void SetObjectData(JsonElement element)
        {
            base.SetObjectData(element);
            _id = element.GetProperty(nameof(Id)).GetInt32();
            Start = element.GetProperty(nameof(Start)).GetInt32();
            End = element.GetProperty(nameof(End)).GetInt32();
            Layer = element.GetProperty(nameof(Layer)).GetInt32();
            LabelText = element.GetProperty("Text").GetString() ?? string.Empty;
            var effects = element.GetProperty("Effects");
            _effect = new();
            foreach (var effect in effects.EnumerateArray())
            {
                var typeName = effect.GetProperty("_type").GetString() ?? string.Empty;
                if (Type.GetType(typeName) is var type && type is not null)
                {
                    var obj = (EffectElement)FormatterServices.GetUninitializedObject(type);
                    obj.SetObjectData(effect);

                    Effect.Add(obj);
                }
            }

            Metadata = ObjectMetadata.LoadedObjects.First(i => i.Name == Effect[0].Name);
        }
        #endregion

        #region IFormattable

        /// <inheritdoc cref="IFormattable.ToString(string?, IFormatProvider?)"/>
        public string ToString(string? format)
        {
            return ToString(format, CultureInfo.CurrentCulture);
        }

        /// <inheritdoc/>
        public string ToString(string? format, IFormatProvider? formatProvider)
        {
            if (string.IsNullOrEmpty(format)) format = "#";

            return format switch
            {
                "#" => $"{Parent.Name}.{Name}",
                _ => throw new FormatException(string.Format("The {0} format string is not supported.", format))
            };
        }
        #endregion

        #region Commands

        /// <summary>
        /// Create a command to add an effect to this clip.
        /// </summary>
        /// <param name="effect"><see cref="EffectElement"/> to be added.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="effect"/> is <see langword="null"/>.</exception>
        [Pure]
        public IRecordCommand AddEffect(EffectElement effect)
        {
            if (effect is null) throw new ArgumentNullException(nameof(effect));

            return new EffectElement.AddCommand(effect, this);
        }

        /// <summary>
        /// Create a command to remove an effect to this clip.
        /// </summary>
        /// <param name="effect"><see cref="EffectElement"/> to be removed.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        /// <exception cref="ArgumentNullException"><paramref name="effect"/> is <see langword="null"/>.</exception>
        [Pure]
        public IRecordCommand RemoveEffect(EffectElement effect)
        {
            if (effect is null) throw new ArgumentNullException(nameof(effect));

            return new EffectElement.RemoveCommand(effect, this);
        }

        /// <summary>
        /// Create a command to move this clip frames and layers.
        /// </summary>
        /// <param name="newFrame">Frame to be moved.</param>
        /// <param name="newLayer">Layer to be moved.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="newFrame"/> or <paramref name="newLayer"/> is less than 0.</exception>
        [Pure]
        public IRecordCommand MoveFrameLayer(Frame newFrame, int newLayer)
        {
            return new MoveCommand(this, newFrame, newLayer);
        }

        /// <summary>
        /// Create a command to move this clip frames and layers.
        /// </summary>
        /// <param name="newFrame">Frame to be moved.</param>
        /// <param name="oldFrame">Frame to be moved from.</param>
        /// <param name="newLayer">Layer to be moved.</param>
        /// <param name="oldLayer">Layer to be moved from.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="newFrame"/>, <paramref name="oldFrame"/>, <paramref name="newLayer"/>, <paramref name="oldLayer"/> is less than 0.</exception>
        [Pure]
        public IRecordCommand MoveFrameLayer(Frame newFrame, Frame oldFrame, int newLayer, int oldLayer)
        {
            return new MoveCommand(this, newFrame, oldFrame, newLayer, oldLayer);
        }

        /// <summary>
        /// Create a command to change the length of this clip.
        /// </summary>
        /// <param name="start">New start frame for this <see cref="ClipElement"/>.</param>
        /// <param name="end">New end frame for this <see cref="ClipElement"/>.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="start"/> or <paramref name="end"/> is less than 0.</exception>
        [Pure]
        public IRecordCommand ChangeLength(Frame start, Frame end)
        {
            return new LengthChangeCommand(this, start, end);
        }

        /// <summary>
        /// Create a command to split this clip at the specified frame.
        /// </summary>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand Split(Frame frame)
        {
            return new SplitCommand(this, frame);
        }
        #endregion

        /// <summary>
        /// Render this clip.
        /// </summary>
        /// <exception cref="RenderingException">Faileds to rendering.</exception>
        public void Render(ClipRenderArgs args)
        {
            try
            {
                var loadargs = new EffectRenderArgs(args.Frame, args.Type);

                if (Effect[0] is ObjectElement obj)
                {
                    if (!obj.IsEnabled) return;

                    obj.Render(loadargs);
                }
            }
            catch (Exception e)
            {
                throw new RenderingException("Faileds to rendering.", e);
            }
        }

        /// <summary>
        /// Prepare this clip for rendering.
        /// </summary>
        /// <exception cref="RenderingException">Faileds to rendering.</exception>
        public void PreviewRender(ClipRenderArgs args)
        {
            try
            {
                var enableEffects = Effect.Where(x => x.IsEnabled);
                var loadargs = new EffectRenderArgs(args.Frame, args.Type);

                foreach (var item in enableEffects)
                {
                    item.PreviewRender(loadargs);
                }
            }
            catch (Exception e)
            {
                throw new RenderingException("Faileds to rendering.", e);
            }
        }

        /// <summary>
        /// 指定した開始フレームにクリップを移動します.
        /// </summary>
        /// <param name="start">開始フレームです.</param>
        internal void MoveTo(Frame start)
        {
            var length = Length;
            Start = start;
            End = length + start;
        }
    }
}
