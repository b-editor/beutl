using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Core.Graphics;
using BEditor.Core.Service;
using BEditor.Core.Properties;
using BEditor.Core.Data.Control;
using BEditor.Core.Data.Primitive.Objects;

namespace BEditor.Core.Data
{
    /// <summary>
    /// Represents the project to be used in editing.
    /// </summary>
    [DataContract(Namespace = "")]
    public class Project : BasePropertyChanged, IExtensibleDataObject, IDisposable, IParent<Scene>, IChild<IApplication>
    {
        #region Fields

        private static readonly PropertyChangedEventArgs previreSceneArgs = new(nameof(PreviewScene));
        private Scene previewScene;
        private ObservableCollection<Scene> sceneList = new ObservableCollection<Scene>();
        private IApplication parent;

        #endregion


        #region Contructor

        /// <summary>
        /// <see cref="Project"/> Initialize a new instance of the class.
        /// </summary>
        public Project(int width, int height, int framerate, int samplingrate = 0, IApplication app = null)
        {
            Parent = app;
            Framerate = framerate;
            Samplingrate = samplingrate;
            SceneList.Add(new RootScene(width, height)
            {
                Parent = this
            });
        }
        /// <summary>
        /// <see cref="Project"/> Initialize a new instance of the class.
        /// </summary>
        public Project(string file, IApplication app = null)
        {
            var o = Serialize.LoadFromFile<Project>(file);

            if (o != null)
            {
                var project = o;

                foreach (var scene in project.SceneList)
                {
                    scene.GraphicsContext = new GraphicsContext(scene.Width, scene.Height);
                    scene.PropertyLoaded();
                }

                project.CopyTo(this);
                Parent = app;
            }
            else
            {
                throw new Exception();
            }
        }

        #endregion


        /// <summary>
        /// Occurs after saving this <see cref="Project"/>.
        /// </summary>
        public event EventHandler<ProjectSavedEventArgs> Saved;


        #region Properties

        #region 保存用

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
                Parallel.ForEach(value, scene =>
                {
                    scene.Parent = this;
                    scene.PropertyLoaded();
                });
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

        #endregion


        #region Methods

        /// <summary>
        /// Create a backup of this <see cref="Project"/>.
        /// </summary>
        public void BackUp()
        {
            if (Filename == null)
            {
                var record = new SaveFileRecord
                {
                    DefaultFileName = "新しいプロジェクト.bedit",
                    Filters =
                    {
                        new(Resources.ProjectFile, "bedit")
                    }
                };

                //ダイアログを表示する
                if (Services.FileDialogService.ShowSaveFileDialog(record))
                {
                    //OKボタンがクリックされたとき、選択されたファイル名を表示する
                    Filename = record.FileName;
                }
            }

            Serialize.SaveToFile(this, $"{Services.Path}\\user\\backup\\" + Path.GetFileNameWithoutExtension(Filename) + ".backup");
        }
        /// <inheritdoc/>
        public void Dispose()
        {
            foreach (var scene in SceneList)
            {
                scene.GraphicsContext.Dispose();
            }

            previewScene = null;
            sceneList = null;
            GC.Collect();
            IsDisposed = true;
        }

        /// <summary>
        /// Save this <see cref="Project"/>.
        /// </summary>
        /// <returns><see langword="true"/> if the save is successful, otherwise <see langword="false"/>.</returns>
        public bool Save()
        {
            //SaveFileDialogクラスのインスタンスを作成
            var record = new SaveFileRecord
            {
                DefaultFileName = (Filename is not null) ? Path.GetFileName(Filename) : "新しいプロジェクト.bedit",
                Filters =
                {
                    new(Resources.ProjectFile, "bedit")
                }
            };

            //ダイアログを表示する
            if (Services.FileDialogService.ShowSaveFileDialog(record))
            {
                //OKボタンがクリックされたとき、選択されたファイル名を表示する
                Filename = record.FileName;
            }

            if (Serialize.SaveToFile(this, Filename))
            {
                Saved?.Invoke(this, new(SaveType.Save));
                return true;
            }
            return false;
        }
        /// <summary>
        /// Save this <see cref="Project"/> with a name.
        /// </summary>
        /// <param name="filename">New File Name</param>
        /// <returns><see langword="true"/> if the save is successful, otherwise <see langword="false"/>.</returns>
        public bool Save(string filename)
        {
            Filename = filename;
            if (Serialize.SaveToFile(this, filename))
            {
                Saved?.Invoke(this, new(SaveType.Save));
                return true;
            }
            return false;
        }
        /// <summary>
        /// Save this <see cref="Project"/> overwrite.
        /// </summary>
        /// <remarks>If <see cref="Filename"/> is <see langword="null"/>, a dialog will appear</remarks>
        /// <returns><see langword="true"/> if the save is successful, otherwise <see langword="false"/>.</returns>
        public bool SaveAs()
        {
            if (Filename == null)
            {
                var record = new SaveFileRecord
                {
                    DefaultFileName = "新しいプロジェクト.bedit",
                    Filters =
                    {
                        new(Properties.Resources.ProjectFile, "bedit")
                    }
                };

                //ダイアログを表示する
                if (Services.FileDialogService.ShowSaveFileDialog(record))
                {
                    //OKボタンがクリックされたとき、選択されたファイル名を表示する
                    Filename = record.FileName;
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

        #endregion
    }

    /// <summary>
    /// Represents the type of save used in <see cref="ProjectSavedEventArgs"/>.
    /// </summary>
    public enum SaveType
    {
        /// <summary>
        /// 
        /// </summary>
        Save,
        /// <summary>
        /// 
        /// </summary>
        SaveAs
    }

    /// <summary>
    /// Provides data for the <see cref="Project.Saved"/> event.
    /// </summary>
    public class ProjectSavedEventArgs : EventArgs
    {
        /// <summary>
        /// <see cref="ProjectSavedEventArgs"/> Initialize a new instance of the class.
        /// </summary>
        public ProjectSavedEventArgs(SaveType type) => Type = type;

        /// <summary>
        /// 
        /// </summary>
        public SaveType Type { get; }
    }
}
