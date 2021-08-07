using System.Reflection;

using BEditor.Drawing;
using BEditor.Drawing.Pixel;

using NUnit.Framework;

namespace DrawingTest
{
    public class LookupTableTest
    {
        public const string FilePath = "../../../../../docs/example/original.png";

        [Test]
        public void FromStream()
        {
            using var img = Image<BGRA32>.FromFile(FilePath);

            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DrawingTest.Resources.PB_Basin.CUBE");
            using var table = LookupTable.FromStream(stream);

            img.Apply(table, 0.5f);
        }

        [Test]
        public void Contrast()
        {
            using var img = Image<BGRA32>.FromFile(FilePath);

            using var table = LookupTable.Contrast(255);

            img.Apply(table);
        }

        [Test]
        public void Gamma()
        {
            using var img = Image<BGRA32>.FromFile(FilePath);

            using var table = LookupTable.Gamma(3);

            img.Apply(table);
        }

        [Test]
        public void Negaposi()
        {
            using var img = Image<BGRA32>.FromFile(FilePath);

            using var table = LookupTable.Negaposi(255);

            img.Apply(table);
        }

        [Test]
        public void Solarisation()
        {
            using var img = Image<BGRA32>.FromFile(FilePath);

            using var table = LookupTable.Solarisation(5);

            img.Apply(table);
        }
    }
}