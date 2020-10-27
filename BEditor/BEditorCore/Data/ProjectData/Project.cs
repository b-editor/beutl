using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Serialization;

using BEditorCore.Interfaces;

namespace BEditorCore.Data.ProjectData {
    [DataContract(Namespace = "")]
    public class Project : BasePropertyChanged, IExtensibleDataObject, IDisposable {

        public Project(int width, int height, int framerate) {
            FrameRate = framerate;
            SceneList.Add(new RootScene(width, height));
        }

        #region 保存するだけのプロパティ

        [DataMember(Name = "Framerate", Order = 0)]
        public int FrameRate { get; private set; }

        [DataMember(Name = "Samplingrate", Order = 1)]
        public int SamplingRate { get; private set; }

        [DataMember(Name = "FilePath", Order = 2)]
        public string FoldPath { get; set; }

        [DataMember(Name = "SceneList", Order = 4)]
        public ObservableCollection<Scene> SceneList { get; set; } = new ObservableCollection<Scene>();

        [DataMember(Name = "PreviewScene", Order = 3)]
        public int PreviewSceneIndex { get; set; }

        #endregion

        public string GetBackUpName => Path.GetDirectoryName(FoldPath) + "\\" + Path.GetFileNameWithoutExtension(FoldPath) + ".backup";


        private Scene previewSceneProperty;
        public Scene PreviewScene {
            get => previewSceneProperty ??= SceneList[PreviewSceneIndex];
            set {
                SetValue(value, ref previewSceneProperty, nameof(PreviewScene));
                PreviewSceneIndex = SceneList.IndexOf(value);
            }
        }


        public bool IsDisposed { get; private set; }
        /// <summary>
        /// OpenGLなどのFBOを廃棄
        /// </summary>
        public void Dispose() {
            Disposing?.Invoke(this, EventArgs.Empty);

            foreach (var scene in SceneList) {
                scene.RenderingContext.Dispose();
            }

            IsDisposed = true;
            Disposed?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler Disposed;
        public event EventHandler Disposing;
        public ExtensionDataObject ExtensionData { get; set; }



        /// <summary>
        /// プロジェクトを閉じたあとに発生
        /// </summary>
        public static event EventHandler ProjectClosed;

        /// <summary>
        /// プロジェクト開かれたあとに発生
        /// </summary>
        public static event EventHandler ProjectOpend;

        #region Backup

        /// <summary>
        /// プロジェクトのバックアップを作成します
        /// </summary>
        public static void BackUp(Project project) {
            if (project.FoldPath == null) {
                //SaveFileDialogクラスのインスタンスを作成
                ISaveFileDialog sfd = Component.Funcs.SaveFileDialog();

                sfd.DefaultFileName = "新しいプロジェクト.bedit";


                sfd.Filters.Add((Properties.Resources.ProjectFile, "bedit"));

                //ダイアログを表示する
                if (sfd.ShowDialog()) {
                    //OKボタンがクリックされたとき、選択されたファイル名を表示する
                    project.FoldPath = sfd.FileName;
                }
            }

            Serialize.SaveToFile(project, Component.Current.Path + "\\user\\backup\\" + Path.GetFileNameWithoutExtension(project.FoldPath) + ".backup");
        }

        #endregion


        #region Save
        /// <summary>
        /// プロジェクトを名前をつけて保存します
        /// </summary>
        /// <param name="project">保存するプロジェクト</param>
        public static bool Save(Project project) {
            if (project == null) {
                return false;
            }
            //SaveFileDialogクラスのインスタンスを作成
            ISaveFileDialog sfd = Component.Funcs.SaveFileDialog();
            if (project.FoldPath != null) {
                sfd.DefaultFileName = Path.GetFileName(project.FoldPath);
            }
            else {
                sfd.DefaultFileName = "新しいプロジェクト.bedit";
            }

            sfd.Filters.Add((Properties.Resources.ProjectFile, "bedit"));

            //ダイアログを表示する
            if (sfd.ShowDialog()) {
                //OKボタンがクリックされたとき、選択されたファイル名を表示する
                project.FoldPath = sfd.FileName;
            }

            if (Serialize.SaveToFile(project, project.FoldPath)) {
                Component.Current.Status = Status.Saved;
                return true;
            }
            return false;
        }
        /// <summary>
        /// プロジェクトを名前をつけて保存します
        /// </summary>
        /// <param name="project">保存するプロジェクト</param>
        /// <param name="path">保存するパス</param>
        public static bool Save(Project project, string path) {
            if (project == null) {
                return false;
            }

            if (Serialize.SaveToFile(project, path)) {
                Component.Current.Status = Status.Saved;
                return true;
            }
            return false;
        }

        /// <summary>
        /// プロジェクトを上書き保存します
        /// </summary>
        /// <param name="project">保存するプロジェクト</param>
        public static bool SaveAs(Project project) {
            if (project == null) {
                return false;
            }

            if (project.FoldPath == null) {
                //SaveFileDialogクラスのインスタンスを作成
                ISaveFileDialog sfd = Component.Funcs.SaveFileDialog();

                sfd.DefaultFileName = "新しいプロジェクト.bedit";


                sfd.Filters.Add((Properties.Resources.ProjectFile, "bedit"));

                //ダイアログを表示する
                if (sfd.ShowDialog()) {
                    //OKボタンがクリックされたとき、選択されたファイル名を表示する
                    project.FoldPath = sfd.FileName;
                }
                else {
                    return false;
                }
            }

            if (Serialize.SaveToFile(project, project.FoldPath)) {
                Component.Current.Status = Status.Saved;
                return true;
            }
            return false;
        }


        #endregion


        #region Open
        /// <summary>
        /// プロジェクトを開きます
        /// </summary>
        /// <param name="path">プロジェクトのパス</param>
        /// <returns>プロジェクト 失敗した場合null</returns>
        public static Project Open(string path) {
            var o = Serialize.LoadFromFile(path, typeof(Project));

            if (o != null) {
                var project = (Project)o;

                foreach (var scene in project.SceneList) {
                    scene.RenderingContext = Component.Funcs.CreateRenderingContext(scene.Width, scene.Height);

                }

                Component.Current.Project = project;
                Component.Current.Status = Status.Edit;
                ProjectOpend?.Invoke(null, EventArgs.Empty);
                return project;
            }
            else {

                return null;
            }
        }
        #endregion


        #region Close
        /// <summary>
        /// 現在開かれているプロジェクトを閉じます
        /// </summary>
        public static void Close(Project project) {
            if (project == null) {
                return;
            }

            for (int i = 0; i < project.SceneList.Count; i++) {
                Scene scene = project.SceneList[i];

                scene.RenderingContext.Dispose();
            }

            Component.Current.Project = null;
            Component.Current.Status = Status.Idle;
            ProjectClosed?.Invoke(null, null);
        }
        #endregion


        #region Create
        /// <summary>
        /// プロジェクトを作成します
        /// </summary>
        /// <param name="width">rootsceneの横幅</param>
        /// <param name="height">rootsceneの高さ</param>
        /// <param name="framerate">フレームレート</param>
        /// <param name="path">保存するパス</param>
        public static Project Create(int width, int height, int framerate, string path) {
            var project = new Project(width, height, framerate) {
                FoldPath = path
            };

            project.PreviewSceneIndex = 0;
            Component.Current.Project = project;
            ProjectOpend?.Invoke(null, null);

            SaveAs(project);
            Component.Current.Status = Status.Edit;
            return project;
        }
        #endregion


    }
}
