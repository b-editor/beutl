using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Timers;

namespace BEditor.Core
{
    public class DirectoryManager
    {
        private Timer timer;
        public static readonly DirectoryManager Default = new();

        public DirectoryManager() : this(new())
        {

        }
        public DirectoryManager(List<string> directories)
        {
            Directories = directories;
            timer = new()
            {
                Interval = 2500
            };
            timer.Elapsed += Timer_Elapsed;
        }

        public List<string> Directories { get; }
        public bool IsRunning { get; private set; }

        public void Run()
        {
            if (!IsRunning)
            {
                foreach (var dir in Directories)
                {
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                }

                timer.Start();

                IsRunning = true;
            }
        }
        public void Stop()
        {
            if (IsRunning)
            {
                timer.Stop();

                IsRunning = false;
            }
        }
        private void Timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            foreach(var dir in Directories)
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }
        }
    }
}
