using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Core.Graphics;
using BEditor.Core.Interfaces;

namespace BEditor.Core.Data.ProjectData
{
    /// <summary>
    /// プロジェクトクラス
    /// </summary>
    [DataContract(Namespace = "")]
    public class Project : BasePropertyChanged, IExtensibleDataObject, IDisposable, IParent<Scene>, IChild<IApplication>, INotifyPropertyChanged
    {
        private static readonly PropertyChangedEventArgs previreSceneArgs = new(nameof(PreviewScene));
        private Scene previewScene;
        private ObservableCollection<Scene> sceneList = new ObservableCollection<Scene>();
        private IApplication parent;

        /// <summary>
        /// <see cref="Project"/> Initialize a new instance of the class.
        /// </summary>
        public Project(int width, int height, int framerate, int samplingrate = 0, IApplication app = null)
        {
            Parent = app;
            Framerate = framerate;
            Samplingrate = samplingrate;
            SceneList.Add(new(width, height)
            {
                Parent = this
            });
        }
        /// <summary>
        /// <see cref="Project"/> Initialize a new instance of the class.
        /// </summary>
        public Project(string file, IApplication app = null)
        {
            var o = Serialize.LoadFromFile(file, typeof(Project));

            if (o != null)
            {
                var project = (Project)o;

                foreach (var scene in project.SceneList)
                {
                    scene.GraphicsContext = new GraphicsContext(scene.Width, scene.Height);

                }

                project.CopyTo(this);
                Parent = app;
            }
            else
            {
                throw new Exception();
            }
        }

        #region 保存するだけのプロパティ

        /// <summary>
        /// Get the framerate for this <see cref="Project"/>.
        /// </summary>
        [DataMember(Order = 0)]
        public int Framerate { get; private set; }

        /// <summary>
        /// Get the sampling rate for this <see cref="Project"/>.
        /// </summary>
        [DataMember(Order = 1)]
        public int Samplingrate { get; private set; }

        /// <summary>
        /// Get or set the file name of this <see cref="Project"/>.
        /// </summary>
        [DataMember(Order = 2)]
        public string Filename { get; set; }

        /// <summary>
        /// Get a list of Scenes in this <see cref="Project"/>.
        /// </summary>
        [DataMember(Order = 4)]
        public ObservableCollection<Scene> SceneList
        {
            get => sceneList;
            private set
            {
                sceneList = value;
                Parallel.ForEach(value, scene => scene.Parent = this);
            }
        }

        /// <summary>
        /// Get an index of the <see cref="SceneList"/> being previewed.
        /// </summary>
        [DataMember(Name = "PreviewScene", Order = 3)]
        public int PreviewSceneIndex { get; private set; }

        #endregion

        /// <summary>
        /// Get or set the <see cref="Scene"/> that is being previewed
        /// </summary>
        public Scene PreviewScene
        {
            get => previewScene ??= SceneList[PreviewSceneIndex];
            set
            {
                SetValue(value, ref previewScene, previreSceneArgs);
                PreviewSceneIndex = SceneList.IndexOf(value);
            }
        }
        /// <summary>
        /// Get whether an object has been discarded.
        /// </summary>
        public bool IsDisposed { get; private set; }
        /// <inheritdoc/>
        public ExtensionDataObject ExtensionData { get; set; }
        /// <inheritdoc/>
        public IEnumerable<Scene> Children => SceneList;
        /// <inheritdoc/>
        public IApplication Parent
        {
            get => parent;
            init => parent = value;
        }

        //TODO : xml英語ここまで
        public event EventHandler<ProjectSavedEventArgs> Saved;

        /// <summary>
        /// 
        /// </summary>
        public void BackUp()
        {
            if (Filename == null)
            {
                //SaveFileDialogクラスのインスタンスを作成
                ISaveFileDialog sfd = Component.Funcs.SaveFileDialog();

                sfd.DefaultFileName = "新しいプロジェクト.bedit";


                sfd.Filters.Add((Properties.Resources.ProjectFile, "bedit"));

                //ダイアログを表示する
                if (sfd.ShowDialog())
                {
                    //OKボタンがクリックされたとき、選択されたファイル名を表示する
                    Filename = sfd.FileName;
                }
            }

            Serialize.SaveToFile(this, $"{Component.Path}\\user\\backup\\" + Path.GetFileNameWithoutExtension(Filename) + ".backup");
        }
        /// <inheritdoc/>
        public void Dispose()
        {
            foreach (var scene in SceneList)
            {
                scene.GraphicsContext.Dispose();
            }

            previewScene = null;
            SceneList = null;
            GC.Collect();
            IsDisposed = true;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public bool Save()
        {
            //SaveFileDialogクラスのインスタンスを作成
            ISaveFileDialog sfd = Component.Funcs.SaveFileDialog();
            if (Filename != null)
            {
                sfd.DefaultFileName = Path.GetFileName(Filename);
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
                Filename = sfd.FileName;
            }

            if (Serialize.SaveToFile(this, Filename))
            {
                Saved?.Invoke(this, new(SaveType.Save));
                return true;
            }
            return false;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <param name="file"></param>
        /// <returns></returns>
        public bool Save(string file)
        {
            Filename = file;
            if (Serialize.SaveToFile(this, file))
            {
                Saved?.Invoke(this, new(SaveType.Save));
                return true;
            }
            return false;
        }
        /// <summary>
        /// 
        /// </summary>
        /// <returns></returns>
        public bool SaveAs()
        {
            if (Filename == null)
            {
                //SaveFileDialogクラスのインスタンスを作成
                ISaveFileDialog sfd = Component.Funcs.SaveFileDialog();

                sfd.DefaultFileName = "新しいプロジェクト.bedit";


                sfd.Filters.Add((Properties.Resources.ProjectFile, "bedit"));

                //ダイアログを表示する
                if (sfd.ShowDialog())
                {
                    //OKボタンがクリックされたとき、選択されたファイル名を表示する
                    Filename = sfd.FileName;
                }
                else
                {
                    return false;
                }
            }

            if (Serialize.SaveToFile(this, Filename))
            {
                Saved?.Invoke(this, new(SaveType.SaveAs));
                return true;
            }
            return false;
        }

        private void CopyTo(Project project)
        {
            project.Filename = Filename;
            project.Framerate = Framerate;
            project.parent = parent;
            project.PreviewScene = PreviewScene;
            project.PreviewSceneIndex = PreviewSceneIndex;
            project.Samplingrate = Samplingrate;
            project.SceneList = SceneList;
        }
    }

    public enum SaveType
    {
        Save,
        SaveAs
    }
    public class ProjectSavedEventArgs : EventArgs
    {
        public ProjectSavedEventArgs(SaveType type) => Type = type;

        public SaveType Type { get; }
    }
}
