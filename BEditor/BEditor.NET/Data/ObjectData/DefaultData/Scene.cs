using System.Collections.Generic;
using System.Runtime.Serialization;

using BEditor.NET.Data.ProjectData;
using BEditor.NET.Data.PropertyData;
using BEditor.NET.Media;

namespace BEditor.NET.Data.ObjectData {
    public partial class DefaultData {
        [DataContract(Namespace = "")]
        public class Scene : DefaultImageObject {
            public static readonly SelectorPropertyMetadata SelectSceneMetadata = new ScenesSelectorMetadata();

            #region DefaultImageObjectメンバー

            public override IList<PropertyElement> GroupItems => new List<PropertyElement> {
                Start,
                SelectScene
            };

            public override Media.Image Load(EffectLoadArgs args) {
                var frame = args.Frame - ClipData.Start;//相対的なフレーム
                ProjectData.Scene scene = SelectScene.SelectItem as ProjectData.Scene;

                return scene.Rendering(frame + (int)Start.GetValue(args.Frame));
            }

            #endregion

            public Scene() {
                Start = new(Video.SpeedMetadata);
                SelectScene = new(SelectSceneMetadata);
            }


            [DataMember(Order = 0)]
            [PropertyMetadata(nameof(Video.StartMetadata), typeof(Video))]
            public EaseProperty Start { get; set; }

            [DataMember(Order = 1)]
            [PropertyMetadata(nameof(SelectSceneMetadata), typeof(Scene))]
            public SelectorProperty SelectScene { get; set; }


            internal class ScenesSelectorMetadata : SelectorPropertyMetadata {
                internal ScenesSelectorMetadata() : base(Properties.Resources.Scenes, null) {
                    MemberPath = "SceneName";
                    ItemSource = Component.Current.Project.SceneList;
                    Project.ProjectOpend += (_, _) => ItemSource = Component.Current.Project.SceneList;
                    Project.ProjectClosed += (_, _) => ItemSource = null;
                }
            }
        }
    }
}
