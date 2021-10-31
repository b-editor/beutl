using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;

using Avalonia;
using Avalonia.Controls;

using BEditor.Command;
using BEditor.Data;
using BEditor.Extensions;
using BEditor.LangResources;
using BEditor.Models;
using BEditor.Primitive.Objects;
using BEditor.ViewModels.Dialogs;
using BEditor.Views;
using BEditor.Views.DialogContent;
using BEditor.Views.Dialogs;

using Microsoft.Extensions.Logging;

using Reactive.Bindings;
using Reactive.Bindings.Extensions;

namespace BEditor.ViewModels
{
    public sealed class MainWindowViewModel
    {
        public static readonly MainWindowViewModel Current = new();

        public MainWindowViewModel()
        {
            Open.Subscribe(async () =>
            {
                var dialog = new OpenFileRecord
                {
                    Filters =
                    {
                        new(Strings.ProjectFile, new[] { "bedit", "beproj" }),
                        new(Strings.BackupFile, new[] { "backup" }),
                    }
                };
                var service = App.FileDialog;

                if (await service.ShowOpenFileDialogAsync(dialog))
                {
                    ProgressDialog? ndialog = null;
                    try
                    {
                        ndialog = new ProgressDialog
                        {
                            IsIndeterminate = { Value = true }
                        };
                        ndialog.Show(BEditor.App.GetMainWindow());

                        await DirectOpenAsync(dialog.FileName);
                    }
                    catch (Exception e)
                    {
                        Debug.Fail(string.Empty);
                        App.Project = null;
                        App.AppStatus = Status.Idle;

                        var msg = string.Format(Strings.FailedToLoad, Strings.Project);
                        App.Message.Snackbar(msg, string.Empty, IMessage.IconType.Error);

                        BEditor.App.Logger?.LogError(e, "Failed to load the project.");
                    }
                    finally
                    {
                        ndialog?.Close();
                    }
                }
            });

            Save.Select(_ => App.Project)
                .Where(p => p != null)
                .Subscribe(async p => await p!.SaveAsync());

            SaveAs.Select(_ => App.Project)
                .Where(p => p != null)
                .Subscribe(async p =>
                {
                    var record = new SaveFileRecord
                    {
                        DefaultFileName = (p!.Name is not null) ? p.Name + ".bedit" : "新しいプロジェクト.bedit",
                        Filters =
                        {
                            new(Strings.ProjectFile, new[] { "bedit" }),
                        }
                    };

                    if (await AppModel.Current.FileDialog.ShowSaveFileDialogAsync(record))
                    {
                        await p.SaveAsync(record.FileName);
                    }
                });

            PackProject.Subscribe(async () =>
            {
                var dialog = new CreateProjectPackage();
                await dialog.ShowDialog(BEditor.App.GetMainWindow());
            });

            Close.Select(_ => App)
                .Where(app => app.Project != null)
                .Subscribe(async app =>
                {
                    if (app.Project.LastSavedTime < CommandManager.Default.LastExecutedTime)
                    {
                        var dialog = new ProjectClosing();
                        if (!await dialog.ShowDialog<bool>(BEditor.App.GetMainWindow()))
                        {
                            return;
                        }
                    }

                    if (app.Project != null)
                    {
                        app.Project.Unload();
                        app.Project = null;
                    }

                    app.AppStatus = Status.Idle;
                });

            Shutdown.Subscribe(() => BEditor.App.Shutdown(0));

            Undo.Where(_ => CommandManager.Default.CanUndo)
                .Subscribe(async _ =>
                {
                    CommandManager.Default.Undo();

                    await AppModel.Current.Project!.PreviewUpdateAsync();
                    AppModel.Current.AppStatus = Status.Edit;
                });

            Redo.Where(_ => CommandManager.Default.CanRedo)
                .Subscribe(async _ =>
                {
                    CommandManager.Default.Redo();

                    await AppModel.Current.Project!.PreviewUpdateAsync();
                    AppModel.Current.AppStatus = Status.Edit;
                });

            Split.Where(_ => App.Project != null)
                .Select(_ => App.Project!.CurrentScene.SelectItem)
                .Where(c => c != null)
                .Subscribe(clip => clip!.GetCreateClipViewModel().Split.Execute());

            Remove.Where(_ => App.Project != null)
                .Select(_ => App.Project!.CurrentScene.SelectItem)
                .Where(c => c != null)
                .Subscribe(clip => clip!.Parent.RemoveClip(clip).Execute());

            Copy.Where(_ => App.Project != null)
                .Select(_ => App.Project!.CurrentScene.SelectItem)
                .Where(clip => clip != null)
                .Subscribe(async clip =>
                {
                    await using var memory = new MemoryStream();
                    await Serialize.SaveToStreamAsync(clip!, memory);

                    var json = Encoding.Default.GetString(memory.ToArray());
                    await Application.Current.Clipboard.SetTextAsync(json);
                });

            Cut.Where(_ => App.Project != null)
                .Select(_ => App.Project!.CurrentScene.SelectItem)
                .Where(clip => clip != null)
                .Subscribe(async clip =>
                {
                    clip!.Parent.RemoveClip(clip).Execute();

                    await using var memory = new MemoryStream();
                    await Serialize.SaveToStreamAsync(clip, memory);

                    var json = Encoding.Default.GetString(memory.ToArray());
                    await Application.Current.Clipboard.SetTextAsync(json);
                });

            Paste.Where(_ => App.Project != null)
                .Select(_ => App.Project!.CurrentScene.GetCreateTimelineViewModel())
                .Subscribe(async timeline =>
                {
                    var mes = AppModel.Current.Message;
                    var clipboard = Application.Current.Clipboard;
                    var text = await clipboard.GetTextAsync();
                    await using var memory = new MemoryStream();
                    await memory.WriteAsync(Encoding.Default.GetBytes(text));

                    if (await Serialize.LoadFromStreamAsync<ClipElement>(memory) is var clip && clip is not null)
                    {
                        var length = clip.Length;
                        clip.Start = timeline.ClickedFrame;
                        clip.End = length + timeline.ClickedFrame;

                        clip.Layer = timeline.ClickedLayer;

                        if (!timeline.Scene.InRange(clip.Start, clip.End, clip.Layer))
                        {
                            mes?.Snackbar(Strings.ClipExistsInTheSpecifiedLocation, string.Empty);
                            BEditor.App.Logger.LogInformation("Cannot place a new clip because a clip already exists in the specified location. Start: {start} End: {end} Layer: {layer}", clip.Start, clip.End, clip.Layer);

                            return;
                        }

                        timeline.Scene.AddClip(clip).Execute();
                    }
                    else if (File.Exists(text))
                    {
                        var start = timeline.ClickedFrame;
                        var end = timeline.ClickedFrame + 180;
                        var layer = timeline.ClickedLayer;
                        var ext = Path.GetExtension(text);

                        if (!timeline.Scene.InRange(start, end, layer))
                        {
                            mes?.Snackbar(Strings.ClipExistsInTheSpecifiedLocation, string.Empty);
                            return;
                        }

                        if (ext is ".bobj")
                        {
                            var efct = await Serialize.LoadFromFileAsync<EffectWrapper>(text);
                            if (efct?.Effect is not ObjectElement obj)
                            {
                                mes?.Snackbar(Strings.FailedToLoad, string.Empty, IMessage.IconType.Error);
                                return;
                            }

                            obj.Load();
                            obj.UpdateId();
                            timeline.Scene.AddClip(start, layer, obj, out _).Execute();
                        }
                        else
                        {
                            var supportedObjects = ObjectMetadata.LoadedObjects
                                .Where(i => i.IsSupported is not null && i.CreateFromFile is not null && i.IsSupported(text))
                                .ToArray();
                            var result = supportedObjects.FirstOrDefault();

                            if (supportedObjects.Length > 1)
                            {
                                var dialog = new SelectObjectMetadata
                                {
                                    Metadatas = supportedObjects,
                                    Selected = result,
                                };

                                result = await dialog.ShowDialog<ObjectMetadata?>(BEditor.App.GetMainWindow());
                            }

                            if (result is not null)
                            {
                                timeline.Scene.AddClip(start, layer, result.CreateFromFile!.Invoke(text), out _).Execute();
                            }
                        }
                    }
                });

            IsOpened.Subscribe(v =>
            {
                CommandManager.Default.Clear();

                if (v)
                {
                    App.RaiseProjectOpened(App.Project);
                }
            });

            ImageOutput.Where(_ => App.Project != null).Subscribe(async _ =>
            {
                var scene = AppModel.Current.Project.CurrentScene!;

                var record = new SaveFileRecord
                {
                    Filters =
                    {
                        new(Strings.ImageFile, ImageFile.SupportExtensions)
                    }
                };

                if (await AppModel.Current.FileDialog.ShowSaveFileDialogAsync(record))
                {
                    using var img = scene.Render(ApplyType.Image);

                    img.Save(record.FileName);
                }
            });

            VideoOutput.Where(_ => App.Project != null).Subscribe(async _ =>
            {
                var dialog = new VideoOutput();
                await dialog.ShowDialog(BEditor.App.GetMainWindow());
            });

            Previewer = new(IsOpened);

            NoticeIsVisible = NoticeCount.Select(i => i > 0).ToReadOnlyReactivePropertySlim();
        }

