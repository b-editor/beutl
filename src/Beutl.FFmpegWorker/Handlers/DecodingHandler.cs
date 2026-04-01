
using System.Collections.Concurrent;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegIpc.SharedMemory;
using Beutl.FFmpegWorker.Decoding;
using Beutl.Media.Decoding;

namespace Beutl.FFmpegWorker.Handlers;

internal sealed class DecodingHandler : IDisposable
{
    private const int DefaultSlotCount = 4;

    private readonly ConcurrentDictionary<int, ReaderState> _readers = [];
    private int _nextReaderId;
    private int _shmGeneration;

    private sealed class ReaderState : IDisposable
    {
        public required FFmpegReader Reader { get; init; }
        public SharedMemoryBuffer? VideoBuffer { get; set; }
        public SharedMemoryBuffer? AudioBuffer { get; set; }

        // 色空間キャッシュ: 前フレームと同じなら送信を省略
        public float[]? LastTransferFn { get; set; }
        public float[]? LastToXyzD50 { get; set; }

        // リングバッファ
        public int SlotCount { get; set; }
        public long SlotSize { get; set; }
        public int[] SlotFrameNumbers { get; set; } = [];
        public SlotMetadata[] Slots { get; set; } = [];
        public int NextWriteSlot { get; set; }
        public volatile int LastRequestedFrame = -1;
        public int LastServedSlot { get; set; } = -1;

        // プリフェッチ制御
        public SemaphoreSlim ReaderLock { get; } = new(1, 1);
        public CancellationTokenSource? PrefetchCts { get; set; }
        public Task? PrefetchTask { get; set; }
        public ManualResetEventSlim PrefetchSignal { get; } = new(false);

        public void InvalidateAllSlots()
        {
            for (int i = 0; i < SlotFrameNumbers.Length; i++)
            {
                SlotFrameNumbers[i] = -1;
                Slots[i] = default;
            }
            NextWriteSlot = 0;
            LastServedSlot = -1;
        }

        public int FindSlot(int frame)
        {
            for (int i = 0; i < SlotFrameNumbers.Length; i++)
            {
                if (SlotFrameNumbers[i] == frame)
                    return i;
            }
            return -1;
        }

        public void Dispose()
        {
            StopPrefetch();
            ReaderLock.Dispose();
            PrefetchSignal.Dispose();
            Reader.Dispose();
            VideoBuffer?.Dispose();
            AudioBuffer?.Dispose();
        }

        public void StopPrefetch()
        {
            if (PrefetchCts != null)
            {
                PrefetchCts.Cancel();
                try { PrefetchTask?.Wait(); } catch { }
                PrefetchCts.Dispose();
                PrefetchCts = null;
                PrefetchTask = null;
            }
        }
    }

    internal struct SlotMetadata
    {
        public int FrameNumber;
        public int Width;
        public int Height;
        public int BytesPerPixel;
        public int DataLength;
        public bool IsHdr;
        public float[]? TransferFn;
        public float[]? ToXyzD50;
    }

