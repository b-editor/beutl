using Avalonia.Controls;

namespace BEditorNext.Framework;

public interface IStorableControl : IControl, IStorable
{
}

public interface IEditor : IControl, IAsyncDisposable
{
}
