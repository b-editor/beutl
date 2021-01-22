using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;

using BEditor.Core.Command;
using BEditor.Core.Data.Property;
using BEditor.Core.Extensions;
using BEditor.Core.Properties;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;

namespace BEditor.Core.Data.Primitive.Objects
{
    [DataContract]
    public class SceneObject : ImageObject
    {
        SelectorPropertyMetadata? SelectSceneMetadata;

        public SceneObject()
        {
            Start = new(VideoFile.StartMetadata);

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

        protected override Image<BGRA32>? OnRender(EffectRenderArgs args)
        {
            var scene = (Scene)SelectScene.SelectItem! ?? Parent!.Parent;
            if (scene.Equals(this.GetParent2())) return null;

            // Clipの相対的なフレーム
            var frame = args.Frame - Parent!.Start;

            return scene.Render(frame + (int)Start[args.Frame], RenderType.ImageOutput).Image;
        }
        protected override void OnLoad()
        {
            base.OnLoad();
            SelectSceneMetadata = new ScenesSelectorMetadata(this);
            Start.Load(VideoFile.StartMetadata);
            SelectScene.Load(SelectSceneMetadata);
        }
        protected override void OnUnload()
        {
            base.OnUnload();
            Start.Unload();
            SelectScene.Unload();
        }

        internal record ScenesSelectorMetadata : SelectorPropertyMetadata
        {
            internal ScenesSelectorMetadata(SceneObject scene) : base(Resources.Scenes, Array.Empty<object>())
            {
                MemberPath = "SceneName";
                ItemSource = scene.GetParent3()!.SceneList;
            }
        }
    }
}
