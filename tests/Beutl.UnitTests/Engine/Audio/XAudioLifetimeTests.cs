using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Beutl.Audio.Platforms.XAudio2;
using Vortice.Multimedia;
using Vortice.XAudio2;

namespace Beutl.UnitTests.Engine.Audio;

[TestFixture]
public class XAudioLifetimeTests
{
    [Test]
    public Task Buffer_DisposeTwice_DoesNotFreeNativeMemoryTwice()
    {
        return TestWorkerProgram.RunAsync(TestWorkerProgram.XAudioLifetimeWorkerArgument, "dispose-buffer-twice");
    }

    [Test]
    public void Buffer_Dispose_ClearsTheNativeDescriptor()
    {
        var buffer = new XAudioBuffer();
        buffer.BufferData<float>([0.25f, -0.25f], new WaveFormat(44100, 32, 2));
        buffer.Dispose();

        Assert.Multiple(() =>
        {
            Assert.That(buffer.Buffer.AudioDataPointer, Is.EqualTo(IntPtr.Zero));
            Assert.That(buffer.Buffer.AudioBytes, Is.Zero);
            Assert.That(buffer.SizeInBytes, Is.Zero);
        });
    }

    [Test]
    public void Buffer_BufferDataAfterDispose_Throws()
    {
        var buffer = new XAudioBuffer();
        buffer.Dispose();

        try
        {
            Assert.Throws<ObjectDisposedException>(() =>
                buffer.BufferData<float>([0.25f, -0.25f], new WaveFormat(44100, 32, 2)));
        }
        finally
        {
            buffer.Dispose();
        }
    }

    [Test, Platform("Win")]
    public Task Context_FinalizingIncompleteConstruction_DoesNotTerminateTheProcess()
    {
        return TestWorkerProgram.RunAsync(TestWorkerProgram.XAudioLifetimeWorkerArgument, "finalize-incomplete-context");
    }

    [Test, Platform("Win")]
    public Task Context_FailedNativeInitialization_DoesNotTerminateTheProcess()
    {
        return TestWorkerProgram.RunAsync(TestWorkerProgram.XAudioLifetimeWorkerArgument, "create-context");
    }

    internal static void RunWorker(string action)
    {
        switch (action)
        {
            case "dispose-source-twice":
            case "use-disposed-source":
            case "queue-disposed-source":
                RunSourceWorker(action);
                return;
        }

        RunBufferOrContextWorker(action);
    }

    [Test, Platform("Win")]
    public Task Source_DisposeTwice_DestroysNativeVoiceOnce()
    {
        return TestWorkerProgram.RunAsync(TestWorkerProgram.XAudioLifetimeWorkerArgument, "dispose-source-twice");
    }

    [Test, Platform("Win")]
    public Task Source_AfterDispose_StateAndPlaybackCallsDoNotUseDestroyedVoice()
    {
        return TestWorkerProgram.RunAsync(TestWorkerProgram.XAudioLifetimeWorkerArgument, "use-disposed-source");
    }

    [Test, Platform("Win")]
    public Task Source_QueueBufferAfterDispose_ThrowsObjectDisposedException()
    {
        return TestWorkerProgram.RunAsync(TestWorkerProgram.XAudioLifetimeWorkerArgument, "queue-disposed-source");
    }

    private static void RunBufferOrContextWorker(string action)
    {
        switch (action)
        {
            case "dispose-buffer-twice":
                var buffer = new XAudioBuffer();
                buffer.BufferData<float>([0.25f, -0.25f], new WaveFormat(44100, 32, 2));
                buffer.Dispose();
                buffer.Dispose();
                break;
            case "finalize-incomplete-context":
                // A throwing constructor leaves the finalizable object in this state. Keep the
                // reproduction independent of whether the test machine has an audio endpoint.
                CreateIncompleteContext();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                break;
            case "create-context":
                TryCreateContext();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                break;
            default:
                throw new ArgumentException("Unknown XAudio lifetime test action.", nameof(action));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CreateIncompleteContext()
    {
        _ = RuntimeHelpers.GetUninitializedObject(typeof(XAudioContext));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void TryCreateContext()
    {
        try
        {
            using var context = new XAudioContext();
            Console.WriteLine("XAudio2 device and mastering voice initialized successfully.");
        }
        catch (SharpGen.Runtime.SharpGenException ex)
        {
            // Missing/disabled endpoints are a supported failure; finalization after the
            // constructor throws must still be safe. Print the real native result as evidence.
            Console.WriteLine(ex);
        }
    }

    private static void RunSourceWorker(string action)
    {
        using var context = (XAudioContext)RuntimeHelpers.GetUninitializedObject(typeof(XAudioContext));
        using var nativeVoice = new NativeVoiceStub();
        var source = new XAudioSource(context);
        typeof(XAudioSource).GetField("_sourceVoice",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(source, nativeVoice.Voice);

        source.Dispose();
        Assert.That(nativeVoice.DestroyCount, Is.EqualTo(1));
        Console.WriteLine($"Source voice destroyed {nativeVoice.DestroyCount} time; wrapper pointer {nativeVoice.Voice.NativePointer}.");

        switch (action)
        {
            case "dispose-source-twice":
                source.Dispose();
                Assert.That(nativeVoice.DestroyCount, Is.EqualTo(1));
                break;
            case "use-disposed-source":
                Assert.Multiple(() =>
                {
                    Assert.That(source.BuffersQueued, Is.EqualTo(-1));
                    Assert.That(source.SamplesPlayed, Is.Zero);
                    Assert.That(source.IsPlaying(), Is.False);
                });
                source.Play();
                source.Stop();
                source.Flush();
                break;
            case "queue-disposed-source":
                {
                    using var buffer = new XAudioBuffer();
                    buffer.BufferData<float>([0.25f, -0.25f], new WaveFormat(44100, 32, 2));
                    Assert.Throws<ObjectDisposedException>(() => source.QueueBuffer(buffer));
                    break;
                }
        }
    }

    private sealed unsafe class NativeVoiceStub : IDisposable
    {
        private static int s_destroyCount;
        private readonly void** _vtable;
        private readonly void** _instance;

        public NativeVoiceStub()
        {
            s_destroyCount = 0;
            _vtable = (void**)NativeMemory.AllocZeroed(29, (nuint)sizeof(void*));
            _instance = (void**)NativeMemory.AllocZeroed((nuint)sizeof(void*));
            // IXAudio2Voice.DestroyVoice occupies slot 18 in the XAudio2 ABI. All other slots
            // stay null: touching one after disposal must fail in this bounded worker process.
            _vtable[18] = (delegate* unmanaged[Stdcall]<IntPtr, void>)&DestroyVoice;
            *_instance = _vtable;
            Voice = new IXAudio2SourceVoice((IntPtr)_instance);
        }

        public IXAudio2SourceVoice Voice { get; }

        public int DestroyCount => s_destroyCount;

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static void DestroyVoice(IntPtr instance)
        {
            s_destroyCount++;
        }

        public void Dispose()
        {
            Voice.Dispose();
            NativeMemory.Free(_instance);
            NativeMemory.Free(_vtable);
        }
    }
}
