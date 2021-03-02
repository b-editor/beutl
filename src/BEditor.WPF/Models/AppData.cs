using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

using BEditor;
using BEditor.Command;
using BEditor.Data;
using BEditor.Models.Services;
using BEditor.Plugin;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;

using NLog.Extensions.Logging;

using NLogLevel = NLog.LogLevel;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;
using NLog.Layouts;
using System.Reactive.Disposables;
using Reactive.Bindings;
using BEditor.ViewModels.ToolControl;
using System.Threading;
using System.Windows.Threading;
using System.Windows;

namespace BEditor.Models
{
    public class AppData : BasePropertyChanged, IApplication
    {
        private static readonly PropertyChangedEventArgs _ProjectArgs = new(nameof(Project));
        private static readonly PropertyChangedEventArgs _StatusArgs = new(nameof(AppStatus));
        private static readonly PropertyChangedEventArgs _IsPlayingArgs = new(nameof(IsNotPlaying));
        private Project? _Project;
        private Status _Status;
        private bool _Isplaying = true;

        public static AppData Current { get; } = new();

        private AppData()
        {
            CommandManager.Executed += (_, _) => AppStatus = Status.Edit;


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


            // DIの設定
            Services = new ServiceCollection()
                .AddSingleton<IFileDialogService>(p => new FileDialogService())
                .AddSingleton<IMessage>(p => new MessageService())
                .AddSingleton(_ => LoggerFactory.Create(builder =>
                {
                    builder
                        .AddFilter("Microsoft", LogLevel.Warning)
                        .AddFilter("System", LogLevel.Warning)
                        .AddFilter("BEditor.Data", LogLevel.Warning)
                        .AddFilter("BEditor.Graphics", LogLevel.Warning)
                        .AddFilter("BEditor.Views", LogLevel.Debug)
                        .AddFilter("BEditor.ViewModels", LogLevel.Debug)
                        .AddFilter("BEditor.Models", LogLevel.Debug)
                        .AddFilter("LoggingConsoleApp.Program", LogLevel.Debug)
                        .AddConsole()
                        .AddNLog()
                        .AddProvider(new BEditorLoggerProvider());
                }));

            ServiceProvider = Services.BuildServiceProvider();

            LoggingFactory = ServiceProvider.GetService<ILoggerFactory>()!;
            Message = ServiceProvider.GetService<IMessage>()!;
            FileDialog = ServiceProvider.GetService<IFileDialogService>()!;
        }

        public Project? Project
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
        public IServiceProvider ServiceProvider { get; }
        public IMessage Message { get; }
        public IFileDialogService FileDialog { get; }
        public ILoggerFactory LoggingFactory { get; }
    }

    public class BEditorLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName)
        {
            var logger = new BEditorLogger(categoryName);

            LogViewModel.Current.Loggers.Add(logger);

            return logger;
        }

        public event EventHandler? Disposed;

        public void Dispose()
        {
            Disposed?.Invoke(this, EventArgs.Empty);
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
            str.Append("[");
            str.Append(Enum.GetName(typeof(LogLevel), logLevel));
            str.Append("] ");

            str.Append(formatter?.Invoke(state, exception) ?? "");
            str.Append(exception?.Message ?? "");
            str.Append(exception?.StackTrace ?? "");
            str.Append("\n");

            Text.Value += str.ToString();
        }
    }
}
