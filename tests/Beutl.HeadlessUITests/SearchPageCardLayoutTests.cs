using System.Net.Http;
using Avalonia;
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

// Pins the store's package card for long text: single-line fields ellipsize, the description wraps with a
// line cap, and every line, the price included, stays inside the card. Layout and TextBlock properties only.
[TestFixture]
public class SearchPageCardLayoutTests
{
    private const string LongName = "ThisIsAnExtremelyLongPackageNameThatOverflowsTheCardWithoutTrimming";
    private const string LongDisplayName = "An Extremely Long Human Friendly Display Name That Must Be Ellipsized To Fit";
    private const string LongDescription =
        "A very long short description that should wrap to at most a couple of lines and then be "
        + "ellipsized rather than expanding the card height without bound, going on and on and on and on.";
    private const string LongOwner = "AnExtremelyLongAuthorAccountNameThatShouldNotPushTheCardWider";

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
    public async Task Long_card_text_ellipsizes_wraps_and_stays_bounded()
    {
        using var httpClient = new HttpClient();
        await using var clients = new BeutlApiApplication(httpClient, new ExtensionProvider());
        var viewModel = new SearchPageViewModel(new DiscoverService(clients), "search");
        Package package = CreatePackage(clients);
        viewModel.Packages.Add(package);

        var view = new SearchPage { DataContext = viewModel };
        var window = new Window { Content = view, Width = 800, Height = 600 };

        try
        {
            window.Show();
            HeadlessTestHelpers.Render();

            Button? card = view.GetVisualDescendants().OfType<Button>().FirstOrDefault(b => b.DataContext == package);
            Assert.That(
                card,
                Is.Not.Null,
                "the package card did not materialize headlessly - revisit the test host before trusting it");

            List<TextBlock> blocks = card!.GetVisualDescendants().OfType<TextBlock>().ToList();
            TextBlock? name = blocks.FirstOrDefault(t => t.Text == LongName);
            TextBlock? displayName = blocks.FirstOrDefault(t => t.Text == LongDisplayName);
            TextBlock? description = blocks.FirstOrDefault(t => t.Text == LongDescription);
            TextBlock? owner = blocks.FirstOrDefault(t => t.Text == LongOwner);
            TextBlock? price = blocks.FirstOrDefault(t => t.Text == package.FormattedPrice.Value);

            Assert.That(displayName, Is.Not.Null);
            Assert.That(description, Is.Not.Null);
            Assert.That(owner, Is.Not.Null);
            Assert.That(price, Is.Not.Null);

            Assert.That(
                name is null || !name.IsEffectivelyVisible,
                Is.True,
                "the package name only stands in for a missing display name");

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

            Assert.That(
                double.IsFinite(price!.MaxWidth),
                Is.True,
                "the price must declare an explicit MaxWidth cap");
            Assert.That(
                price.TextTrimming,
                Is.EqualTo(TextTrimming.CharacterEllipsis),
                "the price must ellipsize instead of clipping");

            // Every line stays inside the card, and the price keeps its place next to a very long author.
            foreach (TextBlock block in new[] { displayName, owner, description, price })
            {
                Point? right = block.TranslatePoint(new Point(block.Bounds.Width, 0), card);
                Assert.That(right, Is.Not.Null);
                Assert.That(
                    right!.Value.X,
                    Is.LessThanOrEqualTo(card.Bounds.Width + 1),
                    $"'{block.Text}' must stay inside the card");
            }

            Assert.That(price.IsEffectivelyVisible, Is.True, "a paid package must show its price");
            Assert.That(price.Bounds.Width, Is.GreaterThan(0), "the price must keep room next to a very long author");
            Assert.That(owner.Bounds.Width, Is.GreaterThan(0), "the author must keep room next to the price");
        }
        finally
        {
            window.Close();
            HeadlessTestHelpers.Settle();
        }
    }
}
