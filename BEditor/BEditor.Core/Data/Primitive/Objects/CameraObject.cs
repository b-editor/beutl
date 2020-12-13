using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core.Command;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;
using BEditor.Core.Graphics;
using BEditor.Core.Properties;

namespace BEditor.Core.Data.Primitive.Objects
{
    [DataContract]
    public class CameraObject : ObjectElement
    {
        public static readonly EasePropertyMetadata XMetadata = new(Resources.X, 0);
        public static readonly EasePropertyMetadata YMetadata = new(Resources.Y, 0);
        public static readonly EasePropertyMetadata ZMetadata = new(Resources.Z, 1024);
        public static readonly EasePropertyMetadata TargetXMetadata = new(Resources.TargetX, 0);
        public static readonly EasePropertyMetadata TargetYMetadata = new(Resources.TargetY, 0);
        public static readonly EasePropertyMetadata TargetZMetadata = new(Resources.TargetZ, 0);
        public static readonly EasePropertyMetadata ZNearMetadata = new(Resources.ZNear, 0.1F);
        public static readonly EasePropertyMetadata ZFarMetadata = new(Resources.ZFar, 20000);
        public static readonly EasePropertyMetadata AngleMetadata = new(Resources.Angle, 0);
        public static readonly EasePropertyMetadata FovMetadata = new(Resources.Fov, 55, 179, 1);
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
            GLTK.LookAt(
                scene.Width, scene.Height,
                X.GetValue(frame), Y.GetValue(frame), Z.GetValue(frame),
                TargetX.GetValue(frame), TargetY.GetValue(frame), TargetZ.GetValue(frame),
                ZNear.GetValue(frame), ZFar.GetValue(frame),
                Fov.GetValue(frame),
                Mode.IsChecked);

            for (int i = 1; i < args.Schedules.Count; i++)
            {
                var effect = args.Schedules[i];

                effect.Render(args);
            }
        }
        public override void PropertyLoaded()
        {
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
    }
}
