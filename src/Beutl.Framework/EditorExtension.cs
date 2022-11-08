using System.Diagnostics.CodeAnalysis;

using Avalonia.Media;

using Beutl.Framework.Services;

using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Framework;

public interface IEditorContext : IDisposable
{
    EditorExtension Extension { get; }

    string EdittingFile { get; }

    IKnownEditorCommands? Commands { get; }

    T? FindToolTab<T>(Func<T, bool> condition)
        where T : IToolContext;

    T? FindToolTab<T>()
        where T : IToolContext;

    bool OpenToolTab(IToolContext item);

    void CloseToolTab(IToolContext item);
}

// ファイルのエディタを追加
public abstract class EditorExtension : ViewExtension
{
    public abstract Geometry? Icon { get; }

    // Todo: Avalonia.Platform.Storageに対応する
    public abstract string[] FileExtensions { get; }

    public abstract string FileTypeName { get; }

    public abstract bool TryCreateEditor(
        string file,
        [NotNullWhen(true)] out IEditor? editor);

    // NOTE: ここからIWorkspaceItemを取得する場合、
    //       IWorkspaceItemContainerから取得すればいい
    public abstract bool TryCreateContext(
        string file,
        [NotNullWhen(true)] out IEditorContext? context);

    public virtual bool IsSupported(string file)
    {
        return MatchFileExtension(Path.GetExtension(file));
    }

    // extはピリオドを含む
    public bool MatchFileExtension(string ext)
    {
        for (int i = 0; i < FileExtensions.Length; i++)
        {
            string item = FileExtensions[i];
            string includePeriod = item.StartsWith('.') ? item : $".{item}";

            if (includePeriod == ext)
            {
                return true;
            }
        }

        return false;
    }

    // 'ServiceLocator'から'IProjectService'を取得し、Projectのインスタンスを取得します。
    protected static IWorkspace? GetCurrentProject()
    {
        return ServiceLocator.Current.GetRequiredService<IProjectService>().CurrentProject.Value;
    }
}
