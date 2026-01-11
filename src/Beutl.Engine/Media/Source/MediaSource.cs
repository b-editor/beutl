using System.Text.Json.Serialization;
using Beutl.Engine;
using Beutl.IO;

namespace Beutl.Media.Source;

[JsonConverter(typeof(MediaSourceJsonConverter))]
[SuppressResourceClassGeneration]
public abstract class MediaSource : EngineObject, IFileSource
{
    private Uri? _uri;

    public new Uri Uri
    {
        get => _uri ?? throw new InvalidOperationException("URI is not set.");
        protected set => _uri = value;
    }

    public bool HasUri => _uri != null;

    public abstract void ReadFrom(Uri uri);

    public new abstract class Resource : EngineObject.Resource
    {
    }
}