    public IpcMessage HandleOpen(IpcMessage msg)
    {
        var request = msg.GetPayload<OpenFileRequest>()!;
        var settings = new FFmpegDecodingSettings();
        settings.ThreadCount = request.ThreadCount;
        settings.Acceleration = (FFmpegDecodingSettings.AccelerationOptions)request.Acceleration;
        settings.ForceSrgbGamma = request.ForceSrgbGamma;

        var options = new MediaOptions((MediaMode)request.StreamsToLoad);
        var reader = new FFmpegReader(request.FilePath, options, settings);
        int id = Interlocked.Increment(ref _nextReaderId);

        var state = new ReaderState { Reader = reader };

        int slotCount = DefaultSlotCount;
        long slotSize = 0;

        // 共有メモリ作成（リングバッファ）
        if (reader.HasVideo)
        {
            int videoWidth = reader.VideoInfo.FrameSize.Width;
            int videoHeight = reader.VideoInfo.FrameSize.Height;
            slotSize = (long)videoWidth * videoHeight * 8 + 64; // RGBA64LE max

            long totalSize = slotSize * slotCount;
            string videoShmName = $"beutl-ffmpeg-video-{Environment.ProcessId}-{id}";
            state.VideoBuffer = SharedMemoryBuffer.Create(videoShmName, totalSize);

            state.SlotCount = slotCount;
            state.SlotSize = slotSize;
            state.SlotFrameNumbers = new int[slotCount];
            state.Slots = new SlotMetadata[slotCount];
            Array.Fill(state.SlotFrameNumbers, -1);
        }

        if (reader.HasAudio)
        {
            long audioBufferSize = reader.AudioInfo.SampleRate * 8 + 64; // 1 second stereo float
            string audioShmName = $"beutl-ffmpeg-audio-{Environment.ProcessId}-{id}";
            state.AudioBuffer = SharedMemoryBuffer.Create(audioShmName, audioBufferSize);
        }

        _readers[id] = state;

        var response = new OpenFileResponse
        {
            ReaderId = id,
            HasVideo = reader.HasVideo,
            HasAudio = reader.HasAudio,
            VideoSharedMemoryName = state.VideoBuffer?.Name,
            AudioSharedMemoryName = state.AudioBuffer?.Name,
            VideoRingBufferSlotCount = reader.HasVideo ? slotCount : 0,
            VideoRingBufferSlotSize = slotSize,
        };

        if (reader.HasVideo)
        {
            var vi = reader.VideoInfo;
            response.VideoCodecName = vi.CodecName;
            response.VideoNumFrames = vi.NumFrames;
            response.VideoWidth = vi.FrameSize.Width;
            response.VideoHeight = vi.FrameSize.Height;
            response.FrameRateNum = vi.FrameRate.Numerator;
            response.FrameRateDen = vi.FrameRate.Denominator;
            response.DurationNum = vi.Duration.Numerator;
            response.DurationDen = vi.Duration.Denominator;
        }

        if (reader.HasAudio)
        {
            var ai = reader.AudioInfo;
            response.AudioCodecName = ai.CodecName;
            response.AudioDurationNum = ai.Duration.Numerator;
            response.AudioDurationDen = ai.Duration.Denominator;
            response.AudioSampleRate = ai.SampleRate;
            response.AudioNumChannels = ai.NumChannels;
        }

        return IpcMessage.Create(msg.Id, MessageType.OpenFileResult, response);
    }

    public unsafe IpcMessage HandleReadVideo(IpcMessage msg)
    {
        var request = msg.GetPayload<ReadVideoRequest>()!;
        if (!_readers.TryGetValue(request.ReaderId, out var state))
            return IpcMessage.CreateError(msg.Id, $"Unknown reader ID: {request.ReaderId}");

        if (state.SlotCount > 0)
            return HandleReadVideoRingBuffer(msg.Id, request, state);

        return HandleReadVideoLegacy(msg.Id, request, state);
    }

