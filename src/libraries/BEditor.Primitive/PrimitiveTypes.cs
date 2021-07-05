// PrimitiveTypes.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

using BEditor.Data;
using BEditor.Drawing;
using BEditor.Primitive.Objects;
using BEditor.Primitive.Resources;

[assembly: InternalsVisibleTo("NUnitTestProject1")]

namespace BEditor.Primitive
{
    /// <summary>
    /// Standard clip types.
    /// </summary>
    public static class PrimitiveTypes
    {
        /// <summary>
        /// <see cref="Type"/> of <see cref="VideoFile"/> class.
        /// </summary>
        public static readonly Type Video = typeof(VideoFile);

        /// <summary>
        /// <see cref="Type"/> of <see cref="AudioFile"/> class.
        /// </summary>
        public static readonly Type Audio = typeof(AudioFile);

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
        /// Metadata of <see cref="VideoFile"/> class.
        /// </summary>
        public static readonly ObjectMetadata VideoMetadata = ObjectMetadata.Create(
            name: Strings.Video,
            pathIcon: "M19.7287 3.87505L19.7797 4.0347L20.331 5.95722C20.4356 6.32221 20.2509 6.70162 19.9127 6.85026L19.8168 6.8849L9.08985 9.95999L20.2489 9.96093C20.6286 9.96093 20.9424 10.2431 20.9921 10.6092L20.9989 10.7109V19.2089C20.9989 20.6715 19.8572 21.8673 18.4164 21.9539L18.2489 21.9589H5.75006C4.28752 21.9589 3.09165 20.8172 3.00507 19.3765L3.00006 19.2089L2.99985 10.817L2.47809 8.99586C2.07496 7.58998 2.84281 6.12574 4.20391 5.64538L4.36356 5.59438L16.3782 2.14923C17.7841 1.7461 19.2483 2.51395 19.7287 3.87505ZM6.27274 6.60739L4.77701 7.03628C4.15488 7.21467 3.77871 7.83423 3.89091 8.45778L3.91994 8.58241L4.26427 9.78326L4.55936 9.6984L6.27274 6.60739ZM11.0291 5.24353L8.31157 6.02276L6.5982 9.11377L9.31568 8.33454L11.0291 5.24353ZM15.7863 3.8794L13.0689 4.65863L11.3555 7.74964L14.072 6.97069L15.7863 3.8794ZM17.6335 3.64584L16.1128 6.38551L18.6813 5.64925L18.3378 4.44815C18.2306 4.07444 17.9643 3.7895 17.6335 3.64584Z",
            createFromFile: VideoFile.FromFile,
            isSupported: VideoFile.IsSupported);

        /// <summary>
        /// Metadata of <see cref="AudioFile"/> class.
        /// </summary>
        public static readonly ObjectMetadata AudioMetadata = ObjectMetadata.Create(
            name: Strings.Audio,
            accentColor: Color.FromUInt32(0xffff1744),
            pathIcon: "M20 2.75001C20 2.51293 19.8879 2.28981 19.6977 2.14829C19.5075 2.00677 19.2616 1.96351 19.0345 2.03164L9.03449 5.03164C8.71725 5.12681 8.5 5.4188 8.5 5.75001V15.6273C7.93308 15.2319 7.24362 15 6.5 15C4.567 15 3 16.567 3 18.5C3 20.433 4.567 22 6.5 22C8.433 22 10 20.433 10 18.5C10 18.4426 9.99862 18.3856 9.99589 18.3289C9.99861 18.303 10 18.2766 10 18.25V10.308L18.5 7.75803V13.6273C17.9331 13.2319 17.2436 13 16.5 13C14.567 13 13 14.567 13 16.5C13 18.433 14.567 20 16.5 20C18.433 20 20 18.433 20 16.5C20 16.4427 19.9986 16.3856 19.9959 16.329C19.9986 16.303 20 16.2767 20 16.25V2.75001Z",
            createFromFile: AudioFile.FromFile,
            isSupported: AudioFile.IsSupported);

