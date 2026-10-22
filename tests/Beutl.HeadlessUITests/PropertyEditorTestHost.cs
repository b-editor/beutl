using Avalonia.Controls;
using Beutl.Editor;
using Beutl.Editor.Components.PropertyEditors.ViewModels;
using Beutl.Editor.Observers;
using Beutl.Editor.PropertyEditors;
using Beutl.Extensibility;
using Beutl.ProjectSystem;

namespace Beutl.HeadlessUITests;

public enum EditorSurface { Property, Node, Settings, ListItem }

// Shared setup for the property editor tests.
internal static class PropertyEditorTestHost
{
    // Creates the editor the way the surface does.
    public static (PropertyEditorViewModel Vm, Control Control) CreateControl(IPropertyAdapter adapter, EditorSurface surface = EditorSurface.Property)
    {
        PropertyEditorExtension extension = PropertyEditorExtension.Instance;
        Assert.That(extension.MatchProperty([adapter]), Is.EqualTo(new[] { adapter }));
        IPropertyEditorContext? context;
        Control? control;
        bool createdContext = surface switch
        {
            EditorSurface.Node => extension.TryCreateContextForNode([adapter], out context),
            EditorSurface.Settings => extension.TryCreateContextForSettings([adapter], out context),
            EditorSurface.ListItem => extension.TryCreateContextForListItem(adapter, out context),
            _ => extension.TryCreateContext([adapter], out context)
        };
        Assert.That(createdContext, Is.True);
        bool createdControl;
        if (surface == EditorSurface.ListItem)
        {
            createdControl = extension.TryCreateControlForListItem(context!, out var listControl);
            control = listControl as Control;
        }
        else
        {
            createdControl = surface switch
            {
                EditorSurface.Node => extension.TryCreateControlForNode(context!, out control),
                EditorSurface.Settings => extension.TryCreateControlForSettings(context!, out control),
                _ => extension.TryCreateControl(context!, out control)
            };
        }
        Assert.That(createdControl, Is.True);
        control!.DataContext = context;
        return ((PropertyEditorViewModel)context!, control);
    }

    // The contexts and those nested in their groups, depth first, with the groups themselves when includeGroups is true.
    public static IEnumerable<IPropertyEditorContext> Flatten(IEnumerable<IPropertyEditorContext?> contexts, bool includeGroups)
    {
        foreach (IPropertyEditorContext? context in contexts)
        {
            if (context is PropertyEditorGroupContext group)
            {
                if (includeGroups)
                    yield return group;
                foreach (IPropertyEditorContext nested in Flatten(group.Properties, includeGroups))
                    yield return nested;
            }
            else if (context is not null)
            {
                yield return context;
            }
        }
    }

    public sealed class HistoryScope : IDisposable
    {
        private readonly CoreObjectOperationObserver _observer;
        private readonly IDisposable _subscription;

        public HistoryScope(CoreObject target)
        {
            var sequence = new OperationSequenceGenerator();
            History = new HistoryManager(sequence);
            _observer = new CoreObjectOperationObserver(null, target, sequence);
            _subscription = History.Subscribe(_observer);
        }

        public HistoryManager History { get; }

        public void Dispose()
        {
            _subscription.Dispose();
            _observer.Dispose();
            History.Dispose();
        }
    }

    // A visitor whose GetService answers with the services it was given, by type.
    public sealed class ServiceVisitor(params (Type Type, object? Service)[] services) : IServiceProvider, IPropertyEditorContextVisitor
    {
        // The history and, when given, the element that the property editors ask for.
        public static ServiceVisitor For(HistoryManager history, Element? element = null)
            => new((typeof(HistoryManager), history), (typeof(Element), element));

        public object? GetService(Type type) => Array.Find(services, service => service.Type == type).Service;

        public void Visit(IPropertyEditorContext context) { }
    }
}
