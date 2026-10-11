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

    public static void CopyTo(MediaEncoderSettings? source, MediaEncoderSettings? destination)
    {
        if (source == null || destination == null) return;

        Populate(destination, CoreSerializer.SerializeToJsonObject(source));
    }

    /// <summary>Applies a preset to <paramref name="settings"/>, keeping their source size, output size and frame rate.</summary>
    public static void PopulateVideoPreset(VideoEncoderSettings settings, JsonObject json)
    {
        PopulateVideoPreset(settings, json, appliesFrameSizeAndRate: false);
    }

    /// <summary>Applies a preset to <paramref name="settings"/>, keeping their source size.</summary>
    /// <param name="settings">The settings to apply the preset to.</param>
    /// <param name="json">The preset's video settings.</param>
    /// <param name="appliesFrameSizeAndRate">
    /// Whether the preset sets the output size and the frame rate, as a platform preset named after them does. When
    /// it does not, both stay as they are. The source size always does, because it is the scene's frame size.
    /// </param>
    public static void PopulateVideoPreset(VideoEncoderSettings settings, JsonObject json, bool appliesFrameSizeAndRate)
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
            if (!appliesFrameSizeAndRate)
            {
                settings.DestinationSize = destinationSize;
                settings.FrameRate = frameRate;
            }
        }
    }

    /// <summary>Applies a preset to <paramref name="settings"/>, keeping their sample rate.</summary>
    public static void PopulateAudioPreset(AudioEncoderSettings settings, JsonObject json)
    {
        PopulateAudioPreset(settings, json, appliesSampleRate: false);
    }

    /// <summary>Applies a preset to <paramref name="settings"/>.</summary>
    /// <param name="settings">The settings to apply the preset to.</param>
    /// <param name="json">The preset's audio settings.</param>
    /// <param name="appliesSampleRate">
    /// Whether the preset sets the sample rate, as a platform preset does. When it does not, the sample rate stays as
    /// it is.
    /// </param>
    public static void PopulateAudioPreset(AudioEncoderSettings settings, JsonObject json, bool appliesSampleRate)
    {
        int sampleRate = settings.SampleRate;

        try
        {
            Populate(settings, json);
        }
        finally
        {
            if (!appliesSampleRate)
                settings.SampleRate = sampleRate;
        }
    }
}
