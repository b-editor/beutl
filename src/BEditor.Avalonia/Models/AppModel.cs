using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;

using BEditor.Command;
using BEditor.Data;
using BEditor.Extensions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Serilog;
using Serilog.Formatting.Json;

#nullable disable

namespace BEditor.Models
{
    public sealed class AppModel : BasePropertyChanged, IApplication
    {
        private static readonly PropertyChangedEventArgs _ProjectArgs = new(nameof(Project));
        private static readonly PropertyChangedEventArgs _StatusArgs = new(nameof(AppStatus));
        private static readonly PropertyChangedEventArgs _IsPlayingArgs = new(nameof(IsNotPlaying));
        private Project _project;
        private Status _status;
        private bool _isplaying = true;
        private IServiceProvider _serviceProvider;

        private AppModel()
        {
            CommandManager.Default.Executed += (_, _) =>
            {
                Project?.PreviewUpdate(RenderType.Preview);
                AppStatus = Status.Edit;
            };

            Log.Logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .WriteTo.File(new JsonFormatter(), Path.Combine(AppContext.BaseDirectory, "user", "log.json"))
                .CreateLogger();

            LoggingFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddDebug()
                    .AddSerilog(Log.Logger);
            });

            // DIの設定
            Services = new ServiceCollection()
                .AddSingleton<IFileDialogService>(_ => new FileDialogService())
                .AddSingleton<IMessage>(_ => new MessageService())
                .AddSingleton(_ => LoggingFactory)
                .AddSingleton<HttpClient>();

            LogManager.Logger= LoggingFactory.CreateLogger<LogManager>();
        }

        public static AppModel Current { get; } = new();

        public Project Project
        {
            get => _project;
            set => SetValue(value, ref _project, _ProjectArgs);
        }

        public Status AppStatus
        {
            get => _status;
            set => SetValue(value, ref _status, _StatusArgs);
        }

        public bool IsNotPlaying
        {
            get => _isplaying;
            set => SetValue(value, ref _isplaying, _IsPlayingArgs);
        }

        public IServiceCollection Services { get; }

        public IServiceProvider ServiceProvider
        {
            get => _serviceProvider ??= Services.BuildServiceProvider();
            set
            {
                _serviceProvider = value;

                Message = ServiceProvider.GetService<IMessage>()!;
                FileDialog = ServiceProvider.GetService<IFileDialogService>()!;
            }
        }

        public IMessage Message { get; set; }

        public IFileDialogService FileDialog { get; set; }

        public ILoggerFactory LoggingFactory { get; }

        public SynchronizationContext UIThread { get; set; }

        Project IParentSingle<Project>.Child => Project;

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
                    var sceneCache = Path.Combine(sceneCacheDir, scene.SceneName + ".cache");
                    var cacheObj = new SceneCache(scene.SelectItems.Select(i => i.Name).ToArray())
                    {
                        Select = scene.SelectItem?.Name,
                        PreviewFrame = scene.PreviewFrame,
                        TimelineScale = scene.TimeLineZoom,
                        TimelineHorizonOffset = scene.TimeLineHorizonOffset,
                        TimelineVerticalOffset = scene.TimeLineVerticalOffset
                    };

                    await using var stream = new FileStream(sceneCache, FileMode.Create);
                    await JsonSerializer.SerializeAsync(stream, cacheObj, new JsonSerializerOptions
                    {
                        WriteIndented = true
                    });
                }
            }
        }

        public unsafe void RestoreAppConfig(Project project, string directory)
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
                    var sceneCache = Path.Combine(sceneCacheDir, scene.SceneName + ".cache");

                    if (!File.Exists(sceneCache)) continue;
                    Stream stream = null;
                    UnmanagedArray<byte> buffer = default;

                    try
                    {
                        stream = File.OpenRead(sceneCache);
                        buffer = new UnmanagedArray<byte>((int)stream.Length);
                        var span = buffer.AsSpan();
                        stream.Read(span);

                        var cacheObj = JsonSerializer.Deserialize<SceneCache>(span, new JsonSerializerOptions
                        {
                            WriteIndented = true
                        });

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
                    finally
                    {
                        stream?.Dispose();
                        buffer.Dispose();
                    }
                }
            }
        }
    }
}