using Avalonia.Media;

using BeUtl.Framework.Services;
using BeUtl.ProjectSystem;

using Microsoft.Extensions.DependencyInjection;

namespace BeUtl.Framework;

// ファイルのエディタを追加
public abstract class EditorExtension : ViewExtension
{
    public abstract Geometry? Icon { get; }

    public abstract string[] FileExtensions { get; }

    public abstract ResourceReference<string> FileTypeName { get; }

    public abstract bool TryCreateEditor(string file, out IEditor? editor);

    public virtual bool IsSupported(string file)
    {
        string ext = Path.GetExtension(file);
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

    protected static Project? GetCurrentProject()
    {
        return ServiceLocator.Current.GetRequiredService<IProjectService>().CurrentProject.Value;
    }
}