        public ReactiveCommand Open { get; } = new();

        public ReactiveCommand Save { get; } = new();

        public ReactiveCommand SaveAs { get; } = new();

        public ReactiveCommand PackProject { get; } = new();

        public ReactiveCommand Close { get; } = new();

        public ReactiveCommand Shutdown { get; } = new();

        public ReactiveCommand Undo { get; } = new();

        public ReactiveCommand Redo { get; } = new();

        public ReactiveCommand Split { get; } = new();

        public ReactiveCommand Remove { get; } = new();

        public ReactiveCommand Cut { get; } = new();

        public ReactiveCommand Copy { get; } = new();

        public ReactiveCommand Paste { get; } = new();

        public ReactiveCommand New { get; } = new();

        public ReactiveCommand ImageOutput { get; } = new();

        public ReactiveCommand VideoOutput { get; } = new();

        public ReadOnlyReactivePropertySlim<bool> IsOpened { get; } = AppModel.Current
            .ObserveProperty(p => p.Project)
            .Select(p => p is not null)
            .ToReadOnlyReactivePropertySlim();

        public ReactivePropertySlim<int> NoticeCount { get; } = new();

        public ReadOnlyReactivePropertySlim<bool> NoticeIsVisible { get; }

        public PreviewerViewModel Previewer { get; }

