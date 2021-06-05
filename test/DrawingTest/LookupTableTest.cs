using System.Reflection;

using BEditor.Drawing;

using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DrawingTest
{
    [TestClass]
    public class LookupTableTest
    {
        [TestMethod]
        public void FromStream()
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("DrawingTest.Resources.PB_Basin.CUBE");
            var table = LookupTable.FromStream(stream);

            table.Dispose();
        }
    }
}
