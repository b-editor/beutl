using System.Windows.Input;

using Avalonia.Headless;
using Avalonia.Headless.NUnit;
using Avalonia.Threading;
using Beutl.Language;
using Beutl.Services.AI;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AiPromptLibraryViewModelTests
{
    [AvaloniaTest]
    public async Task BackgroundConstructionDoesNotWaitForTheUiDispatcher()
    {
        var library = new FakePromptLibrary(
            templates: [CreateTemplate(PromptTaskKind.Image, "Deferred", updatedMinute: 1)]);
        Task<AiPromptLibraryViewModel> creation = Task.Run(() =>
            new AiPromptLibraryViewModel(
                PromptTaskKind.Image,
                static () => string.Empty,
                static _ => { },
                library));

        // Deliberately block the UI dispatcher. A synchronous Invoke in the
        // constructor cannot complete until RunJobs below and fails this bound.
        bool completedWithoutUiPump = creation.Wait(TimeSpan.FromSeconds(2));
        Dispatcher.UIThread.RunJobs();
        using AiPromptLibraryViewModel viewModel =
            await creation.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Multiple(() =>
        {
            Assert.That(completedWithoutUiPump, Is.True);
            Assert.That(viewModel.Templates.Select(item => item.Name), Is.EqualTo(["Deferred"]));
        });
    }

    [Test]
    public void TemplatesAndHistory_AreSeparateListsFilteredByTaskWithPinnedItemsFirst()
    {
        PromptTemplate recentTemplate = CreateTemplate(
            PromptTaskKind.Image,
            "Recent template",
            updatedMinute: 40);
        PromptTemplate pinnedTemplate = CreateTemplate(
            PromptTaskKind.Image,
            "Pinned template",
            updatedMinute: 10,
            isPinned: true);
        PromptTemplate foreignTemplate = CreateTemplate(
            PromptTaskKind.Video,
            "Foreign template",
            updatedMinute: 50,
            isPinned: true);
        PromptHistoryEntry recentHistory = CreateHistory(
            PromptTaskKind.Image,
            "recent history",
            usedMinute: 40);
        PromptHistoryEntry pinnedHistory = CreateHistory(
            PromptTaskKind.Image,
            "pinned history",
            usedMinute: 10,
            isPinned: true);
        PromptHistoryEntry foreignHistory = CreateHistory(
            PromptTaskKind.ImageEdit,
            "foreign history",
            usedMinute: 50,
            isPinned: true);
        var library = new FakePromptLibrary(
            [recentHistory, foreignHistory, pinnedHistory],
            [recentTemplate, foreignTemplate, pinnedTemplate]);

        using var viewModel = new AiPromptLibraryViewModel(
            PromptTaskKind.Image,
            () => string.Empty,
            _ => { },
            library,
            dispatchToUi: static action => action());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                viewModel.Templates.Select(choice => choice.Id),
                Is.EqualTo(new[] { pinnedTemplate.Id, recentTemplate.Id }));
            Assert.That(
                viewModel.History.Select(choice => choice.Id),
                Is.EqualTo(new[] { pinnedHistory.Id, recentHistory.Id }));
            Assert.That(
                viewModel.Templates.Select(choice => choice.Id),
                Does.Not.Contain(foreignTemplate.Id));
            Assert.That(
                viewModel.History.Select(choice => choice.Id),
                Does.Not.Contain(foreignHistory.Id));
            Assert.That(viewModel.HasTemplates.Value, Is.True);
            Assert.That(viewModel.HasHistory.Value, Is.True);
        }
    }

    [Test]
    public void HistorySummary_ShortensALongPromptWithoutLosingWhatIsApplied()
    {
        string prompt = new string('a', 120) + "\nsecond line";
        var library = new FakePromptLibrary(history: [CreateHistory(PromptTaskKind.Image, prompt, usedMinute: 1)]);
        string? applied = null;
        using var viewModel = new AiPromptLibraryViewModel(
            PromptTaskKind.Image,
            () => string.Empty,
            value => applied = value,
            library,
            dispatchToUi: static action => action());

        AiPromptChoice choice = viewModel.History.Single();
        Execute(viewModel.Apply, choice);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(choice.Name, Has.Length.EqualTo(70));
            Assert.That(choice.Name, Does.EndWith("\u2026"));
            Assert.That(applied, Is.EqualTo(prompt), "The whole prompt is applied, not the summary.");
        }
    }

    [Test]
    public void Apply_AppliesTheFullPromptAndClosesTheHistoryPopup()
    {
        PromptHistoryEntry history = CreateHistory(
            PromptTaskKind.Image,
            "first line\nsecond line",
            usedMinute: 1);
        var library = new FakePromptLibrary(history: [history]);
        string? appliedPrompt = null;
        using var viewModel = new AiPromptLibraryViewModel(
            PromptTaskKind.Image,
            () => string.Empty,
            prompt => appliedPrompt = prompt,
            library,
            dispatchToUi: static action => action());
        viewModel.Error.Value = "previous error";
        viewModel.IsHistoryOpen.Value = true;

        Execute(viewModel.Apply, viewModel.History.Single());

        using (Assert.EnterMultipleScope())
        {
            Assert.That(appliedPrompt, Is.EqualTo(history.Prompt));
            Assert.That(viewModel.Error.Value, Is.Null);
            Assert.That(viewModel.IsHistoryOpen.Value, Is.False,
                "The popup closes over the box the prompt just landed in.");
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void TogglePin_RoutesByChoiceType(bool isTemplate)
    {
        var library = CreateSingleItemLibrary(isTemplate);
        using var viewModel = new AiPromptLibraryViewModel(
            PromptTaskKind.Image,
            () => string.Empty,
            _ => { },
            library,
            dispatchToUi: static action => action());
        AiPromptChoice choice = Single(viewModel, isTemplate);

        Execute(viewModel.TogglePin, choice);

        AiPromptChoice refreshed = Single(viewModel, isTemplate);
        using (Assert.EnterMultipleScope())
        {
            Assert.That(refreshed.Id, Is.EqualTo(choice.Id));
            Assert.That(refreshed.IsPinned, Is.True);
            Assert.That(
                isTemplate ? library.TemplatePinCalls : library.HistoryPinCalls,
                Is.EqualTo(new[] { (choice.Id, true) }));
            Assert.That(
                isTemplate ? library.HistoryPinCalls : library.TemplatePinCalls,
                Is.Empty);
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Delete_RoutesByChoiceType(bool isTemplate)
    {
        var library = CreateSingleItemLibrary(isTemplate);
        using var viewModel = new AiPromptLibraryViewModel(
            PromptTaskKind.Image,
            () => string.Empty,
            _ => { },
            library,
            dispatchToUi: static action => action());
        AiPromptChoice choice = Single(viewModel, isTemplate);

        Execute(viewModel.Delete, choice);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.Templates, Is.Empty);
            Assert.That(viewModel.History, Is.Empty);
            Assert.That(viewModel.HasTemplates.Value, Is.False);
            Assert.That(viewModel.HasHistory.Value, Is.False);
            Assert.That(
                isTemplate ? library.TemplateDeleteCalls : library.HistoryDeleteCalls,
                Is.EqualTo(new[] { choice.Id }));
            Assert.That(
                isTemplate ? library.HistoryDeleteCalls : library.TemplateDeleteCalls,
                Is.Empty);
        }
    }

    [Test]
    public void ClearHistory_LeavesTheSavedTemplatesAlone()
    {
        var library = new FakePromptLibrary(
            history: [CreateHistory(PromptTaskKind.Image, "history", usedMinute: 1)],
            templates: [CreateTemplate(PromptTaskKind.Image, "Template", updatedMinute: 1)]);
        using var viewModel = new AiPromptLibraryViewModel(
            PromptTaskKind.Image,
            () => string.Empty,
            _ => { },
            library,
            dispatchToUi: static action => action());

        Execute(viewModel.ClearHistory);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(viewModel.History, Is.Empty);
            Assert.That(viewModel.HasHistory.Value, Is.False);
            Assert.That(viewModel.Templates, Has.Count.EqualTo(1));
            Assert.That(library.Templates, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void Record_AddsToHistoryWithoutTouchingTemplates()
    {
        PromptTemplate template = CreateTemplate(
            PromptTaskKind.Image,
            "Saved template",
            updatedMinute: 1);
        var library = new FakePromptLibrary(templates: [template]);
        using var viewModel = new AiPromptLibraryViewModel(
            PromptTaskKind.Image,
            () => string.Empty,
            _ => { },
            library,
            dispatchToUi: static action => action());

        viewModel.Record("new history");

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                library.RecordCalls,
                Is.EqualTo(new[] { (PromptTaskKind.Image, "new history") }));
            Assert.That(viewModel.History.Select(choice => choice.Prompt), Is.EqualTo(new[] { "new history" }));
            Assert.That(viewModel.Templates.Select(choice => choice.Id), Is.EqualTo(new[] { template.Id }));
        }
    }

    [Test]
    public void SaveTemplate_RoutesCurrentValuesAndShowsTheSavedTemplate()
    {
        var library = new FakePromptLibrary();
        using var viewModel = new AiPromptLibraryViewModel(
            PromptTaskKind.Video,
            () => "current prompt",
            _ => { },
            library,
            dispatchToUi: static action => action());
        viewModel.TemplateName.Value = "Trailer";
        viewModel.Error.Value = "previous error";

        Execute(viewModel.SaveTemplate);

        using (Assert.EnterMultipleScope())
        {
            Assert.That(
                library.SaveTemplateCalls,
                Is.EqualTo(new[] { ("Trailer", PromptTaskKind.Video, "current prompt") }));
            Assert.That(viewModel.TemplateName.Value, Is.Empty);
            Assert.That(viewModel.Error.Value, Is.Null);
            Assert.That(viewModel.Templates.Single().Id, Is.EqualTo(library.Templates.Single().Id));
            Assert.That(viewModel.Templates.Single().IsTemplate, Is.True);
        }
    }

    [TestCase("", "valid prompt")]
    [TestCase("Template", "   ")]
    public void SaveTemplate_InvalidNameOrPromptSetsLocalizedError(string templateName, string prompt)
    {
        string tempDirectory = Path.Combine(
            Path.GetTempPath(),
            $"beutl-prompt-view-model-tests-{Guid.NewGuid():N}");
        string storagePath = Path.Combine(tempDirectory, "prompts.json");
        try
        {
            var library = new PersistentPromptLibrary(storagePath);
            using var viewModel = new AiPromptLibraryViewModel(
                PromptTaskKind.ImageEdit,
                () => prompt,
                _ => { },
                library,
                dispatchToUi: static action => action());
            viewModel.TemplateName.Value = templateName;

            Execute(viewModel.SaveTemplate);

            using (Assert.EnterMultipleScope())
            {
                Assert.That(viewModel.Error.Value, Is.EqualTo(Strings.AiPromptTemplateInvalid));
                Assert.That(viewModel.Templates, Is.Empty);
                Assert.That(library.Templates, Is.Empty);
                Assert.That(File.Exists(storagePath), Is.False);
            }
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, true);
            }
        }
    }

    private static FakePromptLibrary CreateSingleItemLibrary(bool isTemplate) =>
        isTemplate
            ? new FakePromptLibrary(templates: [CreateTemplate(PromptTaskKind.Image, "Template", updatedMinute: 1)])
            : new FakePromptLibrary(history: [CreateHistory(PromptTaskKind.Image, "history", usedMinute: 1)]);

    private static AiPromptChoice Single(AiPromptLibraryViewModel viewModel, bool isTemplate) =>
        isTemplate ? viewModel.Templates.Single() : viewModel.History.Single();

    private static PromptTemplate CreateTemplate(
        PromptTaskKind taskKind,
        string name,
        int updatedMinute,
        bool isPinned = false)
    {
        DateTimeOffset updatedAt = CreateTimestamp(updatedMinute);
        return new PromptTemplate(
            Guid.NewGuid(),
            name,
            taskKind,
            $"{name} prompt",
            updatedAt.AddMinutes(-1),
            updatedAt,
            isPinned);
    }

    private static PromptHistoryEntry CreateHistory(
        PromptTaskKind taskKind,
        string prompt,
        int usedMinute,
        bool isPinned = false) =>
        new(
            Guid.NewGuid(),
            taskKind,
            prompt,
            CreateTimestamp(usedMinute),
            1,
            isPinned);

    private static DateTimeOffset CreateTimestamp(int minute) =>
        new DateTimeOffset(2026, 8, 9, 0, 0, 0, TimeSpan.Zero).AddMinutes(minute);

    private static void Execute(ICommand command, object? parameter = null)
    {
        Assert.That(command.CanExecute(parameter), Is.True);
        command.Execute(parameter);
    }

    private sealed class FakePromptLibrary : IPromptLibrary
    {
        private readonly List<PromptHistoryEntry> _history;
        private readonly List<PromptTemplate> _templates;
        private int _timestampMinute = 100;

        public FakePromptLibrary(
            IEnumerable<PromptHistoryEntry>? history = null,
            IEnumerable<PromptTemplate>? templates = null)
        {
            _history = history?.ToList() ?? [];
            _templates = templates?.ToList() ?? [];
        }

        public string StoragePath => "in-memory";

        public bool RetainRecentPromptText => true;

        public string? RecoveredCorruptFilePath => null;

        public IReadOnlyList<PromptHistoryEntry> History => _history;

        public IReadOnlyList<PromptTemplate> Templates => _templates;

        public List<(PromptTaskKind TaskKind, string Prompt)> RecordCalls { get; } = [];

        public List<(string Name, PromptTaskKind TaskKind, string Prompt)> SaveTemplateCalls { get; } = [];

        public List<(Guid Id, bool IsPinned)> HistoryPinCalls { get; } = [];

        public List<(Guid Id, bool IsPinned)> TemplatePinCalls { get; } = [];

        public List<Guid> HistoryDeleteCalls { get; } = [];

        public List<Guid> TemplateDeleteCalls { get; } = [];

        public PromptHistoryEntry Record(PromptTaskKind taskKind, string prompt)
        {
            RecordCalls.Add((taskKind, prompt));
            var entry = new PromptHistoryEntry(
                Guid.NewGuid(),
                taskKind,
                prompt,
                CreateTimestamp(_timestampMinute++),
                1,
                false);
            _history.Insert(0, entry);
            return entry;
        }

        public PromptTemplate SaveTemplate(string name, PromptTaskKind taskKind, string prompt)
        {
            SaveTemplateCalls.Add((name, taskKind, prompt));
            DateTimeOffset timestamp = CreateTimestamp(_timestampMinute++);
            var template = new PromptTemplate(
                Guid.NewGuid(),
                name,
                taskKind,
                prompt,
                timestamp,
                timestamp,
                false);
            _templates.Insert(0, template);
            return template;
        }

        public bool SetHistoryPinned(Guid id, bool isPinned)
        {
            HistoryPinCalls.Add((id, isPinned));
            int index = _history.FindIndex(item => item.Id == id);
            if (index < 0 || _history[index].IsPinned == isPinned)
            {
                return false;
            }

            _history[index] = _history[index] with { IsPinned = isPinned };
            return true;
        }

        public bool SetTemplatePinned(Guid id, bool isPinned)
        {
            TemplatePinCalls.Add((id, isPinned));
            int index = _templates.FindIndex(item => item.Id == id);
            if (index < 0 || _templates[index].IsPinned == isPinned)
            {
                return false;
            }

            _templates[index] = _templates[index] with { IsPinned = isPinned };
            return true;
        }

        public bool DeleteHistory(Guid id)
        {
            HistoryDeleteCalls.Add(id);
            int index = _history.FindIndex(item => item.Id == id);
            if (index < 0)
            {
                return false;
            }

            _history.RemoveAt(index);
            return true;
        }

        public bool DeleteTemplate(Guid id)
        {
            TemplateDeleteCalls.Add(id);
            int index = _templates.FindIndex(item => item.Id == id);
            if (index < 0)
            {
                return false;
            }

            _templates.RemoveAt(index);
            return true;
        }

        public void ClearHistory() => _history.Clear();

        public void ClearTemplates() => _templates.Clear();

        public void ClearAll()
        {
            _history.Clear();
            _templates.Clear();
        }
    }
}
