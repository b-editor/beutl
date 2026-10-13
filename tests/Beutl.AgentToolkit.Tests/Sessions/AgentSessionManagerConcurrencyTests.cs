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
        Assert.That(keyA, Is.EqualTo(manager.GetSessionKey(manager.RequireSession())));

        manager.UseSource(new AgentToolkitTestSessionSource(sessionB));

        Assert.Multiple(() =>
        {
            Assert.That(manager.GetSessionKey(sessionA), Is.EqualTo(keyA));
            Assert.That(manager.GetSessionKey(manager.RequireSession()), Is.Not.EqualTo(keyA));
        });
    }

    [Test]
    public void Get_composition_plan_validates_against_the_supplied_session_key()
    {
        var manager = new AgentSessionManager();
        const string plannedKey = "File:11111111-1111-1111-1111-111111111111";
        const string swappedKey = "File:22222222-2222-2222-2222-222222222222";
        CompositionPlanState state = manager.StoreCompositionPlan(
            plannedKey, "comp", "seed", new JsonObject(), new JsonObject(), new JsonArray(), new HashSet<Guid>());

        Assert.Multiple(() =>
        {
            // The planned key finds the plan; any other session's key is rejected.
            Assert.That(manager.GetCompositionPlan(state.Id, plannedKey).Id, Is.EqualTo(state.Id));
            Assert.That(
                Assert.Throws<AgentToolkit.Reconciliation.ReconcileException>(
                    () => manager.GetCompositionPlan(state.Id, swappedKey))!.Error.Message,
                Does.Contain("session"));
        });
    }

    [Test]
    public async Task Composition_plans_evict_the_oldest_beyond_the_retention_cap()
    {
        var manager = new AgentSessionManager();
        const string sessionKey = "host";

        CompositionPlanState first = manager.StoreCompositionPlan(
            sessionKey,
            "comp", "seed", new JsonObject(), new JsonObject(), new JsonArray(), new HashSet<Guid>());
        await Task.Delay(10);
        for (int i = 0; i < 32; i++)
        {
            manager.StoreCompositionPlan(
                sessionKey,
                "comp", "seed", new JsonObject(), new JsonObject(), new JsonArray(), new HashSet<Guid>());
        }

        Assert.Throws<AgentToolkit.Reconciliation.ReconcileException>(
            () => manager.GetCompositionPlan(first.Id, sessionKey));
    }

    [Test]
    public void Concurrent_plan_store_get_remove_does_not_throw_or_corrupt()
    {
        var manager = new AgentSessionManager();
        using var source = new FileSessionSource();
        manager.UseSource(source);
        const string sessionKey = "host";

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
                        sessionKey,
                        "comp",
                        "seed",
                        new JsonObject(),
                        new JsonObject(),
                        new JsonArray(),
                        new HashSet<Guid>());
                    plans.Add(state.Id);
                    manager.GetCompositionPlan(state.Id, sessionKey);
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


}
