namespace Beutl.Services.Tutorials;

public interface ITutorialService
{
    IObservable<TutorialState?> CurrentTutorial { get; }

    TutorialState? GetCurrentState();

    void Register(TutorialDefinition tutorial);

    void Unregister(string tutorialId);

    Task StartTutorial(string tutorialId, bool autoFulfillPrerequisites = false);

    void AdvanceStep();

    void PreviousStep();

    void CancelTutorial();

    bool IsTutorialCompleted(string tutorialId);

    IReadOnlyList<TutorialDefinition> GetAvailableTutorials();
}
