using Beutl.Animation;
using Beutl.Engine;
using Beutl.Extensibility;
using Beutl.JsonConverters;
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
    public static IEnumerable<(IKeyFrameAnimation Animation, MediaTime Offset)>
        EnumerateKeyFrameAnimations(Element root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var visited = new HashSet<IKeyFrameAnimation>();

        var enginePass = new ObjectSearcher(root, v => v is EngineObject);
        foreach (EngineObject obj in enginePass.SearchAll().OfType<EngineObject>())
        {
            foreach (IProperty property in obj.Properties)
            {
                if (property.GetKeyFrameAnimation() is { } kfa && visited.Add(kfa))
                {
                    MediaTime offset = kfa.UseGlobalClock.Value
                        ? MediaTime.Zero
                        : obj.TimeRange.Value.Start;
                    yield return (kfa, offset);
                }
            }
        }

        var memberPass = new ObjectSearcher(root, v => v is INodeMember);
        foreach (INodeMember member in memberPass.SearchAll().OfType<INodeMember>())
        {
            if (member is IEnginePropertyBackedInputPort)
                continue;

            if (member.Property is IAnimatablePropertyAdapter { Animation: { } kfa }
                && visited.Add(kfa))
            {
                MediaTime offset;
                if (kfa.UseGlobalClock.Value)
                {
                    offset = MediaTime.Zero;
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

    public static MediaTime? FindAdjacentKeyFrame(
        IEnumerable<Element> roots, MediaTime current, bool forward)
    {
        ArgumentNullException.ThrowIfNull(roots);

        MediaTime? target = null;
        foreach (Element el in roots)
        {
            Rational rate = el.FindHierarchicalParent<Project>().GetFrameRate();
            foreach ((IKeyFrameAnimation anim, MediaTime offset) in EnumerateKeyFrameAnimations(el))
            {
                foreach (IKeyFrame kf in anim.KeyFrames)
                {
                    // Times within a tick of a frame, as a template's key times can be, count as that frame.
                    MediaTime time = MediaTimeJsonConverter.SnapToFrame(kf.KeyTime.Value, rate)
                                     + MediaTimeJsonConverter.SnapToFrame(offset, rate);
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
