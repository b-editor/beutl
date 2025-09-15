using System;
using System.Diagnostics.CodeAnalysis;
using Beutl.Services;

namespace Beutl.UnitTests.Core;

public class ProjectItemContainerTests
{
    private sealed class DummyItem : ProjectItem
    {
        public int RestoreCalls { get; private set; }
        protected override void RestoreCore(string filename) => RestoreCalls++;
    }

    private sealed class FakeGenerator : IProjectItemGenerator
    {
        public bool TryCreateItem(string file, [NotNullWhen(true)] out ProjectItem? obj)
        {
            obj = new DummyItem { FileName = file };
            return true;
        }

        public bool TryCreateItem<T>(string file, [NotNullWhen(true)] out T? obj) where T : ProjectItem
        {
            if (typeof(T) == typeof(DummyItem))
            {
                obj = (T?)(ProjectItem)new DummyItem { FileName = file };
                return true;
            }

            obj = null;
            return false;
        }
    }

    [Test]
    public void TryGetOrCreateItem_Creates_Caches_AndIsCreated()
    {
        var prev = ProjectItemContainer.Current.Generator;
        ProjectItemContainer.Current.Generator = new FakeGenerator();
        try
        {
            string file = Path.Combine(ArtifactProvider.GetArtifactDirectory(), "p1.item");
            File.WriteAllText(file, "x");

            Assert.That(ProjectItemContainer.Current.TryGetOrCreateItem(file, out ProjectItem? item1), Is.True);
            Assert.That(item1, Is.Not.Null);
            Assert.That(ProjectItemContainer.Current.IsCreated(file), Is.True);

            Assert.That(ProjectItemContainer.Current.TryGetOrCreateItem(file, out ProjectItem? item2), Is.True);
            Assert.That(item2, Is.SameAs(item1));

            Assert.That(ProjectItemContainer.Current.Remove(file), Is.True);
            Assert.That(ProjectItemContainer.Current.IsCreated(file), Is.False);
        }
        finally
        {
            ProjectItemContainer.Current.Generator = prev;
        }
    }

    [Test]
    public void TryGetOrCreateItem_Restores_WhenFileIsNewer()
    {
        var prev = ProjectItemContainer.Current.Generator;
        ProjectItemContainer.Current.Generator = new FakeGenerator();
        try
        {
            string file = Path.Combine(ArtifactProvider.GetArtifactDirectory(), "p2.item");
            File.WriteAllText(file, "data");
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddSeconds(1));

            Assert.That(ProjectItemContainer.Current.TryGetOrCreateItem(file, out DummyItem? item1), Is.True);
            Assert.That(item1, Is.Not.Null);
            Assert.That(item1!.RestoreCalls, Is.EqualTo(0));

            // Second lookup should trigger Restore since file is newer than LastSavedTime
            Assert.That(ProjectItemContainer.Current.TryGetOrCreateItem(file, out DummyItem? item2), Is.True);
            Assert.That(item2, Is.SameAs(item1));
            Assert.That(item1.RestoreCalls, Is.GreaterThanOrEqualTo(1));
        }
        finally
        {
            ProjectItemContainer.Current.Generator = prev;
        }
    }

    [Test]
    public void TryGetOrCreateItem_Typed_Generic_Works()
    {
        var prev = ProjectItemContainer.Current.Generator;
        ProjectItemContainer.Current.Generator = new FakeGenerator();
        try
        {
            string file = Path.Combine(ArtifactProvider.GetArtifactDirectory(), "p3.item");
            File.WriteAllText(file, "data");

            Assert.That(ProjectItemContainer.Current.TryGetOrCreateItem<DummyItem>(file, out var item), Is.True);
            Assert.That(item, Is.Not.Null);
            Assert.That(item!.FileName, Is.EqualTo(file));
        }
        finally
        {
            ProjectItemContainer.Current.Generator = prev;
        }
    }
}

