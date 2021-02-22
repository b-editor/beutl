using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using BEditor.Properties;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Graphics;

using Microsoft.Extensions.DependencyInjection;

namespace BEditor.Data
{
    /// <summary>
    /// Represents the project to be used in editing.
    /// </summary>
    [DataContract]
    public class Project : EditorObject, IExtensibleDataObject, IDisposable, IParent<Scene>, IChild<IApplication>, IElementObject, IHasName
    {
        #region Fields

        private static readonly PropertyChangedEventArgs _PrevireSceneArgs = new(nameof(PreviewScene));
        private static readonly PropertyChangedEventArgs _FilenameArgs = new(nameof(Name));
        private static readonly PropertyChangedEventArgs _dirnameArgs = new(nameof(DirectoryName));
        private Scene? _previewScene;
        private string? _filename;
        private string? _dirname;
        private IApplication? _parent;

        #endregion

        #region Contructor

        /// <summary>
        /// Initializes a new instance of the <see cref="Project"/> class.
        /// </summary>
        /// <param name="width">The width of rootscene.</param>
        /// <param name="height">The height of rootscene.</param>
        /// <param name="framerate">The framerate of this project.</param>
        /// <param name="samplingrate">The samplingrate of this project.</param>
        /// <param name="app">The running <see cref="IApplication"/>.</param>
        public Project(int width, int height, int framerate, int samplingrate = 0, IApplication? app = null)
        {
            Parent = app;
            Framerate = framerate;
            Samplingrate = samplingrate;
            SceneList.Add(new Scene(width, height)
            {
                Parent = this,
                SceneName = "root",
            });
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
        /// Get a list of Scenes in this <see cref="Project"/>.
        /// </summary>
        [DataMember(Order = 3)]
        public ObservableCollection<Scene> SceneList { get; private set; } = new ObservableCollection<Scene>();

        /// <summary>
        /// Get an index of the <see cref="SceneList"/> being previewed.
        /// </summary>
        [DataMember(Name = "PreviewScene", Order = 2)]
        public int PreviewSceneIndex { get; private set; }

        #endregion

        /// <summary>
        /// Get or set the <see cref="Scene"/> that is being previewed
        /// </summary>
        public Scene PreviewScene
        {
            get => _previewScene ??= SceneList[PreviewSceneIndex];
            set
            {
                SetValue(value, ref _previewScene, _PrevireSceneArgs);
                PreviewSceneIndex = SceneList.IndexOf(value);
            }
        }

        /// <summary>
        /// Get whether an object has been disposed.
        /// </summary>
        public bool IsDisposed { get; private set; }

        /// <inheritdoc/>
        public IEnumerable<Scene> Children => SceneList;

        /// <inheritdoc/>
        public IApplication? Parent
        {
            get => _parent;
            private set
            {
                _parent = value;

                foreach (var scene in SceneList)
                {
                    scene.Parent = this;
                }
            }
        }

        /// <inheritdoc/>
        public string? Name
        {
            get => _filename;
            set => SetValue(value, ref _filename, _FilenameArgs);
        }

        /// <summary>
        /// Get or set the directory name of this <see cref="Project"/>.
        /// </summary>
        public string? DirectoryName
        {
            get => _dirname;
            set => SetValue(value, ref _dirname, _dirnameArgs);
        }

        #endregion

        #region Methods

        /// <inheritdoc/>
        public void Dispose()
        {
            if (IsDisposed) return;

            foreach (var scene in SceneList)
            {
                scene.GraphicsContext?.Dispose();
                scene.AudioContext?.Dispose();
            }

            GC.SuppressFinalize(this);

            IsDisposed = true;
        }

        /// <summary>
        /// Save this <see cref="Project"/>.
        /// </summary>
        /// <remarks>If <see cref="Name"/> is <see langword="null"/>, a dialog will appear</remarks>
        /// <returns><see langword="true"/> if the save is successful, otherwise <see langword="false"/>.</returns>
        public bool Save()
        {
            if (Name is null || DirectoryName is null)
            {
                var dialog = ServiceProvider?.GetService<IFileDialogService>();
                if (dialog is null) throw new InvalidOperationException();

                var record = new SaveFileRecord
                {
                    DefaultFileName = "新しいプロジェクト.bedit",
                    Filters =
                    {
                        new(Resources.ProjectFile, new FileExtension[] { new("bedit") })
                    }
                };

                // ダイアログを表示する
                if (dialog.ShowSaveFileDialog(record))
                {
                    return Save(record.FileName);
                }
                else
                {
                    return false;
                }
            }

            return Save(Path.Combine(DirectoryName, Name + ".bedit"));
        }

        /// <summary>
        /// Save this <see cref="Project"/> with a name.
        /// </summary>
        /// <param name="filename">New File Name</param>
        /// <param name="mode"></param>
        /// <returns><see langword="true"/> if the save is successful, otherwise <see langword="false"/>.</returns>
        public bool Save(string filename, SerializeMode mode = SerializeMode.Binary)
        {
            static void IfNotExistCreateDir(string dir)
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }

            Name = Path.GetFileNameWithoutExtension(filename);
            DirectoryName = Path.GetDirectoryName(filename);
            IfNotExistCreateDir(DirectoryName!);

            if (PreviewScene.IsLoaded)
            {
                PreviewScene.Synchronize?.Post(async _ =>
                {
                    await using var img = new Image<BGRA32>(PreviewScene.Width, PreviewScene.Height);

                    var thumbnail = Path.Combine(DirectoryName!, "thumbnail.png");
                    PreviewScene.Render(img, RenderType.ImageOutput);

                    img.Encode(thumbnail);
                }, null);
            }

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
        /// Load a <see cref="Project"/> from a file.
        /// </summary>
        /// <param name="file">The project file.</param>
        /// <param name="app">Specify the application.</param>
        /// <returns>Returns the loaded <see cref="Project"/> on success, or <see langword="null"/> on failure.</returns>
        public static Project? FromFile(string file, IApplication app)
        {
            var mode = SerializeMode.Binary;
            if (Path.GetExtension(file) is ".json")
            {
                mode = SerializeMode.Json;
            }

            var proj = Serialize.LoadFromFile<Project>(file, mode);

            if (proj is null) return null;

            proj.DirectoryName = Path.GetDirectoryName(file);
            proj.Name = Path.GetFileNameWithoutExtension(file);
            proj.Parent = app;

            return proj;
        }

        /// <inheritdoc/>
        protected override void OnUnload()
        {
            if(ServiceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
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
