using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using static System.Console;

namespace BEditor
{
    public class Progress
    {
        public int Columns { get; set; }
        public int Width { get; set; }
        public int Par { get; set; } = 0;
        public int ParMax { get; set; }
        protected int RowLate { get; set; } = CursorTop;

        public Progress(int width, int parMax)
        {
            Columns = WindowWidth;
            Width = width;
            ParMax = parMax;
        }

        public virtual void Update(string message)
        {
            int row0 = CursorTop;

            float parcent = (float)Par / ParMax;
            int widthNow = (int)Math.Floor(Width * parcent);

            string gauge = new string('>', widthNow) + new string(' ', Width - widthNow);
            string status = $"({parcent * 100:f1}%<-{Par}/{ParMax})";

            Error.WriteLine($"#[{gauge}]#{status}");
            ClearScreenDown();

            Error.WriteLine(message);
            RowLate = CursorTop;
            SetCursorPosition(0, row0);
            Par++;
        }

        public virtual void Done(string doneAlert)
        {
            int sideLen = (int)Math.Floor((float)(Width - doneAlert.Length) / 2);

            string gauge = new string('=', sideLen) + doneAlert;
            gauge += new string('=', Width - gauge.Length);
            string status = $"(100%<-{ParMax}/{ParMax})";

            ClearScreenDown();
            Error.WriteLine($"#[{gauge}]#{status}");
        }

        protected void ClearScreenDown()
        {
            int clearRange = RowLate - (CursorTop - 1);
            Error.Write(new string(' ', Columns * clearRange));
            SetCursorPosition(CursorLeft, CursorTop - clearRange);
        }
    }

    public class ProgressColor : Progress
    {
        public ProgressColor(int width, int parMax) : base(width, parMax) { }

        public override void Update(string message)
        {
            int row0 = CursorTop;
            float parcent = (float)Par / ParMax;
            int widthNow = (int)Math.Floor(Width * parcent);

            string status = $"({parcent * 100:f1}%<-{Par}/{ParMax})";

            BackgroundColor = ConsoleColor.Yellow;
            ForegroundColor = ConsoleColor.DarkYellow;
            Error.Write("{");
            BackgroundColor = ConsoleColor.Cyan;
            Error.Write(new string(' ', widthNow));
            BackgroundColor = ConsoleColor.DarkCyan;
            Error.Write(new string(' ', Width - widthNow));
            BackgroundColor = ConsoleColor.Yellow;
            Error.Write("}");
            ResetColor();
            Error.WriteLine(status);
            ClearScreenDown();

            Error.WriteLine(message);
            RowLate = CursorTop;
            SetCursorPosition(0, row0);
            Par++;
        }

        public override void Done(string doneAlert)
        {
            int sideLen = (int)Math.Floor((float)(Width - doneAlert.Length) / 2);

            string gauge = new string(' ', sideLen) + doneAlert;
            gauge += new string(' ', Width - gauge.Length);
            string status = $"(100%<-{ParMax}/{ParMax})";

            ClearScreenDown();

            BackgroundColor = ConsoleColor.Yellow;
            ForegroundColor = ConsoleColor.DarkYellow;
            Error.Write("{");
            BackgroundColor = ConsoleColor.Green;
            ForegroundColor = ConsoleColor.Red;
            Error.Write(gauge);
            BackgroundColor = ConsoleColor.Yellow;
            ForegroundColor = ConsoleColor.DarkYellow;
            Error.Write("}");
            ResetColor();
            Error.WriteLine(status);
        }
    }
}
