using System.Diagnostics.CodeAnalysis;

using Avalonia.Media;

using BeUtl.Framework.Services;
using BeUtl.ProjectSystem;

using Microsoft.Extensions.DependencyInjection;

namespace BeUtl.Framework;

public interface IEditorContext : IDisposable
{
    EditorExtension Extension { get; }

    string EdittingFile { get; }

    IKnownEditorCommands? Commands { get; }
}

// ファイルのエディタを追加
public abstract class EditorExtension : ViewExtension
{
    public abstract Geometry? Icon { get; }

    public abstract string[] FileExtensions { get; }

    public abstract ResourceReference<string> FileTypeName { get; }

    public abstract bool TryCreateEditor(
        string file,
        [NotNullWhen(true)] out IEditor? editor);

    // NOTE: ここからIWorkspaceItemを取得する場合、
    //       GetCurrentProjectから取得すればいい
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
    protected static Project? GetCurrentProject()
    {
        return ServiceLocator.Current.GetRequiredService<IProjectService>().CurrentProject.Value;
    }
}
