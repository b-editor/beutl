using BeUtl.Animation;
using BeUtl.Rendering;

namespace BeUtl.Streaming;

// 流れてくる値を変換
public abstract class StreamSelector : StreamOperator
{
    public abstract IRenderable? Select(IRenderable? value, IClock clock);
}
