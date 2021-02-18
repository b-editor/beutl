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
using BEditor.Plugin;
using BEditor.Models.Services;

using Microsoft.Extensions.DependencyInjection;

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

            Services = new ServiceCollection()
                .AddSingleton<IFileDialogService>(p => new FileDialogService())
                .AddSingleton<IMessage>(p => new MessageService());
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
    }
}