    private unsafe IpcMessage HandleReadVideoRingBuffer(int msgId, ReadVideoRequest request, ReaderState state)
    {
        // キャッシュヒットチェック（ロック不要: SlotFrameNumbers は HandleReadVideo の
        // 呼び出しスレッドとプリフェッチスレッドでのみ書き込まれ、
        // HandleReadVideo 中はプリフェッチがキャンセルor待機中）
        state.ReaderLock.Wait();
        try
        {
            int hitSlot = state.FindSlot(request.Frame);
            if (hitSlot >= 0)
            {
                // キャッシュヒット: デコード不要
                ref var slotMeta = ref state.Slots[hitSlot];
                state.LastRequestedFrame = request.Frame;
                state.LastServedSlot = hitSlot;

                // 色空間差分チェック
                bool colorSpaceChanged = false;
                if (slotMeta.TransferFn != null && slotMeta.ToXyzD50 != null)
                {
                    colorSpaceChanged = !slotMeta.TransferFn.AsSpan().SequenceEqual(state.LastTransferFn)
                                        || !slotMeta.ToXyzD50.AsSpan().SequenceEqual(state.LastToXyzD50);
                    if (colorSpaceChanged)
                    {
                        state.LastTransferFn = slotMeta.TransferFn;
                        state.LastToXyzD50 = slotMeta.ToXyzD50;
                    }
                }

                // プリフェッチ再開シグナル
                state.PrefetchSignal.Set();

                return IpcMessage.Create(msgId, MessageType.ReadVideoResult, new ReadVideoResponse
                {
                    Success = true,
                    Width = slotMeta.Width,
                    Height = slotMeta.Height,
                    BytesPerPixel = slotMeta.BytesPerPixel,
                    DataLength = slotMeta.DataLength,
                    IsHdr = slotMeta.IsHdr,
                    SlotIndex = hitSlot,
                    SlotDataOffset = hitSlot * state.SlotSize,
                    TransferFn = colorSpaceChanged ? slotMeta.TransferFn : null,
                    ToXyzD50 = colorSpaceChanged ? slotMeta.ToXyzD50 : null,
                });
            }

            // キャッシュミス: プリフェッチ停止してデコード
            StopPrefetchUnderLock(state);

            if (!state.Reader.ReadVideo(request.Frame, out var bitmap))
            {
                state.LastRequestedFrame = request.Frame;
                StartPrefetch(state);
                return IpcMessage.Create(msgId, MessageType.ReadVideoResult,
                    new ReadVideoResponse { Success = false });
            }

            using (bitmap)
            {
                int dataLen = bitmap.ByteCount;

                // 共有メモリリサイズチェック
                string? newShmName = null;
                if (dataLen > state.SlotSize)
                {
                    // スロットサイズを超えた場合、リングバッファを再作成
                    state.VideoBuffer?.Dispose();
                    state.SlotSize = dataLen + 64;
                    long totalSize = state.SlotSize * state.SlotCount;
                    int gen = Interlocked.Increment(ref _shmGeneration);
                    newShmName = $"beutl-ffmpeg-video-{Environment.ProcessId}-{request.ReaderId}-{gen}";
                    state.VideoBuffer = SharedMemoryBuffer.Create(newShmName, totalSize);
                    state.InvalidateAllSlots();
                }

                // スロットに書き込み
                int writeSlot = state.NextWriteSlot;
                long offset = writeSlot * state.SlotSize;
                state.VideoBuffer!.Write(new ReadOnlySpan<byte>((void*)bitmap.Data, dataLen), offset);

                // 色空間情報
                var cs = bitmap.ColorSpace;
                var transferFn = cs.GetNumericalTransferFunction();
                var xyz = cs.ToColorSpaceXyz();

                float[] currentTransferFn = [transferFn.G, transferFn.A, transferFn.B, transferFn.C, transferFn.D, transferFn.E, transferFn.F];
                float[] currentToXyzD50 = xyz.Values.ToArray();

                bool colorSpaceChanged = !currentTransferFn.AsSpan().SequenceEqual(state.LastTransferFn)
                                         || !currentToXyzD50.AsSpan().SequenceEqual(state.LastToXyzD50);

                if (colorSpaceChanged)
                {
                    state.LastTransferFn = currentTransferFn;
                    state.LastToXyzD50 = currentToXyzD50;
                }

                // スロットメタデータ更新
                state.Slots[writeSlot] = new SlotMetadata
                {
                    FrameNumber = request.Frame,
                    Width = bitmap.Width,
                    Height = bitmap.Height,
                    BytesPerPixel = bitmap.BytesPerPixel,
                    DataLength = dataLen,
                    IsHdr = bitmap.BytesPerPixel == 8,
                    TransferFn = currentTransferFn,
                    ToXyzD50 = currentToXyzD50,
                };
                state.SlotFrameNumbers[writeSlot] = request.Frame;
                state.NextWriteSlot = (writeSlot + 1) % state.SlotCount;
                state.LastRequestedFrame = request.Frame;
                state.LastServedSlot = writeSlot;

                // プリフェッチ開始
                StartPrefetch(state);

                return IpcMessage.Create(msgId, MessageType.ReadVideoResult, new ReadVideoResponse
                {
                    Success = true,
                    Width = bitmap.Width,
                    Height = bitmap.Height,
                    BytesPerPixel = bitmap.BytesPerPixel,
                    DataLength = dataLen,
                    IsHdr = bitmap.BytesPerPixel == 8,
                    SharedMemoryName = newShmName,
                    SlotIndex = writeSlot,
                    SlotDataOffset = offset,
                    TransferFn = colorSpaceChanged ? currentTransferFn : null,
                    ToXyzD50 = colorSpaceChanged ? currentToXyzD50 : null,
                });
            }
        }
        finally
        {
            state.ReaderLock.Release();
        }
    }

