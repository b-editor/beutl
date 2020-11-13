using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Runtime.Serialization;

using BEditor.Core.Interfaces;

namespace BEditor.Core.Data.ProjectData
{
    /// <summary>
    /// プロジェクトクラス
    /// </summary>
    [DataContract(Namespace = "")]
    public class Project : BasePropertyChanged, IExtensibleDataObject, IDisposable, IParent<Scene>
    {
        private Scene previewScene;

        /// <summary>
        /// <see cref="Project"/> クラスの新しいインスタンスを初期化します
        /// </summary>
        /// <param name="width">rootsceneの横幅</param>
        /// <param name="height">rootsceneの高さ</param>
        /// <param name="framerate">フレームレート</param>
        public Project(int width, int height, int framerate)
        {
            Framerate = framerate;
            SceneList.Add(new RootScene(width, height));
        }

        #region 保存するだけのプロパティ

        /// <summary>
        /// フレームレートを取得します
        /// </summary>
        [DataMember(Order = 0)]
        public int Framerate { get; private set; }

        /// <summary>
        /// サンプリングレートを取得します
        /// </summary>
        [DataMember(Order = 1)]
        public int Samplingrate { get; private set; }

        /// <summary>
        /// ファイル名を取得または設定します
        /// </summary>
        [DataMember(Order = 2)]
        public string Filename { get; set; }

        /// <summary>
        /// <see cref="Scene"/> のリストを取得します
        /// </summary>
        [DataMember(Order = 4)]
        public ObservableCollection<Scene> SceneList { get; private set; } = new ObservableCollection<Scene>();

        /// <summary>
        /// プレビュー中のシーンのインデックスを取得します
        /// </summary>
        [DataMember(Name = "PreviewScene", Order = 3)]
        public int PreviewSceneIndex { get; private set; }

        #endregion

        /// <summary>
        /// バックアップ先のファイル名を取得します
        /// </summary>
        public string GetBackUpName => Path.GetDirectoryName(Filename) + "\\" + Path.GetFileNameWithoutExtension(Filename) + ".backup";

        /// <summary>
        /// プレビューしている <see cref="Scene"/> を取得または設定します
        /// </summary>
        public Scene PreviewScene
        {
            get => previewScene ??= SceneList[PreviewSceneIndex];
            set
            {
                SetValue(value, ref previewScene, nameof(PreviewScene));
                PreviewSceneIndex = SceneList.IndexOf(value);
            }
        }

        /// <summary>
        /// オブジェクトが廃棄されているかを取得します
        /// </summary>
        public bool IsDisposed { get; private set; }
        /// <inheritdoc/>
        public void Dispose()
        {
            foreach (var scene in SceneList)
            {
                scene.RenderingContext.Dispose();
            }

            previewScene = null;
            SceneList = null;
            GC.Collect();
            IsDisposed = true;
        }

        /// <inheritdoc/>
        public ExtensionDataObject ExtensionData { get; set; }
        /// <inheritdoc/>
        IEnumerable<Scene> IParent<Scene>.Children => SceneList;



        /// <summary>
        /// プロジェクトを閉じたあとに発生します
        /// </summary>
        public static event EventHandler ProjectClosed;

        /// <summary>
        /// プロジェクトが開かれたあとに発生します
        /// </summary>
        public static event EventHandler ProjectOpend;

        #region Backup

        /// <summary>
        /// <see cref="Project"/> のバックアップを保存します
        /// </summary>
        public static void BackUp(Project project)
        {
            if (project.Filename == null)
            {
                //SaveFileDialogクラスのインスタンスを作成
                ISaveFileDialog sfd = Component.Funcs.SaveFileDialog();

                sfd.DefaultFileName = "新しいプロジェクト.bedit";


                sfd.Filters.Add((Properties.Resources.ProjectFile, "bedit"));

                //ダイアログを表示する
                if (sfd.ShowDialog())
                {
                    //OKボタンがクリックされたとき、選択されたファイル名を表示する
                    project.Filename = sfd.FileName;
                }
            }

            Serialize.SaveToFile(project, Component.Current.Path + "\\user\\backup\\" + Path.GetFileNameWithoutExtension(project.Filename) + ".backup");
        }

        #endregion


