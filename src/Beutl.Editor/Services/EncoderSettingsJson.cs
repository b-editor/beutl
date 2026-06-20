using System.Text.Json.Nodes;
using Beutl.Media;
using Beutl.Media.Encoding;
using Beutl.Serialization;

namespace Beutl.Editor.Services;

public static class EncoderSettingsJson
{
    public static JsonObject? Serialize(MediaEncoderSettings? settings)
    {
        return settings == null ? null : CoreSerializer.SerializeToJsonObject(settings);
    }

    public static void Populate(MediaEncoderSettings? settings, JsonObject json)
    {
        if (settings == null) return;

        CoreSerializer.PopulateFromJsonObject(settings, settings.GetType(), json);
    }

    public static void PopulateVideoPreset(VideoEncoderSettings settings, JsonObject json)
    {
        PixelSize sourceSize = settings.SourceSize;
        PixelSize destinationSize = settings.DestinationSize;
        Rational frameRate = settings.FrameRate;

        try
        {
            Populate(settings, json);
        }
        finally
        {
            settings.SourceSize = sourceSize;
            settings.DestinationSize = destinationSize;
            settings.FrameRate = frameRate;
        }
    }

    public static void PopulateAudioPreset(AudioEncoderSettings settings, JsonObject json)
    {
        int sampleRate = settings.SampleRate;

        try
        {
            Populate(settings, json);
        }
        finally
        {
            settings.SampleRate = sampleRate;
        }
    }
}
