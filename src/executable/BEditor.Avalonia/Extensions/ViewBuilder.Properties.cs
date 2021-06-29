
using Avalonia.Controls;

using BEditor.Data;
using BEditor.Data.Property;
using BEditor.Data.Property.Easing;
using BEditor.ViewModels.Timelines;
using BEditor.Views.Properties;
using BEditor.Views.Timelines;

namespace BEditor.Extensions
{
    public static partial class ViewBuilder
    {
        public static readonly AttachedProperty<Timeline> TimelineProperty
            = EditingProperty.RegisterAttached<Timeline, Scene>("GetTimeline", EditingPropertyOptions<Timeline>.Create(isDisposable: true));

        public static readonly AttachedProperty<TimelineViewModel> TimelineViewModelProperty
            = EditingProperty.RegisterAttached<TimelineViewModel, Scene>("GetTimelineViewModel", EditingPropertyOptions<TimelineViewModel>.Create(isDisposable: true));

        public static readonly AttachedProperty<ClipView> ClipViewProperty
            = EditingProperty.RegisterAttached<ClipView, ClipElement>("GetClipView", EditingPropertyOptions<ClipView>.Create(isDisposable: true));

        public static readonly AttachedProperty<ClipPropertyView> ClipPropertyViewProperty
            = EditingProperty.RegisterAttached<ClipPropertyView, ClipElement>("GetClipPropertyView", EditingPropertyOptions<ClipPropertyView>.Create(isDisposable: true));

        public static readonly AttachedProperty<ClipViewModel> ClipViewModelProperty
            = EditingProperty.RegisterAttached<ClipViewModel, ClipElement>("GetClipViewModel", EditingPropertyOptions<ClipViewModel>.Create(isDisposable: true));

        public static readonly AttachedProperty<Control> EffectElementViewProperty
            = EditingProperty.RegisterAttached<Control, EffectElement>("GetPropertyView", EditingPropertyOptions<Control>.Create(isDisposable: true));

        public static readonly AttachedProperty<Control> PropertyElementViewProperty
            = EditingProperty.RegisterAttached<Control, PropertyElement>("GetPropertyView", EditingPropertyOptions<Control>.Create(isDisposable: true));

        public static readonly AttachedProperty<Control> EasePropertyViewProperty
            = EditingProperty.RegisterAttached<Control, EasingFunc>("GetPropertyView", EditingPropertyOptions<Control>.Create(isDisposable: true));

        public static readonly AttachedProperty<Control> KeyframeProperty
            = EditingProperty.RegisterAttached<Control, EffectElement>("GetKeyframe", EditingPropertyOptions<Control>.Create(isDisposable: true));

        public static readonly AttachedProperty<Control> KeyframeViewProperty
            = EditingProperty.RegisterAttached<Control, IKeyframeProperty>("GetKeyframeView", EditingPropertyOptions<Control>.Create(isDisposable: true));
    }
}