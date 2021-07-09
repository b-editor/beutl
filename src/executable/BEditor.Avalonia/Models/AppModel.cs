using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;

using BEditor.Audio;
using BEditor.Command;
using BEditor.Data;
using BEditor.Extensions;
using BEditor.Models.Authentication;
using BEditor.Packaging;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Serilog;
using Serilog.Formatting.Json;

#nullable disable

namespace BEditor.Models
{
    public sealed class AppModel : BasePropertyChanged, IApplication
    {
        private static readonly PropertyChangedEventArgs _projectArgs = new(nameof(Project));
        private static readonly PropertyChangedEventArgs _statusArgs = new(nameof(AppStatus));
        private static readonly PropertyChangedEventArgs _isPlayingArgs = new(nameof(IsNotPlaying));
        private static readonly PropertyChangedEventArgs _userArgs = new(nameof(User));
        private Project _project;
        private Status _status;
        private bool _isplaying = true;
        private IServiceProvider _serviceProvider;
        private AuthenticationLink _user;

        private AppModel()
        {
            CommandManager.Default.Executed += async (_, _) =>
            {
                if (Project is not null)
                {
                    await Project.PreviewUpdateAsync(ApplyType.Edit);
                }
                AppStatus = Status.Edit;
            };

            Log.Logger = new LoggerConfiguration()
                .Enrich.FromLogContext()
                .WriteTo.File(new JsonFormatter(), Path.Combine(ServicesLocator.GetUserFolder(), "log.json"))
                .CreateLogger();

            LoggingFactory = LoggerFactory.Create(builder =>
            {
                builder
                    .AddDebug()
                    .AddSerilog(Log.Logger);
            });

            // DIの設定
            Services = new ServiceCollection()
                .AddSingleton<IAuthenticationProvider, MockAuthenticationProvider>()
                .AddSingleton<IRemotePackageProvider, MockPackageUploader>()
                .AddSingleton<ITopLevel>(_ => this)
                .AddSingleton<IApplication>(_ => this)
                .AddSingleton(_ => FileDialog)
                .AddSingleton(_ => Message)
                .AddSingleton(_ => LoggingFactory)
                .AddSingleton<Microsoft.Extensions.Logging.ILogger>(_ => LoggingFactory.CreateLogger<IApplication>())
                .AddSingleton<HttpClient>();
        }

        public static AppModel Current { get; } = new();

        public Project Project
        {
            get => _project;
            set => SetAndRaise(value, ref _project, _projectArgs);
        }

        public Status AppStatus
        {
            get => _status;
            set => SetAndRaise(value, ref _status, _statusArgs);
        }

        public bool IsNotPlaying
        {
            get => _isplaying;
            set => SetAndRaise(value, ref _isplaying, _isPlayingArgs);
        }

        public IServiceCollection Services { get; }

        public IServiceProvider ServiceProvider
        {
            get => _serviceProvider ??= Services.BuildServiceProvider();
            set => _serviceProvider = value;
        }

        public IMessage Message { get; } = new MessageService();

        public IFileDialogService FileDialog { get; } = new FileDialogService();

        public ILoggerFactory LoggingFactory { get; }

        public AuthenticationLink User
        {
            get => _user;
            set => SetAndRaise(value, ref _user, _userArgs);
        }

        public SynchronizationContext UIThread { get; set; }

        Project IParentSingle<Project>.Child => Project;

        public AudioContext AudioContext { get; set; }

        public event EventHandler<ProjectOpenedEventArgs> ProjectOpened;
        public event EventHandler Exit;

        public void RaiseExit()
        {
            Exit?.Invoke(this, EventArgs.Empty);
        }

        public void RaiseProjectOpened(Project project)
        {
            ProjectOpened?.Invoke(this, new(project));
        }

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
                var projConfig = new ProjectConfig
                {
                    BackgroundType = ProjectConfig.GetBackgroundType(project),
                };

                await using var stream = new FileStream(Path.Combine(directory, ".config"), FileMode.Create);
                await JsonSerializer.SerializeAsync(stream, projConfig, PackageFile._serializerOptions);
            }

            {
                var sceneCacheDir = Path.Combine(cache, "scene");
                IfNotExistCreateDir(sceneCacheDir);

                foreach (var scene in project.SceneList)
                {
                    var sceneCache = Path.Combine(sceneCacheDir, scene.SceneName + ".cache");
                    var cacheObj = new SceneCache
                    {
                        Select = scene.SelectItem?.Name,
                        PreviewFrame = scene.PreviewFrame,
                        TimelineScale = scene.TimeLineZoom,
                        TimelineHorizonOffset = scene.TimeLineHorizonOffset,
                        TimelineVerticalOffset = scene.TimeLineVerticalOffset
                    };

                    await using var stream = new FileStream(sceneCache, FileMode.Create);
                    await JsonSerializer.SerializeAsync(stream, cacheObj, PackageFile._serializerOptions);
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
                var file = Path.Combine(directory, ".config");
                if (!File.Exists(file))
                {
                    ProjectConfig.SetBackgroundType(project, ViewModels.ConfigurationViewModel.BackgroundType.Transparent);
                }
                else
                {
                    using var reader = new StreamReader(file);
                    var projConfig = JsonSerializer.Deserialize<ProjectConfig>(reader.ReadToEnd(), PackageFile._serializerOptions);

                    ProjectConfig.SetBackgroundType(project, projConfig.BackgroundType);
                }
            }

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

                        var cacheObj = JsonSerializer.Deserialize<SceneCache>(span, PackageFile._serializerOptions);

                        if (cacheObj is not null)
                        {
                            scene.SelectItem = scene[cacheObj.Select];
                            scene.PreviewFrame = cacheObj.PreviewFrame;
                            scene.TimeLineZoom = cacheObj.TimelineScale;
                            scene.TimeLineHorizonOffset = cacheObj.TimelineHorizonOffset;
                            scene.TimeLineVerticalOffset = cacheObj.TimelineVerticalOffset;
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