using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

using BEditor.Core;
using BEditor.Core.Data;
using BEditor.Core.Plugin;
using BEditor.Core.Service;

using Reactive.Bindings;

namespace BEditor.Models
{
    public class AppData : BasePropertyChanged, IApplication
    {
        static AppData()
        {
            #region Xmlの作成
            var path = AppContext.BaseDirectory;
            if (!Directory.Exists(path + "\\user\\colors"))
            {
                Directory.CreateDirectory(path + "\\user\\colors");
            }

            if (!Directory.Exists(path + "\\user\\logs"))
            {
                Directory.CreateDirectory(path + "\\user\\logs");
            }

            if (!Directory.Exists(path + "\\user\\backup"))
            {
                Directory.CreateDirectory(path + "\\user\\backup");
            }

            if (!Directory.Exists(path + "\\user\\plugins"))
            {
                Directory.CreateDirectory(path + "\\user\\plugins");
            }

            if (!File.Exists(path + "\\user\\logs\\errorlog.xml"))
            {
                XDocument XDoc = new XDocument(
                    new XDeclaration("1.0", "utf-8", "true"),
                    new XElement("Logs")
                );

                XDoc.Save(path + "\\user\\logs\\errorlog.xml");
            }

            #endregion
        }
        private AppData()
        {
            //Project.Value = new Project(1920, 1080, 30, 0, this);
        }

        public static AppData Current { get; } = new();
        public Status AppStatus { get; set; }
        public List<IPlugin> LoadedPlugins { get; }
        public ReactiveProperty<Project> Project { get; } = new();
    }
}
