using System.Diagnostics;
using System.Text;

#if FFMPEG_BUILD_IN
namespace Beutl.Embedding.FFmpeg.Encoding;
#else
namespace Beutl.Extensions.FFmpeg.Encoding;
#endif

// https://qiita.com/superriver/items/47fd81b206a59a230c32
public sealed class ProcessWrapper : IDisposable
{
    private readonly StringBuilder? _output;
    private readonly StringBuilder? _error;

    private ProcessWrapper(Process process)
    {
        if (process.StartInfo.RedirectStandardOutput)
        {
            _output = new StringBuilder();
            process.OutputDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    _output.AppendLine(e.Data);
                }
            };
            process.BeginOutputReadLine();
        }

        if (process.StartInfo.RedirectStandardError)
        {
            _error = new StringBuilder();
            process.ErrorDataReceived += (sender, e) =>
            {
                if (e.Data != null)
                {
                    _error.AppendLine(e.Data);
                }
            };
            process.BeginErrorReadLine();
        }

        Process = process;
    }

    public Process Process { get; private set; }

    public static ProcessWrapper? Start(ProcessStartInfo processStartInfo)
    {
        var process = Process.Start(processStartInfo);
        if (process == null)
            return null;
        return new ProcessWrapper(process);
    }

    public string? GetError()
    {
        return _error?.ToString();
    }

    public string? GetOutput()
    {
        return _output?.ToString();
    }

    public void Dispose()
    {
        if (Process != null)
        {
            if (_output != null)
                Process.CancelOutputRead();
            if (_error != null)
                Process.CancelErrorRead();
            Process.Dispose();
            Process = null!;
        }
    }
}
