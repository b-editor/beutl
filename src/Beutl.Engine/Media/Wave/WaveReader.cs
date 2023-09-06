using System.Diagnostics.CodeAnalysis;

using Beutl.Media.Decoding;
using Beutl.Media.Music;
using Beutl.Media.Music.Samples;

using static Beutl.Media.Wave.WaveBitConverter;

namespace Beutl.Media.Wave;

public sealed class WaveReader : MediaReader
{
    private readonly string _file;
    private readonly MediaOptions _options;
    private readonly WaveAnalysis _waveAnalysis;
    private readonly WaveFormat _waveFormat;

    public WaveReader(string file, MediaOptions options)
    {
        _file = file;
        _options = options;
        using FileStream stream = File.OpenRead(file);
        _waveAnalysis = new WaveAnalysis(stream);
        _waveFormat = _waveAnalysis.WaveFomat;

        AudioInfo = new AudioStreamInfo(
            CodecName: $"Wave ({_waveFormat.FormatTag})",
            Duration: _waveAnalysis.Duration,
            SampleRate: _waveFormat.SamplesPerSec,
            NumChannels: _waveFormat.Channels);

        // PCM(8/16/24/32bit)及びIEEE Float(32bit)のみ対応
        if (!(_waveFormat.FormatTag == WaveFormatTag.Pcm ||
            (_waveFormat.FormatTag == WaveFormatTag.IeeeFloat && _waveFormat.BitsPerSample == 32)))
        {
            throw new Exception("Unsupported format.");
        }
    }

    public override VideoStreamInfo VideoInfo => throw new NotSupportedException();

    public override AudioStreamInfo AudioInfo { get; }

    public override bool HasVideo => false;

    public override bool HasAudio => true;

