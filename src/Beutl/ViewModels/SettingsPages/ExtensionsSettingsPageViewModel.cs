using Beutl.Api.Services;

using Beutl.Configuration;
using Beutl.Controls.Navigation;
using Beutl.Extensibility;

using Microsoft.Extensions.DependencyInjection;

using Reactive.Bindings;

namespace Beutl.ViewModels.SettingsPages;

public sealed class ExtensionsSettingsPageViewModel : PageContext
{
    private readonly ExtensionConfig _extensionConfig = GlobalConfiguration.Instance.ExtensionConfig;
    private readonly ExtensionProvider _extensionProvider = ServiceLocator.Current.GetRequiredService<ExtensionProvider>();
    private readonly EditorExtension[] _loadedEExt;
    private IDisposable? _disposable1;

    public sealed record EditorExtensionWrapper(string DisplayName, string Name, string TypeName);

    public ExtensionsSettingsPageViewModel()
    {
        _loadedEExt = _extensionProvider.AllExtensions.OfType<EditorExtension>().ToArray();
        FileExtensions.AddRange(_extensionConfig.EditorExtensions.Keys);
        SelectedFileExtension.Subscribe(fext =>
        {
            _disposable1?.Dispose();
            _disposable1 = null;
            EditorExtensions1.Clear();
            EditorExtensions2.Clear();
            if (fext != null)
            {
                if (_extensionConfig.EditorExtensions.TryGetValue(fext, out ICoreList<ExtensionConfig.TypeLazy>? list))
                {
                    EditorExtensions1.AddRange(list.Select(type =>
                    {
                        string? displayName = null;
                        string? name = null;
                        string typeName = type.FormattedTypeName;

                        if (type.Type != null)
                        {
                            Extension? ext = _extensionProvider.AllExtensions.FirstOrDefault(item => item.GetType() == type.Type);
                            displayName = ext?.DisplayName;
                            name = ext?.Name;
                        }

                        return new EditorExtensionWrapper(
                            displayName ?? "Unknown",
                            name ?? "Unknown",
                            typeName);
                    }));
                }

                EditorExtensions2.AddRange(_loadedEExt
                    .Where(item => item.MatchFileExtension(fext))
                    .Select(item => new EditorExtensionWrapper(item.DisplayName, item.Name, TypeFormat.ToString(item.GetType()))));

                _disposable1 = EditorExtensions1.ForEachItem(
                    (idx, item) =>
                    {
                        if (_disposable1 != null)
                            _extensionConfig.EditorExtensions[fext].Insert(idx, new ExtensionConfig.TypeLazy(item.TypeName));
                    },
                    (idx, _) => _extensionConfig.EditorExtensions[fext].RemoveAt(idx),
                    () => _extensionConfig.EditorExtensions[fext].Clear());
            }
        });

        HighPriority.Subscribe(item =>
        {
            int idx = EditorExtensions1.IndexOf(item);
            if (idx >= 1 && idx < EditorExtensions1.Count)
            {
                EditorExtensions1.Move(idx, idx - 1);
            }
        });
        LowPriority.Subscribe(item =>
        {
            int idx = EditorExtensions1.IndexOf(item);
            if (idx >= 0 && idx < EditorExtensions1.Count - 1)
            {
                EditorExtensions1.Move(idx, idx + 1);
            }
        });
        RemoveExt.Subscribe(item => EditorExtensions1.Remove(item));
        AddExt.Subscribe(item =>
        {
            if (!EditorExtensions1.Contains(item))
            {
                EditorExtensions1.Add(item);
            }
        });

        SelectedFileExtension.Value = _extensionConfig.EditorExtensions.Keys.FirstOrDefault();

        FileExtensionInput = new ReactiveProperty<string>();
        FileExtensionInput.SetValidateNotifyError(str =>
        {
            if (string.IsNullOrWhiteSpace(str))
            {
                return "Please enter a file extension";
            }
            else if (str.Contains('"')
                || str.Contains('>')
                || str.Contains('<')
                || str.Contains('|')
                || str.Contains(':')
                || str.Contains('?')
                || str.Contains('*')
                || str.Contains('\\')
                || str.Contains('/'))
            {
                return "The following characters are not allowed (\" > < | : ? * \\ /)";
            }
            else
            {
                string str1 = str.StartsWith('.') ? str : $".{str}";
                if (_extensionConfig.EditorExtensions.ContainsKey(str1))
                {
                    return "This file extension already exists";
                }
            }

            return null;
        });

        CanAddFileExtension = FileExtensionInput.ObserveHasErrors
            .Select(v => !v)
            .ToReadOnlyReactivePropertySlim();
        AddFileExtension = new();
        AddFileExtension.Subscribe(() =>
        {
            string str = FileExtensionInput.Value;
            str = str.StartsWith('.') ? str : $".{str}";
            _extensionConfig.EditorExtensions.Add(str, new CoreList<ExtensionConfig.TypeLazy>());
            FileExtensionInput.Value = string.Empty;

            FileExtensions.Add(str);
            SelectedFileExtension.Value = str;
        });

        RemoveFileExtension = new(SelectedFileExtension.Select(i => i != null));
        RemoveFileExtension.Subscribe(() =>
        {
            if (SelectedFileExtension.Value != null)
            {
                _extensionConfig.EditorExtensions.Remove(SelectedFileExtension.Value);
                FileExtensions.Remove(SelectedFileExtension.Value);
                SelectedFileExtension.Value = null;
            }
        });
    }

    public CoreList<string> FileExtensions { get; } = new();

    public ReactivePropertySlim<string?> SelectedFileExtension { get; } = new();

    // EditorExtensions1:
    //     ExtensionConfigで指定されているItems
    //     通常これが優先される。
    // EditorExtensions2:
    //     ExtensionProvider.AllExtensionsで取得されるItems
    //     上のアイテムでMatchされなかったときこれが使われる。
    public CoreList<EditorExtensionWrapper> EditorExtensions1 { get; } = new();

    public CoreList<EditorExtensionWrapper> EditorExtensions2 { get; } = new();

    public ReactiveCommand<EditorExtensionWrapper> HighPriority { get; } = new();

    public ReactiveCommand<EditorExtensionWrapper> LowPriority { get; } = new();

    public ReactiveCommand<EditorExtensionWrapper> RemoveExt { get; } = new();

    public ReactiveCommand<EditorExtensionWrapper> AddExt { get; } = new();

    public ReactiveCommand AddFileExtension { get; }

    public ReadOnlyReactivePropertySlim<bool> CanAddFileExtension { get; }

    public ReactiveCommand RemoveFileExtension { get; }

    public ReactiveProperty<string> FileExtensionInput { get; }
}
