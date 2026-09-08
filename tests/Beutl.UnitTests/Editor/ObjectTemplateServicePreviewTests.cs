using System.Text.Json.Nodes;
using Beutl.Editor.Services;
using Beutl.Graphics.Shapes;
using Beutl.Media;

namespace Beutl.UnitTests.Editor;

// BEUTL_HOME is redirected to a temp directory by AssemblySetUp, so these write into a throwaway
// templates directory rather than the developer's own.
[TestFixture]
[NonParallelizable]
public class ObjectTemplateServicePreviewTests
{
    [Test]
    public async Task AddFromInstanceAsync_EmbedsThePreviewInTheSavedFile()
    {
        var shape = new RectShape
        {
            Width = { CurrentValue = 100 },
            Height = { CurrentValue = 100 },
            Fill = { CurrentValue = new SolidColorBrush(Colors.Red) }
        };

        ObjectTemplateItem? item = await ObjectTemplateService.Instance
            .AddFromInstanceAsync(shape, $"preview-{Guid.NewGuid():N}");

        Assert.That(item, Is.Not.Null);
        Assert.That(item!.Preview, Is.Not.Null.And.Not.Empty);
        Assert.That(item.FilePath, Is.Not.Null);

        JsonNode? saved = JsonNode.Parse(await File.ReadAllTextAsync(item.FilePath!));
        Assert.That(saved!["Preview"]?.GetValue<string>(), Is.Not.Null.And.Not.Empty);
    }

    [Test]
    public async Task AddFromInstanceAsync_SavesTheTemplateEvenWithoutAPreview()
    {
        ObjectTemplateItem? item = await ObjectTemplateService.Instance
            .AddFromInstanceAsync(new Audio.Effects.AudioEffectGroup(), $"silent-{Guid.NewGuid():N}");

        Assert.That(item, Is.Not.Null);
        Assert.That(item!.Preview, Is.Null);
        Assert.That(File.Exists(item.FilePath), Is.True);
    }

    [Test]
    public async Task TryLoadFromFile_ReturnsNullForMalformedPathWhenItemsAreCached()
    {
        ObjectTemplateItem? item = await ObjectTemplateService.Instance
            .AddFromInstanceAsync(
                new Audio.Effects.AudioEffectGroup(),
                $"malformed-path-{Guid.NewGuid():N}");
        Assert.That(item, Is.Not.Null);

        ObjectTemplateItem? result = ObjectTemplateService.Instance.TryLoadFromFile("\0");

        Assert.That(result, Is.Null);
    }

    [Test]
    public async Task GetUniqueName_keeps_display_names_case_insensitively_unique()
    {
        ObjectTemplateService service = ObjectTemplateService.Instance;
        string name = $"DisplayName-{Guid.NewGuid():N}";
        ObjectTemplateItem? item = await service.AddFromInstanceAsync(
            new Audio.Effects.AudioEffectGroup(),
            name);
        Assert.That(item, Is.Not.Null);

        string alternateCase = name.ToLowerInvariant();

        Assert.That(service.GetUniqueName(alternateCase), Is.EqualTo($"{alternateCase} (2)"));
    }

    [Test]
    public async Task RefreshFromFileSystem_tracks_case_distinct_files_independently()
    {
        ObjectTemplateService service = ObjectTemplateService.Instance;
        string name = $"CaseSensitive-{Guid.NewGuid():N}";
        ObjectTemplateItem? item = await service.AddFromInstanceAsync(
            new Audio.Effects.AudioEffectGroup(),
            name);
        Assert.That(item, Is.Not.Null);

        string originalPath = item!.FilePath!;
        string alternatePath = Path.Combine(
            Path.GetDirectoryName(originalPath)!,
            Path.GetFileName(originalPath).ToLowerInvariant());
        try
        {
            if (File.Exists(alternatePath))
            {
                Assert.Ignore("The templates volume does not distinguish case-only file names.");
            }

            File.Copy(originalPath, alternatePath);
            service.RefreshFromFileSystem();

            string?[] loadedPaths = service.FindByBaseType(item.BaseType)
                .Select(x => x.FilePath)
                .ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(loadedPaths, Does.Contain(originalPath));
                Assert.That(loadedPaths, Does.Contain(alternatePath));
            });

            File.Delete(originalPath);
            service.RefreshFromFileSystem();

