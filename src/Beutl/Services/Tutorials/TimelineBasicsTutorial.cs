using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.ElementPropertyTab;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.LibraryTab;
using Beutl.Engine;
using Beutl.Graphics.Shapes;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels;
using Beutl.ViewModels.Editors;

namespace Beutl.Services.Tutorials;

public static class TimelineBasicsTutorial
{
    public const string TutorialId = "timeline-basics";

    public static TutorialDefinition Create()
    {
        IDisposable? step1Subscription = null;
        IDisposable? step2Subscription = null;
        IDisposable? step3Subscription = null;
        IDisposable? step4Subscription = null;

        return new TutorialDefinition
        {
            Id = TutorialId,
            Title = TutorialStrings.Tutorial_SceneEdit_Title,
            Description = TutorialStrings.Tutorial_SceneEdit_Description,
            Priority = 10,
            Category = "basics",
            CanStart = TutorialHelpers.OpenLibraryTabIfNeeded,
            FulfillPrerequisites = () => TutorialHelpers.EnsureProjectAsync("Tutorial"),
            Steps =
            [
                // Step 1: Add an ellipse to the timeline
                new TutorialStep
                {
                    Id = "scene-add-ellipse",
                    Title = TutorialStrings.Tutorial_SceneEdit_Step1_Title,
                    Content = TutorialStrings.Tutorial_SceneEdit_Step1_Content,
                    TargetElements =
                    [
                        new TargetElementDefinition { ToolTabType = typeof(TimelineTabExtension), IsPrimary = true },
                        new TargetElementDefinition { ToolTabType = typeof(LibraryTabExtension) },
                    ],
                    PreferredPlacement = TutorialStepPlacement.Top,
                    IsActionRequired = true,
                    OnShown = () =>
                    {
                        EditViewModel? editVm = TutorialHelpers.GetEditViewModel();
                        if (editVm == null) return;

                        step1Subscription = TutorialHelpers.SubscribeToElementAdded<EllipseShape>(
                            editVm.Scene,
                            () => TutorialService.Current.AdvanceStep());
                    },
                    OnDismissed = () =>
                    {
                        step1Subscription?.Dispose();
                        step1Subscription = null;
                    },
                },

                // Step 2: Select the element in the timeline
                new TutorialStep
                {
                    Id = "scene-select-element",
                    Title = TutorialStrings.Tutorial_SceneEdit_Step2_Title,
                    Content = TutorialStrings.Tutorial_SceneEdit_Step2_Content,
                    TargetElements = [new TargetElementDefinition { ToolTabType = typeof(TimelineTabExtension), IsPrimary = true }],
                    PreferredPlacement = TutorialStepPlacement.Top,
                    IsActionRequired = true,
                    OnShown = () =>
                    {
                        EditViewModel? editVm = TutorialHelpers.GetEditViewModel();
                        step2Subscription = TutorialHelpers.SubscribeToElementSelection(
                            editVm,
                            () => TutorialService.Current.AdvanceStep());
                    },
                    OnDismissed = () =>
                    {
                        step2Subscription?.Dispose();
                        step2Subscription = null;
                    },
                },

                // Step 3: Introduce the Source Operators tab
                new TutorialStep
                {
                    Id = "scene-element-properties",
                    Title = TutorialStrings.Tutorial_SceneEdit_Step3_Title,
                    Content = TutorialStrings.Tutorial_SceneEdit_Step3_Content,
                    TargetElements = [new TargetElementDefinition { ToolTabType = typeof(ElementPropertyTabExtension), IsPrimary = true }],
                    PreferredPlacement = TutorialStepPlacement.Left,
                },

                // Step 4: Highlight Width property and prompt to enable animation
                new TutorialStep
                {
                    Id = "scene-enable-animation",
                    Title = TutorialStrings.Tutorial_SceneEdit_Step4_Title,
                    Content = TutorialStrings.Tutorial_SceneEdit_Step4_Content,
                    PreferredPlacement = TutorialStepPlacement.Bottom,
                    IsActionRequired = true,
                    TargetElements = [new TargetElementDefinition { ElementResolver = FindWidthPropertyEditor, IsPrimary = true }],
                    OnShown = () =>
                    {
                        EditViewModel? editVm = TutorialHelpers.GetEditViewModel();
                        if (editVm == null) return;

                        Element? element = TutorialHelpers.FindElementWithObject<EllipseShape>(editVm.Scene);
                        EllipseShape? ellipseOp = TutorialHelpers.GetObject<EllipseShape>(element);
                        if (ellipseOp == null) return;

                        step3Subscription = TutorialHelpers.SubscribeToAnimationEnabled(
                            ellipseOp.Width,
                            () => TutorialService.Current.AdvanceStep());
                    },
                    OnDismissed = () =>
                    {
                        step3Subscription?.Dispose();
                        step3Subscription = null;
                    },
                },

                // Step 5: Move current time and prompt to add a keyframe
                new TutorialStep
                {
                    Id = "scene-add-keyframe",
                    Title = TutorialStrings.Tutorial_SceneEdit_Step5_Title,
                    Content = TutorialStrings.Tutorial_SceneEdit_Step5_Content,
                    PreferredPlacement = TutorialStepPlacement.Bottom,
                    IsActionRequired = true,
                    TargetElements = [new TargetElementDefinition { ElementResolver = FindWidthPropertyEditor, IsPrimary = true }],
                    OnShown = () =>
                    {
                        EditViewModel? editVm = TutorialHelpers.GetEditViewModel();
                        if (editVm == null) return;

                        Element? element = TutorialHelpers.FindElementWithObject<EllipseShape>(editVm.Scene);
                        EllipseShape? ellipseOp = TutorialHelpers.GetObject<EllipseShape>(element);
                        if (ellipseOp == null || element == null) return;

                        IProperty<float> widthProp = ellipseOp.Width;
                        if (widthProp.Animation is not KeyFrameAnimation<float> animation) return;

                        // すでにキーフレームが2つ以上ある場合は現在時間を変更し、次のステップに進む
                        if (animation.KeyFrames.Count >= 2)
                        {
                            editVm.CurrentTime.Value = animation.KeyFrames[1].KeyTime + element.Start;
                            Dispatcher.UIThread.Post(() => TutorialService.Current.AdvanceStep());
                        }
                        else
                        {
                            editVm.CurrentTime.Value = element.Start + TimeSpan.FromSeconds(2);
                            step4Subscription = TutorialHelpers.SubscribeToKeyFrameAdded(
                                animation,
                                2,
                                () => TutorialService.Current.AdvanceStep());
                        }
                    },
                    OnDismissed = () =>
                    {
                        step4Subscription?.Dispose();
                        step4Subscription = null;
                    },
                },

                // Step 6: Change the value (auto-change if default)
                new TutorialStep
                {
                    Id = "scene-change-value",
                    Title = TutorialStrings.Tutorial_SceneEdit_Step6_Title,
                    Content = TutorialStrings.Tutorial_SceneEdit_Step6_Content,
                    PreferredPlacement = TutorialStepPlacement.Bottom,
                    TargetElements = [new TargetElementDefinition { ElementResolver = FindWidthPropertyEditor, IsPrimary = true }],
                    OnDismissed = () =>
                    {
                        EditViewModel? editVm = TutorialHelpers.GetEditViewModel();
                        if (editVm == null) return;

                        Element? element = TutorialHelpers.FindElementWithObject<EllipseShape>(editVm.Scene);
                        EllipseShape? ellipseOp = TutorialHelpers.GetObject<EllipseShape>(element);
                        if (ellipseOp != null)
                        {
                            IProperty<float> widthProp = ellipseOp.Width;
                            if (widthProp.Animation is KeyFrameAnimation<float> animation
                                && animation.KeyFrames.LastOrDefault() is { Value: 100f or <= 100f } lastKeyFrame)
                            {
                                lastKeyFrame.Value = 300f;
                                editVm.HistoryManager.Commit(CommandNames.EditKeyFrame);
                            }
                        }
                    }
                },

                // Step 7: Highlight the Player and prompt to play
                new TutorialStep
                {
                    Id = "scene-preview-animation",
                    Title = TutorialStrings.Tutorial_SceneEdit_Step7_Title,
                    Content = TutorialStrings.Tutorial_SceneEdit_Step7_Content,
                    TargetElements = [new TargetElementDefinition { ElementName = "Player", IsPrimary = true }],
                    PreferredPlacement = TutorialStepPlacement.Bottom,
                    OnShown = () =>
                    {
                        EditViewModel? editVm = TutorialHelpers.GetEditViewModel();
                        Element? element = TutorialHelpers.FindElementWithObject<EllipseShape>(editVm?.Scene);
                        TutorialHelpers.PrepareForPlayback(editVm, element);
                    },
                },

                // Step 8: Completion
                new TutorialStep
                {
                    Id = "scene-complete",
                    Title = TutorialStrings.Tutorial_SceneEdit_Step8_Title,
                    Content = TutorialStrings.Tutorial_SceneEdit_Step8_Content,
                    PreferredPlacement = TutorialStepPlacement.Center,
                },
            ]
        };
    }

    private static Control? FindWidthPropertyEditor()
    {
        TopLevel? topLevel = AppHelper.GetTopLevel();
        return topLevel?.GetVisualDescendants()
            .OfType<NumberEditor<float>>()
            .FirstOrDefault(c =>
                c.DataContext is BaseEditorViewModel vm &&
                vm.PropertyAdapter.GetEngineProperty() is IProperty prop &&
                prop.GetOwnerObject() is EllipseShape &&
                prop.Name == nameof(EllipseShape.Width));
    }
}
