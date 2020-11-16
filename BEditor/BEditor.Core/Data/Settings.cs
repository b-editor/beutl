using System.IO;
using System.Runtime.Serialization;

namespace BEditor.Core.Data
{
    [DataContract(Namespace = "")]
    public class Settings : BasePropertyChanged
    {
        private int clipHeight = 25;
        private bool darkMode = true;
        private bool autoBackUp = true;
        private string lastTimeFolder = "";
        private int lastTimeNum = 0;
        private int widthOf1Frame = 5;
        private bool enableErrorLog = false;

        public static Settings Default { get; }

        static Settings()
        {
            var path = $"{Component.Funcs.GetApp().Path}\\user\\settings.json";
            if (!File.Exists(path))
            {
                Default = new Settings();
                Serialize.SaveToFile(Default, path);
            }
            else
            {
                Default = Serialize.LoadFromFile<Settings>(path);
            }
        }
        private Settings() { }
        public void Save() => Serialize.SaveToFile(this, $"{Component.Funcs.GetApp().Path}\\user\\settings.json");

        [DataMember]
        public int ClipHeight
        {
            get => clipHeight;
            set => SetValue(value, ref clipHeight, nameof(ClipHeight));
        }
        [DataMember]
        public bool UseDarkMode
        {
            get => darkMode;
            set => SetValue(value, ref darkMode, nameof(UseDarkMode));
        }
        [DataMember]
        public bool AutoBackUp
        {
            get => autoBackUp;
            set => SetValue(value, ref autoBackUp, nameof(AutoBackUp));
        }
        [DataMember]
        public string LastTimeFolder
        {
            get => lastTimeFolder;
            set => SetValue(value, ref lastTimeFolder, nameof(LastTimeFolder));
        }
        [DataMember]
        public int LastTimeNum
        {
            get => lastTimeNum;
            set => SetValue(value, ref lastTimeNum, nameof(LastTimeNum));
        }
        [DataMember]
        public int WidthOf1Frame
        {
            get => widthOf1Frame;
            set => SetValue(value, ref widthOf1Frame, nameof(WidthOf1Frame));
        }
        [DataMember]
        public bool EnableErrorLog
        {
            get => enableErrorLog;
            set => SetValue(value, ref enableErrorLog, nameof(EnableErrorLog));
        }
    }
}
