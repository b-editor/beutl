namespace Beutl.Media.Music;

public readonly struct Sample : IEquatable<Sample>
{
    public static readonly Sample One = new(1, 1);

    public Sample(float left, float right)
    {
        Left = left;
        Right = right;
    }

    public float Left { get; }

    public float Right { get; }

    public override bool Equals(object? obj) => obj is Sample sample && Equals(sample);

    public bool Equals(Sample other) => Left == other.Left && Right == other.Right;

    public override int GetHashCode() => HashCode.Combine(Left, Right);

    public static bool operator ==(Sample left, Sample right) => left.Equals(right);

    public static bool operator !=(Sample left, Sample right) => !(left == right);
}
