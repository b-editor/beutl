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
        private static readonly PropertyChangedEventArgs projectArgs = new(nameof(Project));
        private static readonly PropertyChangedEventArgs statusArgs = new(nameof(AppStatus));
        private Project project;
        private Status status;

        /// <summary>
        /// 
        /// </summary>
        public static AppData Current { get; } = new();

        private AppData()
        {
            CommandManager.Executed += (_, _) => AppStatus = Status.Edit;
        }

        /// <inheritdoc/>
        public string Path => Core.Service.Services.Path;
        /// <inheritdoc/>
        public string[] Arguments => Environment.GetCommandLineArgs();
        /// <inheritdoc/>
        public List<IPlugin> LoadedPlugins { get; set; }
        /// <inheritdoc/>
        public Project Project
        {
            get => project;
            set => SetValue(value, ref project, projectArgs);
        }
        /// <inheritdoc/>
        public Status AppStatus
        {
            get => status;
            set => SetValue(value, ref status, statusArgs);
        }
    }
}