    public override bool ReadAudio(int start, int length, [NotNullWhen(true)] out IPcm? sound)
    {
        sound = null;
        if (IsDisposed)
            return false;

        // PCM(8/16/24/32bit)及びIEEE Float(32bit)のみ対応
        if (!(_waveFormat.FormatTag == WaveFormatTag.Pcm ||
            (_waveFormat.FormatTag == WaveFormatTag.IeeeFloat && _waveFormat.BitsPerSample == 32)))
        {
            return false;
        }

        // 1チャンネルのサイズ
        int size = length;
        var tmp = new Pcm<Stereo32BitFloat>(_waveFormat.SamplesPerSec, length);

        byte[] raw = _waveAnalysis.Raw;
        // ----------------
        //  波形データ
        // ----------------            

        // [IEEE Float] 32bit 
        if (_waveFormat.BitsPerSample == 32 && _waveFormat.FormatTag == WaveFormatTag.IeeeFloat)
        {
            int l = 0;
            Span<byte> dataView = stackalloc byte[4];

            int count = _waveAnalysis.Raw.Length / 4;
            int startRaw = start * _waveFormat.Channels;
            count = Math.Min(count, (start + length) * _waveFormat.Channels);

            if (_waveFormat.Channels == 1)
            {
                for (int i = startRaw; i < count; i++)
                {
                    int index = i * 4;
                    float val = ToSingle(raw.AsSpan().Slice(index, 4));

                    tmp.DataSpan[l++] = new Stereo32BitFloat(val, val);
                }
            }
            else
            {
                for (int i = startRaw; i < count; i++)
                {
                    int index = i * 4;
                    float val = ToSingle(raw.AsSpan().Slice(index, 4));
                    if (i % 2 == 0)
                    {
                        tmp.DataSpan[l].Left = val;
                    }
                    else
                    {
                        tmp.DataSpan[l++].Right = val;
                    }
                }
            }

            // [PCM] 8/16/24/32bit  
        }
        else
        {
            if (_waveFormat.BitsPerSample == 4)
            {
                return false;
            }

            if (_waveFormat.BitsPerSample == 8)
            {
                int l = 0;
                int startRaw = start * _waveFormat.Channels;
                int count = _waveAnalysis.Raw.Length;
                count = Math.Min(count, (start + length) * _waveFormat.Channels);

                // モノラル
                if (_waveFormat.Channels == 1)
                {
                    for (int i = startRaw; i < count; i++)
                    {
                        sbyte val = ShiftInt8(raw[i]);
                        float valF = val / (float)sbyte.MaxValue;

                        tmp.DataSpan[l++] = new Stereo32BitFloat(valF, valF);
                    }

                    // ステレオ  
                }
                else
                {
                    for (int i = start; i < count; i++)
                    {
                        sbyte val = ShiftInt8(raw[i]);
                        float valF = val / (float)sbyte.MaxValue;

                        if (i % 2 == 0)
                        {
                            tmp.DataSpan[l].Left = valF;
                        }
                        else
                        {
                            tmp.DataSpan[l++].Right = valF;
                        }
                    }
                }
            }
            else if (_waveFormat.BitsPerSample == 16)
            {
                int l = 0;
                Span<byte> dataView = stackalloc byte[2];

                int count = _waveAnalysis.Raw.Length / 2;
                int startRaw = start * _waveFormat.Channels;
                count = Math.Min(count, (start + length) * _waveFormat.Channels);

                if (_waveFormat.Channels == 1)
                {
                    for (int i = startRaw; i < count; i++)
                    {
                        int index = i * 2;
                        short val = ToInt16(raw.AsSpan().Slice(index, 2));
                        float valF = val / (float)short.MaxValue;

                        tmp.DataSpan[l++] = new Stereo32BitFloat(valF, valF);
                    }
                }
                else
                {
                    for (int i = startRaw; i < count; i++)
                    {
                        int index = i * 2;
                        short val = ToInt16(raw.AsSpan().Slice(index, 2));
                        float valF = val / (float)short.MaxValue;

                        if (i % 2 == 0)
                        {
                            tmp.DataSpan[l].Left = valF;
                        }
                        else
                        {
                            tmp.DataSpan[l++].Right = valF;
                        }
                    }
                }

            }
            else if (_waveFormat.BitsPerSample == 24)
            {
                int l = 0;
                Span<byte> dataView = stackalloc byte[3];

                int count = _waveAnalysis.Raw.Length / 3;
                int startRaw = start * _waveFormat.Channels;
                count = Math.Min(count, (start + length) * _waveFormat.Channels);

                if (_waveFormat.Channels == 1)
                {
                    for (int i = startRaw; i < count; i++)
                    {
                        int index = i * 3;
                        int val = ShiftInt24(ToUInt24(raw.AsSpan().Slice(index, 3)));
                        float valF = val / 16777216f;

                        tmp.DataSpan[l++] = new Stereo32BitFloat(valF, valF);
                    }
                }
                else
                {
                    for (int i = startRaw; i < count; i++)
                    {
                        int index = i * 3;
                        int val = ShiftInt24(ToUInt24(raw.AsSpan().Slice(index, 3)));
                        float valF = val / 16777216f;

                        if (i % 2 == 0)
                        {
                            tmp.DataSpan[l].Left = valF;
                        }
                        else
                        {
                            tmp.DataSpan[l++].Right = valF;
                        }
                    }
                }

            }
            else if (_waveFormat.BitsPerSample == 32)
            {
                int l = 0;
                Span<byte> dataView = stackalloc byte[4];

                int count = _waveAnalysis.Raw.Length / 4;
                int startRaw = start * _waveFormat.Channels;
                count = Math.Min(count, (start + length) * _waveFormat.Channels);

                if (_waveFormat.Channels == 1)
                {
                    for (int i = startRaw; i < count; i++)
                    {
                        int index = i * 4;
                        int val = ToInt32(raw.AsSpan().Slice(index, 4));
                        float valF = val / (float)int.MaxValue;

                        tmp.DataSpan[l++] = new Stereo32BitFloat(valF, valF);
                    }
                }
                else
                {
                    for (int i = startRaw; i < count; i++)
                    {
                        int index = i * 4;
                        int val = ToInt32(raw.AsSpan().Slice(index, 4));
                        float valF = val / (float)int.MaxValue;

                        if (i % 2 == 0)
                        {
                            tmp.DataSpan[l].Left = valF;
                        }
                        else
                        {
                            tmp.DataSpan[l++].Right = valF;
                        }
                    }
                }

            }
        }

        if (tmp.SampleRate == _options.SampleRate)
        {
            sound = tmp;
            return true;
        }
        else
        {
            sound = tmp.Resamples(_options.SampleRate);
            tmp.Dispose();
            return true;
        }
    }

    public override bool ReadVideo(int frame, [NotNullWhen(true)] out IBitmap? image)
    {
        image = null;
        return false;
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
    }
}
