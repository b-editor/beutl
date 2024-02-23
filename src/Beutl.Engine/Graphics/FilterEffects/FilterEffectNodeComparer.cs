using System.Collections.Immutable;

using Beutl.Collections;
using Beutl.Graphics.Rendering;
using Beutl.Rendering.Cache;

namespace Beutl.Graphics.Effects;

public class FilterEffectNodeComparer
{
    private ImmutableArray<FEItemWrapper> _current = [];
    private int? _prevVersion;

    public FilterEffectNodeComparer(FilterEffectNode node)
    {
        Node = node;
    }

    public FilterEffectNode Node { get; }

    private static int CountEquals(IReadOnlyList<FEItemWrapper> left, IReadOnlyList<FEItemWrapper> right)
    {
        int minLength = Math.Min(left.Count, right.Count);
        for (int i = 0; i < minLength; i++)
        {
            if (!left[i].Item.Equals(right[i].Item))
            {
                return i;
            }
        }

        return minLength;
    }

    public void OnRender(FilterEffectActivator activator)
    {
        if (activator.CurrentTargets.Count > 0)
        {
            var captured = activator.CurrentTargets[0]._history.ToImmutableArray();
            _current = captured;
        }
        else
        {
            _current = [];
        }
    }

    public void Accepts(RenderCache cache)
    {
        if (_prevVersion != Node.FilterEffect.Version)
        {
            using (var context = new FilterEffectContext(Node.OriginalBounds))
            {
                context.Apply(Node.FilterEffect);

            Compare:
                int minLength = Math.Min(_current.Length, context._items.Count);
                int d = CountEquals(_current, context._items);

                if (context._renderTimeItems.Count > 0)
                {
                    object[] items = [.. context._renderTimeItems];
                    context._renderTimeItems.Clear();
                    foreach (object item in items)
                    {
                        if (context.Bounds.IsInvalid)
                        {
                            if (context._items.Count < _current.Length)
                            {
                                context.Bounds = _current[context._items.Count].SourceBounds;
                            }
                            else
                            {
                                // エフェクトが追加されたとき
                                d = CountEquals(_current, context._items);
                                cache.ReportSameNumber(d, context._items.Count + 1);
                                _prevVersion = Node.FilterEffect.Version;
                                return;
                            }
                        }

                        if (item is FilterEffect fe)
                        {
                            context.Apply(fe);
                        }
                        else if (item is FEItemWrapper feitem)
                        {
                            context._items.Add(feitem);
                        }
                    }

                    goto Compare;
                }
                else
                {
                    cache.ReportSameNumber(d, context._items.Count);
                }
            }
        }
        else
        {
            cache.ReportSameNumber(_current.Length, _current.Length);
        }

        _prevVersion = Node.FilterEffect.Version;
    }
}
