using System.Net.Http;
using Avalonia.Controls;
using Avalonia.Headless.NUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Beutl.Api;
using Beutl.Api.Clients;
using Beutl.Api.Objects;
using Beutl.Api.Services;
using Beutl.Pages.ExtensionsPages.DiscoverPages;
using Beutl.Testing.Headless;
using Beutl.ViewModels.ExtensionsPages.DiscoverPages;

namespace Beutl.HeadlessUITests;

// Layout-only assertions for the extension search result card (SearchPage). Long package/display
// names, descriptions and author names used to break the card layout because the card's TextBlocks
// declared no TextTrimming / TextWrapping and the author/price column was an unbounded Auto column.
// These tests pin the fix: the single-line fields ellipsize, the description wraps with a capped line
// count, and the author column is bounded so a long author cannot starve the name column or widen the
// card. They inflate the real card headlessly (a real Package fed into the real ViewModel's Packages
// list, no network) and assert arranged layout / TextBlock properties only - never pixels, which
// crashes the software-Vulkan host when a heavy view is frame-captured.
[TestFixture]
public class SearchPageCardLayoutTests
{
    private const string LongName = "ThisIsAnExtremelyLongPackageNameThatOverflowsTheCardWithoutTrimming";
    private const string LongDisplayName = "An Extremely Long Human Friendly Display Name That Must Be Ellipsized To Fit";
    private const string LongDescription =
        "A very long short description that should wrap to at most a couple of lines and then be "
        + "ellipsized rather than expanding the card height without bound, going on and on and on and on.";
    private const string LongOwner = "AnExtremelyLongAuthorAccountNameThatShouldNotPushTheCardWider";

    private static BeutlApiApplication CreateClients() => new(new HttpClient(), new ExtensionProvider());

    private static Package CreatePackage(BeutlApiApplication clients)
    {
        var ownerResponse = new ProfileResponse
        {
            Id = "owner-id",
            Name = LongOwner,
            DisplayName = LongOwner,
            Bio = null,
            IconId = null,
            IconUrl = null,
        };
        var profile = new Profile(ownerResponse, clients);
        var response = new PackageResponse
        {
            Id = "package-id",
            Owner = ownerResponse,
            Name = LongName,
            DisplayName = LongDisplayName,
            Description = LongDescription,
            ShortDescription = LongDescription,
            WebSite = "",
            Tags = [],
            LogoId = null,
            LogoUrl = null,
            Screenshots = [],
            Currency = "JPY",
            Price = 999900,
            Paid = true,
            Owned = false,
        };
        return new Package(profile, response, clients);
    }

    [AvaloniaTest]
    public void Long_card_text_ellipsizes_wraps_and_stays_bounded()
    {
        BeutlApiApplication clients = CreateClients();
        var viewModel = new SearchPageViewModel(new DiscoverService(clients), "search");
        viewModel.Packages.Add(CreatePackage(clients));

        var view = new SearchPage { DataContext = viewModel };
        var window = new Window { Content = view, Width = 800, Height = 600 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            List<TextBlock> blocks = view.GetVisualDescendants().OfType<TextBlock>().ToList();
            TextBlock? name = blocks.FirstOrDefault(t => t.Text == LongName);
            TextBlock? displayName = blocks.FirstOrDefault(t => t.Text == LongDisplayName);
            TextBlock? description = blocks.FirstOrDefault(t => t.Text == LongDescription);
            TextBlock? owner = blocks.FirstOrDefault(t => t.Text == LongOwner);

            Assert.That(
                name,
                Is.Not.Null,
                "the package card did not materialize headlessly - revisit the test host before trusting it");
            Assert.That(displayName, Is.Not.Null);
            Assert.That(description, Is.Not.Null);
            Assert.That(owner, Is.Not.Null);

            Assert.That(
                name!.TextTrimming,
                Is.EqualTo(TextTrimming.CharacterEllipsis),
                "the package name must ellipsize instead of clipping");
            Assert.That(
                displayName!.TextTrimming,
                Is.EqualTo(TextTrimming.CharacterEllipsis),
                "the display name must ellipsize instead of clipping");
            Assert.That(
                owner!.TextTrimming,
                Is.EqualTo(TextTrimming.CharacterEllipsis),
                "the author name must ellipsize instead of clipping");

            Assert.That(
                description!.TextWrapping,
                Is.EqualTo(TextWrapping.Wrap),
                "the description must wrap rather than overflow on one line");
            Assert.That(
                description.TextTrimming,
                Is.EqualTo(TextTrimming.CharacterEllipsis),
                "the description must ellipsize once it hits its line cap");
            Assert.That(
                description.MaxLines,
                Is.GreaterThan(0),
                "the description must cap its line count so the card height stays bounded");

            // A long author must not be allowed to grow the Auto column unbounded: the column is now
            // capped so the name column keeps room and the card does not widen off-screen.
            Assert.That(
                double.IsFinite(owner.MaxWidth),
                Is.True,
                "the author column must declare an explicit MaxWidth cap");
            Assert.That(
                owner.Bounds.Width,
                Is.LessThanOrEqualTo(owner.MaxWidth + 1),
                "the arranged author width must respect its MaxWidth cap");
            Assert.That(
                name.Bounds.Width,
                Is.GreaterThan(0),
                "the name column must keep positive width even next to a very long author");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }
}
