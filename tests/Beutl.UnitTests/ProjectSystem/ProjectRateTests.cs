using System.Globalization;
using System.Text.Json.Nodes;
using Beutl.Audio;
using Beutl.ProjectSystem;
using Beutl.Serialization;

namespace Beutl.UnitTests.ProjectSystem;

[TestFixture]
public sealed class ProjectRateTests
{
    [TestCase("24", 48000, "24", 48000)]
    [TestCase("30000/1001", 48000, "30000/1001", 48000)]
    [TestCase("0", -1, "30", 44100)]
    [TestCase("-24", 0, "30", 44100)]
    [TestCase("30000/0", 48000, "30", 48000)]
    [TestCase("-30000/1001", 48000, "30", 48000)]
    public void Reads_positive_scene_rates_or_uses_defaults(
        string frameRate, int sampleRate, string expectedFrameRate, int expectedSampleRate)
    {
        var scene = new Scene
        {
            FrameRate = { Value = Rational.Parse(frameRate, CultureInfo.InvariantCulture) },
            SampleRate = { Value = sampleRate },
        };

        Assert.Multiple(() =>
        {
            Assert.That(scene.GetFrameRate(), Is.EqualTo(Rational.Parse(expectedFrameRate, CultureInfo.InvariantCulture)));
            Assert.That(scene.GetSampleRate(), Is.EqualTo(expectedSampleRate));
        });
    }

    [Test]
    public void Detached_scene_uses_defaults()
    {
        Scene? scene = null;
        Assert.Multiple(() =>
        {
            Assert.That(scene.GetFrameRate(), Is.EqualTo(new Rational(30)));
            Assert.That(scene.GetSampleRate(), Is.EqualTo(44100));
        });
    }

    [Test]
    public void SceneLayout_DefaultsToStereoAndSurvivesSerialization()
    {
        Scene? detached = null;
        Assert.That(detached.GetChannelLayout(), Is.EqualTo(AudioChannelLayout.Stereo));
        Assert.That(ReadScene(new JsonObject()).ChannelLayout.Value, Is.EqualTo(SpeakerLayout.Stereo));
        foreach (SpeakerLayout layout in Enum.GetValues<SpeakerLayout>())
        {
            var scene = new Scene { ChannelLayout = { Value = layout } };
            Scene restored = ReadScene(SerializeEmbedded(scene));
            Assert.That(restored.ChannelLayout.Value, Is.EqualTo(layout));
            Assert.That(restored.GetChannelLayout(), Is.EqualTo(layout.ToAudioChannelLayout()));
        }
    }

    // Earlier versions (before 2.0.0-preview.9) kept rates as project variables, which opening migrates to scenes.
    [TestCase("24", "48000", "24", 48000)]
    [TestCase("30000/1001", "96000", "30000/1001", 96000)]
    [TestCase("invalid", "invalid", "30", 44100)]
    [TestCase("2147483648", "2147483648", "2147483648", 44100)]
    public void A_project_of_an_earlier_version_moves_its_rate_variables_to_its_scenes(
        string frameRate, string sampleRate, string expectedFrameRate, int expectedSampleRate)
    {
        Project project = ReadProjectWithScene(new JsonObject
        {
            ["variables"] = new JsonObject { ["framerate"] = frameRate, ["samplerate"] = sampleRate, ["author"] = "beutl" },
        });
        Scene scene = (Scene)project.Items.Single();
        JsonObject savedProject = SerializeEmbedded(project);
        JsonObject savedScene = SerializeEmbedded(scene);

        Assert.Multiple(() =>
        {
            Assert.That(scene.FrameRate.Value, Is.EqualTo(Rational.Parse(expectedFrameRate, CultureInfo.InvariantCulture)));
            Assert.That(scene.SampleRate.Value, Is.EqualTo(expectedSampleRate));
            Assert.That(project.Variables.Value, Is.EquivalentTo(new Dictionary<string, string> { ["author"] = "beutl" }));
            Assert.That(savedProject.ContainsKey("FrameRate"), Is.False);
            Assert.That(savedProject.ContainsKey("SampleRate"), Is.False);
            Assert.That(savedProject.ContainsKey("ChannelLayout"), Is.False);
            Assert.That((string?)savedScene["FrameRate"], Is.EqualTo(expectedFrameRate));
            Assert.That((int?)savedScene["SampleRate"], Is.EqualTo(expectedSampleRate));
            Assert.That(savedProject["variables"]!.AsObject().Select(pair => pair.Key), Is.EqualTo(new[] { "author" }));
        });
    }

