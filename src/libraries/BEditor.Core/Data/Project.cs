// Project.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Text.Json;
using System.Threading.Tasks;

using BEditor.Audio;
using BEditor.Drawing;
using BEditor.Drawing.Pixel;
using BEditor.Resources;

using Microsoft.Extensions.DependencyInjection;

namespace BEditor.Data
{
    /// <summary>
    /// Represents the type of save used in <see cref="ProjectSavedEventArgs"/>.
    /// </summary>
    public enum SaveType
    {
        /// <summary>
        /// Save.
        /// </summary>
        Save,

        /// <summary>
        /// Backup.
        /// </summary>
        Backup,
    }

    /// <summary>
    /// Represents the project to be used in editing.
    /// </summary>
    public class Project : EditingObject, IParent<Scene>, IChild<IApplication>
    {
        /// <summary>
        /// Defines the <see cref="CurrentScene"/> property.
        /// </summary>
        public static readonly DirectProperty<Project, Scene> CurrentSceneProperty
            = EditingProperty.RegisterDirect<Scene, Project>(
                nameof(CurrentScene),
                owner => owner.CurrentScene,
                (owner, obj) => owner.CurrentScene = obj,
                EditingPropertyOptions<Scene>.Create().Notify(true));

        /// <summary>
        /// Defines the <see cref="CurrentSceneIndex"/> property.
        /// </summary>
        public static readonly DirectProperty<Project, int> CurrentSceneIndexProperty
            = EditingProperty.RegisterDirect<int, Project>(
                $"{nameof(CurrentSceneIndex)},PreviewSceneIndex",
                owner => owner.CurrentSceneIndex,
                (owner, obj) => owner.CurrentSceneIndex = obj,
                EditingPropertyOptions<int>.Create().Notify(true).Serialize());

        /// <summary>
        /// Defines the <see cref="Name"/> property.
        /// </summary>
        public static readonly DirectProperty<Project, string> NameProperty
            = EditingProperty.RegisterDirect<string, Project>(
                nameof(Name),
                owner => owner.Name,
                (owner, obj) => owner.Name = obj,
                EditingPropertyOptions<string>.Create().Notify(true));

        /// <summary>
        /// Defines the <see cref="DirectoryName"/> property.
        /// </summary>
        public static readonly DirectProperty<Project, string> DirectoryNameProperty
            = EditingProperty.RegisterDirect<string, Project>(
                nameof(DirectoryName),
                owner => owner.DirectoryName,
                (owner, obj) => owner.DirectoryName = obj,
                EditingPropertyOptions<string>.Create().Notify(true));

        private Scene? _currentScene;
        private string _name;
        private string _dirname;
        private IApplication _parent;
        private int _currentSceneIndex;

        /// <summary>
        /// Initializes a new instance of the <see cref="Project"/> class.
        /// </summary>
        /// <param name="width">The width of rootscene.</param>
        /// <param name="height">The height of rootscene.</param>
        /// <param name="framerate">The framerate of this project.</param>
        /// <param name="samplingrate">The samplingrate of this project.</param>
        /// <param name="app">The running <see cref="IApplication"/>.</param>
        /// <param name="filename">The project file name.</param>
        public Project(int width, int height, int framerate, int samplingrate, IApplication app, string filename)
        {
            Parent = _parent = app;
            Framerate = framerate;
            Samplingrate = samplingrate;
            Name = _name = Path.GetFileNameWithoutExtension(filename)!;
            DirectoryName = _dirname = Path.GetDirectoryName(filename)!;
            SceneList.Add(new Scene(width, height)
            {
                Parent = this,
                SceneName = "root",
            });
        }

        /// <summary>
        /// Occurs after saving this <see cref="Project"/>.
        /// </summary>
        public event EventHandler<ProjectSavedEventArgs>? Saved;

        /// <summary>
        /// Gets the framerate for this <see cref="Project"/>.
        /// </summary>
        public int Framerate { get; private set; }

        /// <summary>
        /// Gets the sampling rate for this <see cref="Project"/>.
        /// </summary>
        public int Samplingrate { get; private set; }

        /// <summary>
        /// Gets a list of Scenes in this <see cref="Project"/>.
        /// </summary>
        public ObservableCollection<Scene> SceneList { get; private set; } = new ObservableCollection<Scene>();

        /// <summary>
        /// Gets an index of the <see cref="SceneList"/> being previewed.
        /// </summary>
        [Obsolete("Use CurrentSceneIndex.")]
        public int PreviewSceneIndex => CurrentSceneIndex;

        /// <summary>
        /// Gets or sets the <see cref="Scene"/> that is being previewed.
        /// </summary>
        [Obsolete("Use CurrentScene.")]
        public Scene PreviewScene
        {
            get => CurrentScene;
            set
            {
                CurrentScene = value;
                RaisePropertyChanged(new(nameof(PreviewScene)));
            }
        }

