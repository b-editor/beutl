using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Xaml.Interactivity;
using Beutl.Controls;
using Beutl.Controls.Navigation;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;
using FluentIcons.Avalonia.Fluent;
using FluentIcons.Common;

namespace Beutl.HeadlessUITests;

[TestFixture]
public class Avalonia12NavigationTests
{
    [AvaloniaTest]
    public void Navigation_item_switches_between_regular_and_selected_icons()
    {
        var regular = new FAFontIconSource { Glyph = "R" };
        var filled = new FluentIconSource { Icon = Icon.Settings };
        var behavior = new NavItemHelper { RegularIcon = regular, FilledIcon = filled };
        var item = new FANavigationViewItem { Content = "Settings" };
        Interaction.GetBehaviors(item).Add(behavior);
        try
        {
            Assert.That(item.IconSource, Is.SameAs(regular));
            item.IsSelected = true;
            Assert.That(item.IconSource, Is.SameAs(filled));
            item.IsSelected = false;
            Assert.Multiple(() =>
            {
                Assert.That(item.IconSource, Is.SameAs(regular));
                Assert.That(regular.FontSize, Is.EqualTo(48));
                Assert.That(filled.FontSize, Is.EqualTo(48));
            });
        }
        finally
        {
            Interaction.GetBehaviors(item).Remove(behavior);
        }
    }

    [AvaloniaTest]
    public async Task Navigation_reuses_contexts_and_removes_them_from_both_history_stacks()
    {
        var frame = new FAFrame();
        var navigation = new NavigationProvider(frame, new PageResolver());
        var first = new Context("first");
        var second = new Context("second");
        var third = new Context("third");
        var window = new Window { Content = frame, Width = 400, Height = 300 };
        try
        {
            window.Show();
            await navigation.NavigateAsync<Context>(_ => false, () => first);
            await navigation.NavigateAsync<Context>(_ => false, () => second);
            await navigation.NavigateAsync<Context>(_ => false, () => third);
            await navigation.GoBackAsync();
            HeadlessTestHelpers.Settle();

            Assert.Multiple(() =>
            {
                Assert.That(navigation.CurrentContext, Is.SameAs(second));
                Assert.That(((Control)frame.Content!).DataContext, Is.SameAs(second));
                Assert.That(frame.BackStack.Single().Parameter, Is.SameAs(first));
                Assert.That(frame.ForwardStack.Single().Parameter, Is.SameAs(third));
            });
            Assert.That(await navigation.FindAsync<Context>(context => context.Name == "third"), Is.SameAs(third));
            await navigation.NavigateAsync<Context>(context => context.Name == "first",
                () => throw new AssertionException("Navigation must reuse the cached context."));
            await navigation.GoBackAsync();
            await navigation.RemoveAllAsync<Context>(context => context == first);
            Assert.Multiple(() =>
            {
                Assert.That(frame.BackStack, Is.Empty);
                Assert.That(frame.ForwardStack, Is.Empty);
                Assert.That(navigation.CurrentContext, Is.SameAs(second));
            });
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }

    private sealed record Context(string Name);

    [AvaloniaTest]
    public void Frame_helpers_find_and_remove_matching_contexts_from_both_stacks()
    {
        var frame = new FAFrame();
        var first = new Context("first");
        var second = new Context("second");
        var third = new Context("third");
        frame.Navigate(typeof(Page), first);
        frame.Navigate(typeof(Page), second);
        frame.Navigate(typeof(Page), third);
        frame.GoBack();
        object currentPage = frame.Content!;

        Assert.Multiple(() =>
        {
            Assert.That(frame.FindParameter<Context>(context => context.Name == "first"), Is.SameAs(first));
            Assert.That(frame.FindParameter<Context>(context => context.Name == "third"), Is.SameAs(third));
            Assert.That(frame.FindParameter<Context>(_ => false), Is.Null);
        });
        frame.RemoveAllStack(context => ReferenceEquals(context, first) || ReferenceEquals(context, third));
        Assert.Multiple(() =>
        {
            Assert.That(frame.BackStack, Is.Empty);
            Assert.That(frame.ForwardStack, Is.Empty);
            Assert.That(frame.Content, Is.SameAs(currentPage));
        });
    }

    public sealed class Page : UserControl;

    private sealed class PageResolver : IPageResolver
    {
        public int GetOrder(Type pagetype) => 0;

        public int GetDepth(Type pagetype) => 0;

        public Type GetPageType(Type contextType) => typeof(Page);
    }
}
