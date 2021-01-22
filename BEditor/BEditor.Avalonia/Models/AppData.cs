using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Core;
using BEditor.Core.Command;
using BEditor.Core.Data;
using BEditor.Core.Plugin;
using BEditor.Core.Service;

namespace BEditor.Models
{
    public class AppData : BasePropertyChanged, IApplication
    {
        private static readonly PropertyChangedEventArgs projectArgs = new(nameof(Project));
        private static readonly PropertyChangedEventArgs statusArgs = new(nameof(AppStatus));
        private static readonly PropertyChangedEventArgs isPlayingArgs = new(nameof(IsNotPlaying));
        private Project? project;
        private Status status;
        private bool isplaying = true;

        public static AppData Current { get; } = new();

        private AppData()
        {
            CommandManager.Executed += (_, _) => AppStatus = Status.Edit;
        }

        public List<IPlugin>? LoadedPlugins { get; set; }
        public Project? Project
        {
            get => project;
            set => SetValue(value, ref project, projectArgs);
        }
        public Status AppStatus
        {
            get => status;
            set => SetValue(value, ref status, statusArgs);
        }
        public bool IsNotPlaying
        {
            get => isplaying;
            set => SetValue(value, ref isplaying, isPlayingArgs);
        }
    }
}
