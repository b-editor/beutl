using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

using BEditor.Core.Data.ProjectData;
using BEditor.Core.Interfaces;
using BEditor.Core.Media;
using BEditor.Core.Plugin;
using BEditor.Core.Renderer;

namespace BEditor.Core.Data {
    /// <summary>
    /// シングルトンで現在のプロジェクトやステータスなどを取得できるクラスを表します
    /// </summary>
    public sealed class Component : BasePropertyChanged {
        private Project project;
        private Status status;

        /// <summary>
        /// 
        /// </summary>
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
        /// </summary>
        public string Path { get; } = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);

        /// <summary>
        /// コマンドライン引数を取得します
        /// </summary>
        public string[] Arguments { get; set; }

        /// <summary>
        /// 読み込まれたプラグインを取得します
        /// </summary>
        public List<IPlugin> LoadedPlugins { get; } = new List<IPlugin>();

        /// <summary>
        /// 開かれている <see cref="ProjectData.Project"/> を取得または設定します
        /// </summary>
        public Project Project { get => project; set => SetValue(value, ref project, nameof(Project)); }

        /// <summary>
        /// 現在のステータスを取得または設定します
        /// </summary>
        public Status Status { get => status; set => SetValue(value, ref status, nameof(Status)); }

        /// <summary>
        /// プラットフォームに依存する関数を共有するフィールド
        /// </summary>
        public static class Funcs {
            private static Func<int, int, BaseRenderingContext> createRenderingContext;

            /// <summary>
            /// レンダリングコンテキストを作成する関数を取得または設定します
            /// </summary>
            public static Func<int, int, BaseRenderingContext> CreateRenderingContext {
                get => createRenderingContext;
                set {
                    createRenderingContext = value;
                    //ImageHelper.renderer = createRenderingContext(1, 1);
                }
            }
            /// <summary>
            /// ファイルを保存するダイアログを作成する関数を取得または設定します
            /// </summary>
            public static Func<ISaveFileDialog> SaveFileDialog { get; set; }
        }
    }

    /// <summary>
    /// アプリケーションのステータスを表します
    /// </summary>
    public enum Status {
        /// <summary>
        /// 作業をしていない状態を表します
        /// </summary>
        Idle,
        /// <summary>
        /// 編集中であることを表します
        /// </summary>
        Edit,
        /// <summary>
        /// 保存直後であることを表します
        /// </summary>
        Saved,
        /// <summary>
        /// プレビュー再生中であることを表します
        /// </summary>
        Playing,
        /// <summary>
        /// 一時停止している状態を表します
        /// </summary>
        Pause,
        /// <summary>
        /// 出力中であることを表します
        /// </summary>
        Output
    }
}
