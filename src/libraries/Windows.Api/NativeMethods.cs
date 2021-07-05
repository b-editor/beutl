using System;
using System.IO;
using System.Runtime.Versioning;

namespace Windows.Api
{
    public class FileLink
    {
        public FileLink(string ext, string filetype, string description, string verb)
        {
            Extension = ext;
            FileType = filetype;
            Description = description;
            Verb = verb;
        }

        public string Extension { get; set; }

        public string FileType { get; set; }

        public string Description { get; set; }

        public string Verb { get; set; }

        public string? Icon { get; set; }

        [SupportedOSPlatform("windows")]
        public void Link()
        {
            var commandline = "\"" + Path.Combine(AppContext.BaseDirectory, "beditor.exe") + "\" \"%1\"";

            var iconPath = Icon ?? Path.Combine(AppContext.BaseDirectory, "beditor.exe");
            var iconIndex = 0;

            var currentUserKey = Microsoft.Win32.Registry.CurrentUser;

            var regkey = currentUserKey.CreateSubKey("Software\\Classes\\" + Extension);
            regkey.SetValue("", FileType);
            regkey.Close();

            var typekey = currentUserKey.CreateSubKey("Software\\Classes\\" + FileType);
            typekey.SetValue("", Description);
            typekey.Close();

            // commandline
            var cmdkey = currentUserKey.CreateSubKey("Software\\Classes\\" + FileType + "\\shell\\" + Verb + "\\command");
            cmdkey.SetValue("", commandline);
            cmdkey.Close();

            // Icon
            var iconkey = currentUserKey.CreateSubKey("Software\\Classes\\" + FileType + "\\DefaultIcon");
            iconkey.SetValue("", iconPath + "," + iconIndex.ToString());
            iconkey.Close();
        }
    }
}