    // Earlier versions (2.0.0-preview.9..15) kept rates as members on Project; opening migrates them to scenes.
    [Test]
    public void A_project_of_preview_15_moves_its_rates_and_layout_to_its_scenes()
    {
        Project project = ReadProjectWithScene(new JsonObject
        {
            ["FrameRate"] = "30000/1001",
            ["SampleRate"] = 48000,
            ["ChannelLayout"] = (int)SpeakerLayout.Surround51,
            ["variables"] = new JsonObject { ["framerate"] = "60", ["samplerate"] = "22050" },
        });
        Scene scene = (Scene)project.Items.Single();
        JsonObject savedProject = SerializeEmbedded(project);

        Assert.Multiple(() =>
        {
            Assert.That(scene.FrameRate.Value, Is.EqualTo(new Rational(30000, 1001)));
            Assert.That(scene.SampleRate.Value, Is.EqualTo(48000));
            Assert.That(scene.ChannelLayout.Value, Is.EqualTo(SpeakerLayout.Surround51));
            Assert.That(project.Variables.Value, Is.Empty);
            Assert.That(savedProject.ContainsKey("FrameRate"), Is.False);
            Assert.That(savedProject.ContainsKey("SampleRate"), Is.False);
            Assert.That(savedProject.ContainsKey("ChannelLayout"), Is.False);
        });
    }

    [Test]
    public void Scene_rates_win_over_legacy_project_rates()
    {
        var sceneJson = new JsonObject
        {
            ["$type"] = "[Beutl.ProjectSystem]:Scene",
            ["FrameRate"] = "60",
            ["SampleRate"] = 96000,
            ["ChannelLayout"] = (int)SpeakerLayout.Surround71,
            ["Width"] = 1920,
            ["Height"] = 1080,
        };
        Project project = ReadProjectWithScene(new JsonObject
        {
            ["FrameRate"] = "24",
            ["SampleRate"] = 48000,
            ["ChannelLayout"] = (int)SpeakerLayout.Mono,
        }, sceneJson);
        Scene scene = (Scene)project.Items.Single();

        Assert.Multiple(() =>
        {
            Assert.That(scene.FrameRate.Value, Is.EqualTo(new Rational(60)));
            Assert.That(scene.SampleRate.Value, Is.EqualTo(96000));
            Assert.That(scene.ChannelLayout.Value, Is.EqualTo(SpeakerLayout.Surround71));
        });
    }

    [Test]
    public void Multiple_scenes_in_one_project_keep_independent_rates_and_layouts()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"beutl-multi-scene-rates-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var projectUri = new Uri(Path.Combine(dir, "project.bep"));
            var first = new Scene(1920, 1080, "first")
            {
                Uri = new Uri(Path.Combine(dir, "first", "first.scene")),
                FrameRate = { Value = new Rational(24) },
                SampleRate = { Value = 48000 },
                ChannelLayout = { Value = SpeakerLayout.Surround51 },
            };
            var second = new Scene(1080, 1920, "second")
            {
                Uri = new Uri(Path.Combine(dir, "second", "second.scene")),
                FrameRate = { Value = new Rational(60) },
                SampleRate = { Value = 44100 },
                ChannelLayout = { Value = SpeakerLayout.Stereo },
            };
            var project = new Project { Uri = projectUri, Items = { first, second } };

            CoreSerializer.StoreToUri(project, projectUri);
            Project reopened = CoreSerializer.RestoreFromUri<Project>(projectUri);
            Scene reopenedFirst = (Scene)reopened.Items[0];
            Scene reopenedSecond = (Scene)reopened.Items[1];

            Assert.Multiple(() =>
            {
                Assert.That(reopenedFirst.FrameRate.Value, Is.EqualTo(new Rational(24)));
                Assert.That(reopenedFirst.SampleRate.Value, Is.EqualTo(48000));
                Assert.That(reopenedFirst.ChannelLayout.Value, Is.EqualTo(SpeakerLayout.Surround51));
                Assert.That(reopenedSecond.FrameRate.Value, Is.EqualTo(new Rational(60)));
                Assert.That(reopenedSecond.SampleRate.Value, Is.EqualTo(44100));
                Assert.That(reopenedSecond.ChannelLayout.Value, Is.EqualTo(SpeakerLayout.Stereo));
            });
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public void Project_retains_legacy_rates_when_auto_saving_until_all_migrated_scenes_are_persisted()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"beutl-legacy-autosave-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var projectUri = new Uri(Path.Combine(dir, "project.bep"));
            var firstUri = new Uri(Path.Combine(dir, "first", "first.scene"));
            var secondUri = new Uri(Path.Combine(dir, "second", "second.scene"));
            var first = new Scene(1920, 1080, "first") { Uri = firstUri };
            var second = new Scene(1920, 1080, "second") { Uri = secondUri };
            var project = new Project { Uri = projectUri, Items = { first, second } };
            CoreSerializer.StoreToUri(project, projectUri);