        /// <summary>
        /// Gets or sets the current <see cref="Scene"/>.
        /// </summary>
        public Scene CurrentScene
        {
            get => _currentScene ??= SceneList[CurrentSceneIndex];
            set
            {
                SetAndRaise(CurrentSceneProperty, ref _currentScene!, value);
                CurrentSceneIndex = SceneList.IndexOf(value);
            }
        }

        /// <summary>
        /// Gets the index of the current scene.
        /// </summary>
        public int CurrentSceneIndex
        {
            get => _currentSceneIndex;
            set => SetAndRaise(CurrentSceneIndexProperty, ref _currentSceneIndex, value);
        }

        /// <inheritdoc/>
        public IEnumerable<Scene> Children => SceneList;

        /// <inheritdoc/>
        public IApplication Parent
        {
            get => _parent;
            set
            {
                _parent = value;

                foreach (var prop in Children)
                {
                    prop.Parent = this;
                }
            }
        }

        /// <summary>
        /// Gets or sets the name of project.
        /// </summary>
        public string Name
        {
            get => _name;
            set => SetAndRaise(NameProperty, ref _name, value);
        }

        /// <summary>
        /// Gets or sets the directory name of this <see cref="Project"/>.
        /// </summary>
        public string DirectoryName
        {
            get => _dirname;
            set => SetAndRaise(DirectoryNameProperty, ref _dirname, value);
        }

        /// <summary>
        /// Load a <see cref="Project"/> from a file.
        /// </summary>
        /// <param name="file">The project file.</param>
        /// <param name="app">Specify the application.</param>
        /// <returns>Returns the loaded <see cref="Project"/> on success, or <see langword="null"/> on failure.</returns>
        public static async Task<Project?> FromFileAsync(string file, IApplication app)
        {
            static void IfNotExistCreateDir(string dir)
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }

            // Dirを渡された
            if (Directory.Exists(file))
            {
                var dir = new DirectoryInfo(file);

                file = Path.Combine(file, dir.Name + ".bedit");
                if (!File.Exists(file))
                {
                    file = Path.ChangeExtension(file, "json");

                    if (!File.Exists(file))
                    {
                        return null;
                    }
                }
            }

            var proj = await Serialize.LoadFromFileAsync<Project>(file);

            if (proj is null)
            {
                return null;
            }

            proj.DirectoryName = Path.GetDirectoryName(file)!;
            proj.Name = Path.GetFileNameWithoutExtension(file);
            proj.Parent = app;

            var appConf = Path.Combine(proj.DirectoryName!, ".app");
            IfNotExistCreateDir(appConf);
            app.RestoreAppConfig(proj, appConf);

            return proj;
        }

        /// <summary>
        /// Load a <see cref="Project"/> from a file.
        /// </summary>
        /// <param name="file">The project file.</param>
        /// <param name="app">Specify the application.</param>
        /// <returns>Returns the loaded <see cref="Project"/> on success, or <see langword="null"/> on failure.</returns>
        public static Project? FromFile(string file, IApplication app)
        {
            static void IfNotExistCreateDir(string dir)
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }

            // Dirを渡された
            if (Directory.Exists(file))
            {
                var dir = new DirectoryInfo(file);

                file = Path.Combine(file, dir.Name + ".bedit");
                if (!File.Exists(file))
                {
                    file = Path.ChangeExtension(file, "json");

                    if (!File.Exists(file))
                    {
                        return null;
                    }
                }
            }

            var proj = Serialize.LoadFromFile<Project>(file);

            if (proj is null)
            {
                return null;
            }

            proj.DirectoryName = Path.GetDirectoryName(file)!;
            proj.Name = Path.GetFileNameWithoutExtension(file);
            proj.Parent = app;

            var appConf = Path.Combine(proj.DirectoryName!, ".app");
            IfNotExistCreateDir(appConf);
            app.RestoreAppConfig(proj, appConf);

