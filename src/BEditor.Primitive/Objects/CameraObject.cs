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
        /// Represents <see cref="X"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata XMetadata = new(Strings.X, 0, UseOptional: true);
        /// <summary>
        /// Represents <see cref="Y"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata YMetadata = new(Strings.Y, 0, UseOptional: true);
        /// <summary>
        /// Represents <see cref="Z"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata ZMetadata = new(Strings.Z, 1024, UseOptional: true);
        /// <summary>
        /// Represents <see cref="TargetX"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata TargetXMetadata = new(Strings.TargetX, 0, UseOptional: true);
        /// <summary>
        /// Represents <see cref="TargetY"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata TargetYMetadata = new(Strings.TargetY, 0, UseOptional: true);
        /// <summary>
        /// Represents <see cref="TargetZ"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata TargetZMetadata = new(Strings.TargetZ, 0, UseOptional: true);
        /// <summary>
        /// Represents <see cref="ZNear"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata ZNearMetadata = new(Strings.ZNear, 0.1F, Min: 0.1F, UseOptional: true);
        /// <summary>
        /// Represents <see cref="ZFar"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata ZFarMetadata = new(Strings.ZFar, 20000, UseOptional: true);
        /// <summary>
        /// Represents <see cref="Angle"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata AngleMetadata = new(Strings.Angle, 0, UseOptional: true);
        /// <summary>
        /// Represents <see cref="Fov"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata FovMetadata = new(Strings.Fov, 45, 179, 1, UseOptional: true);
        /// <summary>
        /// Represents <see cref="Mode"/> metadata.
        /// </summary>
        public static readonly CheckPropertyMetadata ModeMetadata = new(Strings.Perspective, true);

        /// <summary>
        /// Initializes a new instance <see cref="CameraObject"/> class.
        /// </summary>
        public CameraObject()
        {
            X = new(XMetadata);
            Y = new(YMetadata);
            Z = new(ZMetadata);
            TargetX = new(TargetXMetadata);
            TargetY = new(TargetYMetadata);
            TargetZ = new(TargetZMetadata);
            ZNear = new(ZNearMetadata);
            ZFar = new(ZFarMetadata);
            Angle = new(AngleMetadata);
            Fov = new(FovMetadata);
            Mode = new(ModeMetadata);
        }

        /// <inheritdoc/>
        public override string Name => Strings.Camera;
        /// <inheritdoc/>
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            X, Y, Z,
            TargetX, TargetY, TargetZ,
            ZNear, ZFar,
            Angle,
            Fov,
            Mode
        };
        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the X coordinate of the camera.
        /// </summary>
        [DataMember]
        public EaseProperty X { get; private set; }
        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the Y coordinate of the camera.
        /// </summary>
        [DataMember]
        public EaseProperty Y { get; private set; }
        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the Z coordinate of the camera.
        /// </summary>
        [DataMember]
        public EaseProperty Z { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the X coordinate of the camera's target position.
        /// </summary>
        [DataMember]
        public EaseProperty TargetX { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the Y coordinate of the camera's target position.
        /// </summary>
        [DataMember]
        public EaseProperty TargetY { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the Z coordinate of the camera's target position.
        /// </summary>
        [DataMember]
        public EaseProperty TargetZ { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> of the area to be drawn by the camera.
        /// </summary>
        [DataMember]
        public EaseProperty ZNear { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> of the area to be drawn by the camera.
        /// </summary>
        [DataMember]
        public EaseProperty ZFar { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the camera angle.
        /// </summary>
        [DataMember]
        public EaseProperty Angle { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the camera fov.
        /// </summary>
        [DataMember]
        public EaseProperty Fov { get; private set; }
        /// <summary>
        /// Gets the <see cref="CheckProperty"/> representing whether the camera is Perspective or not.
        /// </summary>
        [DataMember]
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
        /// <inheritdoc/>
        protected override void OnLoad()
        {
            X.Load(XMetadata);
            Y.Load(YMetadata);
            Z.Load(ZMetadata);
            TargetX.Load(TargetXMetadata);
            TargetY.Load(TargetYMetadata);
            TargetZ.Load(TargetZMetadata);
            ZNear.Load(ZNearMetadata);
            ZFar.Load(ZFarMetadata);
            Angle.Load(AngleMetadata);
            Fov.Load(FovMetadata);
            Mode.Load(ModeMetadata);
        }
        /// <inheritdoc/>
        protected override void OnUnload()
        {
            X.Unload();
            Y.Unload();
            Z.Unload();
            TargetX.Unload();
            TargetY.Unload();
            TargetZ.Unload();
            ZNear.Unload();
            ZFar.Unload();
            Angle.Unload();
            Fov.Unload();
            Mode.Unload();
        }
    }
}
