using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

using BEditor.Packaging;

namespace BEditor
{
    public class Settings : INotifyPropertyChanged
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
        private static readonly PropertyChangedEventArgs graphicsProfileArgs = new(nameof(GraphicsProfile));
        private static readonly PropertyChangedEventArgs audioProfileArgs = new(nameof(AudioProfile));
        private uint clipHeight = 25;
        private bool darkMode = true;
        private bool showStartWindow = true;
        private bool autoBackUp = true;
        private uint? backUpInterval = 10;
        private string lastTimeFolder = "";
        private string graphicsProfile = "OpenGL";
        private string? audioProfile;
        private uint widthOf1Frame = 5;
        private ObservableCollection<string>? includeFonts;
        private ObservableCollection<string>? recentFiles;
        private ObservableCollection<PackageSourceInfo>? packageSources;
        private string? language;
        private bool prioritizeGPU = true;

        #endregion

        static Settings()
        {
            var path = Path.Combine(ServicesLocator.GetUserFolder(), "settings.json");
            if (!File.Exists(path))
            {
                Default = new Settings();
                File.WriteAllText(path, JsonSerializer.Serialize(Default, PackageFile._serializerOptions));
            }
            else
            {
                Default = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), PackageFile._serializerOptions) ?? new Settings();
            }
        }

        public Settings() { }

        public event PropertyChangedEventHandler? PropertyChanged;

        #region Properties

        public static Settings Default { get; }

        [JsonPropertyName(nameof(ClipHeight))]
        public uint ClipHeight
        {
            get => clipHeight;
            set => SetValue(value, ref clipHeight, clipHeightArgs);
        }

        [JsonPropertyName(nameof(UseDarkMode))]
        public bool UseDarkMode
        {
            get => darkMode;
            set => SetValue(value, ref darkMode, darkModeArgs);
        }

        [JsonPropertyName(nameof(AutoBackUp))]
        public bool AutoBackUp
        {
            get => autoBackUp;
            set => SetValue(value, ref autoBackUp, autoBackUpArgs);
        }

        [JsonPropertyName(nameof(BackUpInterval))]
        public uint BackUpInterval
        {
            get => backUpInterval ??= 10;
            set => SetValue(value, ref backUpInterval, backUpIntervalArgs);
        }

        [JsonPropertyName(nameof(LastTimeFolder))]
        public string LastTimeFolder
        {
            get => lastTimeFolder ??= "";
            set => SetValue(value, ref lastTimeFolder, lastTimeFolderArgs);
        }

        [JsonPropertyName(nameof(WidthOf1Frame))]
        public uint WidthOf1Frame
        {
            get => widthOf1Frame;
            set => SetValue(value, ref widthOf1Frame, widthOf1FrameArgs);
        }

        [JsonPropertyName(nameof(RecentFiles))]
        public ObservableCollection<string> RecentFiles
        {
            get => recentFiles ??= new();
            set => recentFiles = new(value.Where(file => File.Exists(file)));
        }

        [JsonPropertyName(nameof(IncludeFontDir))]
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

        [JsonPropertyName(nameof(PackageSources))]
        public ObservableCollection<PackageSourceInfo> PackageSources
        {
            get => packageSources ??= new()
            {
                new() { Name = "BEditor", Url = new("https://beditor.net/api/packages") }
            };
            set => packageSources = value;
        }

        [JsonPropertyName(nameof(Language))]
        public string Language
        {
            get => language ??= CultureInfo.CurrentCulture.Name;
            set => SetValue(value, ref language, langArgs);
        }

        [JsonPropertyName(nameof(ShowStartWindow))]
        public bool ShowStartWindow
        {
            get => showStartWindow;
            set => SetValue(value, ref showStartWindow, showStartWindowArgs);
        }

        [JsonPropertyName(nameof(PrioritizeGPU))]
        public bool PrioritizeGPU
        {
            get => prioritizeGPU;
            set => SetValue(value, ref prioritizeGPU, prioritizeGPUArgs);
        }

        [JsonPropertyName(nameof(GraphicsProfile))]
        public string GraphicsProfile
        {
            get => graphicsProfile ??= "";
            set => SetValue(value, ref graphicsProfile, graphicsProfileArgs);
        }

        [JsonPropertyName(nameof(AudioProfile))]
        public string AudioProfile
        {
            get => audioProfile ??= (OperatingSystem.IsWindows()) ? "XAudio2" : "OpenAL";
            set => SetValue(value, ref audioProfile, audioProfileArgs);
        }

        #endregion

        public void Save()
        {
            File.WriteAllText(
                Path.Combine(ServicesLocator.GetUserFolder(), "settings.json"),
                JsonSerializer.Serialize(this, PackageFile._serializerOptions));
        }

        public async Task SaveAsync()
        {
            await File.WriteAllTextAsync(
                Path.Combine(ServicesLocator.GetUserFolder(), "settings.json"),
                JsonSerializer.Serialize(this, PackageFile._serializerOptions));
        }

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
    }
}