        /// <summary>
        /// Metadata of <see cref="ImageFile"/> class.
        /// </summary>
        public static readonly ObjectMetadata ImageMetadata = ObjectMetadata.Create(
            name: Strings.Image,
            accentColor: Color.FromUInt32(0xff0091ea),
            pathIcon: "M13.4736 15.7226L13.5574 15.6513C13.8169 15.4611 14.171 15.4588 14.4329 15.6443L14.5264 15.7226L23.4531 24.5186C22.9578 24.8239 22.3745 25 21.75 25H6.25C5.62551 25 5.04216 24.8239 4.54692 24.5186L13.4736 15.7226L13.5574 15.6513L13.4736 15.7226ZM21.75 3C23.5449 3 25 4.45507 25 6.25V21.75C25 22.3766 24.8227 22.9618 24.5154 23.4582L15.5791 14.6541L15.4505 14.5367C14.6168 13.8323 13.3923 13.8303 12.5565 14.5309L12.4209 14.6541L3.48457 23.4582C3.17734 22.9618 3 22.3766 3 21.75V6.25C3 4.45507 4.45507 3 6.25 3H21.75ZM19 7C17.6193 7 16.5 8.11929 16.5 9.5C16.5 10.8807 17.6193 12 19 12C20.3807 12 21.5 10.8807 21.5 9.5C21.5 8.11929 20.3807 7 19 7ZM19 8.5C19.5523 8.5 20 8.94772 20 9.5C20 10.0523 19.5523 10.5 19 10.5C18.4477 10.5 18 10.0523 18 9.5C18 8.94772 18.4477 8.5 19 8.5Z",
            createFromFile: ImageFile.FromFile,
            isSupported: ImageFile.IsSupported);

        /// <summary>
        /// Metadata of <see cref="Objects.Text"/> class.
        /// </summary>
        public static readonly ObjectMetadata TextMetadata = ObjectMetadata.Create<Text>(
            name: Strings.Text,
            accentColor: Color.FromUInt32(0xff6200ea),
            pathIcon: "M5.25 3C4.00736 3 3 4.00736 3 5.25V18.75C3 19.9926 4.00736 21 5.25 21H18.75C19.9926 21 21 19.9926 21 18.75V5.25C21 4.00736 19.9926 3 18.75 3H5.25ZM8.75 8.25C8.75 8.80228 8.30228 9.25 7.75 9.25C7.19772 9.25 6.75 8.80228 6.75 8.25C6.75 7.69772 7.19772 7.25 7.75 7.25C8.30228 7.25 8.75 7.69772 8.75 8.25ZM10.5 8.25C10.5 7.83579 10.8358 7.5 11.25 7.5H16.75C17.1642 7.5 17.5 7.83579 17.5 8.25C17.5 8.66421 17.1642 9 16.75 9H11.25C10.8358 9 10.5 8.66421 10.5 8.25ZM10.5001 12C10.5001 11.5858 10.8358 11.25 11.2501 11.25H16.7499C17.1642 11.25 17.4999 11.5858 17.4999 12C17.4999 12.4142 17.1642 12.75 16.7499 12.75H11.2501C10.8358 12.75 10.5001 12.4142 10.5001 12ZM11.2501 15H16.7499C17.1642 15 17.4999 15.3358 17.4999 15.75C17.4999 16.1642 17.1642 16.5 16.7499 16.5H11.2501C10.8358 16.5 10.5001 16.1642 10.5001 15.75C10.5001 15.3358 10.8358 15 11.2501 15ZM7.75 13C7.19772 13 6.75 12.5523 6.75 12C6.75 11.4477 7.19772 11 7.75 11C8.30228 11 8.75 11.4477 8.75 12C8.75 12.5523 8.30228 13 7.75 13ZM8.75 15.75C8.75 16.3023 8.30228 16.75 7.75 16.75C7.19772 16.75 6.75 16.3023 6.75 15.75C6.75 15.1977 7.19772 14.75 7.75 14.75C8.30228 14.75 8.75 15.1977 8.75 15.75Z");

        /// <summary>
        /// Metadata of <see cref="Objects.Shape"/> class.
        /// </summary>
        public static readonly ObjectMetadata ShapeMetadata = ObjectMetadata.Create<Shape>(
            name: Strings.Shape,
            accentColor: Color.FromUInt32(0xff0091ea),
            pathIcon: "M2 8.75C2 5.02208 5.02208 2 8.75 2C12.2244 2 15.0857 4.62504 15.4588 8H12.25C9.90279 8 8 9.90279 8 12.25V15.4588C4.62504 15.0857 2 12.2244 2 8.75Z M12.25 9C10.4551 9 9 10.4551 9 12.25V18.75C9 20.5449 10.4551 22 12.25 22H18.75C20.5449 22 22 20.5449 22 18.75V12.25C22 10.4551 20.5449 9 18.75 9H12.25Z");

