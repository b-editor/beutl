namespace Beutl.Audio;

public readonly record struct AudioPushedState : IDisposable
{
    public AudioPushedState(
        IAudio audio,
        int level,
        PushedStateType type)
    {
        Audio = audio;
        Level = level;
        Type = type;
    }

    public AudioPushedState()
    {
        Audio = null!;
        Level = -1;
        Type = PushedStateType.None;
    }

    public enum PushedStateType
    {
        None,
        Gain,
        Effect,
        Filter,
    }

    public IAudio Audio { get; init; }

    public int Level { get; init; }

    public PushedStateType Type { get; init; }

    public void Dispose()
    {
        if (Audio == null)
            return;

        switch (Type)
        {
            case PushedStateType.None:
                break;
            case PushedStateType.Gain:
                Audio.PopGain(Level);
                break;
            default:
                break;
        }
    }
}
