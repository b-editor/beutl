using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.Serialization;
using System.Threading.Tasks;

using BEditor.Graphics;
using BEditor.Core.Service;
using BEditor.Core.Properties;
using System.Text.RegularExpressions;
using static System.Net.WebRequestMethods;

namespace BEditor.Core.Data
{
    /// <summary>
    /// Represents the project to be used in editing.
    /// </summary>
    [DataContract]
    public class Project : BasePropertyChanged, IExtensibleDataObject, IDisposable, IParent<Scene>, IChild<IApplication>, IElementObject
    {
        #region Fields

        private static readonly PropertyChangedEventArgs _PrevireSceneArgs = new(nameof(PreviewScene));
        private static readonly PropertyChangedEventArgs _FilenameArgs = new(nameof(Filename));
        private Scene? _PreviewScene;
        private ObservableCollection<Scene> _SceneList = new ObservableCollection<Scene>();
        private IApplication? _Parent;
        private string? _Filename;

        #endregion


        #region Contructor

        /// <summary>
        /// <see cref="Project"/> Initialize a new instance of the class.
        /// </summary>
        public Project(int width, int height, int framerate, int samplingrate = 0, IApplication? app = null)
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
        public Project(string file, IApplication? app = null)
        {
            var mode = SerializeMode.Binary;
            if(Path.GetExtension(file) is ".json")
            {
                mode = SerializeMode.Json;
            }

            var o = Serialize.LoadFromFile<Project>(file, mode);

            if (o != null)
            {
                var project = o;

                project.CopyTo(this);
                Parent = app;
            }
            else
            {
                throw new Exception();
            }
        }
        /// <summary>
        /// <see cref="Project"/> Initialize a new instance of the class.
        /// </summary>
        public Project(Stream stream, SerializeMode mode, IApplication? app = null)
        {
            var o = Serialize.LoadFromStream<Project>(stream, mode);

            if (o != null)
            {
                var project = o;

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
        public event EventHandler<ProjectSavedEventArgs> Saved = delegate { };


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
        public string? Filename
        {
            get => _Filename;
            set => SetValue(value, ref _Filename, _FilenameArgs);
        }

        /// <summary>
        /// Get a list of Scenes in this <see cref="Project"/>.
        /// </summary>
        [DataMember(Order = 4)]
        public ObservableCollection<Scene> SceneList
        {
            get => _SceneList;
            private set
            {
                _SceneList = value;
                Parallel.ForEach(value, scene =>
                {
                    scene.Parent = this;
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
            get => _PreviewScene ??= SceneList[PreviewSceneIndex];
            set
            {
                SetValue(value, ref _PreviewScene, _PrevireSceneArgs);
                PreviewSceneIndex = SceneList.IndexOf(value);
            }
        }
        /// <summary>
        /// Get whether an object has been disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }
        /// <inheritdoc/>
        public ExtensionDataObject? ExtensionData { get; set; }
        /// <inheritdoc/>
        public IEnumerable<Scene> Children => SceneList;
        /// <inheritdoc/>
        public IApplication? Parent
        {
            get => _Parent;
            init => _Parent = value;
        }
        /// <inheritdoc/>
        public bool IsLoaded { get; private set; }

        #endregion


        #region Methods

        /// <summary>
        /// Create a backup of this <see cref="Project"/>.
        /// </summary>
        public void BackUp()
        {
            if (Filename is null)
            {
                if (Services.FileDialogService is null) throw new InvalidOperationException();

                var record = new SaveFileRecord
                {
                    DefaultFileName = "新しいプロジェクト.bedit",
                    Filters =
                    {
                        new(Resources.ProjectFile, new FileExtension[] { new("bedit") })
                    }
                };

                //ダイアログを表示する
                if (Services.FileDialogService.ShowSaveFileDialog(record))
                {
                    //OKボタンがクリックされたとき、選択されたファイル名を表示する
                    Filename = record.FileName;
                }
            }

            Serialize.SaveToFile(this, Path.Combine(AppContext.BaseDirectory, "user", "backup", Path.GetFileNameWithoutExtension(Filename!)) + ".backup");
        }
        /// <inheritdoc/>
        public void Dispose()
        {
            if (IsDisposed) return;

            foreach (var scene in SceneList)
            {
                scene.GraphicsContext?.Dispose();
                scene.AudioContext?.Dispose();
            }

            IsDisposed = true;
        }

        /// <summary>
        /// Save this <see cref="Project"/>.
        /// </summary>
        /// <remarks>If <see cref="Filename"/> is <see langword="null"/>, a dialog will appear</remarks>
        /// <returns><see langword="true"/> if the save is successful, otherwise <see langword="false"/>.</returns>
        public bool Save()
        {
            if (Filename == null)
            {
                if (Services.FileDialogService is null) throw new InvalidOperationException();

                var record = new SaveFileRecord
                {
                    DefaultFileName = "新しいプロジェクト.bedit",
                    Filters =
                    {
                        new(Resources.ProjectFile, new FileExtension[] { new("bedit") })
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
                Saved?.Invoke(this, new(SaveType.Save));
                return true;
            }
            return false;
        }
        /// <summary>
        /// Save this <see cref="Project"/> with a name.
        /// </summary>
        /// <param name="filename">New File Name</param>
        /// <param name="mode"></param>
        /// <returns><see langword="true"/> if the save is successful, otherwise <see langword="false"/>.</returns>
        public bool Save(string filename, SerializeMode mode = SerializeMode.Binary)
        {
            Filename = filename;
            if (Serialize.SaveToFile(this, filename, mode))
            {
                Saved?.Invoke(this, new(SaveType.Save));
                return true;
            }
            return false;
        }
        /// <summary>
        /// Save this <see cref="Project"/> with a name.
        /// </summary>
        /// <param name="stream">Stream to save</param>
        /// <param name="mode"></param>
        /// <returns><see langword="true"/> if the save is successful, otherwise <see langword="false"/>.</returns>
        public bool Save(Stream stream, SerializeMode mode = SerializeMode.Binary)
        {
            if (Serialize.SaveToStream(this, stream, mode))
            {
                Saved?.Invoke(this, new(SaveType.Save));
                return true;
            }
            return false;
        }
        /// <summary>
        /// Save this <see cref="Project"/> overwrite.
        /// </summary>
        /// <returns><see langword="true"/> if the save is successful, otherwise <see langword="false"/>.</returns>
        public bool SaveAs()
        {
            if (Services.FileDialogService is null) throw new InvalidOperationException();

            //SaveFileDialogクラスのインスタンスを作成
            var record = new SaveFileRecord
            {
                DefaultFileName = (Filename is not null) ? Path.GetFileName(Filename) : "新しいプロジェクト.bedit",
                Filters =
                {
                    new(Resources.ProjectFile, new FileExtension[] { new("bedit") }),
                    new(Resources.JsonFile, new FileExtension[] { new("json") }),
                }
            };
            var mode = SerializeMode.Binary;
            //ダイアログを表示する
            if (Services.FileDialogService.ShowSaveFileDialog(record))
            {
                //OKボタンがクリックされたとき、選択されたファイル名を表示する
                if (Path.GetExtension(record.FileName) is ".json")
                {
                    mode = SerializeMode.Json;
                }
                else
                {
                    Filename = record.FileName;
                }
            }

            if (Serialize.SaveToFile(this, record.FileName, mode))
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
            project._Parent = _Parent;
            project.PreviewScene = PreviewScene;
            project.PreviewSceneIndex = PreviewSceneIndex;
            project.Samplingrate = Samplingrate;
            project.SceneList = SceneList;
        }
        /// <inheritdoc/>
        public void Load()
        {
            if (IsLoaded) return;

            foreach (var scene in SceneList)
            {
                scene.Load();
            }

            IsLoaded = true;
        }
        /// <inheritdoc/>
        public void Unload()
        {
            if (!IsLoaded) return;

            foreach (var scene in SceneList)
            {
                scene.Unload();
            }

            IsLoaded = false;
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
