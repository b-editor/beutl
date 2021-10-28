using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Serialization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using System.Threading.Tasks;

using BEditor.Data;
using BEditor.LangResources;
using BEditor.Packaging;

namespace BEditor
{
    public enum LayerBorder
    {
        None,

        Strong,

        Thin,
    }

    public sealed class Settings : EditingObject
    {
        public static readonly EditingProperty<uint> ClipHeightProperty
            = EditingProperty.Register<uint, Settings>(
                "ClipHeight",
                EditingPropertyOptions<uint>.Create().DefaultValue(25).Serialize());

        public static readonly EditingProperty<uint> FrameWidthProperty
            = EditingProperty.Register<uint, Settings>(
                "FrameWidth",
                EditingPropertyOptions<uint>.Create().DefaultValue(5).Serialize());

        public static readonly EditingProperty<bool> FixSeekbarProperty
            = EditingProperty.Register<bool, Settings>(
                "FixSeekbar",
                EditingPropertyOptions<bool>.Create().DefaultValue(true).Serialize());

        public static readonly EditingProperty<LayerBorder> LayerBorderProperty
            = EditingProperty.Register<LayerBorder, Settings>(
                "LayerBorder",
                EditingPropertyOptions<LayerBorder>.Create().DefaultValue(LayerBorder.Strong).Notify(true).Serialize(
                    (writer, obj) => writer.WriteNumberValue((int)obj),
                    ctx => (LayerBorder)ctx.Element.GetInt32()));

        public static readonly EditingProperty<bool> UseDarkModeProperty
            = EditingProperty.Register<bool, Settings>(
                "UseDarkMode",
                EditingPropertyOptions<bool>.Create().DefaultValue(true).Serialize());

        public static readonly EditingProperty<bool> AutoBackUpProperty
            = EditingProperty.Register<bool, Settings>(
                "AutoBackUp",
                EditingPropertyOptions<bool>.Create().DefaultValue(true).Serialize());

        public static readonly EditingProperty<uint> BackUpIntervalProperty
            = EditingProperty.Register<uint, Settings>(
                "BackUpInterval",
                EditingPropertyOptions<uint>.Create().DefaultValue(10).Serialize());

        public static readonly EditingProperty<string> LastTimeFolderProperty
            = EditingProperty.Register<string, Settings>(
                "LastTimeFolder",
                EditingPropertyOptions<string>.Create().DefaultValue(string.Empty)!.Serialize()!);

        public static readonly EditingProperty<ObservableCollection<string>> RecentFilesProperty
            = EditingProperty.Register<ObservableCollection<string>, Settings>(
                "RecentFiles",
                EditingPropertyOptions<ObservableCollection<string>>.Create()
                    .DefaultValue(new())
                    .Serialize(WriteRecentFiles, ReadRecentFiles));

        public static readonly EditingProperty<ObservableCollection<string>> IncludeFontDirProperty
            = EditingProperty.Register<ObservableCollection<string>, Settings>(
                "IncludeFontDir",
                EditingPropertyOptions<ObservableCollection<string>>.Create()
                    .Initialize(InitIncludeFontDir)
                    .Serialize(WriteIncludeFontDir, ReadIncludeFontDir));

        public static readonly EditingProperty<ObservableCollection<SupportedLanguage>> SupportedLanguagesProperty
            = EditingProperty.Register<ObservableCollection<SupportedLanguage>, Settings>(
                "SupportedLanguages",
                EditingPropertyOptions<ObservableCollection<SupportedLanguage>>.Create()
                    .Initialize(InitSupportedLanguages)
                    .Serialize(WriteSupportedLanguages, ReadSupportedLanguages));

        public static readonly EditingProperty<ObservableCollection<PackageSourceInfo>> PackageSourcesProperty
            = EditingProperty.Register<ObservableCollection<PackageSourceInfo>, Settings>(
                "PackageSources",
                EditingPropertyOptions<ObservableCollection<PackageSourceInfo>>.Create()
                    .Initialize(InitPackageSources)
                    .Serialize(WritePackageSources, ReadPackageSources));

        public static readonly EditingProperty<SupportedLanguage> LanguageProperty
            = EditingProperty.Register<SupportedLanguage, Settings>(
                "Language",
                EditingPropertyOptions<SupportedLanguage>.Create()!.Initialize(InitLanguage).Serialize(WriteLanguage, ReadLanguage)!);

        public static readonly EditingProperty<bool> ShowStartWindowProperty
            = EditingProperty.Register<bool, Settings>(
                "ShowStartWindow",
                EditingPropertyOptions<bool>.Create().DefaultValue(true).Serialize());