            // Rewrite files to look like a 2.0.0-preview.15 project: rates on project, absent from both scenes.
            JsonObject projectJson = JsonNode.Parse(File.ReadAllText(projectUri.LocalPath))!.AsObject();
            projectJson["FrameRate"] = "24";
            projectJson["SampleRate"] = 48000;
            projectJson["ChannelLayout"] = (int)SpeakerLayout.Surround51;
            projectJson["minAppVersion"] = "2.0.0-preview.15";
            File.WriteAllText(projectUri.LocalPath, projectJson.ToJsonString());

            foreach (Uri sceneUri in new[] { firstUri, secondUri })
            {
                JsonObject sceneJson = JsonNode.Parse(File.ReadAllText(sceneUri.LocalPath))!.AsObject();
                sceneJson.Remove(nameof(Scene.FrameRate));
                sceneJson.Remove(nameof(Scene.SampleRate));
                sceneJson.Remove(nameof(Scene.ChannelLayout));
                sceneJson["minAppVersion"] = "2.0.0-preview.15";
                File.WriteAllText(sceneUri.LocalPath, sceneJson.ToJsonString());
            }

            // Reopen and simulate AutoSaveService saving only the first scene and the project file (CoreSerializationMode.Write).
            Project opened = CoreSerializer.RestoreFromUri<Project>(projectUri);
            Scene openedFirst = (Scene)opened.Items[0];
            Scene openedSecond = (Scene)opened.Items[1];
            openedFirst.FrameRate.Value = new Rational(60);
            CoreSerializer.StoreToUri(openedFirst, firstUri, CoreSerializationMode.Write);
            CoreSerializer.StoreToUri(opened, projectUri, CoreSerializationMode.Write);

            // Reopen after an unexpected exit before the second scene was ever saved:
            // first keeps its edited rate, second still inherits the legacy project rates.
            Project afterCrash = CoreSerializer.RestoreFromUri<Project>(projectUri);
            Scene afterCrashFirst = (Scene)afterCrash.Items[0];
            Scene afterCrashSecond = (Scene)afterCrash.Items[1];
            Assert.Multiple(() =>
            {
                Assert.That(afterCrashFirst.FrameRate.Value, Is.EqualTo(new Rational(60)));
                Assert.That(afterCrashFirst.SampleRate.Value, Is.EqualTo(48000));
                Assert.That(afterCrashFirst.ChannelLayout.Value, Is.EqualTo(SpeakerLayout.Surround51));
                Assert.That(afterCrashSecond.FrameRate.Value, Is.EqualTo(new Rational(24)));
                Assert.That(afterCrashSecond.SampleRate.Value, Is.EqualTo(48000));
                Assert.That(afterCrashSecond.ChannelLayout.Value, Is.EqualTo(SpeakerLayout.Surround51));
            });