        public AppModel App { get; } = AppModel.Current;

        public static async ValueTask DirectOpenAsync(string filename)
        {
            var app = AppModel.Current;
            app.Project?.Unload();
            Project? project = null;

            if (Path.GetExtension(filename) is ".beproj")
            {
                var dialog = new OpenFolderDialog
                {
                    Title = Strings.SelectLocationToUnpackProject
                };
                var dir = await dialog.ShowAsync(BEditor.App.GetMainWindow());

                if (!Directory.Exists(dir) || dir == null) return;

                var viewModel = new OpenProjectPackageViewModel(filename);
                var openDialog = new OpenProjectPackage
                {
                    DataContext = viewModel,
                };
                var result = await openDialog.ShowDialog<OpenProjectPackageViewModel.State>(BEditor.App.GetMainWindow());

                if (result == OpenProjectPackageViewModel.State.Open)
                {
                    project = ProjectPackage.OpenFile(filename, dir);
                }
            }
            else
            {
                project = Project.FromFile(filename, app);
            }

            if (project is null) return;

            await Task.Run(() =>
            {
                project.Load();

                app.Project = project;
                app.AppStatus = Status.Edit;

                BEditor.Settings.Default.RecentFiles.Remove(filename);
                BEditor.Settings.Default.RecentFiles.Add(filename);
            });
        }
    }
}