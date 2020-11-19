using NUnit.Framework;
using System.IO;

using BEditor.Core.Media;
using BEditor.Core.Renderings;
using BEditor.Core.Renderings.Extensions;
using System;
using BEditor.Core.Graphics;
using BEditor.Core.Data.ProjectData;
using BEditor.Core.Data.ObjectData;
using BEditor.Core.Extensions;
using BEditor.Core.Data.EffectData;

namespace NUnitTestProject1
{
    public class Tests
    {
        [SetUp]
        public void Setup()
        {
        }

        [Test]
        public void BindableTest()
        {
            var blur1 = new Blur() { AlphaBlur = { IsChecked = false } };
            var blur2 = new Blur() { AlphaBlur = { IsChecked = true } };


            blur1.PropertyLoaded();
            blur2.PropertyLoaded();

            // blur1Ç…çáÇÌÇπÇÈ
            blur2.AlphaBlur.Bind(blur1.AlphaBlur);

            // óºï˚true
            Console.WriteLine($"blur1: {blur1.AlphaBlur.IsChecked}");
            Console.WriteLine($"blur2: {blur2.AlphaBlur.IsChecked}");

            blur2.AlphaBlur.IsChecked = false;

            Console.WriteLine("blur2ïœçX");
            Console.WriteLine($"blur1: {blur1.AlphaBlur.IsChecked}");
            Console.WriteLine($"blur2: {blur2.AlphaBlur.IsChecked}");

            blur1.AlphaBlur.IsChecked = true;

            Console.WriteLine("blur1ïœçX");
            Console.WriteLine($"blur1: {blur1.AlphaBlur.IsChecked}");
            Console.WriteLine($"blur2: {blur2.AlphaBlur.IsChecked}");
            
        }
    }
}