            // Once the second scene is persisted too, saving the project drops the legacy keys.
            CoreSerializer.StoreToUri(afterCrashSecond, secondUri, CoreSerializationMode.Write);
            CoreSerializer.StoreToUri(afterCrash, projectUri, CoreSerializationMode.Write);
            JsonObject cleanedProject = JsonNode.Parse(File.ReadAllText(projectUri.LocalPath))!.AsObject();
            Assert.Multiple(() =>
            {
                Assert.That(cleanedProject.ContainsKey("FrameRate"), Is.False);
                Assert.That(cleanedProject.ContainsKey("SampleRate"), Is.False);
                Assert.That(cleanedProject.ContainsKey("ChannelLayout"), Is.False);
            });
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Test]
    public void Rolled_back_full_save_keeps_legacy_rates_for_later_project_only_save()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"beutl-legacy-rollback-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var projectUri = new Uri(Path.Combine(dir, "project.bep"));
            var sceneUri = new Uri(Path.Combine(dir, "scene", "scene.scene"));
            var scene = new Scene(1920, 1080, "scene") { Uri = sceneUri };
            var project = new Project { Uri = projectUri, Items = { scene } };
            CoreSerializer.StoreToUri(project, projectUri);

            JsonObject projectJson = JsonNode.Parse(File.ReadAllText(projectUri.LocalPath))!.AsObject();
            projectJson["FrameRate"] = "24";
            projectJson["SampleRate"] = 48000;
            projectJson["ChannelLayout"] = (int)SpeakerLayout.Surround51;
            projectJson["minAppVersion"] = "2.0.0-preview.15";
            File.WriteAllText(projectUri.LocalPath, projectJson.ToJsonString());

            JsonObject sceneJson = JsonNode.Parse(File.ReadAllText(sceneUri.LocalPath))!.AsObject();
            sceneJson.Remove(nameof(Scene.FrameRate));
            sceneJson.Remove(nameof(Scene.SampleRate));
            sceneJson.Remove(nameof(Scene.ChannelLayout));
            sceneJson["minAppVersion"] = "2.0.0-preview.15";
            File.WriteAllText(sceneUri.LocalPath, sceneJson.ToJsonString());

            Project opened = CoreSerializer.RestoreFromUri<Project>(projectUri);

            // Simulate a full save inside a storage transaction that rolls back before commit.
            using (StorageWriteTransaction.Begin())
            {
                CoreSerializer.StoreToUri(opened, projectUri);

                // Even before rollback, a concurrent project-only snapshot must retain legacy keys until commit.
                JsonObject midTransaction = CoreSerializer.SerializeToJsonObject(
                    opened,
                    new CoreSerializerOptions { BaseUri = projectUri, Mode = CoreSerializationMode.Write });
                Assert.Multiple(() =>
                {
                    Assert.That(midTransaction.ContainsKey("FrameRate"), Is.True);
                    Assert.That(midTransaction.ContainsKey("SampleRate"), Is.True);
                    Assert.That(midTransaction.ContainsKey("ChannelLayout"), Is.True);
                });
            }

            // A subsequent project-only save must still retain the legacy keys because the scene file was rolled back.
            CoreSerializer.StoreToUri(opened, projectUri, CoreSerializationMode.Write);

            Project reopened = CoreSerializer.RestoreFromUri<Project>(projectUri);
            Scene reopenedScene = (Scene)reopened.Items.Single();
            Assert.Multiple(() =>
            {
                Assert.That(reopenedScene.FrameRate.Value, Is.EqualTo(new Rational(24)));
                Assert.That(reopenedScene.SampleRate.Value, Is.EqualTo(48000));
                Assert.That(reopenedScene.ChannelLayout.Value, Is.EqualTo(SpeakerLayout.Surround51));
            });

            // Once a transaction commits, the scene's flag clears and a project-only save drops the legacy keys.
            using (StorageWriteTransaction transaction = StorageWriteTransaction.Begin())
            {
                CoreSerializer.StoreToUri(reopenedScene, sceneUri, CoreSerializationMode.Write);
                transaction.Commit();
            }

            CoreSerializer.StoreToUri(reopened, projectUri, CoreSerializationMode.Write);
            JsonObject committedProject = JsonNode.Parse(File.ReadAllText(projectUri.LocalPath))!.AsObject();
            Assert.Multiple(() =>
            {
                Assert.That(committedProject.ContainsKey("FrameRate"), Is.False);
                Assert.That(committedProject.ContainsKey("SampleRate"), Is.False);
                Assert.That(committedProject.ContainsKey("ChannelLayout"), Is.False);
            });
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static JsonObject SerializeEmbedded(ICoreSerializable obj)
    {
        return CoreSerializer.SerializeToJsonObject(
            obj,
            new CoreSerializerOptions { Mode = CoreSerializationMode.EmbedReferencedObjects });
    }

    private static Scene ReadScene(JsonObject json)
    {
        return (Scene)CoreSerializer.DeserializeFromJsonObject(json, typeof(Scene));
    }

    private static Project ReadProjectWithScene(JsonObject json, JsonObject? sceneJson = null)
    {
        json["items"] = new JsonArray
        {
            sceneJson ?? new JsonObject
            {
                ["$type"] = "[Beutl.ProjectSystem]:Scene",
                ["Width"] = 1920,
                ["Height"] = 1080,
            }
        };
        return (Project)CoreSerializer.DeserializeFromJsonObject(json, typeof(Project));
    }
}
