using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core.Data.EffectData;
using BEditor.Core.Data.ProjectData;
using BEditor.Core.Data.PropertyData;
using BEditor.Core.Renderer;

namespace BEditor.Core.Data.ObjectData {
    [DataContract(Namespace = "")]
    public sealed class CameraObject : ObjectElement {
        public static readonly EasePropertyMetadata XMetadata = new EasePropertyMetadata(Properties.Resources.X, 0);
        public static readonly EasePropertyMetadata YMetadata = new EasePropertyMetadata(Properties.Resources.Y, 0);
        public static readonly EasePropertyMetadata ZMetadata = new EasePropertyMetadata(Properties.Resources.Z, 1024);
        public static readonly EasePropertyMetadata TargetXMetadata = new EasePropertyMetadata(Properties.Resources.TargetX, 0);
        public static readonly EasePropertyMetadata TargetYMetadata = new EasePropertyMetadata(Properties.Resources.TargetY, 0);
        public static readonly EasePropertyMetadata TargetZMetadata = new EasePropertyMetadata(Properties.Resources.TargetZ, 0);
        public static readonly EasePropertyMetadata ZNearMetadata = new EasePropertyMetadata(Properties.Resources.ZNear, 0.1F);
        public static readonly EasePropertyMetadata ZFarMetadata = new EasePropertyMetadata(Properties.Resources.ZFar, 20000);
        public static readonly EasePropertyMetadata AngleMetadata = new EasePropertyMetadata(Properties.Resources.Angle, 0);
        public static readonly EasePropertyMetadata FovMetadata = new EasePropertyMetadata(Properties.Resources.Fov, 55, 179, 1);
        public static readonly CheckPropertyMetadata ModeMetadata = new CheckPropertyMetadata(Properties.Resources.Perspective, true);


        public CameraObject() {
            X = new EaseProperty(XMetadata);
            Y = new EaseProperty(YMetadata);
            Z = new EaseProperty(ZMetadata);
            TargetX = new EaseProperty(TargetXMetadata);
            TargetY = new EaseProperty(TargetYMetadata);
            TargetZ = new EaseProperty(TargetZMetadata);
            ZNear = new EaseProperty(ZNearMetadata);
            ZFar = new EaseProperty(ZFarMetadata);
            Angle = new EaseProperty(AngleMetadata);
            Fov = new EaseProperty(FovMetadata);
            Mode = new CheckProperty(ModeMetadata);
        }


        #region ObjectElement
        public override string Name => Properties.Resources.Camera;

        #region Load
        public override void Load(EffectLoadArgs args) {
            int frame = args.Frame;
            var scene = ClipData.Scene;
            scene.RenderingContext.MakeCurrent();
            Graphics.LookAt(
                scene.Width, scene.Height,
                X.GetValue(frame), Y.GetValue(frame), Z.GetValue(frame),
                TargetX.GetValue(frame), TargetY.GetValue(frame), TargetZ.GetValue(frame),
                ZNear.GetValue(frame), ZFar.GetValue(frame),
                Fov.GetValue(frame),
                Mode.IsChecked);

            for (int i = 1; i < args.Schedules.Count; i++) {
                var effect = args.Schedules[i];

                effect.Load(args);
            }
        }
        #endregion

        #region PropertySettings
        public override IList<PropertyElement> PropertySettings => new List<PropertyElement>
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
        public EaseProperty X { get; set; }

        [DataMember(Order = 1)]
        [PropertyMetadata(nameof(YMetadata), typeof(CameraObject))]
        public EaseProperty Y { get; set; }

        [DataMember(Order = 2)]
        [PropertyMetadata(nameof(ZMetadata), typeof(CameraObject))]
        public EaseProperty Z { get; set; }

        [DataMember(Order = 3)]
        [PropertyMetadata(nameof(TargetXMetadata), typeof(CameraObject))]
        public EaseProperty TargetX { get; set; }

        [DataMember(Order = 4)]
        [PropertyMetadata(nameof(TargetYMetadata), typeof(CameraObject))]
        public EaseProperty TargetY { get; set; }

        [DataMember(Order = 5)]
        [PropertyMetadata(nameof(TargetZMetadata), typeof(CameraObject))]
        public EaseProperty TargetZ { get; set; }

        [DataMember(Order = 6)]
        [PropertyMetadata(nameof(ZNearMetadata), typeof(CameraObject))]
        public EaseProperty ZNear { get; set; }

        [DataMember(Order = 7)]
        [PropertyMetadata(nameof(ZFarMetadata), typeof(CameraObject))]
        public EaseProperty ZFar { get; set; }

        [DataMember(Order = 8)]
        [PropertyMetadata(nameof(AngleMetadata), typeof(CameraObject))]
        public EaseProperty Angle { get; set; }

        [DataMember(Order = 9)]
        [PropertyMetadata(nameof(FovMetadata), typeof(CameraObject))]
        public EaseProperty Fov { get; set; }

        [DataMember(Order = 10)]
        [PropertyMetadata(nameof(ModeMetadata), typeof(CameraObject))]
        public CheckProperty Mode { get; set; }
    }
}
