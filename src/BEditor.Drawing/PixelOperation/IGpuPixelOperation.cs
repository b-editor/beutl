namespace BEditor.Drawing.PixelOperation
{
    public interface IGpuPixelOperation
    {
        // C# 10 の static virtual 使う

        public string GetSource();
        public string GetKernel();
    }

    public interface IGpuPixelOperation<T> where T : notnull
    {
        public string GetSource();
        public string GetKernel();
    }

    public interface IGpuPixelOperation<T1, T2>
        where T1 : notnull
        where T2 : notnull
    {
        public string GetSource();
        public string GetKernel();
    }

    public interface IGpuPixelOperation<T1, T2, T3>
        where T1 : notnull
        where T2 : notnull
        where T3 : notnull
    {
        public string GetSource();
        public string GetKernel();
    }

    public interface IGpuPixelOperation<T1, T2, T3, T4>
        where T1 : notnull
        where T2 : notnull
        where T3 : notnull
        where T4 : notnull
    {
        public string GetSource();
        public string GetKernel();
    }
}