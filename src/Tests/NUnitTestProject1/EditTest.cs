using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core.Command;
using BEditor.Core.Data;
using BEditor.Core.Data.Property;
using BEditor.Drawing;
using BEditor.Primitive;

using NUnit.Framework;

namespace NUnitTestProject1
{
    public class EditTest
    {
        [SetUp]
        public void Setup()
        {

        }

        [Test]
        public void Test()
        {
            using var project = new Project(1920, 1080, 60);
            project.Load();
            var scene = project.PreviewScene;

            scene.AddClip(1, 1, PrimitiveTypes.FigureMetadata, out _).Execute();

            var result = scene.Render(1);

            result.Image.Encode("E:\\TestProject\\Image.png");
        }
    }
}
