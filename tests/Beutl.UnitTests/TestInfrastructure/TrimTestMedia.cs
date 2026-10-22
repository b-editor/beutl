using Beutl.Animation;
using Beutl.Animation.Easings;
using Beutl.Audio;
using Beutl.Configuration;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Media;
using Beutl.Media.Source;
using Beutl.ProjectSystem;
using Beutl.UnitTests.Engine.Graphics.Rendering;

namespace Beutl.UnitTests.TestInfrastructure;

/// <summary>
/// Media, compositors and speed curves shared by the slip, roll, slide and resize fixtures.
/// </summary>
internal static class TrimTestMedia
{
    public static MediaTime Seconds(double value) => MediaTime.FromSeconds(value);

    // A 100x100 test video at 30 fps that lasts durationSeconds.
    public static VideoSource CreateVideoSource(int durationSeconds)
    {
        var source = new VideoSource();
        source.ReadFrom(new Uri(TestMediaHelper.CreateTestVideoFile(100, 100, new Rational(30, 1), durationSeconds * 30)));
        return source;
    }

    public static SourceVideo CreateVideo(int durationSeconds, float speed = 100, double offsetSeconds = 0)
        => new()
        {
            Source = { Value = CreateVideoSource(durationSeconds) },
            Speed = { Value = speed },
            OffsetPosition = { Value = Seconds(offsetSeconds) }
        };

    // Adds the SourceVideo, SourceSound or SceneSound that mediaType names, with a source of durationSeconds.
    public static (IProperty<float> Speed, IProperty<MediaTime> Offset) AddMedia(
        Element element,
        string mediaType,
        float speed,
        double offsetSeconds = 0,
        int durationSeconds = 20,
        KeyFrameAnimation<float>? speedAnimation = null)
    {
        EngineObject media;
        IProperty<float> speedProperty;
        IProperty<MediaTime> offsetProperty;
        switch (mediaType)
        {
            case nameof(SourceVideo):
                var video = new SourceVideo { Source = { Value = CreateVideoSource(durationSeconds) } };
                media = video;
                speedProperty = video.Speed;
                offsetProperty = video.OffsetPosition;
                break;
            case nameof(SourceSound):
                var soundSource = new SoundSource();
                soundSource.ReadFrom(new Uri(TestMediaHelper.CreateTestAudioFile(durationSeconds: durationSeconds)));
                var sound = new SourceSound { Source = { Value = soundSource } };
                media = sound;
                speedProperty = sound.Speed;
                offsetProperty = sound.OffsetPosition;
                break;
            case nameof(SceneSound):
                var sceneSound = new SceneSound
                {
                    ReferencedScene = { Value = new Scene { Duration = Seconds(durationSeconds) } }
                };
                media = sceneSound;
                speedProperty = sceneSound.Speed;
                offsetProperty = sceneSound.OffsetPosition;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mediaType), mediaType, "Unknown media type.");
        }

        speedProperty.Value = speed;
        if (speedAnimation != null)
            speedProperty.SetKeyFrameAnimation(speedAnimation);
        offsetProperty.Value = Seconds(offsetSeconds);
        element.Objects.Add(media);
        return (speedProperty, offsetProperty);
    }

    public static SceneCompositor CreateCompositor(Scene scene)
        => new(scene) { DisableResourceShare = true, ForceOriginalSource = true };

    // Speed rising linearly from 100% at 0s to 200% at 10s.
    public static KeyFrameAnimation<float> CreateSpeedRamp(bool useGlobalClock)
    {
        var animation = new KeyFrameAnimation<float> { UseGlobalClock = useGlobalClock };
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = MediaTime.Zero,
            Value = 100,
            Easing = new LinearEasing()
        });
        animation.KeyFrames.Add(new KeyFrame<float>
        {
            KeyTime = Seconds(10),
            Value = 200,
            Easing = new LinearEasing()
        });
        return animation;
    }
}

/// <summary>
/// Sets <see cref="Beutl.Configuration.EditorConfig.ClampResizeToOriginalLength"/> and puts the previous value
/// back on dispose.
/// </summary>
internal sealed class ClampResizeToOriginalLengthScope : IDisposable
{
    private readonly bool _original;

    public ClampResizeToOriginalLengthScope(bool value)
    {
        _original = GlobalConfiguration.Instance.EditorConfig.ClampResizeToOriginalLength;
        GlobalConfiguration.Instance.EditorConfig.ClampResizeToOriginalLength = value;
    }

    public void Dispose() => GlobalConfiguration.Instance.EditorConfig.ClampResizeToOriginalLength = _original;
}
