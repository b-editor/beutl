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
    public partial class ClipElement : ICloneable, IFormattable, IJsonObject, IElementObject
    {
        #region ICloneable
        /// <inheritdoc/>
        object ICloneable.Clone()
        {
            return Clone();
        }

        /// <inheritdoc cref="ICloneable.Clone"/>
        public ClipElement Clone()
        {
            var clip = this.DeepClone()!;

            clip.Parent = Parent;
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
            Id = element.GetProperty(nameof(Id)).GetInt32();
            Start = element.GetProperty(nameof(Start)).GetInt32();
            End = element.GetProperty(nameof(End)).GetInt32();
            Layer = element.GetProperty(nameof(Layer)).GetInt32();
            LabelText = element.GetProperty("Text").GetString() ?? "";
            var effects = element.GetProperty("Effects");
            Effect = new();
            foreach (var effect in effects.EnumerateArray())
            {
                var typeName = effect.GetProperty("_type").GetString() ?? "";
                if (Type.GetType(typeName) is var type && type is not null)
                {
                    var obj = (EffectElement)FormatterServices.GetUninitializedObject(type);
                    obj.SetObjectData(effect);

                    Effect.Add(obj);
                }
            }
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
        /// Create a command to add an effect to this clip
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
        /// Create a command to remove an effect to this clip
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
        /// <param name="toFrame">Frame to be moved</param>
        /// <param name="toLayer">Layer to be moved.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="toFrame"/> or <paramref name="toLayer"/> is less than 0.</exception>
        [Pure]
        public IRecordCommand MoveFrameLayer(Frame toFrame, int toLayer)
        {
            return new MoveCommand(this, toFrame, toLayer);
        }

        /// <summary>
        /// Create a command to move this clip frames and layers.
        /// </summary>
        /// <param name="to">Frame to be moved.</param>
        /// <param name="from">Frame to be moved from.</param>
        /// <param name="tolayer">Layer to be moved.</param>
        /// <param name="fromlayer">Layer to be moved from.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="to"/>, <paramref name="from"/>, <paramref name="tolayer"/>, <paramref name="fromlayer"/> is less than 0.</exception>
        [Pure]
        public IRecordCommand MoveFrameLayer(Frame to, Frame from, int tolayer, int fromlayer)
        {
            return new MoveCommand(this, to, from, tolayer, fromlayer);
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

        internal void MoveFrame(Frame f)
        {
            Start += f;
            End += f;
        }

        internal void MoveTo(Frame start)
        {
            var length = Length;
            Start = start;
            End = length + start;
        }

        /// <summary>
        /// Get the clip from its full name.
        /// </summary>
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
    }
}
