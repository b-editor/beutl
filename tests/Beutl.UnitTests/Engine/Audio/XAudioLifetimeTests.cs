using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Beutl.Audio;
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
    public Task Buffer_ConcurrentDisposal_DoesNotFreeNativeMemoryTwice()
    {
        return TestWorkerProgram.RunAsync(TestWorkerProgram.XAudioLifetimeWorkerArgument, "dispose-buffer-concurrently");
    }

    [Test]
    public Task Buffer_ConcurrentDataReplacementAndDisposal_LeavesNoNativeDescriptor()
    {
        return TestWorkerProgram.RunAsync(TestWorkerProgram.XAudioLifetimeWorkerArgument, "write-and-dispose-buffer");
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
    public unsafe void Buffer_BufferDataFromAnAudioBuffer_HoldsInterleavedFloats()
    {
        using var audio = new Beutl.Audio.AudioBuffer(48000, 2, 2);
        audio.GetChannelData(0)[1] = 0.5f;
        audio.GetChannelData(1)[1] = -0.5f;
        using var buffer = new XAudioBuffer();

        buffer.BufferData(audio);

        Assert.Multiple(() =>
        {
            Assert.That(new ReadOnlySpan<float>((void*)buffer.Buffer.AudioDataPointer, 4).ToArray(),
                Is.EqualTo(new[] { 0f, 0f, 0.5f, -0.5f }));
            Assert.That(buffer.Format!.SampleRate, Is.EqualTo(48000));
            Assert.That(buffer.Format.Encoding, Is.EqualTo(WaveFormatEncoding.IeeeFloat));
            Assert.That(buffer.Format.Channels, Is.EqualTo(2));
        });
    }

    [TestCase(2, 0x3)]
    [TestCase(4, 0x33)]
    [TestCase(6, 0x3F)]
    [TestCase(6, 0x60F)]
    [TestCase(8, 0x63F)]
    public unsafe void Buffer_ExplicitLayout_PreservesFloatSamplesAndSpeakerMask(int channels, int mask)
    {
        var layout = new AudioChannelLayout(channels, (AudioSpeakers)mask);
        using var audio = AudioTestBuffers.CreateBuffer(channels, 3,
            (channel, sample) => sample == 0 ? BitConverter.Int32BitsToSingle(unchecked((int)0x80000000))
                : sample == 1 ? BitConverter.Int32BitsToSingle(0x7FC12345) : (channel + 1) * -0.125f);
        using var buffer = new XAudioBuffer();

        buffer.BufferData(audio, layout);

        Assert.That(buffer.Format, Is.TypeOf<WaveFormatExtensible>());
        var format = (WaveFormatExtensible)buffer.Format!;
        Assert.Multiple(() =>
        {
            Assert.That(format.Channels, Is.EqualTo(channels));
            Assert.That(format.ChannelMask, Is.EqualTo(mask));
            Assert.That(format.SampleRate, Is.EqualTo(audio.SampleRate));
            Assert.That(format.BitsPerSample, Is.EqualTo(32));
            Assert.That(format.Encoding, Is.EqualTo(WaveFormatEncoding.Extensible));
            Assert.That(format.GuidSubFormat, Is.EqualTo(new Guid("00000003-0000-0010-8000-00aa00389b71")));
            Assert.That(format.BlockAlign, Is.EqualTo(channels * sizeof(float)));
            Assert.That(buffer.SizeInBytes, Is.EqualTo(channels * audio.SampleCount * sizeof(float)));
            Assert.That(buffer.Buffer.AudioBytes, Is.EqualTo(buffer.SizeInBytes));
        });
        var submitted = new ReadOnlySpan<int>((void*)buffer.Buffer.AudioDataPointer, channels * audio.SampleCount);
        for (int sample = 0; sample < audio.SampleCount; sample++)
        {
            for (int channel = 0; channel < channels; channel++)
                Assert.That(submitted[sample * channels + channel], Is.EqualTo(BitConverter.SingleToInt32Bits(audio.GetChannelData(channel)[sample])));
        }
        if (channels == 2)
        {
            using var legacy = new XAudioBuffer();
            legacy.BufferData(audio);
            Assert.That(submitted.ToArray(), Is.EqualTo(new ReadOnlySpan<int>((void*)legacy.Buffer.AudioDataPointer, submitted.Length).ToArray()));
        }
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

    [TestCase(false), TestCase(true), Platform("Win")]
    public Task Context_FailedNativeInitialization_DoesNotTerminateTheProcess(bool useDeviceDefaults)
    {
        return TestWorkerProgram.RunAsync(TestWorkerProgram.XAudioLifetimeWorkerArgument,
            useDeviceDefaults ? "create-context-device-defaults" : "create-context");
    }

    internal static void RunWorker(string action)
    {
        switch (action)
        {
            case "dispose-buffer-twice":
                var buffer = new XAudioBuffer();
                buffer.BufferData<float>([0.25f, -0.25f], new WaveFormat(44100, 32, 2));
                buffer.Dispose();
                buffer.Dispose();
                break;
            case "dispose-buffer-concurrently":
                RunConcurrentBufferDisposal();
                break;
            case "write-and-dispose-buffer":
                RunConcurrentBufferDisposal(writeData: true);
                break;
            case "finalize-incomplete-context":
                // A throwing constructor leaves the finalizable object in this state. Keep the
                // reproduction independent of whether the test machine has an audio endpoint.
                CreateIncompleteContext();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                break;
            case "create-context":
            case "create-context-device-defaults":
                TryCreateContext(action == "create-context-device-defaults");
                GC.Collect();
                GC.WaitForPendingFinalizers();
                break;
            case "dispose-source-twice":
            case "use-disposed-source":
            case "queue-disposed-source":
                RunSourceWorker(action);
                break;
            case "dispose-source-during-create":
            case "dispose-source-during-submit":
                RunConcurrentSourceDisposal(action == "dispose-source-during-create");
                break;
            default:
                throw new ArgumentException("Unknown XAudio lifetime test action.", nameof(action));
        }
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

    [TestCase("create"), TestCase("submit"), Platform("Win")]
    public Task Source_ConcurrentQueueAndDispose_RetiresVoiceAfterQueueOperation(string phase)
    {
        return TestWorkerProgram.RunAsync(TestWorkerProgram.XAudioLifetimeWorkerArgument, "dispose-source-during-" + phase);
    }

    private static void RunConcurrentBufferDisposal(bool writeData = false)
    {
        const int iterations = 256;
        const int disposerCount = 4;
        using var barrier = new Barrier(disposerCount + 1);
        XAudioBuffer? buffer = null;
        byte[] replacement = new byte[64 * 1024];
        var disposers = Enumerable.Range(0, disposerCount).Select(index => new Thread(() =>
        {
            for (int i = 0; i < iterations; i++)
            {
                barrier.SignalAndWait();
                if (writeData && index < 2)
                {
                    try { buffer!.BufferData<byte>(replacement, new WaveFormat(44100, 32, 2)); }
                    catch (ObjectDisposedException) { }
                }
                else
                {
                    buffer!.Dispose();
                }
                barrier.SignalAndWait();
            }
        })
        { IsBackground = true }).ToArray();
        foreach (Thread disposer in disposers)
            disposer.Start();
        for (int i = 0; i < iterations; i++)
        {
            buffer = new XAudioBuffer();
            buffer.BufferData<float>([0.25f, -0.25f], new WaveFormat(44100, 32, 2));
            barrier.SignalAndWait();
            barrier.SignalAndWait();
            Assert.That(buffer.Buffer.AudioDataPointer, Is.EqualTo(IntPtr.Zero));
            Assert.That(buffer.Buffer.AudioBytes, Is.Zero);
            Assert.That(buffer.SizeInBytes, Is.Zero);
            Assert.Throws<ObjectDisposedException>(() =>
                buffer.BufferData<float>([0.25f, -0.25f], new WaveFormat(44100, 32, 2)));
        }
        foreach (Thread disposer in disposers)
            Assert.That(disposer.Join(TimeSpan.FromSeconds(5)), Is.True);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void CreateIncompleteContext()
    {
        _ = RuntimeHelpers.GetUninitializedObject(typeof(XAudioContext));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void TryCreateContext(bool useDeviceDefaults)
    {
        try
        {
            using var context = useDeviceDefaults ? new XAudioContext(useDeviceDefaults: true) : new XAudioContext();
            if (useDeviceDefaults)
            {
                Assert.That(context.DeviceLayout.Speakers, Is.EqualTo((AudioSpeakers)context.MasteringVoice.ChannelMask));
            }
            else
            {
                Assert.That(context.DeviceLayout, Is.EqualTo(AudioChannelLayout.Stereo));
                Assert.That(context.MasteringVoice.VoiceDetails.InputChannels, Is.EqualTo(2));
                Assert.That(context.MasteringVoice.VoiceDetails.InputSampleRate, Is.EqualTo(44100));
            }
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
        using var nativeVoice = new NativeVoiceStub();
        WaveFormat? sourceFormat = null;
        using var source = new XAudioSource(format =>
        {
            sourceFormat = format;
            return nativeVoice.Voice;
        });
        using var buffer = new XAudioBuffer();
        using var audio = AudioTestBuffers.CreateBuffer(8, 1, (channel, _) => channel * 0.125f);
        buffer.BufferData(audio, AudioChannelLayout.Surround71);
        source.QueueBuffer(buffer);
        Assert.That(sourceFormat, Is.SameAs(buffer.Format));
        Assert.That(((WaveFormatExtensible)sourceFormat!).ChannelMask, Is.EqualTo(0x63F));

        source.Dispose();
        Assert.That(nativeVoice.DestroyCount, Is.EqualTo(1));

        switch (action)
        {
            case "dispose-source-twice":
                source.Dispose();
                Assert.That(nativeVoice.DestroyCount, Is.EqualTo(1));
                break;
            case "use-disposed-source":
                Assert.That(source.BuffersQueued, Is.EqualTo(-1));
                Assert.That(source.SamplesPlayed, Is.Zero);
                Assert.That(source.IsPlaying(), Is.False);
                source.Play();
                source.Stop();
                source.Flush();
                break;
            case "queue-disposed-source":
                Assert.Throws<ObjectDisposedException>(() => source.QueueBuffer(buffer));
                break;
        }
    }

    private static void RunConcurrentSourceDisposal(bool duringCreation)
    {
        using var releaseQueue = new ManualResetEventSlim();
        var queueEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Action holdQueue = () =>
        {
            queueEntered.TrySetResult();
            if (!releaseQueue.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The test did not release voice creation/submission.");
        };
        using var nativeVoice = new NativeVoiceStub(duringCreation ? null : holdQueue);
        using var source = new XAudioSource(_ =>
        {
            if (duringCreation)
                holdQueue();
            return nativeVoice.Voice;
        });
        using var buffer = new XAudioBuffer();
        buffer.BufferData<float>([0.25f, -0.25f], new WaveFormat(44100, 32, 2));
        Task queue = Task.Run(() => source.QueueBuffer(buffer));
        Task? dispose = null;
        try
        {
            queueEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            dispose = Task.Run(() =>
            {
                disposeEntered.SetResult();
                source.Dispose();
            });
            disposeEntered.Task.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            Task.WhenAny(dispose, Task.Delay(100)).GetAwaiter().GetResult();
            Assert.That(dispose.IsCompleted, Is.False,
                "Voice disposal must wait for the admitted native creation/submission.");
            Assert.That(nativeVoice.DestroyCount, Is.Zero);
        }
        finally
        {
            releaseQueue.Set();
            queue.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            dispose?.WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
            source.Dispose();
        }
        Assert.That(nativeVoice.DestroyCount, Is.EqualTo(1));
        Assert.That(nativeVoice.SubmitCount, Is.EqualTo(1));
        Assert.Throws<ObjectDisposedException>(() => source.QueueBuffer(buffer));
    }

    private sealed unsafe class NativeVoiceStub : IDisposable
    {
        private static int s_destroyCount;
        private static int s_submitCount;
        private static Action? s_submit;
        private readonly void** _vtable;
        private readonly void** _instance;

        public NativeVoiceStub(Action? submit = null)
        {
            s_destroyCount = 0;
            s_submitCount = 0;
            s_submit = submit;
            _vtable = (void**)NativeMemory.AllocZeroed(29, (nuint)sizeof(void*));
            _instance = (void**)NativeMemory.AllocZeroed((nuint)sizeof(void*));
            // IXAudio2Voice.DestroyVoice occupies slot 18 in the XAudio2 ABI. All other slots
            // stay null: touching one after disposal must fail in this bounded worker process.
            _vtable[18] = (delegate* unmanaged[Stdcall]<IntPtr, void>)&DestroyVoice;
            _vtable[21] = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, int>)&SubmitSourceBuffer;
            *_instance = _vtable;
            Voice = new IXAudio2SourceVoice((IntPtr)_instance);
        }

        public IXAudio2SourceVoice Voice { get; }

        public int DestroyCount => s_destroyCount;

        public int SubmitCount => s_submitCount;

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static int SubmitSourceBuffer(IntPtr instance, IntPtr buffer, IntPtr bufferWma)
        {
            Interlocked.Increment(ref s_submitCount);
            try { s_submit?.Invoke(); }
            catch (Exception) { return unchecked((int)0x80004005); }
            return 0;
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
        private static void DestroyVoice(IntPtr instance)
        {
            Interlocked.Increment(ref s_destroyCount);
        }

        public void Dispose()
        {
            s_submit = null;
            Voice.Dispose();
            NativeMemory.Free(_instance);
            NativeMemory.Free(_vtable);
        }
    }
}