        public static readonly EditingProperty<bool> PrioritizeGPUProperty
            = EditingProperty.Register<bool, Settings>(
                "PrioritizeGPU",
                EditingPropertyOptions<bool>.Create().DefaultValue(true).Serialize());

        public static readonly EditingProperty<string> GraphicsProfileProperty
            = EditingProperty.Register<string, Settings>(
                "GraphicsProfile",
                EditingPropertyOptions<string>.Create().DefaultValue("OpenGL")!.Serialize()!);

        public static readonly EditingProperty<string> AudioProfileProperty
            = EditingProperty.Register<string, Settings>(
                "AudioProfile",
                EditingPropertyOptions<string>.Create().DefaultValue(OperatingSystem.IsWindows() ? "XAudio2" : "OpenAL")!.Serialize()!);

        private static readonly JsonWriterOptions _options = new()
        {
            Indented = true,
            Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        };

        static Settings()
        {
            var path = Path.Combine(ServicesLocator.GetUserFolder(), "settings.json");
            if (!File.Exists(path))
            {
                Default = new Settings();
                Default.Save();
            }
            else
            {
                Default = new Settings();
                using var stream = new FileStream(path, FileMode.Open);
                using var doc = JsonDocument.Parse(stream);
                Default.SetObjectData(new(doc.RootElement, null));
            }
        }

        public static Settings Default { get; }

        public uint ClipHeight
        {
            get => GetValue(ClipHeightProperty);
            set => SetValue(ClipHeightProperty, value);
        }

        public uint FrameWidth
        {
            get => GetValue(FrameWidthProperty);
            set => SetValue(FrameWidthProperty, value);
        }

        public bool FixSeekbar
        {
            get => GetValue(FixSeekbarProperty);
            set => SetValue(FixSeekbarProperty, value);
        }

        public LayerBorder LayerBorder
        {
            get => GetValue(LayerBorderProperty);
            set => SetValue(LayerBorderProperty, value);
        }

        public bool UseDarkMode
        {
            get => GetValue(UseDarkModeProperty);
            set => SetValue(UseDarkModeProperty, value);
        }

        public bool AutoBackUp
        {
            get => GetValue(AutoBackUpProperty);
            set => SetValue(AutoBackUpProperty, value);
        }

        public uint BackUpInterval
        {
            get => GetValue(BackUpIntervalProperty);
            set => SetValue(BackUpIntervalProperty, value);
        }

        public string LastTimeFolder
        {
            get => GetValue(LastTimeFolderProperty);
            set => SetValue(LastTimeFolderProperty, value);
        }

        public ObservableCollection<string> RecentFiles
        {
            get => GetValue(RecentFilesProperty);
            set => SetValue(RecentFilesProperty, value);
        }

        public ObservableCollection<string> IncludeFontDir
        {
            get => GetValue(IncludeFontDirProperty);
            set => SetValue(IncludeFontDirProperty, value);
        }

        public ObservableCollection<PackageSourceInfo> PackageSources
        {
            get => GetValue(PackageSourcesProperty);
            set => SetValue(PackageSourcesProperty, value);
        }

        public SupportedLanguage Language
        {
            get => GetValue(LanguageProperty);
            set => SetValue(LanguageProperty, value);
        }

        public bool ShowStartWindow
        {
            get => GetValue(ShowStartWindowProperty);
            set => SetValue(ShowStartWindowProperty, value);
        }

        public bool PrioritizeGPU
        {
            get => GetValue(PrioritizeGPUProperty);
            set => SetValue(PrioritizeGPUProperty, value);
        }

        public string GraphicsProfile
        {
            get => GetValue(GraphicsProfileProperty);
            set => SetValue(GraphicsProfileProperty, value);
        }

        public string AudioProfile
        {
            get => GetValue(AudioProfileProperty);
            set => SetValue(AudioProfileProperty, value);
        }

        public ObservableCollection<SupportedLanguage> SupportedLanguages
        {
            get => GetValue(SupportedLanguagesProperty);
            set => SetValue(SupportedLanguagesProperty, value);
        }

        public void Save()
        {
            var path = Path.Combine(ServicesLocator.GetUserFolder(), "settings.json");
            using var stream = new FileStream(path, FileMode.Create);
            using var writer = new Utf8JsonWriter(stream, _options);

            writer.WriteStartObject();
            GetObjectData(writer);
            writer.WriteEndObject();
            writer.Flush();
        }

        public async Task SaveAsync()
        {
            var path = Path.Combine(ServicesLocator.GetUserFolder(), "settings.json");
            using var stream = new FileStream(path, FileMode.Create);
            using var writer = new Utf8JsonWriter(stream, _options);

            writer.WriteStartObject();
            GetObjectData(writer);
            writer.WriteEndObject();
            await writer.FlushAsync();
        }

