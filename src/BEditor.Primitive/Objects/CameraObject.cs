using System.Collections.Generic;
using System.Linq;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Graphics;
using BEditor.Primitive.Resources;

namespace BEditor.Primitive.Objects
{
    /// <summary>
    /// Represents an <see cref="ObjectElement"/> that sets the camera for OpenGL.
    /// </summary>
    public class CameraObject : ObjectElement
    {
        /// <summary>
        /// Defines the <see cref="X"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<CameraObject, EaseProperty> XProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, CameraObject>(
            nameof(X),
            owner => owner.X,
            (owner, obj) => owner.X = obj,
            new EasePropertyMetadata(Strings.X, 0, UseOptional: true));

        /// <summary>
        /// Defines the <see cref="Y"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<CameraObject, EaseProperty> YProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, CameraObject>(
            nameof(Y),
            owner => owner.Y,
            (owner, obj) => owner.Y = obj,
            new EasePropertyMetadata(Strings.Y, 0, UseOptional: true));

        /// <summarZ>
        /// Defines the <see cref="Z"/> property.
        /// </summarZ>
        public static readonly DirectEditingProperty<CameraObject, EaseProperty> ZProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, CameraObject>(
            nameof(Z),
            owner => owner.Z,
            (owner, obj) => owner.Z = obj,
            new EasePropertyMetadata(Strings.Z, 1024, UseOptional: true));

        /// <summary>
        /// Defines the <see cref="TargetX"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<CameraObject, EaseProperty> TargetXProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, CameraObject>(
            nameof(TargetX),
            owner => owner.TargetX,
            (owner, obj) => owner.TargetX = obj,
            new EasePropertyMetadata(Strings.TargetX, 0, UseOptional: true));

        /// <summary>
        /// Defines the <see cref="TargetY"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<CameraObject, EaseProperty> TargetYProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, CameraObject>(
            nameof(TargetY),
            owner => owner.TargetY,
            (owner, obj) => owner.TargetY = obj,
            new EasePropertyMetadata(Strings.TargetY, 0, UseOptional: true));

        /// <summarZ>
        /// Defines the <see cref="TargetZ"/> property.
        /// </summarZ>
        public static readonly DirectEditingProperty<CameraObject, EaseProperty> TargetZProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, CameraObject>(
            nameof(TargetZ),
            owner => owner.TargetZ,
            (owner, obj) => owner.TargetZ = obj,
            new EasePropertyMetadata(Strings.TargetZ, 0, UseOptional: true));

        /// <summary>
        /// Defines the <see cref="ZNear"/> property.
        /// </summary>
        public static readonly DirectEditingProperty<CameraObject, EaseProperty> ZNearProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, CameraObject>(
            nameof(ZNear),
            owner => owner.ZNear,
            (owner, obj) => owner.ZNear = obj,
            new EasePropertyMetadata(Strings.ZNear, 0.1F, Min: 0.1F, UseOptional: true));

        /// <summarZ>
        /// Defines the <see cref="ZFar"/> property.
        /// </summarZ>
        public static readonly DirectEditingProperty<CameraObject, EaseProperty> ZFarProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, CameraObject>(
            nameof(ZFar),
            owner => owner.ZFar,
            (owner, obj) => owner.ZFar = obj,
            new EasePropertyMetadata(Strings.ZFar, 20000, UseOptional: true));

        /// <summarZ>
        /// Defines the <see cref="Angle"/> property.
        /// </summarZ>
        public static readonly DirectEditingProperty<CameraObject, EaseProperty> AngleProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, CameraObject>(
            nameof(Angle),
            owner => owner.Angle,
            (owner, obj) => owner.Angle = obj,
            new EasePropertyMetadata(Strings.Angle, 0, UseOptional: true));

        /// <summarZ>
        /// Defines the <see cref="Fov"/> property.
        /// </summarZ>
        public static readonly DirectEditingProperty<CameraObject, EaseProperty> FovProperty = EditingProperty.RegisterSerializeDirect<EaseProperty, CameraObject>(
            nameof(Fov),
            owner => owner.Fov,
            (owner, obj) => owner.Fov = obj,
            new EasePropertyMetadata(Strings.Fov, 45, 179, 1, UseOptional: true));

        /// <summarZ>
        /// Defines the <see cref="Mode"/> property.
        /// </summarZ>
        public static readonly DirectEditingProperty<CameraObject, CheckProperty> ModeProperty = EditingProperty.RegisterSerializeDirect<CheckProperty, CameraObject>(
            nameof(Mode),
            owner => owner.Mode,
            (owner, obj) => owner.Mode = obj,
            new CheckPropertyMetadata(Strings.Perspective, true));

        /// <summary>
        /// Initializes a new instance <see cref="CameraObject"/> class.
        /// </summary>
#pragma warning disable CS8618
        public CameraObject()
#pragma warning restore CS8618
        {
        }

        /// <inheritdoc/>
        public override string Name => Strings.Camera;

        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties
        {
            get
            {
                yield return X;
                yield return Y;
                yield return Z;
                yield return TargetX;
                yield return TargetY;
                yield return TargetZ;
                yield return ZNear;
                yield return ZFar;
                yield return Angle;
                yield return Fov;
                yield return Mode;
            }
        }

        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the X coordinate of the camera.
        /// </summary>
        public EaseProperty X { get; private set; }

        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the Y coordinate of the camera.
        /// </summary>
        public EaseProperty Y { get; private set; }

        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the Z coordinate of the camera.
        /// </summary>
        public EaseProperty Z { get; private set; }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the X coordinate of the camera's target position.
        /// </summary>
        public EaseProperty TargetX { get; private set; }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the Y coordinate of the camera's target position.
        /// </summary>
        public EaseProperty TargetY { get; private set; }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the Z coordinate of the camera's target position.
        /// </summary>
        public EaseProperty TargetZ { get; private set; }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> of the area to be drawn by the camera.
        /// </summary>
        public EaseProperty ZNear { get; private set; }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> of the area to be drawn by the camera.
        /// </summary>
        public EaseProperty ZFar { get; private set; }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the camera angle.
        /// </summary>
        public EaseProperty Angle { get; private set; }

        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the camera fov.
        /// </summary>
        public EaseProperty Fov { get; private set; }

        /// <summary>
        /// Gets the <see cref="CheckProperty"/> representing whether the camera is Perspective or not.
        /// </summary>
        public CheckProperty Mode { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs args)
        {
            int frame = args.Frame;
            var scene = Parent!.Parent!;

            if (Mode.Value)
            {
                scene.GraphicsContext!.Camera =
                    new PerspectiveCamera(new(X[frame], Y[frame], Z[frame]), scene.Width / (float)scene.Height)
                    {
                        Far = ZFar[frame],
                        Near = ZNear[frame],
                        Fov = Fov[frame],
                        Target = new(TargetX[frame], TargetY[frame], TargetZ[frame])
                    };
            }
            else
            {
                scene.GraphicsContext!.Camera =
                    new OrthographicCamera(new(X[frame], Y[frame], Z[frame]), scene.Width, scene.Height)
                    {
                        Far = ZFar[frame],
                        Near = ZNear[frame],
                        Fov = Fov[frame],
                        Target = new(TargetX[frame], TargetY[frame], TargetZ[frame])
                    };
            }

            var list = Parent.Effect.Where(e => e.IsEnabled).ToArray();
            for (int i = 1; i < list.Length; i++)
            {
                var effect = list[i];

                effect.Render(args);
            }
        }
    }
}