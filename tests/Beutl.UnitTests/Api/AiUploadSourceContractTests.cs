using System.Reflection;
using Beutl.Api.Services;

namespace Beutl.UnitTests.Api;

[TestFixture]
public sealed class AiUploadSourceContractTests
{
    [Test]
    public async Task OpenReadAsync_IsPublicForExternalCapabilityImplementations()
    {
        MethodInfo? method = typeof(AiUploadSource).GetMethod(
            nameof(AiUploadSource.OpenReadAsync),
            BindingFlags.Instance | BindingFlags.Public,
            binder: null,
            types: [typeof(CancellationToken)],
            modifiers: null);
        using var cancellationTokenSource = new CancellationTokenSource();
        CancellationToken observedToken = default;
        var expectedStream = new MemoryStream([1, 2, 3]);
        var source = new AiUploadSource(
            "input.bin",
            "application/octet-stream",
            cancellationToken =>
            {
                observedToken = cancellationToken;
                return ValueTask.FromResult<Stream>(expectedStream);
            });

        await using Stream stream = await source.OpenReadAsync(cancellationTokenSource.Token);

        Assert.Multiple(() =>
        {
            Assert.That(method, Is.Not.Null);
            Assert.That(method!.ReturnType, Is.EqualTo(typeof(ValueTask<Stream>)));
            Assert.That(method.GetParameters().Single().HasDefaultValue, Is.False);
            Assert.That(stream, Is.SameAs(expectedStream));
            Assert.That(observedToken, Is.EqualTo(cancellationTokenSource.Token));
        });
    }
}
