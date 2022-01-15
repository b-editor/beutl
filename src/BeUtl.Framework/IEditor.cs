using Avalonia.Controls;

namespace BeUtl.Framework;

public interface IStorableControl : IControl, IStorable
{
}

public interface IEditor : IControl, IAsyncDisposable
{
}
