using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Beutl.AgentToolkit.Sessions;

namespace Beutl.AgentToolkit.Tests.Sessions;

public sealed class AgentSessionManagerConcurrencyTests
{
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
