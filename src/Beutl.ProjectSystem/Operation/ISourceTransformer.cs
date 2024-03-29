﻿using Beutl.Animation;
using Beutl.Rendering;

namespace Beutl.Operation;

// 流れてくる値を変換
public interface ISourceTransformer : ISourceOperator
{
    void Transform(IList<Renderable> value, IClock clock);
}