            loadedPaths = service.FindByBaseType(item.BaseType)
                .Select(x => x.FilePath)
                .ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(loadedPaths, Does.Not.Contain(originalPath));
                Assert.That(loadedPaths, Does.Contain(alternatePath));
            });

            File.Copy(alternatePath, originalPath);
            service.RefreshFromFileSystem();
            File.Delete(alternatePath);
            service.RefreshFromFileSystem();

            loadedPaths = service.FindByBaseType(item.BaseType)
                .Select(x => x.FilePath)
                .ToArray();
            Assert.Multiple(() =>
            {
                Assert.That(loadedPaths, Does.Contain(originalPath));
                Assert.That(loadedPaths, Does.Not.Contain(alternatePath));
            });
        }
        finally
        {
            File.Delete(originalPath);
            File.Delete(alternatePath);
            service.RestoreItems();
        }
    }

    [Test]
    public async Task RefreshFromFileSystem_removes_a_template_hidden_by_a_dangling_link()
    {
        ObjectTemplateService service = ObjectTemplateService.Instance;
        ObjectTemplateItem? item = await service.AddFromInstanceAsync(
            new Audio.Effects.AudioEffectGroup(),
            $"Dangling-{Guid.NewGuid():N}");
        Assert.That(item, Is.Not.Null);
        string originalPath = item!.FilePath!;
        string aliasPath = Path.Combine(
            Path.GetDirectoryName(originalPath)!,
            $"dangling-{Guid.NewGuid():N}.json");
        try
        {
            try
            {
                File.CreateSymbolicLink(aliasPath, originalPath);
            }
            catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or PlatformNotSupportedException)
            {
                Assert.Ignore($"Symbolic links are unavailable: {ex.Message}");
            }

            File.Delete(originalPath);
            service.RefreshFromFileSystem();

            Assert.That(
                service.FindByBaseType(item.BaseType).Select(template => template.FilePath),
                Does.Not.Contain(originalPath));
        }
        finally
        {
            File.Delete(aliasPath);
            File.Delete(originalPath);
            service.RestoreItems();
        }
    }

    [Test]
    public void RefreshFromFileSystem_reloads_a_retargeted_alias_even_when_target_is_older()
    {
        ObjectTemplateService service = ObjectTemplateService.Instance;
        string templates = BeutlEnvironment.GetTemplatesDirectoryPath();
        string tempRoot = Path.Combine(
            Path.GetTempPath(),
            $"template-retarget-{Guid.NewGuid():N}");
        string firstTarget = Path.Combine(tempRoot, "first.json");
        string secondTarget = Path.Combine(tempRoot, "second.json");
        string alias = Path.Combine(templates, $"retarget-{Guid.NewGuid():N}.json");
        Directory.CreateDirectory(tempRoot);
        Directory.CreateDirectory(templates);
        ObjectTemplateItem first = ObjectTemplateItem.CreateFromInstance(
            new Audio.Effects.AudioEffectGroup(),
            "first");
        ObjectTemplateItem second = ObjectTemplateItem.CreateFromInstance(
            new Audio.Effects.AudioEffectGroup(),
            "second");
        File.WriteAllText(firstTarget, ObjectTemplateItem.ToJson(first).ToJsonString());
        File.WriteAllText(secondTarget, ObjectTemplateItem.ToJson(second).ToJsonString());
        File.SetLastWriteTimeUtc(firstTarget, DateTime.UtcNow);
        File.SetLastWriteTimeUtc(secondTarget, DateTime.UtcNow.AddMinutes(-10));
        try
        {
            try
            {
                File.CreateSymbolicLink(alias, firstTarget);
            }
            catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or PlatformNotSupportedException)
            {
                Assert.Ignore($"File symbolic links are unavailable: {ex.Message}");
            }

            service.RestoreItems();
            ObjectTemplateItem loadedFirst = service.FindByBaseType(first.BaseType)
                .Single(item => string.Equals(item.FilePath, alias, StringComparison.Ordinal));
            Assert.That(loadedFirst.Id, Is.EqualTo(first.Id));

            File.Delete(alias);
            File.CreateSymbolicLink(alias, secondTarget);
            service.RefreshFromFileSystem();

            ObjectTemplateItem loadedSecond = service.FindByBaseType(first.BaseType)
                .Single(item => string.Equals(item.FilePath, alias, StringComparison.Ordinal));
            Assert.That(loadedSecond.Id, Is.EqualTo(second.Id));
        }
        finally
        {
            File.Delete(alias);
            Directory.Delete(tempRoot, recursive: true);
            service.RestoreItems();
        }
    }

    [Test]
    public async Task RestoreItems_deduplicates_a_template_and_its_symbolic_link_alias()
    {
        ObjectTemplateService service = ObjectTemplateService.Instance;
        ObjectTemplateItem? item = await service.AddFromInstanceAsync(
            new Audio.Effects.AudioEffectGroup(),
            $"deduplicate-{Guid.NewGuid():N}");
        Assert.That(item, Is.Not.Null);
        string originalPath = item!.FilePath!;
        string aliasPath = Path.Combine(
            Path.GetDirectoryName(originalPath)!,
            $"alias-{Guid.NewGuid():N}.json");
        try
        {
            try
            {
                File.CreateSymbolicLink(aliasPath, originalPath);
            }
            catch (Exception ex) when (ex is IOException
                                       or UnauthorizedAccessException
                                       or PlatformNotSupportedException)
            {
                Assert.Ignore($"File symbolic links are unavailable: {ex.Message}");
            }

            service.RestoreItems();

            string?[] matchingPaths = service.FindByBaseType(item.BaseType)
                .Select(template => template.FilePath)
                .Where(path => path == originalPath || path == aliasPath)
                .ToArray();
            Assert.That(matchingPaths, Is.EqualTo(new[] { originalPath }));
        }
        finally
        {
            File.Delete(aliasPath);
            File.Delete(originalPath);
            service.RestoreItems();
        }
    }
}
