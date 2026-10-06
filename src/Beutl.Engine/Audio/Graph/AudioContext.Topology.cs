namespace Beutl.Audio.Graph;

public sealed partial class AudioContext
{
    private sealed record InputSnapshot(AudioNode Node, AudioNode[] Inputs, object?[] States);

    /// <summary>
    /// Clears all connections while keeping nodes.
    /// </summary>
    public void ClearConnections()
    {
        ThrowIfDisposed();
        ThrowIfTopologyMutation();

        _topologyMutationInProgress = true;
        try
        {
            ClearConnectionsCore();
        }
        finally
        {
            _topologyMutationInProgress = false;
        }
    }

    private void ClearConnectionsCore()
    {
        List<InputSnapshot> snapshots = CaptureInputSnapshots();
        foreach (InputSnapshot snapshot in snapshots)
        {
            snapshot.Node.BeginInputTopologyTransaction();
        }

        var cleared = new List<InputSnapshot>(_nodes.Count);
        List<Exception>? commitFailures = null;
        try
        {
            foreach (InputSnapshot snapshot in snapshots)
            {
                snapshot.Node.ClearInputs();
                cleared.Add(snapshot);
                if (snapshot.Node.Inputs.Count != 0)
                {
                    throw new InvalidOperationException(
                        "Audio connection clearing hooks must leave every node without inputs.");
                }
            }

            if (!MatchesNodeSet(snapshots))
            {
                throw new InvalidOperationException(
                    "Audio connection clearing hooks must not mutate the context node set.");
            }

            foreach (InputSnapshot snapshot in snapshots)
            {
                if (snapshot.Node.Inputs.Count != 0)
                {
                    throw new InvalidOperationException(
                        "Audio connection clearing hooks must leave every node without inputs.");
                }
            }

            foreach (InputSnapshot snapshot in snapshots)
            {
                snapshot.Node.BeginInputTopologyCommit();
            }

            foreach (InputSnapshot snapshot in snapshots)
            {
                if (snapshot.Node.CompleteInputTopologyCommit() is { } commitFailure)
                {
                    (commitFailures ??= []).Add(commitFailure);
                }
            }

            foreach (InputSnapshot snapshot in snapshots)
            {
                snapshot.Node.EndInputTopologyCommit();
            }
        }
        catch (Exception clearException)
        {
            RollBackClear(clearException, cleared, snapshots);
            throw;
        }

        foreach (var list in _connections.Values)
        {
            list.Clear();
        }

        _outputNodes.Clear();
        _currentNode = null;

        ThrowIfLifecycleHooksFailed(
            commitFailures,
            "Audio connection clearing committed, but one or more lifecycle hooks failed.");
    }

    private List<InputSnapshot> CaptureInputSnapshots()
    {
        var snapshots = new List<InputSnapshot>(_nodes.Count);
        foreach (AudioNode node in _nodes)
        {
            AudioNode[] inputs = [.. node.Inputs];
            object?[] states = new object?[inputs.Length];
            for (int i = 0; i < inputs.Length; i++)
            {
                states[i] = node.CaptureInputStateForRollback(inputs[i], i);
            }

            snapshots.Add(new InputSnapshot(node, inputs, states));
        }

        return snapshots;
    }

    // Clearing hooks must leave the context's node list exactly as it was captured.
    private bool MatchesNodeSet(List<InputSnapshot> snapshots)
    {
        if (_nodes.Count != snapshots.Count)
            return false;

        for (int i = 0; i < snapshots.Count; i++)
        {
            if (!ReferenceEquals(snapshots[i].Node, _nodes[i]))
                return false;
        }

        return true;
    }

    // Undoes a failed clear newest-first. Throws when rollback itself fails; otherwise the caller rethrows
    // the original failure.
    private static void RollBackClear(
        Exception clearException,
        List<InputSnapshot> cleared,
        List<InputSnapshot> snapshots)
    {
        List<Exception>? rollbackFailures = null;
        for (int i = cleared.Count - 1; i >= 0; i--)
        {
            try
            {
                RestoreInputs(cleared[i]);
            }
            catch (Exception rollbackException)
            {
                (rollbackFailures ??= []).Add(rollbackException);
            }
        }

        for (int i = snapshots.Count - 1; i >= 0; i--)
        {
            try
            {
                snapshots[i].Node.RollbackInputTopologyTransaction();
            }
            catch (Exception rollbackException)
            {
                (rollbackFailures ??= []).Add(rollbackException);
            }
        }

        if (rollbackFailures is { Count: > 0 })
        {
            rollbackFailures.Insert(0, clearException);
            throw new AggregateException(
                "Audio connection clearing failed and rollback encountered one or more errors.",
                rollbackFailures);
        }
    }

