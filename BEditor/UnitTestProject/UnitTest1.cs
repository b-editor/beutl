using System;

using B_Editor;
using B_Editor.FFmpeg;
using B_Editor.Models;
using B_Editor.Models.Datas.EffectData;
using B_Editor.Models.Datas.ObjectData;
using B_Editor.Models.Datas.ProjectData;
using B_Editor.Models.Media;
using B_Editor.Models.TimeLines;
using B_Editor.ViewModels.TimeLines;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace UnitTestProject {
    [TestClass]
    public class UnitTest1 {
        public UnitTest1() {
            UnitTestInvoker.IsUse = true;
        }

        [TestMethod]
        public void Test() {
            /*/ProjectÇçÏê¨
            Project.Create(1000, 1000, 60, @"E:\UnitTest\Test.bedit");

            var command = new TimeLineModel.AddClip(AppData.Current.Project.PreviewScene, 60, 5, ClipType.Figure);
            UndoRedoManager.Do(command);

            UndoRedoManager.Do(new EffectElement.AddEffect(command.data, new Monoc() {
                Color = {
                    Red = 255,
                    Green = 0,
                    Blue = 0
                }
            }));

            AppData.Current.Project.PreviewScene.Rendering(75);

            AppData.Current.Project.PreviewScene.GLRenderer.GetPixels(out Image img);

            img.Save(@"E:\UnitTest\Image.png");
            */
        }
    }
}