    private unsafe IpcMessage HandleReadVideoLegacy(int msgId, ReadVideoRequest request, ReaderState state)
    {
        if (!state.Reader.ReadVideo(request.Frame, out var bitmap))
        {
            return IpcMessage.Create(msgId, MessageType.ReadVideoResult,
                new ReadVideoResponse { Success = false });
        }

        using (bitmap)
        {
            int dataLen = bitmap.ByteCount;

            // 共有メモリが小さければリサイズ（一意な名前で再作成）
            string? newShmName = null;
            if (state.VideoBuffer == null || state.VideoBuffer.Capacity < dataLen)
            {
                state.VideoBuffer?.Dispose();
                long newSize = dataLen + 64;
                int gen = Interlocked.Increment(ref _shmGeneration);
                newShmName = $"beutl-ffmpeg-video-{Environment.ProcessId}-{request.ReaderId}-{gen}";
                state.VideoBuffer = SharedMemoryBuffer.Create(newShmName, newSize);
            }

            state.VideoBuffer.Write(new ReadOnlySpan<byte>((void*)bitmap.Data, dataLen));

            // 色空間情報: 前フレームと異なる場合のみ送信
            var cs = bitmap.ColorSpace;
            var transferFn = cs.GetNumericalTransferFunction();
            var xyz = cs.ToColorSpaceXyz();

            float[] currentTransferFn = [transferFn.G, transferFn.A, transferFn.B, transferFn.C, transferFn.D, transferFn.E, transferFn.F];
            float[] currentToXyzD50 = xyz.Values.ToArray();

            bool colorSpaceChanged = !currentTransferFn.AsSpan().SequenceEqual(state.LastTransferFn)
                                     || !currentToXyzD50.AsSpan().SequenceEqual(state.LastToXyzD50);

            if (colorSpaceChanged)
            {
                state.LastTransferFn = currentTransferFn;
                state.LastToXyzD50 = currentToXyzD50;
            }

            return IpcMessage.Create(msgId, MessageType.ReadVideoResult, new ReadVideoResponse
            {
                Success = true,
                Width = bitmap.Width,
                Height = bitmap.Height,
                BytesPerPixel = bitmap.BytesPerPixel,
                DataLength = dataLen,
                IsHdr = bitmap.BytesPerPixel == 8,
                SharedMemoryName = newShmName,
                TransferFn = colorSpaceChanged ? currentTransferFn : null,
                ToXyzD50 = colorSpaceChanged ? currentToXyzD50 : null,
            });
        }
    }

    private void StopPrefetchUnderLock(ReaderState state)
    {
        // ReaderLock保持下で呼ぶこと。
        // プリフェッチスレッドがReaderLockを待っている状態でキャンセルする。
        if (state.PrefetchCts != null)
        {
            state.PrefetchCts.Cancel();
            state.PrefetchSignal.Set(); // Wait中のスレッドを起こす

            // ロックを一時解放してプリフェッチスレッドの終了を待つ
            state.ReaderLock.Release();
            try { state.PrefetchTask?.Wait(); } catch { }
            state.ReaderLock.Wait();

            state.PrefetchCts.Dispose();
            state.PrefetchCts = null;
            state.PrefetchTask = null;
        }
    }

