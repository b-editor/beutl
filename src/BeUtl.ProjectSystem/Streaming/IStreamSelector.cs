using BeUtl.Animation;
using BeUtl.Rendering;

namespace BeUtl.Streaming;

// 流れてくる値を変換
public interface IStreamSelector : IStreamOperator
{
    IRenderable? Select(IRenderable? value, IClock clock);
}
