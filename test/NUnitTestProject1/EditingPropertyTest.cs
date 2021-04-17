using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Command;
using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Drawing;
using BEditor.Primitive;
using BEditor.Primitive.Objects;

using NUnit.Framework;

using TestObject = BEditor.Primitive.Objects.PropertyTest;

namespace NUnitTestProject1
{
    public class EditingPropertyTest
    {
        [SetUp]
        public void Setup()
        {

        }

        [Test]
        public void PropertyTest()
        {
            var obj = new TestObject();

            Debug.Assert(obj.Children.FirstOrDefault(i => i is null) is null, "全てのEditingPropertyに値がsetされていない。");

            obj.Load();

            Debug.Assert(obj.Children.FirstOrDefault(i => !i.IsLoaded) is null, "EditingPropertyがすべてLoadされていない。");

            obj.Unload();

            Debug.Assert(obj.Children.FirstOrDefault(i => i.IsLoaded) is null, "EditingPropertyがすべてUnloadされていない。");
        }

        [Test]
        public void SetGetTest()
        {
            var obj = new TestObject();

            var @new = new ButtonComponent(new("New"));

            obj.SetValue(TestObject.ButtonProperty, @new);


            Debug.Assert(obj.GetValue(TestObject.ButtonProperty) == @new, "プロパティに値が設定されていない");
        }
    }
}