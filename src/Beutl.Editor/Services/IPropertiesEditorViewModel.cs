using System.Text.Json.Nodes;

using Beutl.Collections;
using Beutl.Extensibility;

namespace Beutl.Editor.Services;

public interface IPropertiesEditorViewModel : IDisposable
{
    ICoreObject Target { get; }

    CoreList<IPropertyEditorContext> Properties { get; }

    void ReadFromJson(JsonObject json);

    void WriteToJson(JsonObject json);
}
