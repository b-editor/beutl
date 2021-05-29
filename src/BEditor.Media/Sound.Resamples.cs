// Sound.Resamples.cs
//
// Copyright (C) BEditor
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.

using System;

namespace BEditor.Media
{
    /// <inheritdoc cref="Sound"/>
    public static partial class Sound
    {
        // 変換パラメータを求める
        private static ConvertParam GetConvertParam(int befor, int affter)
        {
            ConvertParam param;
            param.Sample = affter;
            param.Rate = (byte)((MathF.Max(befor, affter) / MathF.Min(befor, affter)) + 1);
            param.UpSample = befor * param.Rate;
            param.Cutoff = (int)(MathF.Min(befor, affter) / 2 * 0.9);
            param.Gap = ((double)befor) / affter * param.Rate;

            return param;
        }

        // 理想次数を求める
        private static ushort GetDegree(int siderope, ref ConvertParam param)
        {
            var tmp1 = param.Sample / 2 * 0.9 / ((double)param.UpSample / 2) * Math.Acos(-1.0);
            var tmp2 = (double)param.Sample / 2 / ((double)param.UpSample / 2) * Math.Acos(-1.0);
            var tmp = tmp2 - tmp1;
            var r = (ushort)Math.Ceiling((siderope - 8) / (2.285 * tmp));
            if (r % 2 != 0)
            {
                r++;
            }

            return r;
        }

        // 階乗
        private static uint Factorial(uint n)
        {
            uint tmp = 1;
            for (uint i = 1; i <= n; ++i)
            {
                tmp *= i;
            }

            return tmp;
        }

        // カイザー窓関数
        private static double Kaizer(int siderope)
        {
            if (siderope > 21 && siderope < 50)
            {
                return (0.5842 * Math.Pow((double)siderope - 21, 0.4)) + (0.07886 * ((double)siderope - 21));
            }
            else if (siderope >= 50)
            {
                return 0.1102 * (siderope - 8.7);
            }
            else
            {
                return 0.0;
            }
        }

        // 第一種ベッセル関数
        private static double Vessel(double val)
        {
            var tmp = 1.0;
            for (uint i = 1; i <= 20; ++i)
            {
                tmp += Math.Pow(Math.Pow(val / 2.0, i) / Factorial(i), 2.0);
            }

            return tmp;
        }

        // 標本化関数
        private static double[] Sinc(int siderope, ushort degree, ref ConvertParam param)
        {
            var tmp = new double[degree + 1];

            var kaizer = Kaizer(siderope);
            tmp[degree / 2] = 2.0 * param.Cutoff / param.UpSample;
            for (ushort i = 1; i <= degree / 2; ++i)
            {
                var win = Vessel(kaizer * Math.Sqrt(1.0 - Math.Pow(i / ((double)degree / 2), 2.0))) / Vessel(kaizer);
                tmp[(degree / 2) + i] = Math.Sin(2.0 * Math.Acos(-1.0) * param.Cutoff / param.UpSample * i) / (Math.Acos(-1.0) * i) * win;
                tmp[(degree / 2) - i] = tmp[(degree / 2) + i];
            }

            return tmp;
        }

        // リサンプリング
        private static float[] Resampling(double[] corre, ref ConvertParam param, Span<float> data, int sample, byte channel)
        {
            // 変換後データ格納配列
            var convert = new float[(int)(data.Length * (((double)param.Sample) / sample))];
            var offset = (corre.Length - 1) / 2;
            var index = 0;
            var gap = param.Gap;
            while (index < convert.Length)
            {
                var integer = Math.Truncate(gap);
                gap -= integer;
                offset += (int)integer;

                // 変換
                for (uint i = 0; i < (corre.Length - 1); i++)
                {
                    var tmp = offset - ((corre.Length - 1) / 2) + i;
                    if (tmp / param.Rate * channel >= data.Length)
                    {
                        break;
                    }

                    if (tmp % param.Rate == 0)
                    {
                        // 線形補完
                        var comp = ((corre[i + 1] - corre[i]) * (1.0 - gap)) + corre[i];

                        // 二次補完
                        if (i == ((corre.Length - 1) / 2) - 1)
                        {
                            comp = ((corre[((corre.Length - 1) / 2) - 1] - corre[(corre.Length - 1) / 2]) * Math.Pow(-gap, 2.0)) + corre[(corre.Length - 1) / 2];
                        }
                        else if (i == (corre.Length - 1) / 2)
                        {
                            comp = ((corre[((corre.Length - 1) / 2) - 1] - corre[(corre.Length - 1) / 2]) * Math.Pow(1.0 - gap, 2.0)) + corre[(corre.Length - 1) / 2];
                        }

                        for (byte ch = 0; ch < channel; ch++)
                        {
                            if ((tmp / param.Rate * channel) + ch < data.Length)
                            {
                                convert[index + ch] += data[(int)((tmp / param.Rate * channel) + ch)] * ((float)comp);
                            }
                        }
                    }
                }

                // ゲイン調節
                for (byte ch = 0; ch < channel; ch++)
                {
                    convert[index + ch] *= param.Rate - 0.2f;
                }

                index += channel;
                gap += param.Gap;
            }

            return convert;
        }

        private struct ConvertParam
        {
            // 変換したいサンプリング周波数
            public int Sample;

            // 内部アップサンプリング済みのサンプリング周波数
            public int UpSample;

            // カットオフ周波数
            public int Cutoff;

            // 内部アップサンプリング倍率
            public byte Rate;

            // 変換後と変換前のずれ
            public double Gap;
        }
    }
}