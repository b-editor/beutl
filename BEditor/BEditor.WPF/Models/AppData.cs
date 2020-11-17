using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

using BEditor.Core.Data;
using BEditor.Core.Data.ProjectData;
using BEditor.Core.Plugin;

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
            #region Xmlの作成

            if (!Directory.Exists(Path + "\\user\\colors"))
            {
                Directory.CreateDirectory(Path + "\\user\\colors");
            }

            if (!Directory.Exists(Path + "\\user\\logs"))
            {
                Directory.CreateDirectory(Path + "\\user\\logs");
            }

            if (!Directory.Exists(Path + "\\user\\backup"))
            {
                Directory.CreateDirectory(Path + "\\user\\backup");
            }

            if (!Directory.Exists(Path + "\\user\\plugins"))
            {
                Directory.CreateDirectory(Path + "\\user\\plugins");
            }

            if (!File.Exists(Path + "\\user\\logs\\errorlog.xml"))
            {
                XDocument XDoc = new XDocument(
                    new XDeclaration("1.0", "utf-8", "true"),
                    new XElement("Logs")
                );

                XDoc.Save(Path + "\\user\\logs\\errorlog.xml");
            }

            #endregion

            UndoRedoManager.DidEvent += (_, _) => AppStatus = Status.Edit;
        }

        /// <inheritdoc/>
        public string Path { get; } = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
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