        /// <summary>
        /// Metadata of <see cref="Objects.Polygon"/> class.
        /// </summary>
        public static readonly ObjectMetadata PolygonMetadata = ObjectMetadata.Create<Polygon>(Strings.Polygon, Color.FromUInt32(0xff0091ea));

        /// <summary>
        /// Metadata of <see cref="Objects.RoundRect"/> class.
        /// </summary>
        public static readonly ObjectMetadata RoundRectMetadata = ObjectMetadata.Create<RoundRect>(
            name: Strings.RoundRect,
            accentColor: Color.FromUInt32(0xff0091ea),
            pathIcon: "M5 4C3.34315 4 2 5.34315 2 7V13C2 14.6569 3.34315 16 5 16H15C16.6569 16 18 14.6569 18 13V7C18 5.34315 16.6569 4 15 4H5Z");

        /// <summary>
        /// Metadata of <see cref="CameraObject"/> class.
        /// </summary>
        public static readonly ObjectMetadata CameraMetadata = ObjectMetadata.Create<CameraObject>(
            name: Strings.Camera,
            pathIcon: "M16 16.25C16 18.0449 14.5449 19.5 12.75 19.5H5.25C3.45507 19.5 2 18.0449 2 16.25V7.75C2 5.95507 3.45507 4.5 5.25 4.5H12.75C14.5449 4.5 16 5.95507 16 7.75V16.25ZM21.762 5.89334C21.9156 6.07414 22 6.30368 22 6.54096V17.4588C22 18.0111 21.5523 18.4588 21 18.4588C20.7627 18.4588 20.5332 18.3744 20.3524 18.2208L17 15.3709V8.62794L20.3524 5.77899C20.7732 5.42132 21.4043 5.47252 21.762 5.89334Z");

        /// <summary>
        /// Metadata of <see cref="Objects.GL3DObject"/> class.
        /// </summary>
        public static readonly ObjectMetadata GL3DObjectMetadata = ObjectMetadata.Create<GL3DObject>(
            name: Strings.GL3DObject,
            pathIcon: "M13.4089 2.51203C12.5053 2.14573 11.4947 2.14573 10.5911 2.51203L3.09252 5.552C2.43211 5.81973 2 6.46118 2 7.1738V16.8265C2 17.5391 2.43211 18.1806 3.09252 18.4483L10.5911 21.4883C11.4947 21.8546 12.5053 21.8546 13.4089 21.4883L20.9075 18.4483C21.5679 18.1806 22 17.5391 22 16.8265V7.1738C22 6.46118 21.5679 5.81973 20.9075 5.552L13.4089 2.51203ZM6.04873 7.98404C6.19566 7.59676 6.62872 7.40192 7.016 7.54885L12 9.43974L16.9839 7.54885C17.3712 7.40192 17.8043 7.59676 17.9512 7.98404C18.0981 8.37132 17.9033 8.80438 17.516 8.95131L12.75 10.7595V16.2501C12.75 16.6643 12.4142 17.0001 12 17.0001C11.5858 17.0001 11.25 16.6643 11.25 16.2501V10.7595L6.48392 8.95131C6.09664 8.80438 5.9018 8.37132 6.04873 7.98404Z");

        /// <summary>
        /// Metadata of <see cref="SceneObject"/> class.
        /// </summary>
        public static readonly ObjectMetadata SceneMetadata = ObjectMetadata.Create<SceneObject>(Strings.Scene, null);

        /// <summary>
        /// Metadata of <see cref="Framebuffer"/> class.
        /// </summary>
        public static readonly ObjectMetadata FramebufferMetadata = ObjectMetadata.Create<Framebuffer>(Strings.Framebuffer, null);

        /// <summary>
        /// Enumerate all objects.
        /// </summary>
        /// <returns>Returns the object metadata contained in BEditor.Primitive.</returns>
        public static IEnumerable<ObjectMetadata> EnumerateAllObjectMetadata()
        {
            yield return VideoMetadata;
            yield return AudioMetadata;
            yield return ImageMetadata;
            yield return TextMetadata;
            yield return ShapeMetadata;
            yield return RoundRectMetadata;
            yield return CameraMetadata;
            yield return GL3DObjectMetadata;
            yield return SceneMetadata;
            yield return PolygonMetadata;
            yield return FramebufferMetadata;
#if DEBUG
            yield return ObjectMetadata.Create<PropertyTest>("PropertyTest", null);
#endif
        }

