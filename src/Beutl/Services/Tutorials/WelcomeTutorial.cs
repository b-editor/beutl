namespace Beutl.Services.Tutorials;

public static class WelcomeTutorial
{
    public const string TutorialId = "welcome";

    public static TutorialDefinition Create()
    {
        return new TutorialDefinition
        {
            Id = TutorialId,
            Title = TutorialStrings.Tutorial_Welcome_Title,
            Description = TutorialStrings.LearnBeutlBasics,
            Priority = 0,
            Category = "basics",
            Steps =
            [
                new TutorialStep
                {
                    Id = "welcome-intro",
                    Title = TutorialStrings.Tutorial_Welcome_Title,
                    Content = TutorialStrings.Tutorial_Welcome_Content,
                    PreferredPlacement = TutorialStepPlacement.Center,
                },
                new TutorialStep
                {
                    Id = "welcome-menubar",
                    Title = TutorialStrings.Tutorial_Welcome_MenuBar_Title,
                    Content = TutorialStrings.Tutorial_Welcome_MenuBar_Content,
                    TargetElements =
                    [
                        new TargetElementDefinition { ElementName = "MenuBar", IsPrimary = true },
                    ],
                    PreferredPlacement = TutorialStepPlacement.Bottom,
                },
                new TutorialStep
                {
                    Id = "welcome-create",
                    Title = TutorialStrings.Tutorial_Welcome_Create_Title,
                    Content = TutorialStrings.Tutorial_Welcome_Create_Content,
                    TargetElements =
                    [
                        new TargetElementDefinition
                        {
                            ElementName = "createNewButton",
                            IsPrimary = true,
                        },
                    ],
                    PreferredPlacement = TutorialStepPlacement.Bottom,
                },
            ],
        };
    }
}
