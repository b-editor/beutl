using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

using BEditor.Core.Command;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;
using BEditor.Core.Graphics;
using BEditor.Core.Properties;

namespace BEditor.Core.Data.Primitive.Objects
{
    [DataContract]
    public class CameraObject : ObjectElement
    {
        public static readonly EasePropertyMetadata XMetadata = new(Resources.X, 0, UseOptional: true);
        public static readonly EasePropertyMetadata YMetadata = new(Resources.Y, 0, UseOptional: true);
        public static readonly EasePropertyMetadata ZMetadata = new(Resources.Z, 1024, UseOptional: true);
        public static readonly EasePropertyMetadata TargetXMetadata = new(Resources.TargetX, 0, UseOptional: true);
        public static readonly EasePropertyMetadata TargetYMetadata = new(Resources.TargetY, 0, UseOptional: true);
        public static readonly EasePropertyMetadata TargetZMetadata = new(Resources.TargetZ, 0, UseOptional: true);
        public static readonly EasePropertyMetadata ZNearMetadata = new(Resources.ZNear, 0.1F, Min: 0.1F, UseOptional: true);
        public static readonly EasePropertyMetadata ZFarMetadata = new(Resources.ZFar, 20000, UseOptional: true);
        public static readonly EasePropertyMetadata AngleMetadata = new(Resources.Angle, 0, UseOptional: true);
        public static readonly EasePropertyMetadata FovMetadata = new(Resources.Fov, 45, 45, 1, UseOptional: true);
        public static readonly CheckPropertyMetadata ModeMetadata = new(Resources.Perspective, true);

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

        public override string Name => Resources.Camera;
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            X, Y, Z,
            TargetX, TargetY, TargetZ,
            ZNear, ZFar,
            Angle,
            Fov,
            Mode
        };
        [DataMember(Order = 0)]
        public EaseProperty X { get; private set; }
        [DataMember(Order = 1)]
        public EaseProperty Y { get; private set; }
        [DataMember(Order = 2)]
        public EaseProperty Z { get; private set; }
        [DataMember(Order = 3)]
        public EaseProperty TargetX { get; private set; }
        [DataMember(Order = 4)]
        public EaseProperty TargetY { get; private set; }
        [DataMember(Order = 5)]
        public EaseProperty TargetZ { get; private set; }
        [DataMember(Order = 6)]
        public EaseProperty ZNear { get; private set; }
        [DataMember(Order = 7)]
        public EaseProperty ZFar { get; private set; }
        [DataMember(Order = 8)]
        public EaseProperty Angle { get; private set; }
        [DataMember(Order = 9)]
        public EaseProperty Fov { get; private set; }
        [DataMember(Order = 10)]
        public CheckProperty Mode { get; private set; }

        public override void Render(EffectRenderArgs args)
        {
            int frame = args.Frame;
            var scene = Parent.Parent;
            scene.GraphicsContext.MakeCurrent();

            if (Mode.IsChecked)
            {
                scene.GraphicsContext.Camera =
                    new PerspectiveCamera(new(X.GetValue(frame), Y.GetValue(frame), Z.GetValue(frame)), scene.Width / (float)scene.Height)
                    {
                        Far = ZFar.GetValue(frame),
                        Near = ZNear.GetValue(frame),
                        Fov = Fov.GetValue(frame),
                        Target = new(TargetX.GetValue(frame), TargetY.GetValue(frame), TargetZ.GetValue(frame))
                    };
            }
            else
            {
                scene.GraphicsContext.Camera =
                    new OrthographicCamera(new(X.GetValue(frame), Y.GetValue(frame), Z.GetValue(frame)), scene.Width, scene.Height)
                    {
                        Far = ZFar.GetValue(frame),
                        Near = ZNear.GetValue(frame),
                        Fov = Fov.GetValue(frame),
                        Target = new(TargetX.GetValue(frame), TargetY.GetValue(frame), TargetZ.GetValue(frame))
                    };
            }

            var list = Parent.Effect.Where(e => e.IsEnabled).ToArray();
            for (int i = 1; i < list.Length; i++)
            {
                var effect = list[i];

                effect.Render(args);
            }
        }
        public override void Loaded()
        {
            base.Loaded();
            X.ExecuteLoaded(XMetadata);
            Y.ExecuteLoaded(YMetadata);
            Z.ExecuteLoaded(ZMetadata);
            TargetX.ExecuteLoaded(TargetXMetadata);
            TargetY.ExecuteLoaded(TargetYMetadata);
            TargetZ.ExecuteLoaded(TargetZMetadata);
            ZNear.ExecuteLoaded(ZNearMetadata);
            ZFar.ExecuteLoaded(ZFarMetadata);
            Angle.ExecuteLoaded(AngleMetadata);
            Fov.ExecuteLoaded(FovMetadata);
            Mode.ExecuteLoaded(ModeMetadata);
        }
        public override void Unloaded()
        {
            base.Unloaded();
            X.Unloaded();
            Y.Unloaded();
            Z.Unloaded();
            TargetX.Unloaded();
            TargetY.Unloaded();
            TargetZ.Unloaded();
            ZNear.Unloaded();
            ZFar.Unloaded();
            Angle.Unloaded();
            Fov.Unloaded();
            Mode.Unloaded();
        }
    }
}
