using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Primitive;
using BEditor.Data.Property;
using BEditor.Properties;
using BEditor.Graphics;

namespace BEditor.Primitive.Objects
{
    /// <summary>
    /// Represents an <see cref="ObjectElement"/> that sets the camera for OpenGL.
    /// </summary>
    [DataContract]
    public class CameraObject : ObjectElement
    {
        /// <summary>
        /// Represents <see cref="X"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata XMetadata = new(Resources.X, 0, UseOptional: true);
        /// <summary>
        /// Represents <see cref="Y"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata YMetadata = new(Resources.Y, 0, UseOptional: true);
        /// <summary>
        /// Represents <see cref="Z"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata ZMetadata = new(Resources.Z, 1024, UseOptional: true);
        /// <summary>
        /// Represents <see cref="TargetX"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata TargetXMetadata = new(Resources.TargetX, 0, UseOptional: true);
        /// <summary>
        /// Represents <see cref="TargetY"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata TargetYMetadata = new(Resources.TargetY, 0, UseOptional: true);
        /// <summary>
        /// Represents <see cref="TargetZ"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata TargetZMetadata = new(Resources.TargetZ, 0, UseOptional: true);
        /// <summary>
        /// Represents <see cref="ZNear"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata ZNearMetadata = new(Resources.ZNear, 0.1F, Min: 0.1F, UseOptional: true);
        /// <summary>
        /// Represents <see cref="ZFar"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata ZFarMetadata = new(Resources.ZFar, 20000, UseOptional: true);
        /// <summary>
        /// Represents <see cref="Angle"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata AngleMetadata = new(Resources.Angle, 0, UseOptional: true);
        /// <summary>
        /// Represents <see cref="Fov"/> metadata.
        /// </summary>
        public static readonly EasePropertyMetadata FovMetadata = new(Resources.Fov, 45, 45, 1, UseOptional: true);
        /// <summary>
        /// Represents <see cref="Mode"/> metadata.
        /// </summary>
        public static readonly CheckPropertyMetadata ModeMetadata = new(Resources.Perspective, true);

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
        public override string Name => Resources.Camera;
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
        [DataMember(Order = 0)]
        public EaseProperty X { get; private set; }
        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the Y coordinate of the camera.
        /// </summary>
        [DataMember(Order = 1)]
        public EaseProperty Y { get; private set; }
        /// <summary>
        /// Gets the <see cref="EaseProperty"/> representing the Z coordinate of the camera.
        /// </summary>
        [DataMember(Order = 2)]
        public EaseProperty Z { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the X coordinate of the camera's target position.
        /// </summary>
        [DataMember(Order = 3)]
        public EaseProperty TargetX { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the Y coordinate of the camera's target position.
        /// </summary>
        [DataMember(Order = 4)]
        public EaseProperty TargetY { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> that represents the Z coordinate of the camera's target position.
        /// </summary>
        [DataMember(Order = 5)]
        public EaseProperty TargetZ { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> of the area to be drawn by the camera.
        /// </summary>
        [DataMember(Order = 6)]
        public EaseProperty ZNear { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> of the area to be drawn by the camera.
        /// </summary>
        [DataMember(Order = 7)]
        public EaseProperty ZFar { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the camera angle.
        /// </summary>
        [DataMember(Order = 8)]
        public EaseProperty Angle { get; private set; }
        /// <summary>
        /// Get the <see cref="EaseProperty"/> representing the camera fov.
        /// </summary>
        [DataMember(Order = 9)]
        public EaseProperty Fov { get; private set; }
        /// <summary>
        /// Gets the <see cref="CheckProperty"/> representing whether the camera is Perspective or not.
        /// </summary>
        [DataMember(Order = 10)]
        public CheckProperty Mode { get; private set; }

        /// <inheritdoc/>
        public override void Render(EffectRenderArgs args)
        {
            int frame = args.Frame;
            var scene = Parent!.Parent!;
            scene.GraphicsContext!.MakeCurrent();

            if (Mode.Value)
            {
                scene.GraphicsContext.Camera =
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
                scene.GraphicsContext.Camera =
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
