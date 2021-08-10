using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

using BEditor.Data;
using BEditor.Models;
using BEditor.Properties;

using Microsoft.Extensions.Logging;

using Reactive.Bindings;

namespace BEditor.ViewModels.Dialogs
{
    public class CreateProjectPackageViewModel
    {
        public record TreeItem(string Text, string Tip, string Hint = "")
        {
            public bool IsChecked { get; set; }
        }

        private readonly Project _project;
        private readonly Task<ProjectPackageBuilder?> _task;

        public CreateProjectPackageViewModel()
        {
            _project = AppModel.Current.Project;
            Folder.Value = _project.DirectoryName;
            Name.Value = _project.Name;
            Create = new();

            _task = Task.Run(() =>
            {
                try
                {
                    IsLoading.Value = true;

                    var builder = ProjectPackageBuilder.Configure(_project);

                    // フォントを追加
                    foreach (var item in builder.Fonts.Select(i => new TreeItem(i.Name, i.Filename, i.Filename)).Distinct())
                    {
                        Fonts.AddOnScheduler(item);
                    }

                    // ファイルを追加
                    foreach (var item in builder.Files
                        .Where(i => File.Exists(i))
                        .Select(i => new TreeItem(Path.GetFileName(i), i))
                        .Distinct())
                    {
                        Files.AddOnScheduler(item);
                    }

                    // プラグインを追加
                    foreach (var item in builder.Plugins
                        .Select(i => new TreeItem(
                            i.PluginName + "  " + i.GetType().Assembly.GetName()?.Version?.ToString(3) ?? string.Empty,
                            string.Empty))
                        .Distinct())
                    {
                        Plugins.AddOnScheduler(item);
                    }

                    return builder;
                }
                catch
                {
                    return null;
                }
                finally
                {
                    IsLoading.Value = false;
                }
            });

            Create.Subscribe(async () =>
            {
                await Task.Run(async () =>
                {
                    var msg = AppModel.Current.Message;
                    try
                    {
                        IsLoading.Value = true;
                        var builder = await _task ?? throw new Exception("Failed to create project package builder");

                        builder.ExcludeFonts(Fonts.Where(i => !i.IsChecked).Select(i => new Drawing.Font(i.Hint)));
                        builder.ReadMe = ReadMe.Value;
                        builder.OtherFiles.UnionWith(Others.Select(i => i.Hint));

                        if (!await Task.Run(() => builder.Create(Path.Combine(Folder.Value, Name.Value) + ".beproj")))
                        {
                            await msg.DialogAsync(Strings.FailedToPackProject, IMessage.IconType.Error);
                        }
                        else
                        {
                            msg.Snackbar(Strings.ThePackageHasBeenCreated, string.Empty, IMessage.IconType.Success);
                        }
                    }
                    catch (Exception e)
                    {
                        await msg.DialogAsync(Strings.FailedToPackProject, IMessage.IconType.Error);
                        ServicesLocator.Current.Logger.LogError(e, Strings.FailedToPackProject);
                    }
                    finally
                    {
                        IsLoading.Value = false;
                    }
                });
            });

            RemoveOtherFile.Subscribe(item =>
            {
                if (item is TreeItem i)
                    Others.Remove(i);
            });
        }

        public ReactiveCollection<TreeItem> Fonts { get; } = new();

        public ReactiveCollection<TreeItem> Files { get; } = new();

        public ReactiveCollection<TreeItem> Plugins { get; } = new();

        public ReactiveCollection<TreeItem> Others { get; } = new();

        public ReactivePropertySlim<string> Name { get; } = new();

        public ReactivePropertySlim<string> ReadMe { get; } = new();

        public ReactivePropertySlim<string> Folder { get; } = new();

        public ReactiveCommand OpenFolderDialog { get; } = new();

        public ReactiveCommand OpenOtherFile { get; } = new();

        public ReactiveCommand<object> RemoveOtherFile { get; } = new();

        public ReactivePropertySlim<bool> IsLoading { get; } = new(false);

        public AsyncReactiveCommand Create { get; }
    }
}
