using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Helpers;

public class GetCommandGestureExtension : MarkupExtension
{
    public GetCommandGestureExtension(string name)
    {
        Name = name;
    }

    public string Name { get; set; }

    public Type? ExtensionType { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (ExtensionType != null)
        {
            return GetKeyGestureFromExtensionType(ExtensionType)!;
        }

        var t = serviceProvider.GetService<IProvideValueTarget>();
        if (t?.TargetObject is not StyledElement styledElement)
            return null!;

        return styledElement.GetObservable(StyledElement.DataContextProperty)
            .Select(dataContext =>
            {
                ViewExtension? viewExtension = null;
                if (dataContext is IToolContext tc)
                    viewExtension = tc.Extension;
                else if (dataContext is IEditorContext ec)
                    viewExtension = ec.Extension;

                if (viewExtension == null)
                    return null!;

                return GetKeyGestureFromExtensionType(viewExtension.GetType());
            })
            .ToBinding();
    }

    private KeyGesture? GetKeyGestureFromExtensionType(Type extensionType)
    {
        var cm = App.GetContextCommandManager();
        if (cm == null)
            return null!;

        var entries = cm.GetDefinitions(extensionType);
        var e = entries.FirstOrDefault(i => i.Definition.Name == Name);
        if (e == null)
            return null!;

        OSPlatform pid = OperatingSystem.IsWindows() ? OSPlatform.Windows :
            OperatingSystem.IsMacOS() ? OSPlatform.OSX :
            OperatingSystem.IsLinux() ? OSPlatform.Linux :
            throw new NotSupportedException();

        return e.KeyGestures
            .FirstOrDefault(gesture => gesture.Platform == pid)?.KeyGesture!;
    }
}
