using Beutl.Editor.Components.ColorGradingTab.ViewModels;
using Beutl.Editor.Services;
using Beutl.Extensibility;
using Moq;

namespace Beutl.UnitTests.Editor;

[TestFixture]
public sealed class ColorGradingTabViewModelTests
{
    [Test]
    public async Task CloseAfterDetachAsync_ContainsHostCloseFailure()
    {
        var context = new Mock<IEditorContext>();
        context.Setup(x => x.CloseToolTabAsync(It.IsAny<IToolContext>()))
            .Returns(new ValueTask(Task.FromException(new InvalidOperationException("close failed"))));
        var viewModel = new ColorGradingTabViewModel(context.Object);

        Assert.DoesNotThrowAsync(async () => await viewModel.CloseAfterDetachAsync());

        context.Verify(x => x.CloseToolTabAsync(viewModel), Times.Once);
    }
}
