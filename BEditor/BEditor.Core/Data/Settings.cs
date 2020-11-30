using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.Serialization;

using BEditor.Core.Service;

namespace BEditor.Core.Data
{
    [DataContract(Namespace = "")]
    public class Settings : BasePropertyChanged
    {
        #region Fields

        private static readonly PropertyChangedEventArgs clipHeightArgs = new(nameof(ClipHeight));
        private static readonly PropertyChangedEventArgs darkModeArgs = new(nameof(UseDarkMode));
        private static readonly PropertyChangedEventArgs autoBackUpArgs = new(nameof(AutoBackUp));
        private static readonly PropertyChangedEventArgs lastTimeFolderArgs = new(nameof(LastTimeFolder));
        private static readonly PropertyChangedEventArgs lastTimeNumArgs = new(nameof(LastTimeNum));
        private static readonly PropertyChangedEventArgs widthOf1FrameArgs = new(nameof(WidthOf1Frame));
        private static readonly PropertyChangedEventArgs enableErrorLogArgs = new(nameof(EnableErrorLog));
        private int clipHeight = 25;
        private bool darkMode = true;
        private bool autoBackUp = true;
        private string lastTimeFolder = "";
        private int lastTimeNum = 0;
        private int widthOf1Frame = 5;
        private bool enableErrorLog = false;
        private ObservableCollection<string> enablePlugins;
        private ObservableCollection<string> disablePlugins;

        #endregion


        static Settings()
        {
            var path = $"{Services.Path}\\user\\settings.json";
            if (!File.Exists(path))
            {
                Default = new Settings();
                Serialize.SaveToFile(Default, path, SerializeMode.Json);
            }
            else
            {
                Default = Serialize.LoadFromFile<Settings>(path, SerializeMode.Json);
            }
        }
        private Settings() { }


        #region Properties

        public static Settings Default { get; }
        [DataMember]
        public int ClipHeight
        {
            get => clipHeight;
            set => SetValue(value, ref clipHeight, clipHeightArgs);
        }
        [DataMember]
        public bool UseDarkMode
        {
            get => darkMode;
            set => SetValue(value, ref darkMode, darkModeArgs);
        }
        [DataMember]
        public bool AutoBackUp
        {
            get => autoBackUp;
            set => SetValue(value, ref autoBackUp, autoBackUpArgs);
        }
        [DataMember]
        public string LastTimeFolder
        {
            get => lastTimeFolder ??= "";
            set => SetValue(value, ref lastTimeFolder, lastTimeFolderArgs);
        }
        [DataMember]
        public int LastTimeNum
        {
            get => lastTimeNum;
            set => SetValue(value, ref lastTimeNum, lastTimeNumArgs);
        }
        [DataMember]
        public int WidthOf1Frame
        {
            get => widthOf1Frame;
            set => SetValue(value, ref widthOf1Frame, widthOf1FrameArgs);
        }
        [DataMember]
        public bool EnableErrorLog
        {
            get => enableErrorLog;
            set => SetValue(value, ref enableErrorLog, enableErrorLogArgs);
        }
        [DataMember]
        public ObservableCollection<string> EnablePlugins
        {
            get => enablePlugins ??= new();
            set => enablePlugins = value;
        }
        [DataMember]
        public ObservableCollection<string> DisablePlugins
        {
            get => disablePlugins??=new();
            set => disablePlugins = value;
        }

        #endregion


        public void Save() => Serialize.SaveToFile(this, $"{Services.Path}\\user\\settings.json", SerializeMode.Json);
    }
}
