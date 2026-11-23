using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Controls;
using Beutl.Editor.Components.PropertyEditors.Views;
using Beutl.Services;
using Beutl.Views;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.HeadlessUITests;

public sealed class ControlNotificationsTests
{
    [AvaloniaTest]
    public void LookupFollowsCurrentDataContextAndInheritedNotifications()
    {
        using var editor = new NotificationCapture();
        using var inherited = new NotificationCapture();
        using var provider = new ServiceCollection()
            .AddSingleton<INotificationService>(editor)
            .BuildServiceProvider();
        using var emptyProvider = new ServiceCollection().BuildServiceProvider();
        var control = new Border { DataContext = provider };
        var window = new Window { Content = control };
        ControlNotifications.SetNotifications(window, inherited);

        Assert.That(ControlNotifications.FromDataContextOrDefault(control), Is.SameAs(editor));

        control.DataContext = null;
        Assert.That(ControlNotifications.FromDataContextOrDefault(control), Is.SameAs(inherited));

        control.DataContext = new object();
        Assert.That(ControlNotifications.FromDataContextOrDefault(control), Is.SameAs(inherited));

        control.DataContext = emptyProvider;
        Assert.That(ControlNotifications.FromDataContextOrDefault(control), Is.SameAs(inherited));

        window.Content = null;
        int failed = 0;
        ControlNotifications.FromDataContextOrDefault(control).ShowError("Title", "Message", onShowFailed: () => failed++);
        Assert.That(failed, Is.EqualTo(1));
    }

    [AvaloniaTest]
    public void DetachedControlReportsFailureAndFollowsItsCurrentOwner()
    {
        var control = new Border();
        int failed = 0;
        var notification = new Notification("Title", "Message", OnShowFailed: () => failed++);
        ControlNotifications.GetOrDefault(control).Show(notification);
        using var first = new NotificationCapture();
        using var second = new NotificationCapture();
        var window = new Window { Content = control };
        ControlNotifications.SetNotifications(window, first);
        ControlNotifications.GetOrDefault(control).Show(notification);
        ControlNotifications.SetNotifications(window, second);
        ControlNotifications.GetOrDefault(control).Show(notification);
        window.Content = null;
        ControlNotifications.GetOrDefault(control).Show(notification);

        Assert.Multiple(() =>
        {
            Assert.That(failed, Is.EqualTo(2));
            Assert.That(first.All, Has.Count.EqualTo(1));
            Assert.That(second.All, Has.Count.EqualTo(1));
        });
    }

    [AvaloniaTest]
    public void ViewWithoutDataContextUsesInheritedNotificationsAndThenDropsThem()
    {
        var view = new GeometryEditor();
        using var inherited = new NotificationCapture();
        var window = new Window { Content = view };
        ControlNotifications.SetNotifications(window, inherited);
        Notifications(view).ShowError("Title", "Message");
        window.Content = null;
        int failed = 0;
        Notifications(view).ShowError("Title", "Message", onShowFailed: () => failed++);

        Assert.Multiple(() =>
        {
            Assert.That(view.DataContext, Is.Null);
            Assert.That(inherited.All, Has.Count.EqualTo(1));
            Assert.That(failed, Is.EqualTo(1));
        });
    }

    [AvaloniaTest]
    public void MainViewPrefersItsModelAndFallsBackWhenTheModelIsRemoved()
    {
        var view = new MainView { DataContext = TestShell.MainViewModel };
        using var inherited = new NotificationCapture();
        var window = new Window { Content = view };
        ControlNotifications.SetNotifications(window, inherited);
        Assert.That(Notifications(view), Is.SameAs(TestShell.Notifications));
        view.DataContext = null;
        Assert.That(Notifications(view), Is.SameAs(inherited));
        window.Content = null;
        int failed = 0;
        Assert.DoesNotThrow(() => Notifications(view).ShowError("Title", "Message", onShowFailed: () => failed++));
        Assert.That(failed, Is.EqualTo(1));
    }

    private static INotificationService Notifications(Control view) => (INotificationService)view.GetType()
        .GetProperty("Notifications", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(view)!;
}
