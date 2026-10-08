using System.Runtime.InteropServices;
using Beutl.FFmpegIpc.Protocol.Messages;
using FFmpeg.AutoGen.Abstractions;
using FFmpegSharp;

namespace Beutl.FFmpegWorker.Encoding;

internal static class EncoderOptionsQuery
{
    public static unsafe EncoderOptionInfo[] GetOptions(MediaCodec codec, AVPixelFormat pixelFormat)
    {
        AVCodecContext* context = ffmpeg.avcodec_alloc_context3(codec);
        if (context == null) throw new OutOfMemoryException();
        try
        {
            context->pix_fmt = pixelFormat;
            if (context->priv_data == null) return [];
            var nativeOptions = new List<nint>();
            AVOption* previous = null;
            while ((previous = ffmpeg.av_opt_next(context->priv_data, previous)) != null)
                nativeOptions.Add((nint)previous);

            var result = new List<EncoderOptionInfo>();
            foreach (nint pointer in nativeOptions)
            {
                var option = (AVOption*)pointer;
                if (option->type == AVOptionType.AV_OPT_TYPE_CONST
                    || (option->flags & ffmpeg.AV_OPT_FLAG_ENCODING_PARAM) == 0
                    || (option->flags & ffmpeg.AV_OPT_FLAG_READONLY) != 0)
                    continue;

                string? name = Read(option->name);
                if (string.IsNullOrEmpty(name)) continue;
                EncoderOptionKind kind = option->type switch
                {
                    AVOptionType.AV_OPT_TYPE_INT or AVOptionType.AV_OPT_TYPE_INT64
                        or AVOptionType.AV_OPT_TYPE_UINT or AVOptionType.AV_OPT_TYPE_UINT64 => EncoderOptionKind.Integer,
                    AVOptionType.AV_OPT_TYPE_FLOAT or AVOptionType.AV_OPT_TYPE_DOUBLE => EncoderOptionKind.Number,
                    AVOptionType.AV_OPT_TYPE_BOOL => EncoderOptionKind.Boolean,
                    _ => EncoderOptionKind.Text,
                };
                string? unit = Read(option->unit);
                EncoderOptionChoiceInfo[] choices = unit != null
                    ? nativeOptions.Select(p => ReadConstant((AVOption*)p, unit))
                        .OfType<EncoderOptionChoiceInfo>().ToArray()
                    : [];
                bool numeric = kind is EncoderOptionKind.Integer or EncoderOptionKind.Number;
                bool integral = kind == EncoderOptionKind.Integer;
                if (choices.Length > 0 && numeric) kind = EncoderOptionKind.Choice;

                result.Add(new EncoderOptionInfo
                {
                    Name = name,
                    Description = Read(option->help),
                    Kind = kind,
                    AllowsNumericValues = numeric && choices.Length > 0,
                    RequiresInteger = integral,
                    DefaultValue = GetDefaultValue(context->priv_data, name),
                    Minimum = (numeric || kind == EncoderOptionKind.Boolean)
                        && double.IsFinite(option->min) ? option->min : null,
                    Maximum = (numeric || kind == EncoderOptionKind.Boolean)
                        && double.IsFinite(option->max) ? option->max : null,
                    Choices = choices,
                });
            }

            return result.ToArray();
        }
        finally
        {
            ffmpeg.avcodec_free_context(&context);
        }
    }

    private static unsafe EncoderOptionChoiceInfo? ReadConstant(AVOption* option, string unit)
    {
        if (option->type != AVOptionType.AV_OPT_TYPE_CONST || Read(option->unit) != unit)
            return null;
        return new EncoderOptionChoiceInfo
        {
            Value = Read(option->name) ?? "",
            Description = Read(option->help),
            NumericValue = option->default_val.i64,
        };
    }

    private static unsafe string? GetDefaultValue(void* obj, string name)
    {
        byte* value = null;
        try
        {
            return ffmpeg.av_opt_get(obj, name, 0, &value) >= 0 ? Read(value) : null;
        }
        finally
        {
            ffmpeg.av_free(value);
        }
    }

    private static unsafe string? Read(byte* value)
        => value == null ? null : Marshal.PtrToStringUTF8((nint)value);

}