        /// <summary>
        /// Enumerate all effects.
        /// </summary>
        /// <returns>Returns the effect metadata contained in BEditor.Primitive.</returns>
        public static IEnumerable<EffectMetadata> EnumerateAllEffectMetadata()
        {
            yield return new(Strings.ImageEffects)
            {
                Children = new[]
                {
                    EffectMetadata.Create<Effects.ColorKey>(Strings.ColorKey),
                    EffectMetadata.Create<Effects.SetColor>(Strings.Monoc),
                    EffectMetadata.Create<Effects.Clipping>(Strings.Clipping),
                    EffectMetadata.Create<Effects.AreaExpansion>(Strings.AreaExpansion),
                    EffectMetadata.Create<Effects.ChromaKey>(Strings.ChromaKey),
                    EffectMetadata.Create<Effects.ImageSplit>(Strings.ImageSplit),
                    EffectMetadata.Create<Effects.MultipleControls>(Strings.MultipleImageControls),
                    EffectMetadata.Create<Effects.Grayscale>(Strings.Grayscale),
                    EffectMetadata.Create<Effects.Sepia>(Strings.Sepia),
                    EffectMetadata.Create<Effects.Negaposi>(Strings.Negaposi),
                    EffectMetadata.Create<Effects.Xor>(Strings.Xor),
                    EffectMetadata.Create<Effects.BrightnessCorrection>(Strings.BrightnessCorrection),
                    EffectMetadata.Create<Effects.RGBColor>(Strings.RGBColorCorrection),
                    EffectMetadata.Create<Effects.Binarization>(Strings.Binarization),
                    EffectMetadata.Create<Effects.Noise>(Strings.Noise),
                    EffectMetadata.Create<Effects.Diffusion>(Strings.Diffusion),
                    EffectMetadata.Create<Effects.ReverseOpacity>(Strings.ReverseOpacity),
                    EffectMetadata.Create<Effects.InnerShadow>(Strings.InnerShadow),
                    EffectMetadata.Create<Effects.SetAlignment>(Strings.SetAlignment),
                    EffectMetadata.Create<Effects.Mask>(Strings.Mask),
                    EffectMetadata.Create<Effects.Border>(Strings.Border),
                    EffectMetadata.Create<Effects.StrokeText>($"{Strings.Border} ({Strings.Text})"),
                    EffectMetadata.Create<Effects.Shadow>(Strings.DropShadow),
                    EffectMetadata.Create<Effects.Blur>(Strings.Blur),
                    EffectMetadata.Create<Effects.EdgeBlur>(Strings.EdgeBlur),
                    EffectMetadata.Create<Effects.Dilate>(Strings.Dilate),
                    EffectMetadata.Create<Effects.Erode>(Strings.Erode),
                    EffectMetadata.Create<Effects.LinearGradient>(Strings.LinearGradient),
                    EffectMetadata.Create<Effects.CircularGradient>(Strings.CircularGradient),
                    EffectMetadata.Create<Effects.PointLightDiffuse>(Strings.PointLightDiffuse),
                },
            };

            yield return new(Strings.AudioEffect)
            {
                Children = new[]
                {
                    EffectMetadata.Create<Effects.Delay>(Strings.Delay),
                },
            };

            yield return new(Strings.LookupTable)
            {
                Children = new[]
                {
                    EffectMetadata.Create<Effects.LookupTables.ContrastCorrection>(Strings.ContrastCorrection),
                    EffectMetadata.Create<Effects.LookupTables.GammaCorrection>(Strings.GammaCorrection),
                    EffectMetadata.Create<Effects.LookupTables.Negaposi>(Strings.Negaposi),
                    EffectMetadata.Create<Effects.LookupTables.Solarisation>(Strings.Solarisation),
                    EffectMetadata.Create<Effects.LookupTables.LookupTable>(Strings.ApplyLookupTable),
                },
            };

            yield return new("OpenCV")
            {
                Children = new[]
                {
                    EffectMetadata.Create<Effects.OpenCv.GaussianBlur>(Strings.GaussianBlur),
                    EffectMetadata.Create<Effects.OpenCv.Blur>(Strings.Blur),
                    EffectMetadata.Create<Effects.OpenCv.MedianBlur>(Strings.MedianBlur),
                    EffectMetadata.Create<Effects.OpenCv.WarpPolar>(Strings.WarpPolar),
                },
            };

            yield return new(Strings.Camera)
            {
                Children = new[]
                {
                    EffectMetadata.Create<Effects.DepthTest>(Strings.DepthTest),
                    EffectMetadata.Create<Effects.PointLightSource>(Strings.PointLightSource),
                },
            };
        }
    }
}