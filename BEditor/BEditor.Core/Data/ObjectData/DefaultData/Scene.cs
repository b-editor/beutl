using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.Core.Data.ProjectData;
using BEditor.Core.Data.PropertyData;
using BEditor.Core.Media;

namespace BEditor.Core.Data.ObjectData
{
    public static partial class DefaultData
    {
        [DataContract(Namespace = "")]
        public class Scene : DefaultImageObject
        {
            readonly SelectorPropertyMetadata SelectSceneMetadata;

            #region DefaultImageObjectメンバー

            public override IEnumerable<PropertyElement> Properties => new PropertyElement[]
            {
                Start,
                SelectScene
            };

            public override Media.Image Render(EffectRenderArgs args)
            {
                var frame = args.Frame - Parent.Parent.Start;//相対的なフレーム
                ProjectData.Scene scene = SelectScene.SelectItem as ProjectData.Scene;

                return scene.Render(frame + (int)Start.GetValue(args.Frame)).Image;
            }

            #endregion

            public Scene()
            {
                SelectSceneMetadata = new ScenesSelectorMetadata(this);
                Start = new(Video.SpeedMetadata);
                SelectScene = new(SelectSceneMetadata);
            }

            [DataMember(Order = 0)]
            [PropertyMetadata(nameof(Video.StartMetadata), typeof(Video))]
            public EaseProperty Start { get; private set; }

            [DataMember(Order = 1)]
            [PropertyMetadata(nameof(SelectSceneMetadata), typeof(Scene))]
            public SelectorProperty SelectScene { get; private set; }


            internal record ScenesSelectorMetadata : SelectorPropertyMetadata
            {
                internal ScenesSelectorMetadata(Scene scene) : base(Core.Properties.Resources.Scenes, null)
                {
                    MemberPath = "SceneName";
                    ItemSource = scene.Parent.Parent.Parent.Parent.SceneList;
                }
            }
        }
    }
}
