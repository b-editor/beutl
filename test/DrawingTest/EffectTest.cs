using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;

using NUnit.Framework;

namespace DrawingTest
{
    public class EffectTest
    {
        public const string FilePath = "../../../../../docs/example/original.png";

        [Test]
        public void Binarization()
        {
            using var img = Image<BGRA32>.FromFile(FilePath);

            img.Binarization(127);
        }

        [Test]
        public void Blur()
        {
            using var img = Image<BGRA32>.FromFile(FilePath);

            img.Blur(25, 25);
        }

        [Test]
        public void Border()
        {
            using var img = Image<BGRA32>.FromFile(FilePath);

            img.Border(25, Colors.White).Dispose();
        }

        [Test]
        public void Brightness()
        {
            using var img = Image<BGRA32>.FromFile(FilePath);

            img.Brightness(127);
        }

        [Test]
        public void ChromaKey()
        {
            using var img = Image<BGRA32>.FromFile(FilePath);

            img.ChromaKey(Colors.Green, 80, 80);
        }

        [Test]
        public void CircularGradient()
        {
            using var img = Image<BGRA32>.FromFile(FilePath);

            img.CircularGradient(
                new PointF(img.Width / 2, img.Height / 2),
                100,
                new Color[] { Colors.Red, Colors.Blue },
                new float[] { 0, 1 },
                ShaderTileMode.Repeat);
        }

        [Test]
        public void ColorKey()
        {
            using var img = Image<BGRA32>.FromFile(FilePath);

            img.ColorKey(Colors.Green, 80);
        }

        [Test]
        public void Diffusion()
        {
            using var img = Image<BGRA32>.FromFile(FilePath);

            img.Diffusion(10);
        }

        [Test]
        public void Dilate()
        {
            using var img = Image<BGRA32>.FromFile(FilePath);

            img.Dilate(10);
        }

        [Test]
        public void EdgeBlur()
        {
            using var img = Image<BGRA32>.FromFile(FilePath);

            img.EdgeBlur(new Size(10, 10), false);
        }

        [Test]
        public void Erode()
        {
            using var img = Image<BGRA32>.FromFile(FilePath);

            img.Erode(10);
        }

        [Test]
        public void FlatShadow()
        {
            using var img = Image<BGRA32>.FromFile(FilePath);

            img.FlatShadow(Colors.White, 45, 100).Dispose();
        }

        [Test]
        public void Grayscale()
        {
            using var img = Image<BGRA32>.FromFile(FilePath);

            img.Grayscale();
        }

        [Test]
        public void InnerShadow()
        {
            using var img = Image<BGRA32>.FromFile(FilePath);

            img.InnerShadow(10, 10, 50, 0.5f, Colors.Black);
        }
        
        [Test]
        public void LinerGradient()
        {
            using var img = Image<BGRA32>.FromFile(FilePath);

            img.LinerGradient(
                new PointF(0, 0),
                new PointF(0, 0),
                new Color[] { Colors.Red, Colors.Blue },
                new float[] { 0, 1 },
                ShaderTileMode.Repeat);
        }
        
        [Test]
        public void Noise()
        {
            using var img = Image<BGRA32>.FromFile(FilePath);

            img.Noise(127);
        }
        
        [Test]
        public void PartsDisassembly()
        {
            using var img = Image<BGRA32>.FromFile(FilePath);

            foreach (var item in img.PartsDisassembly())
            {
                item.Item1.Dispose();
            }
        }
        
        [Test]
        public void ReverseOpacity()
        {
            using var img = Image<BGRA32>.FromFile(FilePath);

            img.ReverseOpacity();
        }
        
        [Test]
        public void RGBColor()
        {
            using var img = Image<BGRA32>.FromFile(FilePath);

            img.RGBColor(0,0,255);
        }
        
        [Test]
        public void Sepia()
        {
            using var img = Image<BGRA32>.FromFile(FilePath);

            img.Sepia();
        }
        
        [Test]
        public void SetColor()
        {
            using var img = Image<BGRA32>.FromFile(FilePath);

            img.SetColor(Colors.White);
        }
        
        [Test]
        public void Shadow()
        {
            using var img = Image<BGRA32>.FromFile(FilePath);

            img.Shadow(10,10,50,0.5f,Colors.Black);
        }
        
        [Test]
        public void Xor()
        {
            using var img = Image<BGRA32>.FromFile(FilePath);

            img.Xor();
        }
    }
}
