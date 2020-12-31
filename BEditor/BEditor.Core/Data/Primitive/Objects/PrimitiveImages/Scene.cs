using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

using BEditor.Core.Command;
using BEditor.Core.Data.Control;
using BEditor.Core.Data.Primitive.Properties;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;
using BEditor.Core.Properties;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Core.Data.Primitive.Objects.PrimitiveImages
{
    [DataContract]
    public class SceneObject : ImageObject
    {
        SelectorPropertyMetadata SelectSceneMetadata;

        public SceneObject()
        {
            Start = new(Video.StartMetadata);

            // この時点で親要素を取得できないので適当なデータを渡す
            SelectScene = new(new SelectorPropertyMetadata("", new string[1]));
        }

        public override string Name => Resources.Scene;
        public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
        {
            Coordinate,
            Zoom,
            Blend,
            Angle,
            Material,
            Start,
            SelectScene
        };
        [DataMember(Order = 0)]
        public EaseProperty Start { get; private set; }
        [DataMember(Order = 1)]
        public SelectorProperty SelectScene { get; private set; }

        public override Image<BGRA32> OnRender(EffectRenderArgs args)
        {
            var scene = SelectScene.SelectItem as Scene;
            if (scene.Equals(this.GetParent2())) return null;

            // Clipの相対的なフレーム
            var frame = args.Frame - Parent.Start;

            return scene.Render(frame + (int)Start.GetValue(args.Frame), RenderType.ImageOutput).Image;
        }
        public override void PropertyLoaded()
        {
            base.PropertyLoaded();
            SelectSceneMetadata = new ScenesSelectorMetadata(this);
            Start.ExecuteLoaded(Video.StartMetadata);
            SelectScene.ExecuteLoaded(SelectSceneMetadata);
        }

        internal record ScenesSelectorMetadata : SelectorPropertyMetadata
        {
            internal ScenesSelectorMetadata(SceneObject scene) : base(Resources.Scenes, null)
            {
                MemberPath = "SceneName";
                ItemSource = scene.GetParent3().SceneList;
            }
        }
    }
}