    private unsafe void StartPrefetch(ReaderState state)
    {
        // ReaderLock保持下で呼ぶこと
        state.PrefetchCts = new CancellationTokenSource();
        var ct = state.PrefetchCts.Token;
        state.PrefetchSignal.Reset();

        long totalFrames = state.Reader.HasVideo ? state.Reader.VideoInfo.NumFrames : 0;

        state.PrefetchTask = Task.Run(() =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    // プリフェッチ対象フレームを計算
                    int baseFrame = state.LastRequestedFrame;
                    if (baseFrame < 0) break;

                    // 既にバッファにあるフレーム数をカウント
                    int cachedAhead = 0;
                    int maxAhead = state.SlotCount - 1; // 1スロットは次のリクエスト用に確保
                    for (int i = 1; i <= maxAhead; i++)
                    {
                        if (state.FindSlot(baseFrame + i) >= 0)
                            cachedAhead++;
                        else
                            break;
                    }

                    if (cachedAhead >= maxAhead)
                    {
                        // 十分プリフェッチ済み → 次のリクエストを待つ
                        state.PrefetchSignal.Wait(ct);
                        state.PrefetchSignal.Reset();
                        continue;
                    }

                    int nextFrame = baseFrame + cachedAhead + 1;
                    if (nextFrame >= totalFrames) break;

                    // 既にキャッシュ済みならスキップ
                    if (state.FindSlot(nextFrame) >= 0) continue;

                    state.ReaderLock.Wait(ct);
                    try
                    {
                        // ロック取得後に再チェック（HandleReadVideoで状態が変わっている可能性）
                        if (ct.IsCancellationRequested) break;
                        if (state.FindSlot(nextFrame) >= 0) continue;

                        if (!state.Reader.ReadVideo(nextFrame, out var bitmap))
                            continue;

                        using (bitmap)
                        {
                            int dataLen = bitmap.ByteCount;
                            if (dataLen > state.SlotSize)
                                continue; // スロットに収まらない場合はスキップ

                            // LastServedSlotを上書きしないようにする
                            int writeSlot = state.NextWriteSlot;
                            if (writeSlot == state.LastServedSlot)
                            {
                                writeSlot = (writeSlot + 1) % state.SlotCount;
                            }

                            long offset = writeSlot * state.SlotSize;
                            state.VideoBuffer!.Write(
                                new ReadOnlySpan<byte>((void*)bitmap.Data, dataLen), offset);

                            var cs = bitmap.ColorSpace;
                            var transferFn = cs.GetNumericalTransferFunction();
                            var xyz = cs.ToColorSpaceXyz();

                            float[] tfn = [transferFn.G, transferFn.A, transferFn.B, transferFn.C, transferFn.D, transferFn.E, transferFn.F];
                            float[] xyzArr = xyz.Values.ToArray();

                            state.Slots[writeSlot] = new SlotMetadata
                            {
                                FrameNumber = nextFrame,
                                Width = bitmap.Width,
                                Height = bitmap.Height,
                                BytesPerPixel = bitmap.BytesPerPixel,
                                DataLength = dataLen,
                                IsHdr = bitmap.BytesPerPixel == 8,
                                TransferFn = tfn,
                                ToXyzD50 = xyzArr,
                            };
                            state.SlotFrameNumbers[writeSlot] = nextFrame;
                            state.NextWriteSlot = (writeSlot + 1) % state.SlotCount;
                        }
                    }
                    finally
                    {
                        state.ReaderLock.Release();
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Prefetch error: {ex}");
            }
        }, ct);
    }

    public unsafe IpcMessage HandleReadAudio(IpcMessage msg)
    {
        var request = msg.GetPayload<ReadAudioRequest>()!;
        if (!_readers.TryGetValue(request.ReaderId, out var state))
            return IpcMessage.CreateError(msg.Id, $"Unknown reader ID: {request.ReaderId}");

        if (!state.Reader.ReadAudio(request.Start, request.Length, out var sound))
        {
            return IpcMessage.Create(msg.Id, MessageType.ReadAudioResult,
                new ReadAudioResponse { Success = false });
        }

        using (sound)
        {
            int dataLen = sound.NumSamples * (int)sound.SampleSize;

            string? newShmName = null;
            if (state.AudioBuffer == null || state.AudioBuffer.Capacity < dataLen)
            {
                state.AudioBuffer?.Dispose();
                long newSize = dataLen + 64;
                int gen = Interlocked.Increment(ref _shmGeneration);
                newShmName = $"beutl-ffmpeg-audio-{Environment.ProcessId}-{request.ReaderId}-{gen}";
                state.AudioBuffer = SharedMemoryBuffer.Create(newShmName, newSize);
            }

            state.AudioBuffer.Write(new ReadOnlySpan<byte>((void*)sound.Data, dataLen));

            return IpcMessage.Create(msg.Id, MessageType.ReadAudioResult, new ReadAudioResponse
            {
                Success = true,
                SampleRate = sound.SampleRate,
                NumSamples = sound.NumSamples,
                DataLength = dataLen,
                SharedMemoryName = newShmName,
            });
        }
    }

    public IpcMessage HandleClose(IpcMessage msg)
    {
        var request = msg.GetPayload<CloseReaderRequest>()!;
        if (_readers.TryRemove(request.ReaderId, out var state))
        {
            state.Dispose();
        }

        return IpcMessage.CreateSimple(msg.Id, MessageType.CloseReaderResult);
    }

    public IpcMessage HandleUpdateDecoderSettings(IpcMessage msg)
    {
        var request = msg.GetPayload<UpdateDecoderSettingsRequest>()!;

        foreach (KeyValuePair<int, ReaderState> kvp in _readers)
        {
            var state = kvp.Value;

            // プリフェッチ停止、リングバッファ無効化
            if (state.SlotCount > 0)
            {
                state.StopPrefetch();
                state.InvalidateAllSlots();
            }

            state.Reader.Settings.ThreadCount = request.ThreadCount;
            state.Reader.Settings.Acceleration = (FFmpegDecodingSettings.AccelerationOptions)request.Acceleration;
            state.Reader.Settings.ForceSrgbGamma = request.ForceSrgbGamma;
        }

        return IpcMessage.CreateSimple(msg.Id, MessageType.UpdateDecoderSettingsResult);
    }

    public void Dispose()
    {
        foreach (var state in _readers.Values)
        {
            state.Dispose();
        }
        _readers.Clear();
    }
}
