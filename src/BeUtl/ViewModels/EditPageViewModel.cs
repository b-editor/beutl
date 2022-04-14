using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;

using BeUtl.Collections;
using BeUtl.Configuration;
using BeUtl.Framework;
using BeUtl.Framework.Services;
using BeUtl.ProjectSystem;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace BeUtl.ViewModels;

public sealed class EditPageViewModel
{
    public class TabViewModel : IDisposable
    {
        public TabViewModel(string path, EditorExtension extension)
        {
            FilePath = path;
            Extension = extension;
        }

        public string FilePath { get; }

        public string FileName => Path.GetFileName(FilePath);

        public EditorExtension Extension { get; }

        public ReactivePropertySlim<bool> IsSelected { get; } = new();

        public void Dispose()
        {
        }
    }

    private readonly IProjectService _projectService;

    public EditPageViewModel()
    {
        _projectService = ServiceLocator.Current.GetRequiredService<IProjectService>();
        TabItems = new();
        _projectService.ProjectObservable.Subscribe(item => ProjectChanged(item.New, item.Old));
    }

    public IReactiveProperty<Project?> Project => _projectService.CurrentProject;

    public IReadOnlyReactiveProperty<bool> IsProjectOpened => _projectService.IsOpened;

    public CoreList<TabViewModel> TabItems { get; }

    private void ProjectChanged(Project? @new, Project? old)
    {
        // プロジェクトが開いた
        if (@new != null)
        {
            @new.Children.CollectionChanged += Project_Children_CollectionChanged;
            foreach (Scene item in @new.Children)
            {
                SelectOrAddTabItem(item.FileName);
            }
        }

        // プロジェクトが閉じた
        if (old != null)
        {
            old.Children.CollectionChanged -= Project_Children_CollectionChanged;
            foreach (Scene item in old.Children)
            {
                CloseTabItem(item.FileName);
            }
        }
    }

    private void Project_Children_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action == NotifyCollectionChangedAction.Add &&
            e.NewItems != null)
        {
            foreach (Scene item in e.NewItems.OfType<Scene>())
            {
                SelectOrAddTabItem(item.FileName);
            }
        }
        else if (e.Action == NotifyCollectionChangedAction.Remove &&
                 e.OldItems != null)
        {
            foreach (Scene item in e.OldItems.OfType<Scene>())
            {
                CloseTabItem(item.FileName);
            }
        }
    }

    public bool TryGetTabItem(string? file, [NotNullWhen(true)] out TabViewModel? result)
    {
        result = TabItems.FirstOrDefault(i => i.FilePath == file);

        return result != null;
    }

    public void SelectOrAddTabItem(string? file)
    {
        if (File.Exists(file))
        {
            ViewConfig viewConfig = GlobalConfiguration.Instance.ViewConfig;
            viewConfig.UpdateRecentFile(file);

            if (TryGetTabItem(file, out TabViewModel? tabItem))
            {
                tabItem.IsSelected.Value = true;
            }
            else
            {
                EditorExtension? ext = PackageManager.Instance.ExtensionProvider.MatchEditorExtension(file);

                if (ext != null)
                {
                    TabItems.Add(new TabViewModel(file, ext)
                    {
                        IsSelected =
                        {
                            Value = true
                        }
                    });
                }
            }
        }
    }

    public void CloseTabItem(string? file)
    {
        if (TryGetTabItem(file, out var item))
        {
            TabItems.Remove(item);
        }
    }
}
