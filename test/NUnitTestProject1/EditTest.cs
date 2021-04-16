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

namespace NUnitTestProject1
{
    public class EditingPropertyTest
    {
        [SetUp]
        public void Setup()
        {

        }

        [Test]
        public void Test()
        {
            var obj = new PropertyTest();

            Debug.Assert(obj.Children.FirstOrDefault(i => i is null) is null, "全てのEditingPropertyに値がsetされていない。");

            obj.Load();

            Debug.Assert(obj.Children.FirstOrDefault(i => !i.IsLoaded) is null, "EditingPropertyがすべてLoadされていない。");

            obj.Unload();

            Debug.Assert(obj.Children.FirstOrDefault(i => i.IsLoaded) is null, "EditingPropertyがすべてUnloadされていない。");
        }
    }
}