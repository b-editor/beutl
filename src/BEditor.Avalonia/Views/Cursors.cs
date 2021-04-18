using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia.Input;

namespace BEditor.Views
{
    public static class Cursors
    {
        public static Cursor Arrow = new(StandardCursorType.Arrow);
        public static Cursor SizeWestEast = new(StandardCursorType.SizeWestEast);
    }
}