using Beutl.Media.Proxy;

namespace Beutl.UnitTests.Media.Proxy;

[TestFixture]
public class ProxyPresetDefinitionsTests
{
    // ProxyPresetDefinitions holds process-wide static state. Every test that mutates it must restore the
    // built-in defaults in TearDown so the registry is left pristine for other tests.
    [TearDown]
    public void TearDown()
    {
        ProxyPresetDefinitions.Unregister(ProxyPreset.Half);
        ProxyPresetDefinitions.Unregister(ProxyPreset.Quarter);
        ProxyPresetDefinitions.Unregister(ProxyPreset.Eighth);
    }

    [Test]
    public void Get_ReturnsBuiltInParameters()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ProxyPresetDefinitions.Get(ProxyPreset.Half).Crf, Is.EqualTo(25));
            Assert.That(ProxyPresetDefinitions.Get(ProxyPreset.Quarter).Crf, Is.EqualTo(26));
            Assert.That(ProxyPresetDefinitions.Get(ProxyPreset.Eighth).Crf, Is.EqualTo(28));
        });
    }

    [Test]
    public void Register_OverridesBuiltInParameters()
    {
        var overridden = new ProxyEncodeParameters(0.25f, 30, 1280, "fastdecode", "veryfast");

        ProxyPresetDefinitions.Register(ProxyPreset.Quarter, overridden);

        Assert.That(ProxyPresetDefinitions.Get(ProxyPreset.Quarter).Crf, Is.EqualTo(30));
        Assert.That(ProxyPresetDefinitions.Get(ProxyPreset.Quarter).Preset, Is.EqualTo("veryfast"));
    }

    [Test]
    public void Unregister_RestoresBuiltInParameters()
    {
        var overridden = new ProxyEncodeParameters(0.25f, 30, 1280, "fastdecode", "veryfast");
        ProxyPresetDefinitions.Register(ProxyPreset.Quarter, overridden);

        bool changed = ProxyPresetDefinitions.Unregister(ProxyPreset.Quarter);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.True, "Unregister must report a change when the value was overridden.");
            Assert.That(ProxyPresetDefinitions.Get(ProxyPreset.Quarter).Crf, Is.EqualTo(26), "Built-in Quarter CRF is 26.");
            Assert.That(ProxyPresetDefinitions.Get(ProxyPreset.Quarter).Preset, Is.EqualTo("fast"));
        });
    }

    [Test]
    public void Unregister_WhenAlreadyAtDefault_ReturnsFalse()
    {
        bool changed = ProxyPresetDefinitions.Unregister(ProxyPreset.Quarter);

        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.False, "Unregister must report no change when already at the built-in default.");
            Assert.That(ProxyPresetDefinitions.Get(ProxyPreset.Quarter).Crf, Is.EqualTo(26));
        });
    }

    [Test]
    public void Get_UnknownPresetValue_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProxyPresetDefinitions.Get((ProxyPreset)99));
    }

    [Test]
    public void Register_UnknownPresetValue_Throws()
    {
        var parameters = new ProxyEncodeParameters(0.25f, 30, 1280, "fastdecode", "veryfast");

        Assert.Throws<ArgumentOutOfRangeException>(() => ProxyPresetDefinitions.Register((ProxyPreset)99, parameters));
    }

    [Test]
    public void Unregister_UnknownPresetValue_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => ProxyPresetDefinitions.Unregister((ProxyPreset)99));
    }

    [Test]
    public void All_ReturnsNonMutatingSnapshot()
    {
        IReadOnlyDictionary<ProxyPreset, ProxyEncodeParameters> snapshot = ProxyPresetDefinitions.All;
        int countBefore = snapshot.Count;

        ProxyPresetDefinitions.Register(ProxyPreset.Quarter, new ProxyEncodeParameters(0.25f, 30, 1280, "fastdecode", "veryfast"));
        IReadOnlyDictionary<ProxyPreset, ProxyEncodeParameters> snapshotAfter = ProxyPresetDefinitions.All;

        Assert.Multiple(() =>
        {
            Assert.That(snapshot.Count, Is.EqualTo(countBefore), "the earlier snapshot must not mutate.");
            Assert.That(snapshotAfter[ProxyPreset.Quarter].Crf, Is.EqualTo(30), "a fresh snapshot reflects the override.");
            Assert.That(snapshot[ProxyPreset.Quarter].Crf, Is.EqualTo(26), "the earlier snapshot still shows the built-in value.");
        });
    }

    [Test]
    public void Register_FromMultipleThreads_DoesNotCorrupt()
    {
        var slow = new ProxyEncodeParameters(0.25f, 30, 1280, "fastdecode", "veryfast");
        var fast = new ProxyEncodeParameters(0.25f, 26, 1280, "fastdecode", "fast");
        var results = new System.Collections.Concurrent.ConcurrentBag<Exception?>();

        var tasks = Enumerable.Range(0, 32).Select(i => Task.Run(() =>
        {
            try
            {
                ProxyPresetDefinitions.Register(ProxyPreset.Quarter, i % 2 == 0 ? slow : fast);
                _ = ProxyPresetDefinitions.Get(ProxyPreset.Quarter);
            }
            catch (Exception ex)
            {
                results.Add(ex);
            }
        })).ToArray();

        Task.WaitAll(tasks);

        int finalCrf = ProxyPresetDefinitions.Get(ProxyPreset.Quarter).Crf;

        Assert.Multiple(() =>
        {
            Assert.That(results, Is.Empty, "no thread should observe an exception.");
            Assert.That(finalCrf, Is.EqualTo(26).Or.EqualTo(30), "final value must be one of the written values.");
        });
    }
}
