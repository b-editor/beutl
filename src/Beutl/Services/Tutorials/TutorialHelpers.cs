using Avalonia.Threading;
using Beutl.Animation;
using Beutl.Editor.Components.LibraryTab.ViewModels;
using Beutl.Engine;
using Beutl.Graphics;
using Beutl.Graphics.Transformation;
using Beutl.ProjectSystem;
using Beutl.ViewModels;

namespace Beutl.Services.Tutorials;

public static class TutorialHelpers
{
    public static EditViewModel? GetEditViewModel()
    {
        return EditorService.Current.SelectedTabItem.Value?.Context.Value as EditViewModel;
    }

    public static bool OpenLibraryTabIfNeeded()
    {
        var editVm = GetEditViewModel();
        if (editVm == null) return false;

        var tab = editVm.FindToolTab<LibraryTabViewModel>() ?? new LibraryTabViewModel(editVm);
        editVm.OpenToolTab(tab);
        return true;
    }

    public static async Task<bool> EnsureProjectAsync(string projectName = "Tutorial")
    {
        Project? currentProject = ProjectService.Current.CurrentProject.Value;
        // プロジェクトが開いていない場合、新規プロジェクトを作成
        if (currentProject == null)
        {
            // ~/.beutl/tmp/tutorials フォルダに保存
            string tutorialDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".beutl", "tmp", "tutorials");
            Directory.CreateDirectory(tutorialDir);

            string fullProjectName = $"{projectName}_{DateTime.Now:yyyyMMddHHmmss}";

            // プロジェクト作成処理
            currentProject = ProjectService.Current.CreateProject(
                width: 1920,
                height: 1080,
                framerate: 30,
                samplerate: 44100,
                name: fullProjectName,
                location: tutorialDir);

            if (currentProject == null)
            {
                return false;
            }
        }

        Scene? scene = currentProject.Items.OfType<Scene>().FirstOrDefault();
        if (scene != null)
        {
            EditorService.Current.ActivateTabItem(scene);
        }

        // UIの更新を待つ
        await Task.Delay(200);

        return GetEditViewModel() != null;
    }

    public static IDisposable? SubscribeToElementSelection(EditViewModel? editVm, Action onSelected)
    {
        if (editVm == null) return null;

        // Already selected?
        if (editVm.SelectedObject.Value != null)
        {
            Dispatcher.UIThread.Post(onSelected);
            return null;
        }

        return editVm.SelectedObject.Where(obj => obj != null)
            .Take(1)
            .Subscribe(_ => Dispatcher.UIThread.Post(onSelected));
    }

    public static IDisposable? SubscribeToAnimationEnabled<T>(
        IProperty<T>? property,
        Action onAnimationEnabled)
        where T : struct
    {
        if (property == null) return null;

        if (property.Animation != null)
        {
            Dispatcher.UIThread.Post(onAnimationEnabled);
            return null;
        }

        if (property is AnimatableProperty<T> animatableProp)
        {
            void Handler(IAnimation<T>? anm)
            {
                if (anm != null)
                {
                    Dispatcher.UIThread.Post(onAnimationEnabled);
                }
            }

            animatableProp.AnimationChanged += Handler;
            return Disposable.Create(() => animatableProp.AnimationChanged -= Handler);
        }

        return null;
    }

    public static IDisposable? SubscribeToKeyFrameAdded<T>(
        KeyFrameAnimation<T>? animation,
        int requiredKeyFrameCount,
        Action onKeyFrameAdded)
        where T : struct
    {
        if (animation == null) return null;

        // すでに十分なキーフレームがある場合
        if (animation.KeyFrames.Count >= requiredKeyFrameCount)
        {
            Dispatcher.UIThread.Post(onKeyFrameAdded);
            return null;
        }

        void Handler(IKeyFrame _)
        {
            if (animation.KeyFrames.Count >= requiredKeyFrameCount)
            {
                Dispatcher.UIThread.Post(onKeyFrameAdded);
            }
        }

        animation.KeyFrames.Attached += Handler;
        return Disposable.Create(() => animation.KeyFrames.Attached -= Handler);
    }

    public static CompositeDisposable SubscribeToEasingChanged<T>(
        KeyFrameAnimation<T>? animation,
        Action onEasingChanged)
        where T : struct
    {
        var disposables = new CompositeDisposable();

        if (animation == null) return disposables;

        void Handler(IKeyFrame _)
        {
            Dispatcher.UIThread.Post(onEasingChanged);
        }

        void EasingChangedHandler()
        {
            Dispatcher.UIThread.Post(onEasingChanged);
        }

        foreach (var item in animation.KeyFrames)
        {
            item.GetPropertyChangedObservable(KeyFrame.EasingProperty)
                .Subscribe(_ => EasingChangedHandler())
                .DisposeWith(disposables);
        }

        animation.KeyFrames.Attached += Handler;
        Disposable.Create(() => animation.KeyFrames.Attached -= Handler)
            .DisposeWith(disposables);

        return disposables;
    }

    public static void PrepareForPlayback(
        EditViewModel? editVm,
        Element? element,
        TimeSpan? additionalDuration = null)
    {
        if (editVm == null || element == null) return;

        var duration = additionalDuration ?? TimeSpan.FromSeconds(1);
        editVm.Scene.Duration = element.Range.End + duration;
        editVm.CurrentTime.Value = element.Start;
        editVm.HistoryManager.Commit(CommandNames.ChangeSceneDuration);
    }

    public static Element? FindElementWithObject<TEngineObject>(Scene? scene)
        where TEngineObject : EngineObject
    {
        return scene?.Children.FirstOrDefault(e =>
            e.Objects.OfType<TEngineObject>().Any());
    }

    public static TEngineObject? GetObject<TEngineObject>(Element? element)
        where TEngineObject : EngineObject
    {
        return element?.Objects.OfType<TEngineObject>().FirstOrDefault();
    }

    public static bool HasElementWithObject<TEngineObject>(Scene? scene)
        where TEngineObject : EngineObject
    {
        return scene?.Children.Any(e => e.Objects.OfType<TEngineObject>().Any()) ?? false;
    }

    public static IDisposable? SubscribeToElementAdded<TEngineObject>(
        Scene? scene,
        Action onElementAdded)
        where TEngineObject : EngineObject
    {
        if (scene == null) return null;

        // Already has element?
        if (HasElementWithObject<TEngineObject>(scene))
        {
            Dispatcher.UIThread.Post(onElementAdded);
            return null;
        }

        Action<Element>? handler = null;
        handler = element =>
        {
            if (element.Objects.OfType<TEngineObject>().Any())
            {
                Dispatcher.UIThread.Post(onElementAdded);
            }
        };

        scene.Children.Attached += handler;
        return Disposable.Create(() => scene.Children.Attached -= handler);
    }

    public static TranslateTransform? GetTranslateTransform(Drawable? drawable)
    {
        if (drawable?.Transform.CurrentValue is TransformGroup group)
        {
            return group.Children.OfType<TranslateTransform>().FirstOrDefault();
        }

        return drawable?.Transform.CurrentValue as TranslateTransform;
    }

    public static Drawable? GetDrawable(EditViewModel? editVm)
    {
        if (editVm == null) return null;

        Element? element = editVm.SelectedObject.Value as Element;
        element ??= editVm.Scene.Children.FirstOrDefault(e =>
            e.Objects.OfType<Drawable>().Any());

        return element?.Objects.OfType<Drawable>().FirstOrDefault();
    }
}
