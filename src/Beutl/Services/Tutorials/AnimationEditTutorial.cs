using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Beutl.Animation;
using Beutl.Controls.PropertyEditors;
using Beutl.Editor.Components.GraphEditorTab;
using Beutl.Editor.Components.Helpers;
using Beutl.Editor.Components.LibraryTab;
using Beutl.Editor.Components.LibraryTab.Views;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Shapes;
using Beutl.Graphics.Transformation;
using Beutl.ProjectSystem;
using Beutl.Services.PrimitiveImpls;
using Beutl.ViewModels;
using Beutl.ViewModels.Editors;
using Microsoft.Extensions.DependencyInjection;

namespace Beutl.Services.Tutorials;

public static class AnimationEditTutorial
{
    public const string TutorialId = "animation-edit";

    public static TutorialDefinition Create()
    {
        IDisposable? step1Subscription = null;
        IDisposable? step2Subscription = null;
        IDisposable? step3Subscription = null;
        IDisposable? step4Subscription = null;
        IDisposable? step5Subscription = null;
        IDisposable? step10Subscription = null;
        IDisposable? step11Subscription = null;

        return new TutorialDefinition
        {
            Id = TutorialId,
            Title = TutorialStrings.Tutorial_AnimationEdit_Title,
            Description = TutorialStrings.Tutorial_AnimationEdit_Description,
            Priority = 20,
            Category = "advanced",
            CanStart = TutorialHelpers.OpenLibraryTabIfNeeded,
            FulfillPrerequisites = async () =>
            {
                // プロジェクトを準備（シーンを開くまで）
                bool result = await TutorialHelpers.EnsureProjectAsync("AnimationTutorial");
                if (!result) return false;

                EditViewModel? editVm = TutorialHelpers.GetEditViewModel();
                var adder = editVm?.GetService<IElementAdder>();
                if (adder == null) return false;

                // 楕円要素を追加
                adder.AddElement(new ElementDescription(
                    Start: TimeSpan.Zero,
                    Length: TimeSpan.FromSeconds(5),
                    Layer: 0,
                    InitialObject: typeof(EllipseShape)));

                return true;
            },
            Steps =
            [
                // Step 1: Select the element in the timeline
                new TutorialStep
                {
                    Id = "animation-select-element",
                    Title = TutorialStrings.Tutorial_AnimationEdit_Step1_Title,
                    Content = TutorialStrings.Tutorial_AnimationEdit_Step1_Content,
                    TargetElements =
                        [new TargetElementDefinition { ToolTabType = typeof(TimelineTabExtension), IsPrimary = true }],
                    PreferredPlacement = TutorialStepPlacement.Top,
                    IsActionRequired = true,
                    OnShown = () =>
                    {
                        EditViewModel? editVm = TutorialHelpers.GetEditViewModel();
                        step1Subscription = TutorialHelpers.SubscribeToElementSelection(
                            editVm,
                            () => TutorialService.Current.AdvanceStep());
                    },
                    OnDismissed = () =>
                    {
                        step1Subscription?.Dispose();
                        step1Subscription = null;
                    },
                },

                // Step 2: Add Transform via + button
                new TutorialStep
                {
                    Id = "animation-add-translate",
                    Title = TutorialStrings.Tutorial_AnimationEdit_Step2_Title,
                    Content = TutorialStrings.Tutorial_AnimationEdit_Step2_Content,
                    TargetElements =
                        [new TargetElementDefinition { ElementResolver = FindTransformEditor, IsPrimary = true }],
                    PreferredPlacement = TutorialStepPlacement.Left,
                    IsActionRequired = true,
                    OnShown = () =>
                    {
                        EditViewModel? editVm = TutorialHelpers.GetEditViewModel();
                        if (editVm == null) return;

                        Drawable? drawable = TutorialHelpers.GetDrawable(editVm);
                        if (drawable == null) return;

                        // Already has TranslateTransform?
                        if (TutorialHelpers.GetTranslateTransform(drawable) != null)
                        {
                            Dispatcher.UIThread.Post(() => TutorialService.Current.AdvanceStep());
                            return;
                        }

                        // Transformプロパティの変更を監視
                        if (drawable.Transform.CurrentValue is TransformGroup group)
                        {
                            void Handler(Transform t)
                            {
                                if (t is TranslateTransform)
                                {
                                    Dispatcher.UIThread.Post(() => TutorialService.Current.AdvanceStep());
                                }
                            }

                            group.Children.Attached += Handler;
                            step2Subscription = Disposable.Create(() => group.Children.Attached -= Handler);
                        }
                    },
                    OnDismissed = () =>
                    {
                        step2Subscription?.Dispose();
                        step2Subscription = null;
                    },
                },

                // Step 3: Enable animation for X property
                new TutorialStep
                {
                    Id = "animation-enable-x",
                    Title = TutorialStrings.Tutorial_AnimationEdit_Step3_Title,
                    Content = TutorialStrings.Tutorial_AnimationEdit_Step3_Content,
                    TargetElements =
                    [
                        new TargetElementDefinition
                        {
                            ElementResolver = FindTranslateTransformXPropertyEditor, IsPrimary = true
                        }
                    ],
                    PreferredPlacement = TutorialStepPlacement.Bottom,
                    IsActionRequired = true,
                    OnShown = () =>
                    {
                        EditViewModel? editVm = TutorialHelpers.GetEditViewModel();
                        if (editVm == null) return;

                        TranslateTransform? translateTransform = TutorialHelpers.GetTranslateTransform(
                            TutorialHelpers.GetDrawable(editVm));
                        if (translateTransform == null) return;

                        step3Subscription = TutorialHelpers.SubscribeToAnimationEnabled(
                            translateTransform.X,
                            () => TutorialService.Current.AdvanceStep());
                    },
                    OnDismissed = () =>
                    {
                        step3Subscription?.Dispose();
                        step3Subscription = null;
                    },
                },

                // Step 4: Add keyframe
                new TutorialStep
                {
                    Id = "animation-add-keyframe",
                    Title = TutorialStrings.Tutorial_AnimationEdit_Step4_Title,
                    Content = TutorialStrings.Tutorial_AnimationEdit_Step4_Content,
                    TargetElements =
                    [
                        new TargetElementDefinition
                        {
                            ElementResolver = FindTranslateTransformXPropertyEditor, IsPrimary = true
                        }
                    ],
                    PreferredPlacement = TutorialStepPlacement.Bottom,
                    IsActionRequired = true,
                    OnShown = () =>
                    {
                        EditViewModel? editVm = TutorialHelpers.GetEditViewModel();
                        if (editVm == null) return;

                        Element? element = TutorialHelpers.FindElementWithObject<EllipseShape>(editVm.Scene);
                        if (element == null) return;

                        TranslateTransform? translateTransform = TutorialHelpers.GetTranslateTransform(
                            TutorialHelpers.GetDrawable(editVm));
                        if (translateTransform == null) return;

                        if (translateTransform.X.Animation is not KeyFrameAnimation<float> animation) return;

                        // Move playhead forward
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

                // Step 5: Drag and drop easing
                new TutorialStep
                {
                    Id = "animation-drag-easing",
                    Title = TutorialStrings.Tutorial_AnimationEdit_Step5_Title,
                    Content = TutorialStrings.Tutorial_AnimationEdit_Step5_Content,
                    TargetElements =
                    [
                        new TargetElementDefinition { ToolTabType = typeof(GraphEditorTabExtension), IsPrimary = true },
                        new TargetElementDefinition { ToolTabType = typeof(LibraryTabExtension) },
                    ],
                    PreferredPlacement = TutorialStepPlacement.Top,
                    IsActionRequired = true,
                    OnShown = () =>
                    {
                        EditViewModel? editVm = TutorialHelpers.GetEditViewModel();
                        if (editVm == null) return;

                        // Set LibraryTab to Easings tab via View's TabStrip
                        Dispatcher.UIThread.Post(() =>
                        {
                            TopLevel? topLevel = AppHelper.GetTopLevel();
                            var tabStrip = topLevel?.GetVisualDescendants()
                                .OfType<LibraryTabView>()
                                .FirstOrDefault()?.tabStrip;

                            tabStrip?.SelectedIndex = 1; // 0:Search, 1:Easings, 2:Library, 3:Nodes
                        }, DispatcherPriority.Loaded);

                        // Monitor easing change
                        TranslateTransform? translateTransform = TutorialHelpers.GetTranslateTransform(
                            TutorialHelpers.GetDrawable(editVm));
                        if (translateTransform?.X.Animation is not KeyFrameAnimation<float> animation) return;

                        step5Subscription = TutorialHelpers.SubscribeToEasingChanged(
                            animation,
                            () => TutorialService.Current.AdvanceStep());
                    },
                    OnDismissed = () =>
                    {
                        step5Subscription?.Dispose();
                        step5Subscription = null;
                    },
                },

                // Step 6: Graph editor overview
                new TutorialStep
                {
                    Id = "animation-graph-intro",
                    Title = TutorialStrings.Tutorial_AnimationEdit_Step6_Title,
                    Content = TutorialStrings.Tutorial_AnimationEdit_Step6_Content,
                    TargetElements =
                    [
                        new TargetElementDefinition { ToolTabType = typeof(GraphEditorTabExtension), IsPrimary = true }
                    ],
                    PreferredPlacement = TutorialStepPlacement.Top,
                },

                // Step 7: Move keyframe intro
                new TutorialStep
                {
                    Id = "animation-move-keyframe",
                    Title = TutorialStrings.Tutorial_AnimationEdit_Step7_Title,
                    Content = TutorialStrings.Tutorial_AnimationEdit_Step7_Content,
                    TargetElements =
                    [
                        new TargetElementDefinition { ToolTabType = typeof(GraphEditorTabExtension), IsPrimary = true }
                    ],
                    PreferredPlacement = TutorialStepPlacement.Top,
                },

                // Step 8: Preview animation
                new TutorialStep
                {
                    Id = "animation-play-preview",
                    Title = TutorialStrings.Tutorial_AnimationEdit_Step8_Title,
                    Content = TutorialStrings.Tutorial_AnimationEdit_Step8_Content,
                    TargetElements = [new TargetElementDefinition { ElementName = "Player", IsPrimary = true }],
                    PreferredPlacement = TutorialStepPlacement.Bottom,
                    OnShown = () =>
                    {
                        EditViewModel? editVm = TutorialHelpers.GetEditViewModel();
                        Element? element = TutorialHelpers.FindElementWithObject<EllipseShape>(editVm?.Scene);
                        TutorialHelpers.PrepareForPlayback(editVm, element);
                    },
                },

                // Step 9: Copy all keyframes intro
                new TutorialStep
                {
                    Id = "animation-copy-all",
                    Title = TutorialStrings.Tutorial_AnimationEdit_Step9_Title,
                    Content = TutorialStrings.Tutorial_AnimationEdit_Step9_Content,
                    TargetElements =
                    [
                        new TargetElementDefinition { ToolTabType = typeof(GraphEditorTabExtension), IsPrimary = true }
                    ],
                    PreferredPlacement = TutorialStepPlacement.Top,
                },

                // Step 10: Enable animation for Y property
                new TutorialStep
                {
                    Id = "animation-enable-y",
                    Title = TutorialStrings.Tutorial_AnimationEdit_Step10_Title,
                    Content = TutorialStrings.Tutorial_AnimationEdit_Step10_Content,
                    TargetElements =
                    [
                        new TargetElementDefinition
                        {
                            ElementResolver = FindTranslateTransformYPropertyEditor, IsPrimary = true
                        }
                    ],
                    PreferredPlacement = TutorialStepPlacement.Bottom,
                    IsActionRequired = true,
                    OnShown = () =>
                    {
                        EditViewModel? editVm = TutorialHelpers.GetEditViewModel();
                        if (editVm == null) return;

                        TranslateTransform? translateTransform = TutorialHelpers.GetTranslateTransform(
                            TutorialHelpers.GetDrawable(editVm));
                        if (translateTransform == null) return;

                        step10Subscription = TutorialHelpers.SubscribeToAnimationEnabled(
                            translateTransform.Y,
                            () => TutorialService.Current.AdvanceStep());
                    },
                    OnDismissed = () =>
                    {
                        step10Subscription?.Dispose();
                        step10Subscription = null;
                    },
                },

                // Step 11: Paste animation
                new TutorialStep
                {
                    Id = "animation-paste",
                    Title = TutorialStrings.Tutorial_AnimationEdit_Step11_Title,
                    Content = TutorialStrings.Tutorial_AnimationEdit_Step11_Content,
                    TargetElements =
                    [
                        new TargetElementDefinition { ToolTabType = typeof(GraphEditorTabExtension), IsPrimary = true }
                    ],
                    PreferredPlacement = TutorialStepPlacement.Top,
                    IsActionRequired = true,
                    OnShown = () =>
                    {
                        EditViewModel? editVm = TutorialHelpers.GetEditViewModel();
                        if (editVm == null) return;

                        TranslateTransform? translateTransform = TutorialHelpers.GetTranslateTransform(
                            TutorialHelpers.GetDrawable(editVm));
                        if (translateTransform?.Y.Animation is KeyFrameAnimation<float> animation)
                        {
                            step11Subscription = TutorialHelpers.SubscribeToKeyFrameAdded(
                                animation,
                                2,
                                () => TutorialService.Current.AdvanceStep());
                        }
                    },
                    OnDismissed = () =>
                    {
                        step11Subscription?.Dispose();
                        step11Subscription = null;
                    },
                },

                // Step 12: Final preview
                new TutorialStep
                {
                    Id = "animation-final-play",
                    Title = TutorialStrings.Tutorial_AnimationEdit_Step12_Title,
                    Content = TutorialStrings.Tutorial_AnimationEdit_Step12_Content,
                    TargetElements = [new TargetElementDefinition { ElementName = "Player", IsPrimary = true }],
                    PreferredPlacement = TutorialStepPlacement.Bottom,
                    OnShown = () =>
                    {
                        EditViewModel? editVm = TutorialHelpers.GetEditViewModel();
                        Element? element = TutorialHelpers.FindElementWithObject<EllipseShape>(editVm?.Scene);
                        if (editVm != null && element != null)
                        {
                            editVm.CurrentTime.Value = element.Start;
                        }
                    },
                },

                // Step 13: Complete
                new TutorialStep
                {
                    Id = "animation-complete",
                    Title = TutorialStrings.Tutorial_AnimationEdit_Step13_Title,
                    Content = TutorialStrings.Tutorial_AnimationEdit_Step13_Content,
                    PreferredPlacement = TutorialStepPlacement.Center,
                },
            ]
        };
    }

    private static Control? FindTransformEditor()
    {
        TopLevel? topLevel = AppHelper.GetTopLevel();
        return topLevel?.GetVisualDescendants()
            .OfType<Views.Editors.TransformEditor>()
            .FirstOrDefault(c =>
                c.DataContext is TransformEditorViewModel vm &&
                (vm.GetService(typeof(Element)) as Element)?.Objects.OfType<EllipseShape>().Any() ==
                true);
    }

    private static Control? FindTranslateTransformXPropertyEditor()
    {
        TopLevel? topLevel = AppHelper.GetTopLevel();
        return topLevel?.GetVisualDescendants()
            .OfType<NumberEditor<float>>()
            .FirstOrDefault(c =>
                c.DataContext is BaseEditorViewModel vm &&
                vm.PropertyAdapter.GetEngineProperty() is IProperty prop &&
                prop.GetOwnerObject() is TranslateTransform &&
                prop.Name == nameof(TranslateTransform.X));
    }

    private static Control? FindTranslateTransformYPropertyEditor()
    {
        TopLevel? topLevel = AppHelper.GetTopLevel();
        return topLevel?.GetVisualDescendants()
            .OfType<NumberEditor<float>>()
            .FirstOrDefault(c =>
                c.DataContext is BaseEditorViewModel vm &&
                vm.PropertyAdapter.GetEngineProperty() is IProperty prop &&
                prop.GetOwnerObject() is TranslateTransform &&
                prop.Name == nameof(TranslateTransform.Y));
    }
}
