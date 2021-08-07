using System.Reflection;

using BEditor.Drawing;

using NUnit.Framework;

namespace DrawingTest
{
    public class LookupTableTest
    {
        [Test]
        public void FromStream()
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DrawingTest.Resources.PB_Basin.CUBE");
            var table = LookupTable.FromStream(stream);

            table.Dispose();
        }
    }
}