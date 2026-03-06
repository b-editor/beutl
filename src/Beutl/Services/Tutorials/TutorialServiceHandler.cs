using System.Reactive.Subjects;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Beutl.Configuration;
using Beutl.Logging;
using Beutl.Views;
using Beutl.Views.Tutorial;
using Microsoft.Extensions.Logging;

namespace Beutl.Services.Tutorials;

public sealed class TutorialServiceHandler : ITutorialService
{
    private readonly ILogger _logger = Log.CreateLogger<TutorialServiceHandler>();
    private readonly BehaviorSubject<TutorialState?> _currentTutorial = new(null);
    private readonly List<TutorialDefinition> _tutorials = [];

    public IObservable<TutorialState?> CurrentTutorial => _currentTutorial.AsObservable();

    public TutorialState? GetCurrentState() => _currentTutorial.Value;

    public void Register(TutorialDefinition tutorial)
    {
        if (_tutorials.All(t => t.Id != tutorial.Id))
        {
            _tutorials.Add(tutorial);
        }
    }

    public void Unregister(string tutorialId)
    {
        TutorialState? current = _currentTutorial.Value;
        if (current?.Definition.Id == tutorialId)
        {
            CancelTutorial();
        }
        _tutorials.RemoveAll(t => t.Id == tutorialId);
    }

    public async Task StartTutorial(string tutorialId, bool autoFulfillPrerequisites = false)
    {
        TutorialDefinition? tutorial = _tutorials.Find(t => t.Id == tutorialId);
        if (tutorial == null)
        {
            _logger.LogWarning("Tutorial not found: {TutorialId}", tutorialId);
            return;
        }

        // 前提条件の確認と自動満足
        if (tutorial.CanStart != null && !tutorial.CanStart())
        {
            if (!autoFulfillPrerequisites || tutorial.FulfillPrerequisites == null)
            {
                _logger.LogInformation("Tutorial cannot start: {TutorialId}", tutorialId);
                return;
            }

            try
            {
                _logger.LogInformation("Fulfilling prerequisites for: {TutorialId}", tutorialId);
                bool fulfilled = await tutorial.FulfillPrerequisites();

                if (!fulfilled)
                {
                    _logger.LogWarning("Failed to fulfill prerequisites for: {TutorialId}", tutorialId);
                    return;
                }

                // 前提条件が満たされたか再確認
                if (!tutorial.CanStart())
                {
                    _logger.LogWarning("Prerequisites still not met after fulfillment for: {TutorialId}", tutorialId);
                    return;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during prerequisite fulfillment for: {TutorialId}", tutorialId);
                return;
            }
        }

        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            try
            {
                await App.WaitWindowOpened();
                var state = new TutorialState(tutorial, 0);
                _currentTutorial.OnNext(state);
                UpdateOverlay(state);

                state.CurrentStep.OnShown?.Invoke();
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Failed to start tutorial");
            }
        });
    }

    public void AdvanceStep()
    {
        TutorialState? current = _currentTutorial.Value;
        if (current == null || current.IsLastStep)
            return;

        TutorialStep step = current.CurrentStep;
        if (step.CanAdvance != null && !step.CanAdvance())
            return;

        step.OnDismissed?.Invoke();

        var newState = new TutorialState(current.Definition, current.CurrentStepIndex + 1);
        _currentTutorial.OnNext(newState);
        Dispatcher.UIThread.Post(() =>
        {
            UpdateOverlay(newState);
            newState.CurrentStep.OnShown?.Invoke();
        });
    }

    public void PreviousStep()
    {
        TutorialState? current = _currentTutorial.Value;
        if (current == null || current.IsFirstStep)
            return;

        current.CurrentStep.OnDismissed?.Invoke();

        var newState = new TutorialState(current.Definition, current.CurrentStepIndex - 1);
        _currentTutorial.OnNext(newState);
        Dispatcher.UIThread.Post(() =>
        {
            UpdateOverlay(newState);
            newState.CurrentStep.OnShown?.Invoke();
        });
    }

    public void CancelTutorial()
    {
        TutorialState? current = _currentTutorial.Value;
        if (current == null)
            return;

        current.CurrentStep.OnDismissed?.Invoke();

        // 最後のステップまで到達した場合は完了として記録
        if (current.IsLastStep)
        {
            MarkTutorialCompleted(current.Definition.Id);
        }

        _currentTutorial.OnNext(null);
        Dispatcher.UIThread.Post(() => UpdateOverlay(null));
    }

    public bool IsTutorialCompleted(string tutorialId)
    {
        return GlobalConfiguration.Instance.TutorialConfig.CompletedTutorialIds.Contains(tutorialId);
    }

    public IReadOnlyList<TutorialDefinition> GetAvailableTutorials()
    {
        return _tutorials.AsReadOnly();
    }

    private static void MarkTutorialCompleted(string tutorialId)
    {
        GlobalConfiguration.Instance.TutorialConfig.MarkCompleted(tutorialId);
    }

    private static void UpdateOverlay(TutorialState? state)
    {
        TutorialOverlay? overlay = GetOverlay();
        overlay?.UpdateState(state);
    }

    private static TutorialOverlay? GetOverlay()
    {
        MainView? mainView = GetMainView();
        return mainView?.FindControl<TutorialOverlay>("TutorialOverlayPanel");
    }

    private static MainView? GetMainView()
    {
        IApplicationLifetime? lifetime = Application.Current?.ApplicationLifetime;

        if (lifetime is IClassicDesktopStyleApplicationLifetime desktopLifetime)
        {
            if (desktopLifetime.MainWindow is MainWindow window)
            {
                return window.mainView;
            }
            else if (desktopLifetime.MainWindow is MacWindow mwindow)
            {
                return mwindow.mainView;
            }
        }
        else if (lifetime is ISingleViewApplicationLifetime singleViewLifetime)
        {
            return singleViewLifetime.MainView as MainView;
        }

        return null;
    }
}