        #region Save
        /// <summary>
        /// <see cref="Project"/> を名前をつけて保存します
        /// </summary>
        /// <param name="project">保存するプロジェクト</param>
        public static bool Save(Project project)
        {
            if (project == null)
            {
                return false;
            }
            //SaveFileDialogクラスのインスタンスを作成
            ISaveFileDialog sfd = Component.Funcs.SaveFileDialog();
            if (project.Filename != null)
            {
                sfd.DefaultFileName = Path.GetFileName(project.Filename);
            }
            else
            {
                sfd.DefaultFileName = "新しいプロジェクト.bedit";
            }

            sfd.Filters.Add((Properties.Resources.ProjectFile, "bedit"));

            //ダイアログを表示する
            if (sfd.ShowDialog())
            {
                //OKボタンがクリックされたとき、選択されたファイル名を表示する
                project.Filename = sfd.FileName;
            }

            if (Serialize.SaveToFile(project, project.Filename))
            {
                Component.Current.Status = Status.Saved;
                return true;
            }
            return false;
        }
        /// <summary>
        /// <see cref="Project"/> を名前をつけて保存します
        /// </summary>
        /// <param name="project">保存するプロジェクト</param>
        /// <param name="path">保存するパス</param>
        /// <returns>保存に成功した場合は <see langword="true"/>、そうでない場合は <see langword="false"/> となります。<paramref name="project"/> が <see langword="null"/> の場合も <see langword="false"/> を返します</returns>
        public static bool Save(Project project, string path)
        {
            if (project == null)
            {
                return false;
            }

            if (Serialize.SaveToFile(project, path))
            {
                Component.Current.Status = Status.Saved;
                return true;
            }
            return false;
        }

        /// <summary>
        /// <see cref="Project"/> を上書き保存します
        /// </summary>
        /// <param name="project">保存するプロジェクト</param>
        /// <returns>保存に成功した場合は <see langword="true"/>、そうでない場合は <see langword="false"/> となります。<paramref name="project"/> が <see langword="null"/> の場合も <see langword="false"/> を返します</returns>
        public static bool SaveAs(Project project)
        {
            if (project == null)
            {
                return false;
            }

            if (project.Filename == null)
            {
                //SaveFileDialogクラスのインスタンスを作成
                ISaveFileDialog sfd = Component.Funcs.SaveFileDialog();

                sfd.DefaultFileName = "新しいプロジェクト.bedit";


                sfd.Filters.Add((Properties.Resources.ProjectFile, "bedit"));

                //ダイアログを表示する
                if (sfd.ShowDialog())
                {
                    //OKボタンがクリックされたとき、選択されたファイル名を表示する
                    project.Filename = sfd.FileName;
                }
                else
                {
                    return false;
                }
            }

            if (Serialize.SaveToFile(project, project.Filename))
            {
                Component.Current.Status = Status.Saved;
                return true;
            }
            return false;
        }


        #endregion


        #region Open
        /// <summary>
        /// <see cref="Project"/> を開き、編集可能な状態にします
        /// </summary>
        /// <param name="path">プロジェクトのパス</param>
        /// <returns>成功した場合は <see cref="Project"/> のインスタンス、そうでない場合は <see langword="null"/> を返します</returns>
        public static Project Open(string path)
        {
            var o = Serialize.LoadFromFile(path, typeof(Project));

            if (o != null)
            {
                var project = (Project)o;

                foreach (var scene in project.SceneList)
                {
                    scene.RenderingContext = Component.Funcs.CreateGraphicsContext(scene.Width, scene.Height);

                }

                Component.Current.Project = project;
                Component.Current.Status = Status.Edit;
                ProjectOpend?.Invoke(null, EventArgs.Empty);
                return project;
            }
            else
            {
                return null;
            }
        }
        #endregion


        #region Close
        /// <summary>
        /// 現在開かれているプロジェクトを閉じます
        /// </summary>
        public static void Close()
        {
            var project = Component.Current.Project;
            if (project is null) return;

            project.Dispose();

            Component.Current.Project = null;
            Component.Current.Status = Status.Idle;
            ProjectClosed?.Invoke(null, null);
        }
        #endregion


        #region Create
        /// <summary>
        /// プロジェクトを作成し、編集可能な状態にします
        /// </summary>
        /// <param name="width">rootsceneの横幅</param>
        /// <param name="height">rootsceneの高さ</param>
        /// <param name="framerate">フレームレート</param>
        /// <param name="path">保存するパス</param>
        /// <returns>作成された <see cref="Project"/> を返します</returns>
        public static Project Create(int width, int height, int framerate, string path)
        {
            var project = new Project(width, height, framerate)
            {
                Filename = path
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
