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
        ConstructorInfo? constructor = typeof(AiUploadSource).GetConstructor(
            [typeof(string), typeof(string), typeof(Func<CancellationToken, ValueTask<Stream>>), typeof(long)]);
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
            },
            expectedStream.Length);

        await using Stream stream = await source.OpenReadAsync(cancellationTokenSource.Token);

        Assert.Multiple(() =>
        {
            Assert.That(method, Is.Not.Null);
            Assert.That(constructor, Is.Not.Null);
            Assert.That(constructor!.GetParameters().Last().IsOptional, Is.False);
            Assert.That(typeof(AiUploadSource).GetProperty(nameof(AiUploadSource.Length))!.PropertyType,
                Is.EqualTo(typeof(long)));
            Assert.That(method!.ReturnType, Is.EqualTo(typeof(ValueTask<Stream>)));
            Assert.That(method.GetParameters().Single().HasDefaultValue, Is.False);
            Assert.That(stream, Is.SameAs(expectedStream));
            Assert.That(observedToken, Is.EqualTo(cancellationTokenSource.Token));
        });
    }

    [Test]
    public async Task FromBytesSnapshotsMutableCallerMemoryAtConstruction()
    {
        byte[] callerBuffer = [1, 2, 3];
        AiUploadSource source = AiUploadSource.FromBytes(
            "snapshot.bin",
            "application/octet-stream",
            callerBuffer);
        callerBuffer[0] = 9;

        await using Stream first = await source.OpenReadAsync(CancellationToken.None);
        await using Stream second = await source.OpenReadAsync(CancellationToken.None);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(await ReadAllAsync(first), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(await ReadAllAsync(second), Is.EqualTo(new byte[] { 1, 2, 3 }));
        }
    }

    [Test]
    public void Validation_RejectsASeekableStreamThatExceedsItsDeclaredLength()
    {
        var source = new AiUploadSource(
            "input.bin",
            "application/octet-stream",
            _ => ValueTask.FromResult<Stream>(new MemoryStream(new byte[4], writable: false)),
            length: 3);

        Assert.ThrowsAsync<AiFileTooLargeException>(async () =>
        {
            await using Stream _ = await AiUploadValidation.OpenAsync(
                source,
                10,
                CancellationToken.None);
        });
    }

    [Test]
    public async Task Validation_BuffersAndMeasuresANonSeekableStream()
    {
        var original = new NonSeekableStream([1, 2, 3]);
        var source = new AiUploadSource(
            "input.bin",
            "application/octet-stream",
            _ => ValueTask.FromResult<Stream>(original),
            length: 3);

        await using Stream validated = await AiUploadValidation.OpenAsync(
            source,
            maximumBytes: 3,
            CancellationToken.None);

        using var copy = new MemoryStream();
        await validated.CopyToAsync(copy);
        Assert.Multiple(() =>
        {
            Assert.That(copy.ToArray(), Is.EqualTo(new byte[] { 1, 2, 3 }));
            Assert.That(original.WasDisposed, Is.True);
        });
    }

    [TestCase(2, 3)]
    [TestCase(3, 2)]
    public void Validation_RejectsANonSeekableStreamBeyondTheDeclaredOrRouteLimit(
        long declaredLength,
        long maximumBytes)
    {
        var original = new NonSeekableStream([1, 2, 3]);
        var source = new AiUploadSource(
            "input.bin",
            "application/octet-stream",
            _ => ValueTask.FromResult<Stream>(original),
            declaredLength);

        Assert.ThrowsAsync<AiFileTooLargeException>(async () =>
        {
            await using Stream _ = await AiUploadValidation.OpenAsync(
                source,
                maximumBytes,
                CancellationToken.None);
        });
        Assert.That(original.WasDisposed, Is.True);
    }

    private static async Task<byte[]> ReadAllAsync(Stream stream)
    {
        using var destination = new MemoryStream();
        await stream.CopyToAsync(destination);
        return destination.ToArray();
    }

    private sealed class NonSeekableStream(byte[] bytes) : Stream
    {
        private readonly MemoryStream _inner = new(bytes, writable: false);

        public bool WasDisposed { get; private set; }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush() => throw new NotSupportedException();

        public override int Read(byte[] buffer, int offset, int count)
            => _inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken = default)
            => _inner.ReadAsync(buffer, cancellationToken);

        public override long Seek(long offset, SeekOrigin origin)
            => throw new NotSupportedException();

        public override void SetLength(long value)
            => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count)
            => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            if (disposing)
                _inner.Dispose();
            base.Dispose(disposing);
        }

        public override async ValueTask DisposeAsync()
        {
            WasDisposed = true;
            await _inner.DisposeAsync();
            GC.SuppressFinalize(this);
        }
    }
}
