using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reactive.Disposables;
using System.Text;
using System.Text.Json;

using BEditor.Command;
using BEditor.Data;
using BEditor.Models.Services;
using BEditor.ViewModels.ToolControl;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using NLog.Extensions.Logging;
using NLog.Layouts;

using Reactive.Bindings;

using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using NLogLevel = NLog.LogLevel;

#nullable disable

namespace BEditor.Models
{
    public class AppData : BasePropertyChanged, IApplication
    {
        private static readonly PropertyChangedEventArgs _ProjectArgs = new(nameof(Project));
        private static readonly PropertyChangedEventArgs _StatusArgs = new(nameof(AppStatus));
        private static readonly PropertyChangedEventArgs _IsPlayingArgs = new(nameof(IsNotPlaying));
        private Project _Project;
        private Status _Status;
        private bool _Isplaying = true;
        private IServiceProvider serviceProvider;

        private AppData()
        {
            CommandManager.Default.Executed += (_, _) => AppStatus = Status.Edit;


            // NLogの設定
            var config = new NLog.Config.LoggingConfiguration();

            var logfile = new NLog.Targets.FileTarget("logfile")
            {
                FileName = Path.Combine(AppContext.BaseDirectory, "user", "log.json"),
                Layout = new JsonLayout()
                {
                    Attributes =
                    {
                        new JsonAttribute("time", "${longdate}"),
                        new JsonAttribute("level", "${level:upperCase=true}"),
                        new JsonAttribute("type", "${exception:format=Type}"),
                        new JsonAttribute("exception", "${exception:format=Message, ToString:separator=*}"),
                        new JsonAttribute("message", "${message}"),
                        new JsonAttribute("innerException", new JsonLayout
                        {
                            Attributes =
                            {
                                new JsonAttribute("time", "${longdate}"),
                                new JsonAttribute("level", "${level:upperCase=true}"),
                                new JsonAttribute("type", "${exception:format=:innerFormat=Type:MaxInnerExceptionLevel=1:InnerExceptionSeparator=}"),
                                new JsonAttribute("message", "${exception:format=:innerFormat=Message:MaxInnerExceptionLevel=1:InnerExceptionSeparator=}"),
                            }
                        }, false)
                    }
                }
            };
            var logconsole = new NLog.Targets.ConsoleTarget("logconsole");

            config.AddRule(NLogLevel.Info, NLogLevel.Fatal, logconsole);
            config.AddRule(NLogLevel.Debug, NLogLevel.Fatal, logfile);

            NLog.LogManager.Configuration = config;

            LoggingFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddConsole()
                    .AddNLog()
                    .AddProvider(new BEditorLoggerProvider());
            });

            // DIの設定
            Services = new ServiceCollection()
                .AddSingleton<IFileDialogService>(p => new FileDialogService())
                .AddSingleton<IMessage>(p => new MessageService())
                .AddSingleton(_ => LoggingFactory)
                .AddSingleton<HttpClient>();
        }

        public static AppData Current { get; } = new();
        public Project Project
        {
            get => _Project;
            set => SetValue(value, ref _Project, _ProjectArgs);
        }
        public Status AppStatus
        {
            get => _Status;
            set => SetValue(value, ref _Status, _StatusArgs);
        }
        public bool IsNotPlaying
        {
            get => _Isplaying;
            set => SetValue(value, ref _Isplaying, _IsPlayingArgs);
        }
        public IServiceCollection Services { get; }
        public IServiceProvider ServiceProvider
        {
            get => serviceProvider ??= Services.BuildServiceProvider();
            set
            {
                serviceProvider = value;

                Message = ServiceProvider.GetService<IMessage>()!;
                FileDialog = ServiceProvider.GetService<IFileDialogService>()!;
            }
        }
        public IMessage Message { get; set; }
        public IFileDialogService FileDialog { get; set; }
        public ILoggerFactory LoggingFactory { get; }

        public async void SaveAppConfig(Project project, string directory)
        {
            static void IfNotExistCreateDir(string dir)
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }

            var cache = Path.Combine(directory, "cache");

            IfNotExistCreateDir(cache);

            {
                var sceneCacheDir = Path.Combine(cache, "scene");
                IfNotExistCreateDir(sceneCacheDir);

                foreach (var scene in project.SceneList)
                {
                    var sceneCache = Path.Combine(sceneCacheDir, scene.Name + ".cache");
                    var cacheObj = new SceneCache(scene.SelectItems.Select(i => i.Name).ToArray())
                    {
                        Select = scene.SelectItem?.Name,
                        PreviewFrame = scene.PreviewFrame,
                        TimelineScale = scene.TimeLineZoom,
                        TimelineHorizonOffset = scene.TimeLineHorizonOffset,
                        TimelineVerticalOffset = scene.TimeLineVerticalOffset
                    };

                    await using var stream = new FileStream(sceneCache, FileMode.Create);
                    await JsonSerializer.SerializeAsync(stream, cacheObj, new JsonSerializerOptions()
                    {
                        WriteIndented = true
                    });
                }
            }
        }
        public void RestoreAppConfig(Project project, string directory)
        {
            static void IfNotExistCreateDir(string dir)
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }

            var cache = Path.Combine(directory, "cache");

            IfNotExistCreateDir(cache);

            {
                var sceneCacheDir = Path.Combine(cache, "scene");
                IfNotExistCreateDir(sceneCacheDir);

                foreach (var scene in project.SceneList)
                {
                    var sceneCache = Path.Combine(sceneCacheDir, scene.Name + ".cache");

                    if (!File.Exists(sceneCache)) continue;

                    var cacheObj = JsonSerializer.Deserialize<SceneCache>(sceneCache);

                    if (cacheObj is not null)
                    {
                        scene.SelectItem = scene[cacheObj.Select];
                        scene.PreviewFrame = cacheObj.PreviewFrame;
                        scene.TimeLineZoom = cacheObj.TimelineScale;
                        scene.TimeLineHorizonOffset = cacheObj.TimelineHorizonOffset;
                        scene.TimeLineVerticalOffset = cacheObj.TimelineVerticalOffset;

                        foreach (var select in cacheObj.Selects.Select(i => scene[i]).Where(i => i is not null))
                        {
                            scene.SelectItems.Add(select!);
                        }
                    }
                }
            }
        }
    }

    public class BEditorLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            var logger = new BEditorLogger(categoryName);

            LogViewModel.Current.Loggers.Add(logger);

            return logger;
        }

        public event EventHandler Disposed;

        public void Dispose()
        {
            Disposed?.Invoke(this, EventArgs.Empty);

            GC.SuppressFinalize(this);
        }
    }

    public class BEditorLogger : ILogger
    {
        private const string _indent_space = "    ";
        private string _indent = "";

        public BEditorLogger(string category)
        {
            CategoryName = category;
        }

        public string CategoryName { get; }
        public ReactiveProperty<string> Text { get; } = new();

        public IDisposable BeginScope<TState>(TState state)
        {
            _indent += _indent_space;

            return Disposable.Create(this, t =>
            {
                _indent = _indent.Remove(0, 4);
            });
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return logLevel is not LogLevel.Debug;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            var str = new StringBuilder();

            str.Append(_indent);
            str.Append('[');
            str.Append(Enum.GetName(typeof(LogLevel), logLevel));
            str.Append("] ");

            str.Append(formatter?.Invoke(state, exception) ?? "");
            str.Append(exception?.Message ?? "");
            str.Append(exception?.StackTrace ?? "");
            str.Append('\n');

            Text.Value += str.ToString();
        }
    }
}
