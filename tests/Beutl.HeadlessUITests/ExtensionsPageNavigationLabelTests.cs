using System.Globalization;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Beutl.Language;
using Beutl.Pages;
using Beutl.Testing.Headless;
using FluentAvalonia.UI.Controls;

namespace Beutl.HeadlessUITests;

// The extensions window's Home and Library tabs are labelled in the UI language, like the rest of the window.
[TestFixture]
public class ExtensionsPageNavigationLabelTests
{
    [AvaloniaTest]
    public void Navigation_items_use_the_translated_labels()
    {
        CultureInfo previousCulture = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ja-JP");
        var page = new ExtensionsPage();
        try
        {
            FANavigationView nav = page.FindControl<FANavigationView>("nav")!;

            Assert.That(
                nav.MenuItemsSource!.Cast<FANavigationViewItem>().Select(item => item.Content),
                Is.EqualTo(new object[] { Strings.Home, Strings.Library }));
            Assert.That(Strings.Home, Is.Not.EqualTo("Home"), "the labels are checked in a language that translates them");
        }
        finally
        {
            CultureInfo.CurrentUICulture = previousCulture;
            page.Close();
            HeadlessTestHelpers.Settle();
        }
    }
}