        private static SupportedLanguage InitLanguage()
        {
            var current = CultureInfo.CurrentCulture;
            var langs = InitSupportedLanguages();
            foreach (var item in langs)
            {
                if (item.Culture.Equals(current))
                {
                    return item;
                }
            }

            return langs.Single(i => i.Culture.Name == "en-US");
        }

        private static void WriteLanguage(Utf8JsonWriter arg1, SupportedLanguage arg2)
        {
            arg1.WriteStartObject();
            arg2.GetObjectData(arg1);
            arg1.WriteEndObject();
        }

        private static SupportedLanguage ReadLanguage(DeserializeContext arg)
        {
            try
            {
                var obj = (SupportedLanguage)FormatterServices.GetUninitializedObject(typeof(SupportedLanguage));
                obj.SetObjectData(arg);
                return obj;
            }
            catch
            {
                return InitLanguage();
            }
        }

        // 対応している言語を読み取る
        private static ObservableCollection<SupportedLanguage> ReadSupportedLanguages(DeserializeContext arg)
        {
            return new ObservableCollection<SupportedLanguage>(arg.Element.EnumerateArray()
                .Select(i =>
                {
                    try
                    {
                        var obj = new SupportedLanguage(CultureInfo.InvariantCulture, string.Empty, string.Empty, Assembly.GetExecutingAssembly());
                        obj.SetObjectData(new DeserializeContext(i, null));
                        return obj;
                    }
                    catch
                    {
                        return null;
                    }
                })
                .Where(i => i != null)!);
        }

        // 対応している言語を書き込む
        private static void WriteSupportedLanguages(Utf8JsonWriter arg1, ObservableCollection<SupportedLanguage> arg2)
        {
            arg1.WriteStartArray();

            foreach (var item in arg2)
            {
                arg1.WriteStartObject();
                item.GetObjectData(arg1);
                arg1.WriteEndObject();
            }

            arg1.WriteEndArray();
        }

        private static ObservableCollection<SupportedLanguage> InitSupportedLanguages()
        {
            return new()
            {
                new SupportedLanguage(new CultureInfo("ja-JP"), Strings.Japanese, "BEditor.LangResources.Strings", typeof(Strings).Assembly),
                new SupportedLanguage(new CultureInfo("en-US"), $"{Strings.English} ({Strings.MachineTranslation})", "BEditor.LangResources.Strings", typeof(Strings).Assembly),
            };
        }

        // 最近使ったファイルを書き込む
        private static void WriteRecentFiles(Utf8JsonWriter writer, ObservableCollection<string> obj)
        {
            writer.WriteStartArray();

            foreach (var item in obj.Where(i => File.Exists(i)))
            {
                writer.WriteStringValue(item);
            }

            writer.WriteEndArray();
        }

        // 最近使ったファイルを読み取る
        private static ObservableCollection<string> ReadRecentFiles(DeserializeContext ctx)
        {
            return new ObservableCollection<string>(ctx.Element.EnumerateArray()
                .Select(i => i.GetString())
                .Where(i => File.Exists(i))!);
        }

        private static void WriteIncludeFontDir(Utf8JsonWriter writer, ObservableCollection<string> obj)
        {
            writer.WriteStartArray();

            foreach (var item in obj)
            {
                writer.WriteStringValue(item);
            }

            writer.WriteEndArray();
        }

        private static ObservableCollection<string> ReadIncludeFontDir(DeserializeContext ctx)
        {
            return new ObservableCollection<string>(ctx.Element.EnumerateArray()
                .Select(i => i.GetString())!);
        }

        private static ObservableCollection<string> InitIncludeFontDir()
        {
            var includeFonts = new ObservableCollection<string>();
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

            return includeFonts;
        }

        private static void WritePackageSources(Utf8JsonWriter writer, ObservableCollection<PackageSourceInfo> obj)
        {
            writer.WriteStartArray();

            foreach (var item in obj)
            {
                writer.WriteStartObject();
                item.GetObjectData(writer);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        private static ObservableCollection<PackageSourceInfo> ReadPackageSources(DeserializeContext ctx)
        {
            return new ObservableCollection<PackageSourceInfo>(ctx.Element.EnumerateArray()
                .Select(i =>
                {
                    var obj = new PackageSourceInfo();
                    obj.SetObjectData(new DeserializeContext(i, null));
                    return obj;
                })!);
        }

        private static ObservableCollection<PackageSourceInfo> InitPackageSources()
        {
            return new()
            {
                new() { Name = "BEditor", Url = new("https://beditor.net/api/packages") }
            };
        }
    }
}