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
        private static readonly string colorsDir = System.IO.Path.Combine(AppContext.BaseDirectory, "user", "colors");
        private static readonly string logsDir = System.IO.Path.Combine(AppContext.BaseDirectory, "user", "logs");
        private static readonly string backupDir = System.IO.Path.Combine(AppContext.BaseDirectory, "user", "backup");
        private static readonly string pluginsDir = System.IO.Path.Combine(AppContext.BaseDirectory, "user", "plugins");
        private static readonly string errorlogFile = System.IO.Path.Combine(AppContext.BaseDirectory, "user", "logs", "errorlog.xml");
        private Project project;
        private Status status;

        /// <summary>
        /// 
        /// </summary>
        public static AppData Current { get; } = new();

        private AppData()
        {
            static void CreateIfNotExsits(string dir)
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }

            #region ディレクトリの作成

            CreateIfNotExsits(colorsDir);
            CreateIfNotExsits(logsDir);
            CreateIfNotExsits(backupDir);
            CreateIfNotExsits(pluginsDir);

            if (!File.Exists(errorlogFile))
            {
                XDocument XDoc = new XDocument(
                    new XDeclaration("1.0", "utf-8", "true"),
                    new XElement("Logs")
                );

                XDoc.Save(errorlogFile);
            }

            #endregion

            CommandManager.DidEvent += (_, _) => AppStatus = Status.Edit;
        }

        /// <inheritdoc/>
        public string Path => Core.Service.Services.Path;
        /// <inheritdoc/>
        public string[] Arguments { get; set; }
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
