using Beutl.Animation;
using Beutl.Rendering;

namespace Beutl.Streaming;

// 流れてくる値を変換
public interface IStreamSelector : IStreamOperator
{
    IRenderable? Select(IRenderable? value, IClock clock);
}
