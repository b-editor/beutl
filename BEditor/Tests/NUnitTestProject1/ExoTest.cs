using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Extensions.Object;

using Microsoft.Extensions.Configuration;

using NUnit.Framework;

namespace NUnitTestProject1
{
    public class ExoTest
    {
        [SetUp]
        public void Setup()
        {

        }

        [Test]
        public void Test()
        {
            const string file = "E:\\無題.exo";

            var obj = ExoPerser.FromFile(file);
            obj.Perse();
        }
    }
}
