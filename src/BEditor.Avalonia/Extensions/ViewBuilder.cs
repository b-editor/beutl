using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BEditor.Data;
using BEditor.ViewModels.Timelines;
using BEditor.Views.Timelines;

namespace BEditor.Extensions
{
    public static class ViewBuilder
    {
        public static readonly EditingProperty<Timeline> TimelineProperty = EditingProperty.Register<Timeline, Scene>("GetTimeline");
        public static readonly EditingProperty<TimelineViewModel> TimelineViewModelProperty = EditingProperty.Register<TimelineViewModel, Scene>("GetTimelineViewModel");
        public static readonly EditingProperty<ClipView> ClipViewProperty = EditingProperty.Register<ClipView, ClipElement>("GetClipView");
        public static readonly EditingProperty<ClipViewModel> ClipViewModelProperty = EditingProperty.Register<ClipViewModel, ClipElement>("GetClipViewModel");

        public static Timeline GetCreateTimeline(this Scene scene)
        {
            if (scene[TimelineProperty] is null)
            {
                scene[TimelineProperty] = new Timeline(scene);
            }
            return scene.GetValue(TimelineProperty);
        }
        public static TimelineViewModel GetCreateTimelineViewModel(this Scene scene)
        {
            if (scene[TimelineViewModelProperty] is null)
            {
                scene[TimelineViewModelProperty] = new TimelineViewModel(scene);
            }
            return scene.GetValue(TimelineViewModelProperty);
        }
        public static ClipView GetCreateClipView(this ClipElement clip)
        {
            if (clip[ClipViewProperty] is null)
            {
                clip[ClipViewProperty] = new ClipView(clip);
            }
            return clip.GetValue(ClipViewProperty);
        }
        public static ClipViewModel GetCreateClipViewModel(this ClipElement clip)
        {
            if (clip[ClipViewModelProperty] is null)
            {
                clip[ClipViewModelProperty] = new ClipViewModel(clip);
            }
            return clip.GetValue(ClipViewModelProperty);
        }

        public static Timeline GetCreateTimelineSafe(this Scene scene)
        {
            if (scene[TimelineProperty] is null)
            {
                scene.Synchronize.Send(static s =>
                {
                    var scene = (Scene)s!;
                    scene[TimelineProperty] = new Timeline(scene);
                }, scene);
            }
            return scene.GetValue(TimelineProperty);
        }
        public static TimelineViewModel GetCreateTimelineViewModelSafe(this Scene scene)
        {
            if (scene[TimelineViewModelProperty] is null)
            {
                scene.Synchronize.Send(static s =>
                {
                    var scene = (Scene)s!;
                    scene[TimelineViewModelProperty] = new TimelineViewModel(scene);
                }, scene);
            }
            return scene.GetValue(TimelineViewModelProperty);
        }
        public static ClipView GetCreateClipViewSafe(this ClipElement clip)
        {
            if (clip[ClipViewProperty] is null)
            {
                clip.Synchronize.Send(static c =>
                {
                    var clip = (ClipElement)c!;
                    clip[ClipViewProperty] = new ClipView(clip);
                }, clip);
            }
            return clip.GetValue(ClipViewProperty);
        }
        public static ClipViewModel GetCreateClipViewModelSafe(this ClipElement clip)
        {
            if (clip[ClipViewModelProperty] is null)
            {
                clip.Synchronize.Send(static c =>
                {
                    var clip = (ClipElement)c!;
                    clip[ClipViewModelProperty] = new ClipViewModel(clip);
                }, clip);
            }
            return clip.GetValue(ClipViewModelProperty);
        }
    }
}
