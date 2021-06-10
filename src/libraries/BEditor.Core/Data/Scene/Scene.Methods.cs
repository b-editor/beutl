// Scene.Methods.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Buffers;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json;

using BEditor.Audio;
using BEditor.Command;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;
using BEditor.Media;
using BEditor.Media.PCM;
using BEditor.Resources;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BEditor.Data
{
    /// <summary>
    /// Represents a scene to be included in the <see cref="Project"/>.
    /// </summary>
    public partial class Scene : IElementObject, IJsonObject
    {
        /// <inheritdoc/>
        public override void GetObjectData(Utf8JsonWriter writer)
        {
            base.GetObjectData(writer);
            writer.WriteNumber(nameof(Width), Width);
            writer.WriteNumber(nameof(Height), Height);
            writer.WriteString(nameof(SceneName), SceneName);
            writer.WriteNumber(nameof(TotalFrame), TotalFrame);
            writer.WriteStartArray(nameof(HideLayer));

            foreach (var layer in HideLayer)
            {
                writer.WriteNumberValue(layer);
            }

            writer.WriteEndArray();

            writer.WriteStartArray("Clips");
            foreach (var clip in Datas)
            {
                writer.WriteStartObject();
                clip.GetObjectData(writer);
                writer.WriteEndObject();
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

        /// <summary>
        /// Render this <see cref="Scene"/>.
        /// </summary>
        /// <param name="frame">The frame to render.</param>
        /// <param name="renderType">The type of rendering.</param>
        /// <returns>Returns the result of rendering.</returns>
        /// <exception cref="RenderingException">Faileds to rendering.</exception>
        public Image<BGRA32> Render(Frame frame, RenderType renderType = RenderType.Preview)
        {
            if (!IsLoaded)
            {
                return new(Width, Height);
            }

            var layer = GetFrame(frame);

            GraphicsContext!.Camera = new OrthographicCamera(new(0, 0, 1024), Width, Height);
            GraphicsContext!.Light = null;
            AudioContext!.MakeCurrent();
            GraphicsContext!.Clear();

            var args = new ClipRenderArgs(frame, renderType);

            // Preview
            for (var i = 0; i < layer.Length; i++) layer[i].PreviewRender(args);

            for (var i = 0; i < layer.Length; i++) layer[i].Render(args);

            var img = new Image<BGRA32>(Width, Height);
            GraphicsContext.ReadImage(img);

            return img;
        }

        /// <summary>
        /// Render a frame of <see cref="PreviewFrame"/>.
        /// </summary>
        /// <param name="renderType">The type of rendering.</param>
        /// <returns>Returns the result of rendering.</returns>
        /// <exception cref="RenderingException">Faileds to rendering.</exception>
        public Image<BGRA32> Render(RenderType renderType = RenderType.Preview)
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

            var layer = GetFrame(frame);

            GraphicsContext!.Camera = new OrthographicCamera(new(0, 0, 1024), Width, Height);
            GraphicsContext!.Light = null;
            AudioContext!.MakeCurrent();
            GraphicsContext!.Clear();

            var args = new ClipRenderArgs(frame, renderType);

            // Preview
            for (var i = 0; i < layer.Length; i++) layer[i].PreviewRender(args);

            for (var i = 0; i < layer.Length; i++) layer[i].Render(args);

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

        /// <summary>
        /// Get and sort the clips on the specified frame.
        /// </summary>
        /// <param name="frame">Target frame number.</param>
        /// <returns>Returns a clips that contains the specified frame.</returns>
        public ClipElement[] GetFrame(Frame frame)
        {
            var array = Datas
                .AsParallel()
                .Where(item => item.Start <= frame && frame < item.End)
                .Where(item => !HideLayer.Exists(x => x == item.Layer))
                .ToArray();

            Array.Sort(array, (x, y) => x.Layer - y.Layer);
            return array;
        }

        /// <summary>
        /// Get and sort the clips on the specified layer.
        /// </summary>
        /// <param name="layer">Target layer number.</param>
        /// <returns>Returns a clips that contains the specified layer.</returns>
        public ClipElement[] GetLayer(int layer)
        {
            var array = Datas
                .AsParallel()
                .Where(item => item.Layer == layer)
                .ToArray();

            Array.Sort(array, (x, y) => x.Start - y.Start);

            return array;
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
            clip.UpdateId();

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
                    if (scene.SelectItems.Remove(clip) && scene.SelectItem == clip)
                    {
                        scene.SelectItem = null;
                    }
                },
                _ => Strings.AddClip);
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
#pragma warning disable CA1822 // Mark members as static
        public IRecordCommand RemoveClip(ClipElement clip)
#pragma warning restore CA1822 // Mark members as static
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

        /// <inheritdoc/>
        protected override void OnLoad()
        {
            Parent.Parent.UIThread.Send(s =>
            {
                var scene = (Scene)s!;
                scene.GraphicsContext = new GraphicsContext(scene.Width, scene.Height);
                scene.AudioContext = new AudioContext();

                if (BEditor.Settings.Default.PrioritizeGPU)
                {
                    scene.DrawingContext = DrawingContext.Create(0);

                    if (scene.DrawingContext is not null)
                    {
                        scene.ServiceProvider?.GetService<ILogger>()?.LogInformation("{0}はGpuを使用した画像処理が有効です。", SceneName);
                    }
                }
            }, this);
        }

        /// <inheritdoc/>
        protected override void OnUnload()
        {
            Parent.Parent.UIThread.Send(s =>
            {
                var scene = (Scene)s!;
                scene.GraphicsContext?.Dispose();
                scene.DrawingContext?.Dispose();
                scene.AudioContext?.Dispose();
            }, this);
        }
    }
}