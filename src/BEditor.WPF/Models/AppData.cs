using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

using BEditor.Core;
using BEditor.Core.Command;
using BEditor.Core.Data;
using BEditor.Core.Plugin;
using BEditor.Core.Service;

namespace BEditor.Models
{
    public class AppData : BasePropertyChanged, IApplication, INotifyPropertyChanged
    {
        private static readonly PropertyChangedEventArgs _ProjectArgs = new(nameof(Project));
        private static readonly PropertyChangedEventArgs _StatusArgs = new(nameof(AppStatus));
        private static readonly PropertyChangedEventArgs _IsPlayingArgs = new(nameof(IsNotPlaying));
        private Project? _Project;
        private Status _Status;
        private bool _Isplaying = true;

        /// <summary>
        /// 
        /// </summary>
        public static AppData Current { get; } = new();

        private AppData()
        {
            CommandManager.Executed += (_, _) => AppStatus = Status.Edit;
        }

        /// <inheritdoc/>
        public string[] Arguments => Environment.GetCommandLineArgs();
        /// <inheritdoc/>
        public List<IPlugin>? LoadedPlugins { get; set; }
        /// <inheritdoc/>
        public Project? Project
        {
            get => _Project;
            set => SetValue(value, ref _Project, _ProjectArgs);
        }
        /// <inheritdoc/>
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
    }
}
