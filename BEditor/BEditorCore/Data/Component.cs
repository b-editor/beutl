using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

using BEditorCore.Data.EffectData;
using BEditorCore.Data.ObjectData;
using BEditorCore.Data.ProjectData;
using BEditorCore.Data.PropertyData;
using BEditorCore.Data.PropertyData.EasingSetting;
using BEditorCore.Interfaces;
using BEditorCore.Media;
using BEditorCore.Plugin;
using BEditorCore.Renderer;

namespace BEditorCore.Data {
    public class Component : BasePropertyChanged {
        private Project project;
        private Status status;

        public static Component Current { get; } = new Component();

        private Component() {
            #region Xmlの作成

            if (!Directory.Exists(Path + "\\user\\colors")) {
                Directory.CreateDirectory(Path + "\\user\\colors");
            }

            if (!Directory.Exists(Path + "\\user\\logs")) {
                Directory.CreateDirectory(Path + "\\user\\logs");
            }

            if (!Directory.Exists(Path + "\\user\\backup")) {
                Directory.CreateDirectory(Path + "\\user\\backup");
            }

            if (!Directory.Exists(Path + "\\user\\plugins")) {
                Directory.CreateDirectory(Path + "\\user\\plugins");
            }

            if (!File.Exists(Path + "\\user\\logs\\errorlog.xml")) {
                XDocument XDoc = new XDocument(
                    new XDeclaration("1.0", "utf-8", "true"),
                    new XElement("Logs")
                );

                XDoc.Save(Path + "\\user\\logs\\errorlog.xml");
            }

            #endregion
        }

        /// <summary>
        /// Exeファイルが有るフォルダ
        /// \\はつかない
        /// </summary>
        public string Path { get; } = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

        public string[] Arguments { get; set; }

        /// <summary>
        /// 読み込まれたプラグイン
        /// </summary>
        public List<IPlugin> LoadedPlugins { get; } = new List<IPlugin>();

        /// <summary>
        /// 開かれているプロジェクト
        /// </summary>
        public Project Project { get => project; set => SetValue(value, ref project, nameof(Project)); }

        /// <summary>
        /// アプリのステータス
        /// </summary>
        public Status Status { get => status; set => SetValue(value, ref status, nameof(Status)); }

        public static class Funcs {
            private static Func<int, int, BaseRenderingContext> createRenderingContext;

            public static Func<int, int, Renderer.BaseRenderingContext> CreateRenderingContext {
                get => createRenderingContext;
                set {
                    createRenderingContext = value;
                    ImageHelper.renderer = createRenderingContext(1, 1);
                }
            }
            public static Func<ISaveFileDialog> SaveFileDialog { get; set; }
        }
        public static class Settings {
            public static Func<bool> AutoBackUp { get; set; }
            public static Func<bool> EnableErrorLog { get; set; }
        }
    }

    public enum Status {
        Idle,
        Edit,
        Saved,
        Playing,
        Pause,
        Output
    }
}
