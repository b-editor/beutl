namespace Beutl.Engine;

/// <summary>
/// Represents an interface for an operator that processes objects within a flow.
/// This interface is implemented by EngineObjects that need to consume and process preceding objects,
/// such as DrawableGroup, DrawableDecorator, Scene3D, DrawableTimeController, and TakeAfterPortal.
/// </summary>
public interface IFlowOperator
{
    /// <summary>
    /// Processes objects within the flow. Also responsible for adding itself to the flow.
    /// </summary>
    void ProcessFlow(IList<EngineObject> flow, EvaluationTarget target, object? renderer);
    /// <summary>
    /// Called when the element enters the timeline.
    /// </summary>
    void EnterFlow();

    /// <summary>
    /// Called when the element exits the timeline.
    /// </summary>
    void ExitFlow();

    /// <summary>
    /// Clears temporary child elements before serialization.
    /// </summary>
    void OnSerializing();
}
