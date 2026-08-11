using System.Windows.Input;

using Beutl.Language;
using Beutl.Services.AI;
using Beutl.ViewModels;

namespace Beutl.HeadlessUITests;

[TestFixture]
public sealed class AiPromptLibraryViewModelTests
{
    [Test]
    public void Choices_FilterTaskAndGroupTemplatesBeforeHistoryWithPinnedItemsFirst()
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
            library);

        Assert.Multiple(() =>
        {
            Assert.That(
                viewModel.Choices.Select(choice => choice.Id),
                Is.EqualTo(new[]
                {
                    pinnedTemplate.Id,
                    recentTemplate.Id,
                    pinnedHistory.Id,
                    recentHistory.Id,
                }));
            Assert.That(viewModel.Choices.Select(choice => choice.Id), Does.Not.Contain(foreignTemplate.Id));
            Assert.That(viewModel.Choices.Select(choice => choice.Id), Does.Not.Contain(foreignHistory.Id));
            Assert.That(viewModel.HasChoices.Value, Is.True);
        });
    }

    [Test]
    public void ApplySelected_AppliesFullPromptAndClearsPreviousError()
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
            library);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.CanUseSelection.Value, Is.False);
            Assert.That(((ICommand)viewModel.ApplySelected).CanExecute(null), Is.False);
            Assert.That(((ICommand)viewModel.TogglePinSelected).CanExecute(null), Is.False);
            Assert.That(((ICommand)viewModel.DeleteSelected).CanExecute(null), Is.False);
        });

        viewModel.Error.Value = "previous error";
        viewModel.SelectedChoice.Value = viewModel.Choices.Single();
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.CanUseSelection.Value, Is.True);
            Assert.That(((ICommand)viewModel.ApplySelected).CanExecute(null), Is.True);
            Assert.That(((ICommand)viewModel.TogglePinSelected).CanExecute(null), Is.True);
            Assert.That(((ICommand)viewModel.DeleteSelected).CanExecute(null), Is.True);
        });
        Execute(viewModel.ApplySelected);

        Assert.Multiple(() =>
        {
            Assert.That(appliedPrompt, Is.EqualTo(history.Prompt));
            Assert.That(viewModel.Error.Value, Is.Null);
        });

        viewModel.SelectedChoice.Value = null;
        Assert.Multiple(() =>
        {
            Assert.That(viewModel.CanUseSelection.Value, Is.False);
            Assert.That(((ICommand)viewModel.ApplySelected).CanExecute(null), Is.False);
            Assert.That(((ICommand)viewModel.TogglePinSelected).CanExecute(null), Is.False);
            Assert.That(((ICommand)viewModel.DeleteSelected).CanExecute(null), Is.False);
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public void TogglePinSelected_RoutesByChoiceTypeAndPreservesSelection(bool isTemplate)
    {
        PromptTemplate? template = isTemplate
            ? CreateTemplate(PromptTaskKind.Image, "Template", updatedMinute: 1)
            : null;
        PromptHistoryEntry? history = isTemplate
            ? null
            : CreateHistory(PromptTaskKind.Image, "history", usedMinute: 1);
        var library = new FakePromptLibrary(
            history: history is null ? [] : [history],
            templates: template is null ? [] : [template]);
        using var viewModel = new AiPromptLibraryViewModel(
            PromptTaskKind.Image,
            () => string.Empty,
            _ => { },
            library);
        Guid selectedId = viewModel.Choices.Single().Id;
        viewModel.SelectedChoice.Value = viewModel.Choices.Single();

        Execute(viewModel.TogglePinSelected);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.SelectedChoice.Value?.Id, Is.EqualTo(selectedId));
            Assert.That(viewModel.SelectedChoice.Value?.IsPinned, Is.True);
        });
        if (isTemplate)
        {
            Assert.Multiple(() =>
            {
                Assert.That(library.TemplatePinCalls, Is.EqualTo(new[] { (selectedId, true) }));
                Assert.That(library.HistoryPinCalls, Is.Empty);
            });
        }
        else
        {
            Assert.Multiple(() =>
            {
                Assert.That(library.HistoryPinCalls, Is.EqualTo(new[] { (selectedId, true) }));
                Assert.That(library.TemplatePinCalls, Is.Empty);
            });
        }
    }

    [TestCase(true)]
    [TestCase(false)]
    public void DeleteSelected_RoutesByChoiceTypeAndClearsDeletedSelection(bool isTemplate)
    {
        PromptTemplate? template = isTemplate
            ? CreateTemplate(PromptTaskKind.Image, "Template", updatedMinute: 1)
            : null;
        PromptHistoryEntry? history = isTemplate
            ? null
            : CreateHistory(PromptTaskKind.Image, "history", usedMinute: 1);
        var library = new FakePromptLibrary(
            history: history is null ? [] : [history],
            templates: template is null ? [] : [template]);
        using var viewModel = new AiPromptLibraryViewModel(
            PromptTaskKind.Image,
            () => string.Empty,
            _ => { },
            library);
        Guid selectedId = viewModel.Choices.Single().Id;
        viewModel.SelectedChoice.Value = viewModel.Choices.Single();

        Execute(viewModel.DeleteSelected);

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.Choices, Is.Empty);
            Assert.That(viewModel.SelectedChoice.Value, Is.Null);
            Assert.That(viewModel.HasChoices.Value, Is.False);
        });
        if (isTemplate)
        {
            Assert.Multiple(() =>
            {
                Assert.That(library.TemplateDeleteCalls, Is.EqualTo(new[] { selectedId }));
                Assert.That(library.HistoryDeleteCalls, Is.Empty);
            });
        }
        else
        {
            Assert.Multiple(() =>
            {
                Assert.That(library.HistoryDeleteCalls, Is.EqualTo(new[] { selectedId }));
                Assert.That(library.TemplateDeleteCalls, Is.Empty);
            });
        }
    }

    [Test]
    public void Record_PreservesAnExistingSelectionAcrossRefresh()
    {
        PromptTemplate template = CreateTemplate(
            PromptTaskKind.Image,
            "Selected template",
            updatedMinute: 1);
        var library = new FakePromptLibrary(templates: [template]);
        using var viewModel = new AiPromptLibraryViewModel(
            PromptTaskKind.Image,
            () => string.Empty,
            _ => { },
            library);
        viewModel.SelectedChoice.Value = viewModel.Choices.Single();

        viewModel.Record("new history");

        Assert.Multiple(() =>
        {
            Assert.That(
                library.RecordCalls,
                Is.EqualTo(new[] { (PromptTaskKind.Image, "new history") }));
            Assert.That(viewModel.Choices, Has.Count.EqualTo(2));
            Assert.That(viewModel.SelectedChoice.Value?.Id, Is.EqualTo(template.Id));
        });
    }

    [Test]
    public void SaveTemplate_RoutesCurrentValuesAndSelectsTheSavedTemplate()
    {
        var library = new FakePromptLibrary();
        using var viewModel = new AiPromptLibraryViewModel(
            PromptTaskKind.Video,
            () => "current prompt",
            _ => { },
            library);
        viewModel.TemplateName.Value = "Trailer";
        viewModel.Error.Value = "previous error";

        Execute(viewModel.SaveTemplate);

        Assert.Multiple(() =>
        {
            Assert.That(
                library.SaveTemplateCalls,
                Is.EqualTo(new[] { ("Trailer", PromptTaskKind.Video, "current prompt") }));
            Assert.That(viewModel.TemplateName.Value, Is.Empty);
            Assert.That(viewModel.Error.Value, Is.Null);
            Assert.That(viewModel.SelectedChoice.Value?.Id, Is.EqualTo(library.Templates.Single().Id));
            Assert.That(viewModel.SelectedChoice.Value?.IsTemplate, Is.True);
        });
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
                library);
            viewModel.TemplateName.Value = templateName;

            Execute(viewModel.SaveTemplate);

            Assert.Multiple(() =>
            {
                Assert.That(viewModel.Error.Value, Is.EqualTo(Strings.AiPromptTemplateInvalid));
                Assert.That(viewModel.Choices, Is.Empty);
                Assert.That(library.Templates, Is.Empty);
                Assert.That(File.Exists(storagePath), Is.False);
            });
        }
        finally
        {
            if (Directory.Exists(tempDirectory))
            {
                Directory.Delete(tempDirectory, true);
            }
        }
    }

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

    private static void Execute(ICommand command)
    {
        Assert.That(command.CanExecute(null), Is.True);
        command.Execute(null);
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
