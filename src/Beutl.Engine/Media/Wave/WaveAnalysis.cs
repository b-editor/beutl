using static Beutl.Media.Wave.WaveBitConverter;

namespace Beutl.Media.Wave;

public sealed class WaveAnalysis
{
    public WaveAnalysis(Stream stream)
    {
        string errormsg = "It is not a WAVE file.";

        // RIFF
        Span<byte> intBytes = stackalloc byte[4];
        stream.ReadExactly(intBytes);
        if (!intBytes.SequenceEqual("RIFF"u8))
        {
            throw new Exception(errormsg);
        }

        stream.Position += 4;

        // WAVE
        stream.ReadExactly(intBytes);
        if (!intBytes.SequenceEqual("WAVE"u8))
        {
            throw new Exception(errormsg);
        }

        // fmt
        int len;
        short wFormatTag, nChannels, nBlockAlign, wBitsPerSample;
        int nSamplesPerSec, nAvgBytesPerSec;
        while (true)
        {
            stream.ReadExactly(intBytes);
            if (intBytes.SequenceEqual("fmt "u8))
            {
                stream.ReadExactly(intBytes);
                len = ToInt32(intBytes);

                Span<byte> shortBytes = stackalloc byte[2];

                // 種類(1:リニアPCM)
                stream.ReadExactly(shortBytes);
                wFormatTag = ToInt16(shortBytes);

                // チャンネル数(1:モノラル 2:ステレオ)     
                stream.ReadExactly(shortBytes);
                nChannels = ToInt16(shortBytes);

                // サンプリングレート(44100=44.1kHzなど)      
                stream.ReadExactly(intBytes);
                nSamplesPerSec = ToInt32(intBytes);

                // 平均データ転送レート(byte/sec) 
                // ※PCMの場合はnSamplesPerSec * nBlockAlign          
                stream.ReadExactly(intBytes);
                nAvgBytesPerSec = ToInt32(intBytes);

                // ブロックサイズ 
                // ※PCMの場合はwBitsPerSample * nChannels / 8 
                stream.ReadExactly(shortBytes);
                nBlockAlign = ToInt16(shortBytes);

                // サンプルあたりのビット数 (bit/sample) 
                // ※PCMの場合は8bit=8, 16bit =16    
                stream.ReadExactly(shortBytes);
                wBitsPerSample = ToInt16(shortBytes);

                // WaveFomatExなどの対策
                stream.Position = stream.Position + len - 16;

                break;
            }
            else
            {
                stream.ReadExactly(intBytes);
                len = ToInt32(intBytes);
                stream.Position += len;
            }

            if (stream.Position >= stream.Length)
            {
                throw new Exception(errormsg);
            }
        }

        // data
        byte[] raw;
        while (true)
        {
            stream.ReadExactly(intBytes);
            if (intBytes.SequenceEqual("data"u8))
            {
                stream.ReadExactly(intBytes);
                len = ToInt32(intBytes);

                raw = new byte[len];
                stream.ReadExactly(raw);

                break;
            }
            else
            {
                stream.ReadExactly(intBytes);
                len = ToInt32(intBytes);
                stream.Position += len;
            }

            if (stream.Position >= stream.Length)
            {
                throw new Exception(errormsg);
            }
        }

        // WaveFomat構造体(アクセス用)
        WaveFomat = new WaveFormat
        {
            FormatTag = (WaveFormatTag)wFormatTag,
            Channels = nChannels,
            SamplesPerSec = nSamplesPerSec,
            AvgBytesPerSec = nAvgBytesPerSec,
            BlockAlign = nBlockAlign,
            BitsPerSample = wBitsPerSample
        };
        // 波形データ
        Raw = raw;
        // 再生時間
        Duration = new Rational(len, nAvgBytesPerSec);
        // ビットレート (bps)
        Bitrate = nSamplesPerSec * wBitsPerSample * nChannels;
    }

    public WaveFormat WaveFomat { get; }

    public byte[] Raw { get; }

    public Rational Duration { get; }

    public int Bitrate { get; }
}
