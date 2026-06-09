namespace RocketWelder.SDK.Runtime;

/// <summary>
/// Stores program actions (decisions, commands) in EventStore
/// with automatic frame correlation via metadata.
/// The frame ID is implicitly captured from the last retrieved
/// keypoints/segmentation frame and merged into event metadata.
/// </summary>
public interface IActionsStore
{
    /// <summary>
    /// Stores an action command. The frame ID is automatically
    /// captured from the last retrieved keypoints/segmentation frame
    /// and stored as event metadata for frame-by-frame comparison.
    /// </summary>
    /// <typeparam name="TCommand">The action/command type. Must be JSON-serializable.</typeparam>
    Task StoreAsync<TCommand>(TCommand cmd);
}
