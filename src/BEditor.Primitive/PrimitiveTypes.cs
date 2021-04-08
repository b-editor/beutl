using System;
using System.Collections.Generic;

using BEditor.Data;
using BEditor.Primitive.Objects;
using BEditor.Primitive.Resources;

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
        /// <see cref="Type"/> of <see cref="Objects.Shape"/> class.
        /// </summary>
        public static readonly Type Shape = typeof(Shape);
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
        /// <see cref="Type"/> of <see cref="Framebuffer"/> class.
        /// </summary>
        public static readonly Type Framebuffer = typeof(Framebuffer);
        /// <summary>
        /// <see cref="Type"/> of <see cref="ListenerObject"/> class.
        /// </summary>
        public static readonly Type Listener = typeof(ListenerObject);
        /// <summary>
        /// Metadata of <see cref="VideoFile"/> class.
        /// </summary>
        public static readonly ObjectMetadata VideoMetadata = ObjectMetadata.Create<VideoFile>(Strings.Video);
        /// <summary>
        /// Metadata of <see cref="AudioObject"/> class.
        /// </summary>
        public static readonly ObjectMetadata AudioMetadata = ObjectMetadata.Create<AudioObject>(Strings.Audio);
        /// <summary>
        /// Metadata of <see cref="ImageFile"/> class.
        /// </summary>
        public static readonly ObjectMetadata ImageMetadata = ObjectMetadata.Create<ImageFile>(Strings.Image);
        /// <summary>
        /// Metadata of <see cref="Objects.Text"/> class.
        /// </summary>
        public static readonly ObjectMetadata TextMetadata = ObjectMetadata.Create<Text>(Strings.Text);
        /// <summary>
        /// Metadata of <see cref="Objects.Shape"/> class.
        /// </summary>
        public static readonly ObjectMetadata ShapeMetadata = ObjectMetadata.Create<Shape>(Strings.Shape);
        /// <summary>
        /// Metadata of <see cref="Objects.Polygon"/> class.
        /// </summary>
        public static readonly ObjectMetadata PolygonMetadata = ObjectMetadata.Create<Polygon>(Strings.Polygon);
        /// <summary>
        /// Metadata of <see cref="Objects.RoundRect"/> class.
        /// </summary>
        public static readonly ObjectMetadata RoundRectMetadata = ObjectMetadata.Create<RoundRect>(Strings.RoundRect);
        /// <summary>
        /// Metadata of <see cref="CameraObject"/> class.
        /// </summary>
        public static readonly ObjectMetadata CameraMetadata = ObjectMetadata.Create<CameraObject>(Strings.Camera);
        /// <summary>
        /// Metadata of <see cref="Objects.GL3DObject"/> class.
        /// </summary>
        public static readonly ObjectMetadata GL3DObjectMetadata = ObjectMetadata.Create<GL3DObject>(Strings.GL3DObject);
        /// <summary>
        /// Metadata of <see cref="SceneObject"/> class.
        /// </summary>
        public static readonly ObjectMetadata SceneMetadata = ObjectMetadata.Create<SceneObject>(Strings.Scene);
        /// <summary>
        /// Metadata of <see cref="Framebuffer"/> class.
        /// </summary>
        public static readonly ObjectMetadata FramebufferMetadata = ObjectMetadata.Create<Framebuffer>(Strings.Framebuffer);
        /// <summary>
        /// Metadata of <see cref="ListenerObject"/> class.
        /// </summary>
        public static readonly ObjectMetadata ListenerMetadata = ObjectMetadata.Create<ListenerObject>(Strings.Listener);

        /// <summary>
        /// Enumerate all objects.
        /// </summary>
        /// <returns></returns>
        public static IEnumerable<ObjectMetadata> EnumerateAllObjectMetadata()
        {
            yield return VideoMetadata;
            yield return AudioMetadata;
            yield return ImageMetadata;
            yield return TextMetadata;
            yield return ShapeMetadata;
            yield return PolygonMetadata;
            yield return RoundRectMetadata;
            yield return CameraMetadata;
            yield return GL3DObjectMetadata;
            yield return SceneMetadata;
            yield return FramebufferMetadata;
            yield return ListenerMetadata;
            yield return ObjectMetadata.Create<PropertyTest>("PropertyTest");
        }

        /// <summary>
        /// Enumerate all effects.
        /// </summary>
        /// <returns></returns>
        public static IEnumerable<EffectMetadata> EnumerateAllEffectMetadata()
        {
            yield return new(Strings.ImageEffects)
            {
                Children = new[]
                {
                    EffectMetadata.Create<Effects.Border>(Strings.Border),
                    EffectMetadata.Create<Effects.StrokeText>($"{Strings.Border} ({Strings.Text})"),
                    EffectMetadata.Create<Effects.ColorKey>(Strings.ColorKey),
                    EffectMetadata.Create<Effects.Shadow>(Strings.DropShadow),
                    EffectMetadata.Create<Effects.Blur>(Strings.Blur),
                    EffectMetadata.Create<Effects.Monoc>(Strings.Monoc),
                    EffectMetadata.Create<Effects.Dilate>(Strings.Dilate),
                    EffectMetadata.Create<Effects.Erode>(Strings.Erode),
                    EffectMetadata.Create<Effects.Clipping>(Strings.Clipping),
                    EffectMetadata.Create<Effects.AreaExpansion>(Strings.AreaExpansion),
                    EffectMetadata.Create<Effects.LinearGradient>(Strings.LinearGradient),
                    EffectMetadata.Create<Effects.CircularGradient>(Strings.CircularGradient),
                    EffectMetadata.Create<Effects.Mask>(Strings.Mask),
                    EffectMetadata.Create<Effects.PointLightDiffuse>(Strings.PointLightDiffuse),
                    EffectMetadata.Create<Effects.ChromaKey>(Strings.ChromaKey),
                    EffectMetadata.Create<Effects.ImageSplit>(Strings.ImageSplit),
                    EffectMetadata.Create<Effects.MultipleControls>(Strings.MultipleImageControls),
                }
            };

            yield return new(Strings.Camera)
            {
                Children = new[]
                {
                    EffectMetadata.Create<Effects.DepthTest>(Strings.DepthTest),
                    EffectMetadata.Create<Effects.PointLightSource>(Strings.PointLightSource),
                }
            };
#if DEBUG
            yield return new("TestEffect", () => new Effects.TestEffect());
#endif
        }
    }
}
