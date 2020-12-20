using BEditor.Core.Data.Control;
using BEditor.Media;

using NUnit.Framework;

using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace NUnitTestProject1
{
    public class Tests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void Test()
        {
            var frame = Frame.FromMinutes(1, 60);

            Console.WriteLine(frame.Value);
        }
    }
}