    /// <summary>
    /// Removes a specific node from the context.
    /// </summary>
    /// <param name="node">The node to remove.</param>
    public void RemoveNode(AudioNode node)
    {
        ThrowIfDisposed();
        ThrowIfTopologyMutation();
        ArgumentNullException.ThrowIfNull(node, nameof(node));

        if (!ContainsReference(_nodes, node))
            return;

        _topologyMutationInProgress = true;
        try
        {
            RemoveNodeCore(node);
        }
        finally
        {
            _topologyMutationInProgress = false;
        }
    }

    private void RemoveNodeCore(AudioNode node)
    {
        // Remove dependent inputs first. The context bookkeeping is deliberately left untouched until
        // every node hook succeeds, so a failed hook leaves the graph available for a retry.
        var affected = _nodes
            .Where(otherNode => !ReferenceEquals(otherNode, node)
                && ContainsReference(otherNode.Inputs, node))
            .ToArray();
        foreach (AudioNode otherNode in affected)
        {
            otherNode.BeginInputTopologyTransaction();
        }

        var removedFrom = new List<(AudioNode Node, int Index, object? State)>();
        List<Exception>? commitFailures = null;
        try
        {
            foreach (AudioNode otherNode in affected)
            {
                int index = IndexOfReference(otherNode.Inputs, node);
                object? state = otherNode.CaptureInputStateForRollback(node, index);
                otherNode.RemoveInput(node);
                removedFrom.Add((otherNode, index, state));
            }

            foreach (AudioNode contextNode in _nodes)
            {
                contextNode.BeginInputTopologyCommitGuard();
            }

            foreach (AudioNode otherNode in affected)
            {
                otherNode.BeginInputTopologyCommit();
            }

            foreach (AudioNode otherNode in affected)
            {
                if (otherNode.CompleteInputTopologyCommit() is { } commitFailure)
                {
                    (commitFailures ??= []).Add(commitFailure);
                }
            }

            foreach (AudioNode otherNode in affected)
            {
                otherNode.EndInputTopologyCommit();
            }

            foreach (AudioNode contextNode in _nodes)
            {
                contextNode.EndInputTopologyCommitGuard();
            }
        }
        catch (Exception removalException)
        {
            RollBackRemoval(removalException, node, removedFrom, affected);
            throw;
        }

        _connections.Remove(node);
        foreach (var list in _connections.Values)
        {
            RemoveReference(list, node);
        }

        _outputNodes.Remove(node);
        if (ReferenceEquals(_currentNode, node))
            _currentNode = null;

        RemoveReference(_nodes, node);

        ThrowIfLifecycleHooksFailed(
            commitFailures,
            "Audio node removal committed, but one or more lifecycle hooks failed.");
    }

    // Undoes a failed removal newest-first and lifts the commit guard. Throws when rollback itself fails;
    // otherwise the caller rethrows the original failure.
    private void RollBackRemoval(
        Exception removalException,
        AudioNode node,
        List<(AudioNode Node, int Index, object? State)> removedFrom,
        AudioNode[] affected)
    {
        List<Exception>? rollbackFailures = null;
        for (int i = removedFrom.Count - 1; i >= 0; i--)
        {
            (AudioNode otherNode, int index, object? state) = removedFrom[i];
            try
            {
                otherNode.RestoreInput(node, index);
                otherNode.RestoreInputStateForRollback(node, index, state);
            }
            catch (Exception rollbackException)
            {
                (rollbackFailures ??= []).Add(rollbackException);
            }
        }

        for (int i = affected.Length - 1; i >= 0; i--)
        {
            try
            {
                affected[i].RollbackInputTopologyTransaction();
            }
            catch (Exception rollbackException)
            {
                (rollbackFailures ??= []).Add(rollbackException);
            }
        }

        foreach (AudioNode contextNode in _nodes)
        {
            contextNode.EndInputTopologyCommitGuard();
        }

        if (rollbackFailures is { Count: > 0 })
        {
            rollbackFailures.Insert(0, removalException);
            throw new AggregateException(
                "Audio node removal failed and rollback encountered one or more errors.",
                rollbackFailures);
        }
    }

    private static void ThrowIfLifecycleHooksFailed(List<Exception>? failures, string message)
    {
        if (failures is { Count: > 0 })
        {
            throw new AggregateException(message, failures);
        }
    }

    private static void RestoreInputs(InputSnapshot snapshot)
    {
        for (int i = snapshot.Node.Inputs.Count - 1; i >= 0; i--)
        {
            if (!ContainsReference(snapshot.Inputs, snapshot.Node.Inputs[i]))
                snapshot.Node.RemoveInput(snapshot.Node.Inputs[i]);
        }

        for (int i = 0; i < snapshot.Inputs.Length; i++)
        {
            AudioNode input = snapshot.Inputs[i];
            int currentIndex = IndexOfReference(snapshot.Node.Inputs, input);
            if (currentIndex < 0)
            {
                snapshot.Node.RestoreInput(input, i);
            }
            else
            {
                snapshot.Node.RestoreInputOrder(input, i);
            }

            snapshot.Node.RestoreInputStateForRollback(input, i, snapshot.States[i]);
        }
    }
}
