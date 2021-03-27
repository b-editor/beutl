using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

using BEditor.Audio;
using BEditor.Command;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;
using BEditor.Media;

namespace BEditor.Data
{
    /// <summary>
    /// Represents a scene to be included in the <see cref="Project"/>.
    /// </summary>
    public partial class Scene : IElementObject, IJsonObject
    {
        #region IJsonObject

        /// <inheritdoc/>
        public override void GetObjectData(Utf8JsonWriter writer)
        {
            base.GetObjectData(writer);
            writer.WriteNumber(nameof(Width), Width);
            writer.WriteNumber(nameof(Height), Height);
            writer.WriteString(nameof(SceneName), SceneName);
            writer.WriteNumber(nameof(TotalFrame), TotalFrame);
            writer.WriteStartArray(nameof(HideLayer));
            {
                foreach (var layer in HideLayer)
                {
                    writer.WriteNumberValue(layer);
                }
            }
            writer.WriteEndArray();
            writer.WriteStartArray("Clips");
            {
                foreach (var clip in Datas)
                {
                    writer.WriteStartObject();
                    {
                        clip.GetObjectData(writer);
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
            Width = element.GetProperty(nameof(Width)).GetInt32();
            Height = element.GetProperty(nameof(Height)).GetInt32();
            SceneName = element.GetProperty(nameof(SceneName)).GetString() ?? string.Empty;
            TotalFrame = element.GetProperty(nameof(TotalFrame)).GetInt32();
            HideLayer = element.GetProperty(nameof(HideLayer)).EnumerateArray().Select(i => i.GetInt32()).ToList();
            Datas = new(element.GetProperty("Clips").EnumerateArray().Select(i =>
            {
                var clip = (ClipElement)FormatterServices.GetUninitializedObject(typeof(ClipElement));
                clip.SetObjectData(i);

                return clip;
            }));
        }
        #endregion

        #region Render

        /// <summary>
        /// Render this <see cref="Scene"/>.
        /// </summary>
        /// <param name="frame">The frame to render.</param>
        /// <param name="renderType">The type of rendering.</param>
        /// <returns>Returns the result of rendering.</returns>
        /// <exception cref="RenderingException">Faileds to rendering.</exception>
        public RenderingResult Render(Frame frame, RenderType renderType = RenderType.Preview)
        {
            if (!IsLoaded)
            {
                return new()
                {
                    Image = new(Width, Height),
                };
            }

            var layer = GetFrame(frame).ToList();

            GraphicsContext!.Camera = new OrthographicCamera(new(0, 0, 1024), Width, Height);
            GraphicsContext!.Light = null;
            AudioContext!.MakeCurrent();
            GraphicsContext!.Clear();

            var args = new ClipRenderArgs(frame, renderType);

            // Preview
            foreach (var clip in layer) clip.PreviewRender(args);

            foreach (var clip in layer) clip.Render(args);

            var buffer = new Image<BGRA32>(Width, Height);
            GraphicsContext.ReadImage(buffer);

            return new RenderingResult { Image = buffer };
        }

        /// <summary>
        /// Render a frame of <see cref="PreviewFrame"/>.
        /// </summary>
        /// <param name="renderType">The type of rendering.</param>
        /// <returns>Returns the result of rendering.</returns>
        /// <exception cref="RenderingException">Faileds to rendering.</exception>
        public RenderingResult Render(RenderType renderType = RenderType.Preview)
        {
            return Render(PreviewFrame, renderType);
        }

        /// <summary>
        /// Render this <see cref="Scene"/>.
        /// </summary>
        /// <param name="image">The image to be drawn.</param>
        /// <param name="frame">The frame to render.</param>
        /// <param name="renderType">The type of rendering.</param>
        /// <exception cref="RenderingException">Faileds to rendering.</exception>
        public void Render(Image<BGRA32> image, Frame frame, RenderType renderType = RenderType.Preview)
        {
            if (!IsLoaded) return;

            image.ThrowIfDisposed();
            if (image.Width != Width) throw new ArgumentException(null, nameof(image));
            if (image.Height != Height) throw new ArgumentException(null, nameof(image));

            var layer = GetFrame(frame).ToList();

            GraphicsContext!.Camera = new OrthographicCamera(new(0, 0, 1024), Width, Height);
            GraphicsContext!.Light = null;
            AudioContext!.MakeCurrent();
            GraphicsContext!.Clear();

            var args = new ClipRenderArgs(frame, renderType);

            // Preview
            foreach (var clip in layer) clip.PreviewRender(args);

            foreach (var clip in layer) clip.Render(args);

            GraphicsContext!.ReadImage(image);
        }

        /// <summary>
        /// Render a frame of <see cref="PreviewFrame"/>.
        /// </summary>
        /// <param name="image">The image to be drawn.</param>
        /// <param name="renderType">The type of rendering.</param>
        /// <exception cref="RenderingException">Faileds to rendering.</exception>
        public void Render(Image<BGRA32> image, RenderType renderType = RenderType.Preview)
        {
            Render(image, PreviewFrame, renderType);
        }
        #endregion

        /// <summary>
        /// Get and sort the clips on the specified frame.
        /// </summary>
        /// <param name="frame">Target frame number.</param>
        /// <returns>Returns a clips that contains the specified frame.</returns>
        public IEnumerable<ClipElement> GetFrame(Frame frame)
        {
            return Datas
                .AsParallel()
                .Where(item => item.Start <= frame && frame < item.End)
                .Where(item => !HideLayer.Exists(x => x == item.Layer))
                .OrderBy(item => item.Layer);
        }

        /// <summary>
        /// Get and sort the clips on the specified layer.
        /// </summary>
        /// <param name="layer">Target layer number.</param>
        /// <returns>Returns a clips that contains the specified layer.</returns>
        public IEnumerable<ClipElement> GetLayer(int layer)
        {
            return Datas
                .AsParallel()
                .Where(item => item.Layer == layer)
                .OrderBy(item => item.Start.Value);
        }

        /// <summary>
        /// Add a <see cref="ClipElement"/> to this <see cref="Scene"/>.
        /// </summary>
        /// <param name="clip">A <see cref="ClipElement"/> to add.</param>
        public void Add(ClipElement clip)
        {
            clip.Parent = this;

            Datas.Add(clip);
        }

        /// <summary>
        /// Remove certain a <see cref="ClipElement"/> from this <see cref="Scene"/>.
        /// </summary>
        /// <param name="clip"><see cref="ClipElement"/> to be removed.</param>
        /// <returns>
        /// <see langword="true"/> if item is successfully removed; otherwise, <see langword="false"/>. This method also returns
        /// <see langword="false"/> if item was not found in the original <see cref="Collection{T}"/>.
        /// </returns>
        public bool Remove(ClipElement clip)
        {
            return Datas.Remove(clip);
        }

        /// <summary>
        /// Set the selected <see cref="ClipElement"/> and add the name to <see cref="SelectItems"/> if it does not exist.
        /// </summary>
        /// <param name="clip"><see cref="ClipElement"/> to be set to current.</param>
        /// <exception cref="ArgumentNullException"><paramref name="clip"/> is <see langword="null"/>.</exception>
        public void SetCurrentClip(ClipElement clip)
        {
            SelectItem = clip ?? throw new ArgumentNullException(nameof(clip));

            if (!SelectItems.Contains(clip))
            {
                SelectItems.Add(clip);
            }
        }

        #region Commands

        /// <summary>
        /// Create a command to add a <see cref="ClipElement"/> to this <see cref="Scene"/>.
        /// </summary>
        /// <param name="clip"><see cref="ClipElement"/> to be added.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand AddClip(ClipElement clip)
        {
            // オブジェクトの情報
            clip.Parent = this;
            _ClipDataID.SetValue(clip, NewId);

            return RecordCommand.Create(
                clip,
                clip =>
                {
                    var scene = clip.Parent;
                    clip.Load();
                    scene.Add(clip);
                    scene.SetCurrentClip(clip);
                },
                clip =>
                {
                    var scene = clip.Parent;
                    scene.Remove(clip);
                    clip.Unload();

                    // 存在する場合
                    if (scene.SelectItems.Remove(clip))
                    {
                        if (scene.SelectItem == clip)
                        {
                            scene.SelectItem = null;
                        }
                    }
                },
                _ => CommandName.AddClip);
        }

        /// <summary>
        /// Create a command to add a <see cref="ClipElement"/> to this <see cref="Scene"/>.
        /// </summary>
        /// <param name="frame">Frame to add a clip.</param>
        /// <param name="layer">Layer to add a clip.</param>
        /// <param name="metadata">Clip metadata to be added.</param>
        /// <param name="generatedClip">Generated <see cref="ClipElement"/>.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand AddClip(Frame frame, int layer, ObjectMetadata metadata, out ClipElement generatedClip)
        {
            var command = new ClipElement.AddCommand(this, frame, layer, metadata);
            generatedClip = command.Clip;

            return command;
        }

        /// <summary>
        /// Create a command to remove <see cref="ClipElement"/> from this <see cref="Scene"/>.
        /// </summary>
        /// <param name="clip"><see cref="ClipElement"/> to be removed.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand RemoveClip(ClipElement clip)
        {
            return new ClipElement.RemoveCommand(clip);
        }

        /// <summary>
        /// Create a command to remove the specified layer from this <see cref="Scene"/>.
        /// </summary>
        /// <param name="layer">Layer number to be removed.</param>
        /// <returns>Created <see cref="IRecordCommand"/>.</returns>
        [Pure]
        public IRecordCommand RemoveLayer(int layer)
        {
            return new RemoveLayerCommand(this, layer);
        }
        #endregion

        #region IElementObject

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            Debug.Assert(Synchronize is not null);
            Synchronize.Send(_ =>
            {
                GraphicsContext = new GraphicsContext(Width, Height);
                AudioContext = new AudioContext();
            }, null);
        }

        /// <inheritdoc/>
        protected override void OnUnload()
        {
            Synchronize.Send(_ =>
            {
                GraphicsContext?.Dispose();
                AudioContext?.Dispose();
            }, null);
        }
        #endregion
    }
}
