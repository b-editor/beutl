namespace Beutl.Extensibility;

public abstract class ControllableEncodingExtension : Extension
{
    public virtual bool IsSupported(string file)
    {
        return SupportExtensions().Contains(Path.GetExtension(file));
    }

    public abstract IEnumerable<string> SupportExtensions();

    public abstract EncodingController CreateController(
        string file, IFrameProvider frameProvider, ISampleProvider sampleProvider);
}
