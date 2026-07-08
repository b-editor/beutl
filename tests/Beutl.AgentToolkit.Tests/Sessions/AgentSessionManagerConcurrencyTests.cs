using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Sessions;
using Beutl.AgentToolkit.Tests.Helpers;
using Beutl.ProjectSystem;

namespace Beutl.AgentToolkit.Tests.Sessions;

public sealed class AgentSessionManagerConcurrencyTests
{
    [Test]
    public void Get_session_key_keys_the_given_session_not_the_current_one()
    {
        var sceneA = new Scene(640, 360, "A");
        var sceneB = new Scene(640, 360, "B");
        using var sessionA = new AgentToolkitTestSession(sceneA);
        using var sessionB = new AgentToolkitTestSession(sceneB);
        var manager = new AgentSessionManager();
        manager.UseSource(new AgentToolkitTestSessionSource(sessionA));

        string keyA = manager.GetSessionKey(sessionA);
        Assert.That(keyA, Is.EqualTo(manager.CurrentSessionKey));

        manager.UseSource(new AgentToolkitTestSessionSource(sessionB));

        Assert.Multiple(() =>
        {
            Assert.That(manager.GetSessionKey(sessionA), Is.EqualTo(keyA));
            Assert.That(manager.CurrentSessionKey, Is.Not.EqualTo(keyA));
        });
    }

    [Test]
    public async Task Composition_plans_evict_the_oldest_beyond_the_retention_cap()
    {
        var manager = new AgentSessionManager();

        CompositionPlanState first = manager.StoreCompositionPlan(
            "comp", "seed", new JsonObject(), new JsonObject(), new JsonArray(), new HashSet<Guid>());
        await Task.Delay(10);
        for (int i = 0; i < 32; i++)
        {
            manager.StoreCompositionPlan(
                "comp", "seed", new JsonObject(), new JsonObject(), new JsonArray(), new HashSet<Guid>());
        }

        Assert.Throws<AgentToolkit.Reconciliation.ReconcileException>(
            () => manager.GetCompositionPlan(first.Id));
    }

    [Test]
    public void Concurrent_plan_store_get_remove_does_not_throw_or_corrupt()
    {
        var manager = new AgentSessionManager();
        using var source = new FileSessionSource();
        manager.UseSource(source);

        const int threads = 8;
        const int perThread = 100;
        var exceptions = new ConcurrentQueue<Exception>();
        var plans = new ConcurrentBag<string>();

        Task[] tasks = Enumerable.Range(0, threads).Select(_ => Task.Run(() =>
        {
            for (int i = 0; i < perThread; i++)
            {
                try
                {
                    CompositionPlanState state = manager.StoreCompositionPlan(
                        "comp",
                        "seed",
                        new JsonObject(),
                        new JsonObject(),
                        new JsonArray(),
                        new HashSet<Guid>());
                    plans.Add(state.Id);
                    manager.GetCompositionPlan(state.Id);
                    manager.RecordCompositionUse("comp");
                    manager.RemoveCompositionPlan(state.Id);
                }
                catch (Exception ex)
                {
                    exceptions.Enqueue(ex);
                }
            }
        })).ToArray();
        Task.WaitAll(tasks);

        Assert.That(exceptions, Is.Empty, () => string.Join("\n", exceptions.Select(e => e.ToString())));
        Assert.That(plans.Count, Is.EqualTo(threads * perThread));
    }

    [Test]
    public void Concurrent_recent_composition_reads_and_writes_do_not_throw()
    {
        var manager = new AgentSessionManager();
        using var source = new FileSessionSource();
        manager.UseSource(source);

        const int threads = 8;
        const int perThread = 100;
        var exceptions = new ConcurrentQueue<Exception>();

        Task[] tasks = Enumerable.Range(0, threads).Select(index => Task.Run(() =>
        {
            for (int i = 0; i < perThread; i++)
            {
                try
                {
                    manager.RecordCompositionUse($"comp-{index % 4}");
                    manager.GetRecentCompositions();
                    manager.GetAvoidedCompositions();
                }
                catch (Exception ex)
                {
                    exceptions.Enqueue(ex);
                }
            }
        })).ToArray();
        Task.WaitAll(tasks);

        Assert.That(exceptions, Is.Empty, () => string.Join("\n", exceptions.Select(e => e.ToString())));
    }
}
