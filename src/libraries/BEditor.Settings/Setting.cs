using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Packaging;

namespace BEditor
{

    [DataContract]
    public class Settings : INotifyPropertyChanged, IExtensibleDataObject
    {
        #region Fields

        private static readonly PropertyChangedEventArgs clipHeightArgs = new(nameof(ClipHeight));
        private static readonly PropertyChangedEventArgs darkModeArgs = new(nameof(UseDarkMode));
        private static readonly PropertyChangedEventArgs autoBackUpArgs = new(nameof(AutoBackUp));
        private static readonly PropertyChangedEventArgs backUpIntervalArgs = new(nameof(BackUpInterval));
        private static readonly PropertyChangedEventArgs lastTimeFolderArgs = new(nameof(LastTimeFolder));
        private static readonly PropertyChangedEventArgs widthOf1FrameArgs = new(nameof(WidthOf1Frame));
        private static readonly PropertyChangedEventArgs langArgs = new(nameof(Language));
        private static readonly PropertyChangedEventArgs showStartWindowArgs = new(nameof(ShowStartWindow));
        private static readonly PropertyChangedEventArgs prioritizeGPUArgs = new(nameof(PrioritizeGPU));
        private uint clipHeight = 25;
        private bool darkMode = true;
        private bool showStartWindow = true;
        private bool autoBackUp = true;
        private uint? backUpInterval = 10;
        private string lastTimeFolder = "";
        private uint widthOf1Frame = 5;
        private ObservableCollection<string>? includeFonts;
        private ObservableCollection<string>? recentFiles;
        private ObservableCollection<PackageSourceInfo>? packageSources;
        private string? language;
        private bool prioritizeGPU = true;

        #endregion


        static Settings()
        {
            var path = Path.Combine(AppContext.BaseDirectory, "user", "settings.json");
            if (!File.Exists(path))
            {
                Default = new Settings();
                Serialize.SaveToFile(Default, path);
            }
            else
            {
                Default = Serialize.LoadFromFile<Settings>(path) ?? new Settings();
            }
        }
        private Settings() { }

        public event PropertyChangedEventHandler? PropertyChanged;

        #region Properties

        public static Settings Default { get; }
        [DataMember]
        public uint ClipHeight
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
        public uint BackUpInterval
        {
            get => backUpInterval ??= 10;
            set => SetValue(value, ref backUpInterval, backUpIntervalArgs);
        }
        [DataMember]
        public string LastTimeFolder
        {
            get => lastTimeFolder ??= "";
            set => SetValue(value, ref lastTimeFolder, lastTimeFolderArgs);
        }
        [DataMember]
        public uint WidthOf1Frame
        {
            get => widthOf1Frame;
            set => SetValue(value, ref widthOf1Frame, widthOf1FrameArgs);
        }
        [DataMember]
        public ObservableCollection<string> RecentFiles
        {
            get => recentFiles ??= new();
            private set => recentFiles = new(value.Where(file => File.Exists(file)));
        }
        [DataMember]
        public ObservableCollection<string> IncludeFontDir
        {
            get
            {
                if (includeFonts is null)
                {
                    includeFonts = new();
                    string[] fontDirs;

                    if (OperatingSystem.IsWindows())
                    {
                        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                        fontDirs = new string[]
                        {
                            $"{user}\\AppData\\Local\\Microsoft\\Windows\\Fonts",
                            "C:\\Windows\\Fonts"
                        };
                    }
                    else if (OperatingSystem.IsLinux())
                    {
                        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                        fontDirs = new string[]
                        {
                            "/usr/local/share/fonts",
                            "/usr/share/fonts",
                            $"{user}/.local/share/fonts/"
                        };
                    }
                    else if (OperatingSystem.IsMacOS())
                    {
                        var user = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                        fontDirs = new string[]
                        {
                            "/System/Library/Fonts",
                            "/Library/Fonts",
                            $"{user}/Library/Fonts"
                        };
                    }
                    else
                    {
                        fontDirs = Array.Empty<string>();
                    }

                    foreach (var dir in fontDirs.Where(d => Directory.Exists(d)))
                    {
                        includeFonts.Add(dir);
                    }
                }

                return includeFonts;
            }
            set => includeFonts = value;
        }
        [DataMember]
        public ObservableCollection<PackageSourceInfo> PackageSources
        {
            get => packageSources ??= new()
            {
                new() { Name = "BEditor", Url = new("https://beditor.net/api/packages") }
            };
            private set => packageSources = value;
        }
        [DataMember]
        public string Language
        {
            get => language ??= CultureInfo.CurrentCulture.Name;
            set => SetValue(value, ref language, langArgs);
        }
        [DataMember]
        public bool ShowStartWindow
        {
            get => showStartWindow;
            set => SetValue(value, ref showStartWindow, showStartWindowArgs);
        }
        [DataMember]
        public bool PrioritizeGPU
        {
            get => prioritizeGPU;
            set => SetValue(value, ref prioritizeGPU, prioritizeGPUArgs);
        }
        public ExtensionDataObject? ExtensionData { get; set; }

        #endregion

        private void RaisePropertyChanged(PropertyChangedEventArgs args)
        {
            PropertyChanged?.Invoke(this, args);
        }
        private void SetValue<T1>(T1 src, ref T1 dst, PropertyChangedEventArgs args)
        {
            if (src == null || !src.Equals(dst))
            {
                dst = src;
                RaisePropertyChanged(args);
            }
        }
        public void Save() => Serialize.SaveToFile(this, Path.Combine(AppContext.BaseDirectory, "user", "settings.json"));
        public Task SaveAsync() => Task.Run(() => Serialize.SaveToFile(this, Path.Combine(AppContext.BaseDirectory, "user", "settings.json")));
    }
}