            return proj;
        }

        /// <summary>
        /// Save this <see cref="Project"/>.
        /// </summary>
        /// <remarks>If <see cref="Name"/> is <see langword="null"/>, a dialog will appear.</remarks>
        /// <returns><see langword="true"/> if the save is successful, otherwise <see langword="false"/>.</returns>
        public bool Save()
        {
            if (Name is null || DirectoryName is null)
            {
                return false;
            }

            return Save(Path.Combine(DirectoryName, Name + ".bedit"));
        }

        /// <summary>
        /// Save this <see cref="Project"/>.
        /// </summary>
        /// <remarks>If <see cref="Name"/> is <see langword="null"/>, a dialog will appear.</remarks>
        /// <returns><see langword="true"/> if the save is successful, otherwise <see langword="false"/>.</returns>
        public async Task<bool> SaveAsync()
        {
            if (Name is null || DirectoryName is null)
            {
                return false;
            }

            return await SaveAsync(Path.Combine(DirectoryName, Name + ".bedit"));
        }

        /// <summary>
        /// Save this <see cref="Project"/> with a name.
        /// </summary>
        /// <param name="filename">New File Name.</param>
        /// <returns><see langword="true"/> if the save is successful, otherwise <see langword="false"/>.</returns>
        public bool Save(string filename)
        {
            if (filename is null)
            {
                throw new ArgumentNullException(nameof(filename));
            }

            static void IfNotExistCreateDir(string dir)
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }

            var isBackup = Path.GetExtension(filename) is ".backup";

            if (!isBackup)
            {
                Name = Path.GetFileNameWithoutExtension(filename);
                DirectoryName = Path.GetDirectoryName(filename)!;
                IfNotExistCreateDir(DirectoryName);

                if (CurrentScene.IsLoaded)
                {
                    try
                    {
                        using var img = new Image<BGRA32>(CurrentScene.Width, CurrentScene.Height);

                        var thumbnail = Path.Combine(DirectoryName, "thumbnail.png");
                        CurrentScene.Render(img, ApplyType.Image);

                        img.Encode(thumbnail);
                    }
                    catch
                    {
                    }
                }
            }

            if (Serialize.SaveToFile(this, filename))
            {
                Saved?.Invoke(this, new(SaveType.Save));

                if (!isBackup)
                {
                    var appDir = Path.Combine(DirectoryName, ".app");
                    IfNotExistCreateDir(appDir);
                    Parent.SaveAppConfig(this, appDir);
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Save this <see cref="Project"/> with a name.
        /// </summary>
        /// <param name="filename">New File Name.</param>
        /// <returns><see langword="true"/> if the save is successful, otherwise <see langword="false"/>.</returns>
        public async Task<bool> SaveAsync(string filename)
        {
            if (filename is null)
            {
                throw new ArgumentNullException(nameof(filename));
            }

            static void IfNotExistCreateDir(string dir)
            {
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }

            var isBackup = Path.GetExtension(filename) is ".backup";

            if (!isBackup)
            {
                Name = Path.GetFileNameWithoutExtension(filename);
                DirectoryName = Path.GetDirectoryName(filename)!;
                IfNotExistCreateDir(DirectoryName);

                if (CurrentScene.IsLoaded)
                {
                    try
                    {
                        using var img = new Image<BGRA32>(CurrentScene.Width, CurrentScene.Height);

                        var thumbnail = Path.Combine(DirectoryName, "thumbnail.png");
                        CurrentScene.Render(img, ApplyType.Image);

                        img.Encode(thumbnail);
                    }
                    catch
                    {
                    }
                }
            }

            if (await Serialize.SaveToFileAsync(this, filename))
            {
                Saved?.Invoke(this, new(SaveType.Save));

                if (!isBackup)
                {
                    var appDir = Path.Combine(DirectoryName, ".app");
                    IfNotExistCreateDir(appDir);
                    Parent.SaveAppConfig(this, appDir);
                }

                return true;
            }

            return false;
        }

        /// <summary>
        /// Save this <see cref="Project"/> with a name.
        /// </summary>
        /// <param name="stream">Stream to save.</param>
        /// <returns><see langword="true"/> if the save is successful, otherwise <see langword="false"/>.</returns>
        public bool Save(Stream stream)
        {
            if (Serialize.SaveToStream(this, stream))
            {
                Saved?.Invoke(this, new(SaveType.Save));
                return true;
            }

            return false;
        }

        /// <inheritdoc/>
        public override void GetObjectData(Utf8JsonWriter writer)
        {
            base.GetObjectData(writer);
            writer.WriteNumber(nameof(Framerate), Framerate);
            writer.WriteNumber(nameof(Samplingrate), Samplingrate);
            writer.WriteStartArray("Scenes");

            foreach (var scene in SceneList)
            {
                writer.WriteStartObject();

                scene.GetObjectData(writer);

                writer.WriteEndObject();
            }

            writer.WriteEndArray();
        }

        /// <inheritdoc/>
        public override void SetObjectData(JsonElement element)
        {
            base.SetObjectData(element);
            Framerate = element.GetProperty(nameof(Framerate)).GetInt32();
            Samplingrate = element.GetProperty(nameof(Samplingrate)).GetInt32();
            SceneList = new(element.GetProperty("Scenes").EnumerateArray().Select(i =>
            {
                var scene = (Scene)FormatterServices.GetUninitializedObject(typeof(Scene));
                scene.SetObjectData(i);
                return scene;
            }));
        }

        /// <inheritdoc/>
        protected override void OnUnload()
        {
            if (ServiceProvider is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}