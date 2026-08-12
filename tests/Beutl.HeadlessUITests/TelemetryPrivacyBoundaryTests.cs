using System.Diagnostics;
using Beutl.ExceptionHandler;
using Beutl.Services;
using Beutl.Views;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class TelemetryPrivacyBoundaryTests
{
    [Test]
    public void CommandLinesAndFeedbackUrlsNeverContainTelemetryIdentityCanaries()
    {
        const string sessionCanary = "9f061e9d9f84472a9f451616461a0451";
        const string installationCanary = "5bb1aa9ef3a04d01907c918712d9d1a3";
        ProcessStartInfo exceptionHandler = UnhandledExceptionHandler.CreateExceptionHandlerStartInfo();

        Assert.Multiple(() =>
        {
            Assert.That(exceptionHandler.ArgumentList, Does.Not.Contain("--session-id"));
            Assert.That(string.Join(' ', exceptionHandler.ArgumentList), Does.Not.Contain(sessionCanary));
            Assert.That(string.Join(' ', exceptionHandler.ArgumentList), Does.Not.Contain(installationCanary));
            Assert.That(MainView.FeedbackUrl, Is.EqualTo("https://beutl.beditor.net/feedback"));
            Assert.That(MainWindowViewModel.FeedbackUrl, Is.EqualTo("https://beutl.beditor.net/feedback"));
            Assert.That(MainView.FeedbackUrl, Does.Not.Contain("traceId"));
            Assert.That(MainWindowViewModel.FeedbackUrl, Does.Not.Contain("traceId"));
            Assert.That(MainView.FeedbackUrl, Does.Not.Contain(sessionCanary));
            Assert.That(MainWindowViewModel.FeedbackUrl, Does.Not.Contain(installationCanary));
        });
    }
}
