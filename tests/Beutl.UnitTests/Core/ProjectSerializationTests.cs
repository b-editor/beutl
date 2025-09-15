using System;
using System.Text.Json.Nodes;
using Beutl.Serialization;
using Beutl.Services;

namespace Beutl.UnitTests.Core;

public class ProjectSerializationTests
{
    private sealed class DummyItem : ProjectItem {}

    private sealed class FakeGenerator : IProjectItemGenerator
    {
        public bool TryCreateItem(string file, out ProjectItem? obj)
        {
            obj = new DummyItem { FileName = file };
            return true;
        }

        public bool TryCreateItem<T>(string file, out T? obj) where T : ProjectItem
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
    public void Save_IncludesItemsAsRelativePaths_AndVariables()
    {
        var prev = ProjectItemContainer.Current.Generator;
        ProjectItemContainer.Current.Generator = new FakeGenerator();
        try
        {
            var proj = new Project();
            string dir = ArtifactProvider.GetArtifactDirectory();
            string baseDir = Path.GetFullPath(dir);
            string f1 = Path.Combine(baseDir, "a.item");
            string f2 = Path.Combine(baseDir, "b.item");
            File.WriteAllText(f1, "a");
            File.WriteAllText(f2, "b");

            var i1 = new DummyItem { FileName = f1 };
            var i2 = new DummyItem { FileName = f2 };
            proj.Items.Add(i1);
            proj.Items.Add(i2);
            proj.Variables["k1"] = "v1";
            proj.Variables["k2"] = "v2";

            string prjPath = Path.Combine(baseDir, "test.bproj");
            proj.Save(prjPath);

            var json = JsonHelper.JsonRestore(prjPath) as JsonObject;
            Assert.That(json, Is.Not.Null);
            var items = (JsonArray)json!["items"]!;
            // Should be relative paths
            Assert.That(items.Count, Is.EqualTo(2));
            Assert.That(items[0]!.ToJsonString().Contains("a.item"), Is.True);
            Assert.That(items[1]!.ToJsonString().Contains("b.item"), Is.True);

            var vars = (JsonObject)json!["variables"]!;
            Assert.That(vars["k1"]!.ToJsonString(), Is.EqualTo("\"v1\""));
            Assert.That(vars["k2"]!.ToJsonString(), Is.EqualTo("\"v2\""));
        }
        finally
        {
            ProjectItemContainer.Current.Generator = prev;
        }
    }

    [Test]
    public void Restore_RebuildsItemsAndVariables()
    {
        var prev = ProjectItemContainer.Current.Generator;
        ProjectItemContainer.Current.Generator = new FakeGenerator();
        try
        {
            var proj = new Project();
            string dir = ArtifactProvider.GetArtifactDirectory();
            string baseDir = Path.GetFullPath(dir);
            string f1 = Path.Combine(baseDir, "c.item");
            string f2 = Path.Combine(baseDir, "d.item");
            File.WriteAllText(f1, "c");
            File.WriteAllText(f2, "d");
            proj.Items.Add(new DummyItem { FileName = f1 });
            proj.Items.Add(new DummyItem { FileName = f2 });
            proj.Variables["k1"] = "v1";
            string prjPath = Path.Combine(baseDir, "restore.bproj");
            proj.Save(prjPath);

            var proj2 = new Project();
            proj2.Restore(prjPath);

            Assert.That(proj2.Items.Count, Is.EqualTo(2));
            Assert.That(proj2.Items.Any(x => x.FileName.EndsWith("c.item")), Is.True);
            Assert.That(proj2.Variables["k1"], Is.EqualTo("v1"));
        }
        finally
        {
            ProjectItemContainer.Current.Generator = prev;
        }
    }

    [Test]
    public void ItemsChanged_AutoSavesProjectFile()
    {
        var prev = ProjectItemContainer.Current.Generator;
        ProjectItemContainer.Current.Generator = new FakeGenerator();
        try
        {
            var proj = new Project();
            string dir = ArtifactProvider.GetArtifactDirectory();
            string baseDir = Path.GetFullPath(dir);
            string prjPath = Path.Combine(baseDir, "autosave.bproj");
            string f1 = Path.Combine(baseDir, "e.item");
            string f2 = Path.Combine(baseDir, "f.item");
            File.WriteAllText(f1, "e");
            File.WriteAllText(f2, "f");

            proj.Items.Add(new DummyItem { FileName = f1 });
            proj.Save(prjPath);
            var json1 = (JsonObject)JsonHelper.JsonRestore(prjPath)!;
            int beforeCount = ((JsonArray)json1["items"]!).Count;

            proj.Items.Add(new DummyItem { FileName = f2 });
            var json2 = (JsonObject)JsonHelper.JsonRestore(prjPath)!;
            int afterCount = ((JsonArray)json2["items"]!).Count;

            Assert.That(beforeCount, Is.EqualTo(1));
            Assert.That(afterCount, Is.EqualTo(2));
        }
        finally
        {
            ProjectItemContainer.Current.Generator = prev;
        }
    }
}
