using System.Diagnostics;
using Beutl.Extensions.FFmpeg;
using Beutl.Extensions.FFmpeg.Decoding;
using Beutl.FFmpegIpc.Protocol;
using Beutl.FFmpegIpc.Protocol.Messages;
using Beutl.FFmpegWorker;
using Beutl.FFmpegWorker.Decoding;
using Beutl.Media;
using Beutl.Media.Decoding;
using Beutl.Media.Source;

namespace Beutl.FFmpegBenchmarks;

internal static class Program
{
    private static string? s_testVideoPath;

    static int Main(string[] args)
    {
        s_testVideoPath = args.Length > 0 ? args[0] : null;

        if (s_testVideoPath == null || !File.Exists(s_testVideoPath))
        {
            Console.Error.WriteLine("使い方: Beutl.FFmpegBenchmarks <動画ファイルパス>");
            Console.Error.WriteLine("  例: dotnet run -- /path/to/video.mp4");
            return 1;
        }

        Console.WriteLine("=== FFmpeg IPC パフォーマンスベンチマーク ===");
        Console.WriteLine($"テスト動画: {s_testVideoPath}");
        Console.WriteLine();

        // FFmpegライブラリ初期化
        FFmpegLoaderWorker.Initialize();
        Console.WriteLine();

        // --- Direct (in-process) ベンチマーク ---
        Console.WriteLine("--- Direct (in-process) ---");
        var directResults = RunDirectBenchmark();

        // --- IPC (out-of-process) ベンチマーク ---
        Console.WriteLine("--- IPC (out-of-process) ---");
        var ipcResults = RunIpcBenchmark();

        // --- 結果比較 ---
        PrintComparison(directResults, ipcResults);

        return 0;
    }

    static BenchmarkResults RunDirectBenchmark()
    {
        var settings = new FFmpegDecodingSettings();
        var options = new MediaOptions(MediaMode.AudioVideo);
        using var reader = new FFmpegReader(s_testVideoPath!, options, settings);

        return RunBenchmark("Direct", reader);
    }

    static BenchmarkResults RunIpcBenchmark()
    {
        using var worker = new FFmpegWorkerProcess(multiplexed: true);
        var connection = worker.EnsureStarted();

        var request = new OpenFileRequest
        {
            FilePath = s_testVideoPath!,
            StreamsToLoad = (int)MediaMode.AudioVideo,
            ThreadCount = -1,
            Acceleration = 0,
            ForceSrgbGamma = true,
        };

        var response = connection.RequestAsync<OpenFileRequest, OpenFileResponse>(
            MessageType.OpenFile, MessageType.OpenFileResult, request).AsTask().GetAwaiter().GetResult();

        using var reader = new FFmpegReaderProxy(connection, response.ReaderId, response);

        return RunBenchmark("IPC", reader);
    }

    static BenchmarkResults RunBenchmark(string label, MediaReader reader)
    {
        var results = new BenchmarkResults { Label = label };

        if (reader.HasVideo)
        {
            int totalFrames = (int)reader.VideoInfo.NumFrames;
            int width = reader.VideoInfo.FrameSize.Width;
            int height = reader.VideoInfo.FrameSize.Height;
            Console.WriteLine($"  動画: {width}x{height}, {totalFrames}フレーム, {reader.VideoInfo.FrameRate}fps");

            int benchFrames = Math.Min(totalFrames, 300);

            // 1. 連続フレーム読み取り (プリフェッチ効果あり)
            results.SequentialVideo = MeasureSequentialVideo(reader, benchFrames);

            // 2. ランダムアクセス (スクラビング模擬)
            results.RandomVideo = MeasureRandomVideo(reader, totalFrames, Math.Min(50, totalFrames));

            // 3. 最初のフレーム読み取り (コールドスタート)
            results.FirstFrame = MeasureFirstFrame(reader);
        }

        if (reader.HasAudio)
        {
            int sampleRate = reader.AudioInfo.SampleRate;
            Console.WriteLine($"  音声: {sampleRate}Hz, {reader.AudioInfo.NumChannels}ch");

            // 4. 音声読み取り
            results.Audio = MeasureAudio(reader, sampleRate);
        }

        Console.WriteLine();
        return results;
    }

    static TimingResult MeasureSequentialVideo(MediaReader reader, int frameCount)
    {
        // ウォームアップ: 最初の数フレームを読んでデコーダを温める
        for (int i = 0; i < Math.Min(5, frameCount); i++)
        {
            if (reader.ReadVideo(i, out var img))
                img.Dispose();
        }

        var frameTimes = new List<double>(frameCount);
        var sw = new Stopwatch();
        var totalSw = Stopwatch.StartNew();

        for (int i = 0; i < frameCount; i++)
        {
            sw.Restart();
            bool ok = reader.ReadVideo(i, out Ref<Bitmap>? img);
            sw.Stop();

            if (ok) img!.Dispose();
            frameTimes.Add(sw.Elapsed.TotalMilliseconds);
        }

        totalSw.Stop();

        var result = new TimingResult
        {
            Name = "連続フレーム読み取り",
            Count = frameCount,
            TotalMs = totalSw.Elapsed.TotalMilliseconds,
            PerItemMs = frameTimes.OrderBy(x => x).ToList(),
        };
        PrintTiming(result);
        return result;
    }

