using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Data;
using BEditor.Properties;
using BEditor.Primitive.Objects;

namespace BEditor.Primitive
{
    /// <summary>
    /// Standard clip types.
    /// </summary>
    public class PrimitiveTypes
    {
        /// <summary>
        /// <see cref="Type"/> of <see cref="VideoFile"/> class.
        /// </summary>
        public static readonly Type Video = typeof(VideoFile);
        /// <summary>
        /// <see cref="Type"/> of <see cref="AudioObject"/> class.
        /// </summary>
        public static readonly Type Audio = typeof(AudioObject);
        /// <summary>
        /// <see cref="Type"/> of <see cref="ImageFile"/> class.
        /// </summary>
        public static readonly Type Image = typeof(ImageFile);
        /// <summary>
        /// <see cref="Type"/> of <see cref="Objects.Text"/> class.
        /// </summary>
        public static readonly Type Text = typeof(Text);
        /// <summary>
        /// <see cref="Type"/> of <see cref="Objects.Figure"/> class.
        /// </summary>
        public static readonly Type Figure = typeof(Figure);
        /// <summary>
        /// <see cref="Type"/> of <see cref="Objects.Polygon"/> class.
        /// </summary>
        public static readonly Type Polygon = typeof(Polygon);
        /// <summary>
        /// <see cref="Type"/> of <see cref="Objects.RoundRect"/> class.
        /// </summary>
        public static readonly Type RoundRect = typeof(RoundRect);
        /// <summary>
        /// <see cref="Type"/> of <see cref="CameraObject"/> class.
        /// </summary>
        public static readonly Type Camera = typeof(CameraObject);
        /// <summary>
        /// <see cref="Type"/> of <see cref="Objects.GL3DObject"/> class.
        /// </summary>
        public static readonly Type GL3DObject = typeof(GL3DObject);
        /// <summary>
        /// <see cref="Type"/> of <see cref="SceneObject"/> class.
        /// </summary>
        public static readonly Type Scene = typeof(SceneObject);
        /// <summary>
        /// Metadata of <see cref="VideoFile"/> class.
        /// </summary>
        public static readonly ObjectMetadata VideoMetadata = ObjectMetadata.Create<VideoFile>(Resources.Video);
        /// <summary>
        /// Metadata of <see cref="AudioObject"/> class.
        /// </summary>
        public static readonly ObjectMetadata AudioMetadata = ObjectMetadata.Create<AudioObject>(Resources.Audio);
        /// <summary>
        /// Metadata of <see cref="ImageFile"/> class.
        /// </summary>
        public static readonly ObjectMetadata ImageMetadata = ObjectMetadata.Create<ImageFile>(Resources.Image);
        /// <summary>
        /// Metadata of <see cref="Objects.Text"/> class.
        /// </summary>
        public static readonly ObjectMetadata TextMetadata = ObjectMetadata.Create<Text>(Resources.Text);
        /// <summary>
        /// Metadata of <see cref="Objects.Figure"/> class.
        /// </summary>
        public static readonly ObjectMetadata FigureMetadata = ObjectMetadata.Create<Figure>(Resources.Figure);
        /// <summary>
        /// Metadata of <see cref="Objects.Polygon"/> class.
        /// </summary>
        public static readonly ObjectMetadata PolygonMetadata = ObjectMetadata.Create<Polygon>("Polygon");
        /// <summary>
        /// Metadata of <see cref="Objects.RoundRect"/> class.
        /// </summary>
        public static readonly ObjectMetadata RoundRectMetadata = ObjectMetadata.Create<RoundRect>("RoundRect");
        /// <summary>
        /// Metadata of <see cref="CameraObject"/> class.
        /// </summary>
        public static readonly ObjectMetadata CameraMetadata = ObjectMetadata.Create<CameraObject>(Resources.Camera);
        /// <summary>
        /// Metadata of <see cref="Objects.GL3DObject"/> class.
        /// </summary>
        public static readonly ObjectMetadata GL3DObjectMetadata = ObjectMetadata.Create<GL3DObject>(Resources._3DObject);
        /// <summary>
        /// Metadata of <see cref="SceneObject"/> class.
        /// </summary>
        public static readonly ObjectMetadata SceneMetadata = ObjectMetadata.Create<SceneObject>(Resources.Scene);

    }
}
