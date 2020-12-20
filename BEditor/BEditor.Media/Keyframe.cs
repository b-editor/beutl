namespace BEditor.Media
{
    public readonly struct Keyframe<T>
    {
        public Keyframe(T value, Frame frame)
        {
            Value = value;
            Frame = frame;
        }

        public T Value { get; }
        public Frame Frame { get; }
    }
}