    static TimingResult MeasureRandomVideo(MediaReader reader, int totalFrames, int sampleCount)
    {
        // ランダムなフレーム番号を生成 (再現性のためシード固定)
        var rng = new Random(42);
        var frames = Enumerable.Range(0, sampleCount)
            .Select(_ => rng.Next(0, totalFrames))
            .ToArray();

        var frameTimes = new List<double>(sampleCount);
        var sw = new Stopwatch();
        var totalSw = Stopwatch.StartNew();

        foreach (int frame in frames)
        {
            sw.Restart();
            bool ok = reader.ReadVideo(frame, out Ref<Bitmap>? img);
            sw.Stop();

            if (ok) img!.Dispose();
            frameTimes.Add(sw.Elapsed.TotalMilliseconds);
        }

        totalSw.Stop();

        var result = new TimingResult
        {
            Name = "ランダムアクセス",
            Count = sampleCount,
            TotalMs = totalSw.Elapsed.TotalMilliseconds,
            PerItemMs = frameTimes.OrderBy(x => x).ToList(),
        };
        PrintTiming(result);
        return result;
    }

    static TimingResult MeasureFirstFrame(MediaReader reader)
    {
        // フレーム0を10回読み取り
        const int iterations = 10;
        var frameTimes = new List<double>(iterations);
        var sw = new Stopwatch();
        var totalSw = Stopwatch.StartNew();

        for (int i = 0; i < iterations; i++)
        {
            sw.Restart();
            bool ok = reader.ReadVideo(0, out Ref<Bitmap>? img);
            sw.Stop();

            if (ok) img!.Dispose();
            frameTimes.Add(sw.Elapsed.TotalMilliseconds);
        }

        totalSw.Stop();

        var result = new TimingResult
        {
            Name = "フレーム0 読み取り",
            Count = iterations,
            TotalMs = totalSw.Elapsed.TotalMilliseconds,
            PerItemMs = frameTimes.OrderBy(x => x).ToList(),
        };
        PrintTiming(result);
        return result;
    }

    static TimingResult MeasureAudio(MediaReader reader, int sampleRate)
    {
        // 1秒ずつ、10秒分読み取り
        const int seconds = 10;
        var chunkTimes = new List<double>(seconds);
        var sw = new Stopwatch();
        var totalSw = Stopwatch.StartNew();

        for (int i = 0; i < seconds; i++)
        {
            sw.Restart();
            bool ok = reader.ReadAudio(i * sampleRate, sampleRate, out var sound);
            sw.Stop();

            if (ok) sound!.Dispose();
            chunkTimes.Add(sw.Elapsed.TotalMilliseconds);
        }

        totalSw.Stop();

        var result = new TimingResult
        {
            Name = "音声読み取り (1秒×10)",
            Count = seconds,
            TotalMs = totalSw.Elapsed.TotalMilliseconds,
            PerItemMs = chunkTimes.OrderBy(x => x).ToList(),
        };
        PrintTiming(result);
        return result;
    }

    static void PrintTiming(TimingResult r)
    {
        var sorted = r.PerItemMs;
        double median = sorted[sorted.Count / 2];
        double p95 = sorted[(int)(sorted.Count * 0.95)];
        double min = sorted[0];
        double max = sorted[^1];
        double avg = sorted.Average();

        Console.WriteLine($"  [{r.Name}] {r.Count}回");
        Console.WriteLine($"    合計: {r.TotalMs:F1}ms, 平均: {avg:F2}ms, 中央値: {median:F2}ms");
        Console.WriteLine($"    最小: {min:F2}ms, 最大: {max:F2}ms, P95: {p95:F2}ms");
    }

    static void PrintComparison(BenchmarkResults direct, BenchmarkResults ipc)
    {
        Console.WriteLine("=== Comparison Results (IPC / Direct) ===");
        Console.WriteLine();
        Console.WriteLine($"{"Item",-24} {"Direct Avg",12} {"IPC Avg",12} {"Ratio",8} {"Diff",12}");
        Console.WriteLine(new string('-', 72));

        CompareOne("Sequential frame read", direct.SequentialVideo, ipc.SequentialVideo);
        CompareOne("Random access", direct.RandomVideo, ipc.RandomVideo);
        CompareOne("Frame 0 read", direct.FirstFrame, ipc.FirstFrame);
        CompareOne("Audio read", direct.Audio, ipc.Audio);
    }

    static void CompareOne(string name, TimingResult? direct, TimingResult? ipc)
    {
        if (direct == null || ipc == null) return;

        double directAvg = direct.PerItemMs.Average();
        double ipcAvg = ipc.PerItemMs.Average();
        double ratio = directAvg > 0 ? ipcAvg / directAvg : double.NaN;
        double diffMs = ipcAvg - directAvg;

        Console.WriteLine($"{name,-24} {directAvg,10:F2}ms {ipcAvg,10:F2}ms {ratio,7:F2}x {diffMs,+10:F2}ms");
    }
}

internal sealed class BenchmarkResults
{
    public string Label { get; set; } = "";
    public TimingResult? SequentialVideo { get; set; }
    public TimingResult? RandomVideo { get; set; }
    public TimingResult? FirstFrame { get; set; }
    public TimingResult? Audio { get; set; }
}

internal sealed class TimingResult
{
    public string Name { get; set; } = "";
    public int Count { get; set; }
    public double TotalMs { get; set; }
    public List<double> PerItemMs { get; set; } = [];
}
