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
        /// <see cref="Type"/> of <see cref="Primitive.Objects.Text"/> class.
        /// </summary>
        public static readonly Type Text = typeof(Text);
        /// <summary>
        /// <see cref="Type"/> of <see cref="Primitive.Objects.Figure"/> class.
        /// </summary>
        public static readonly Type Figure = typeof(Figure);
        /// <summary>
        /// <see cref="Type"/> of <see cref="Primitive.Objects.Polygon"/> class.
        /// </summary>
        public static readonly Type Polygon = typeof(Polygon);
        /// <summary>
        /// <see cref="Type"/> of <see cref="Primitive.Objects.RoundRect"/> class.
        /// </summary>
        public static readonly Type RoundRect = typeof(RoundRect);
        /// <summary>
        /// <see cref="Type"/> of <see cref="CameraObject"/> class.
        /// </summary>
        public static readonly Type Camera = typeof(CameraObject);
        /// <summary>
        /// <see cref="Type"/> of <see cref="Primitive.Objects.GL3DObject"/> class.
        /// </summary>
        public static readonly Type GL3DObject = typeof(GL3DObject);
        /// <summary>
        /// <see cref="Type"/> of <see cref="SceneObject"/> class.
        /// </summary>
        public static readonly Type Scene = typeof(SceneObject);
        /// <summary>
        /// Metadata of <see cref="VideoFile"/> class.
        /// </summary>
        public static readonly ObjectMetadata VideoMetadata = new(Resources.Video, () => new VideoFile());
        /// <summary>
        /// Metadata of <see cref="AudioObject"/> class.
        /// </summary>
        public static readonly ObjectMetadata AudioMetadata = new(Resources.Audio, () => new AudioObject());
        /// <summary>
        /// Metadata of <see cref="ImageFile"/> class.
        /// </summary>
        public static readonly ObjectMetadata ImageMetadata = new(Resources.Image, () => new ImageFile());
        /// <summary>
        /// Metadata of <see cref="Objects.Text"/> class.
        /// </summary>
        public static readonly ObjectMetadata TextMetadata = new(Resources.Text, () => new Text());
        /// <summary>
        /// Metadata of <see cref="Objects.Figure"/> class.
        /// </summary>
        public static readonly ObjectMetadata FigureMetadata = new(Resources.Figure, () => new Figure());
        /// <summary>
        /// Metadata of <see cref="Objects.Polygon"/> class.
        /// </summary>
        public static readonly ObjectMetadata PolygonMetadata = new("Polygon", () => new Polygon());
        /// <summary>
        /// Metadata of <see cref="Objects.RoundRect"/> class.
        /// </summary>
        public static readonly ObjectMetadata RoundRectMetadata = new("RoundRect", () => new RoundRect());
        /// <summary>
        /// Metadata of <see cref="CameraObject"/> class.
        /// </summary>
        public static readonly ObjectMetadata CameraMetadata = new(Resources.Camera, () => new CameraObject());
        /// <summary>
        /// Metadata of <see cref="Objects.GL3DObject"/> class.
        /// </summary>
        public static readonly ObjectMetadata GL3DObjectMetadata = new(Resources._3DObject, () => new GL3DObject());
        /// <summary>
        /// Metadata of <see cref="SceneObject"/> class.
        /// </summary>
        public static readonly ObjectMetadata SceneMetadata = new(Resources.Scene, () => new SceneObject());

    }
}
