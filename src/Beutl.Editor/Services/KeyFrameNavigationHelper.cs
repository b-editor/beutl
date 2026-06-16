using Beutl.Animation;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.NodeGraph;
using Beutl.ProjectSystem;

namespace Beutl.Editor.Services;

public static class KeyFrameNavigationHelper
{
    // Enumerates KeyFrameAnimations with their timeline-absolute offset:
    //   Pass 1 (EngineObject.Properties): offset = obj.TimeRange.Start (local clock)
    //     or Zero (global clock), matching KeyFrameAnimation<T>.GetAnimatedValue.
    //   Pass 2 (INodeMember.Property): offset = parent GraphNode.Start (local clock),
    //     matching GraphSnapshot.LoadAnimatedValues. IEnginePropertyBackedInputPort is
    //     excluded to avoid double-counting with pass 1.
    public static IEnumerable<(KeyFrameAnimation Animation, TimeSpan Offset)>
        EnumerateKeyFrameAnimations(Element root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var visited = new HashSet<KeyFrameAnimation>();

        var enginePass = new ObjectSearcher(root, v => v is EngineObject);
        foreach (EngineObject obj in enginePass.SearchAll().OfType<EngineObject>())
        {
            foreach (IProperty property in obj.Properties)
            {
                if (property.Animation is KeyFrameAnimation kfa && visited.Add(kfa))
                {
                    TimeSpan offset = kfa.UseGlobalClock
                        ? TimeSpan.Zero
                        : obj.TimeRange.Start;
                    yield return (kfa, offset);
                }
            }
        }

        var memberPass = new ObjectSearcher(root, v => v is INodeMember);
        foreach (INodeMember member in memberPass.SearchAll().OfType<INodeMember>())
        {
            if (member is IEnginePropertyBackedInputPort)
                continue;

            if (member.Property is IAnimatablePropertyAdapter { Animation: KeyFrameAnimation kfa }
                && visited.Add(kfa))
            {
                TimeSpan offset;
                if (kfa.UseGlobalClock)
                {
                    offset = TimeSpan.Zero;
                }
                else
                {
                    GraphNode? parent = member.FindHierarchicalParent<GraphNode>();
                    if (parent == null)
                        continue;
                    offset = parent.Start;
                }

                yield return (kfa, offset);
            }
        }
    }

    public static TimeSpan? FindAdjacentKeyFrame(
        IEnumerable<Element> roots, TimeSpan current, bool forward)
    {
        ArgumentNullException.ThrowIfNull(roots);

        TimeSpan? target = null;
        foreach (Element el in roots)
        {
            foreach ((KeyFrameAnimation anim, TimeSpan offset) in EnumerateKeyFrameAnimations(el))
            {
                foreach (IKeyFrame kf in anim.KeyFrames)
                {
                    TimeSpan time = kf.KeyTime + offset;
                    bool inDirection = forward ? time > current : time < current;
                    bool isCloser = target == null
                        || (forward ? time < target.Value : time > target.Value);
                    if (inDirection && isCloser)
                        target = time;
                }
            }
        }

        return target;
    }
}
