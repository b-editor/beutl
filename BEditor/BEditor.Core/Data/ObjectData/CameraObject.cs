using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core.Data.EffectData;
using BEditor.Core.Data.ProjectData;
using BEditor.Core.Data.PropertyData;
using BEditor.Core.Properties;
using BEditor.Core.Renderer;

namespace BEditor.Core.Data.ObjectData
{
    [DataContract(Namespace = "")]
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


        #region ObjectElement
        public override string Name => Core.Properties.Resources.Camera;

        #region Load
        public override void Render(EffectRenderArgs args)
        {
            int frame = args.Frame;
            var scene = Parent.Parent;
            scene.GraphicsContext.MakeCurrent();
            Graphics.LookAt(
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
        #endregion

        #region PropertySettings
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
                    {
                        X, Y, Z,
                        TargetX, TargetY, TargetZ,
                        ZNear, ZFar,
                        Angle,
                        Fov,
                        Mode
                    };

        #endregion

        #endregion


        [DataMember(Order = 0)]
        [PropertyMetadata(nameof(XMetadata), typeof(CameraObject))]
        public EaseProperty X { get; private set; }

        [DataMember(Order = 1)]
        [PropertyMetadata(nameof(YMetadata), typeof(CameraObject))]
        public EaseProperty Y { get; private set; }

        [DataMember(Order = 2)]
        [PropertyMetadata(nameof(ZMetadata), typeof(CameraObject))]
        public EaseProperty Z { get; private set; }

        [DataMember(Order = 3)]
        [PropertyMetadata(nameof(TargetXMetadata), typeof(CameraObject))]
        public EaseProperty TargetX { get; private set; }

        [DataMember(Order = 4)]
        [PropertyMetadata(nameof(TargetYMetadata), typeof(CameraObject))]
        public EaseProperty TargetY { get; private set; }

        [DataMember(Order = 5)]
        [PropertyMetadata(nameof(TargetZMetadata), typeof(CameraObject))]
        public EaseProperty TargetZ { get; private set; }

        [DataMember(Order = 6)]
        [PropertyMetadata(nameof(ZNearMetadata), typeof(CameraObject))]
        public EaseProperty ZNear { get; private set; }

        [DataMember(Order = 7)]
        [PropertyMetadata(nameof(ZFarMetadata), typeof(CameraObject))]
        public EaseProperty ZFar { get; private set; }

        [DataMember(Order = 8)]
        [PropertyMetadata(nameof(AngleMetadata), typeof(CameraObject))]
        public EaseProperty Angle { get; private set; }

        [DataMember(Order = 9)]
        [PropertyMetadata(nameof(FovMetadata), typeof(CameraObject))]
        public EaseProperty Fov { get; private set; }

        [DataMember(Order = 10)]
        [PropertyMetadata(nameof(ModeMetadata), typeof(CameraObject))]
        public CheckProperty Mode { get; private set; }
    }
}
