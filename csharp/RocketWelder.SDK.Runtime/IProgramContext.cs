using RocketWelder.SDK.Abstractions;
using Microsoft.Extensions.Logging;
using RocketWelder.SDK.Hmi;

namespace RocketWelder.SDK.Runtime;

/// <summary>
/// Context provided to programs during execution.
/// Provides access to ML data providers and registered devices.
/// </summary>
public interface IProgramContext
{
    /// <summary>
    /// Access to keypoint detection results from ML pipeline.
    /// Use <see cref="IDataProvider{T}.HasData(byte)"/> to check availability
    /// before calling <see cref="IDataProvider{T}.GetLatest"/>.
    /// </summary>
    IKeyPointsProvider Keypoints { get; }

    /// <summary>
    /// Access to segmentation results from ML pipeline.
    /// Use <see cref="IDataProvider{T}.HasData(byte)"/> to check availability
    /// before calling <see cref="IDataProvider{T}.GetLatest"/>.
    /// </summary>
    ISegmentationProvider Segmentation { get; }

    /// <summary>
    /// Logger for program output.
    /// </summary>
    ILogger Logger { get; }

    /// <summary>
    /// Access to the UI rendering sink for drawing overlays.
    /// Creates per-frame writers that auto-flush on dispose.
    /// The frame ID is automatically sourced from the segmentation stream.
    /// </summary>
    IUiSink Ui { get; }

    /// <summary>
    /// Store for program actions (steering commands, decisions).
    /// Actions are stored in EventStore with automatic frame correlation —
    /// the frame ID is captured from the last retrieved keypoints/segmentation frame.
    /// </summary>
    IActionsStore Actions { get; }

    /// <summary>
    /// True when running in test/dry-run mode.
    /// Programs should log intended actions instead of executing them.
    /// </summary>
    bool IsDryRun { get; }

    /// <summary>
    /// Identifier of the current program execution (one program runs at a time per program;
    /// preserved across pause/stop within the same run, freshly minted on a new run).
    /// Used by the runtime to scope <see cref="Lifetime.Run"/> data; programs may also use it
    /// for diagnostics / log correlation.
    /// </summary>
    /// <remarks>
    /// Default interface implementation returns <c>default(RunId)</c> so that implementations
    /// authored before 1.11.2 (when this member was introduced) remain source-compatible.
    /// Hosts that support per-run scoping must override this.
    /// </remarks>
    RunId RunId => default;

    /// <summary>
    /// Absolute path to the running program's self-contained repository directory, or <c>null</c> when the
    /// program is not repository-backed. A repository-backed program persists its points, adaptive offsets,
    /// captured frames, and capture-poses under this directory, so the runtime can resolve adaptation data
    /// from the repository instead of the robot-global event-store catalogue. Copying the directory yields a
    /// complete, runnable program.
    /// </summary>
    /// <remarks>
    /// Default interface implementation returns <c>null</c> so that implementations authored before this
    /// member was introduced remain source-compatible. Hosts that run repository-backed programs override
    /// this to expose the program's repository directory; legacy catalogue-backed programs leave it
    /// <c>null</c>.
    /// </remarks>
    string? ProgramDirectory => null;

    /// <summary>
    /// Gets a registered device by type and optional name.
    /// Returns null if device not found.
    /// </summary>
    /// <typeparam name="T">The device interface type (e.g., IRobot, IWeldingMachine).</typeparam>
    /// <param name="name">Optional name to distinguish multiple devices of the same type.</param>
    T? GetDevice<T>(string? name = null) where T : class;

    /// <summary>
    /// Gets a registered device by type and optional name.
    /// Throws if device not found.
    /// </summary>
    /// <typeparam name="T">The device interface type (e.g., IRobot, IWeldingMachine).</typeparam>
    /// <param name="name">Optional name to distinguish multiple devices of the same type.</param>
    /// <exception cref="InvalidOperationException">Thrown when device is not registered.</exception>
    T GetRequiredDevice<T>(string? name = null) where T : class;

    /// <summary>
    /// Gets a registered device by type and device number.
    /// Returns null if device not found.
    /// </summary>
    /// <typeparam name="T">The device interface type (e.g., IRobot, IWeldingMachine).</typeparam>
    /// <param name="id">The device number (auto-assigned during creation).</param>
    T? GetById<T>(uint id) where T : class;

    /// <summary>
    /// Reads a value previously stored via <see cref="SetData{T}"/>.
    /// Returns <c>default</c> if the key is absent in the addressed scope, or on type mismatch.
    /// </summary>
    /// <typeparam name="T">The data type.</typeparam>
    /// <param name="key">The data key.</param>
    /// <param name="lifetime">Run-scoped (cleared on new run) vs Permanent (survives runs).</param>
    /// <param name="share">Program-private vs Global (visible to all programs).</param>
    /// <remarks>
    /// Default interface implementation returns <c>default</c> so that implementations
    /// authored before 1.11.2 (when this signature was introduced) remain source-compatible.
    /// Hosts must override to provide actual data storage.
    /// </remarks>
    T? GetData<T>(string key,
                  Lifetime lifetime = Lifetime.Permanent,
                  ShareMode share = ShareMode.Global)
        => default;

    /// <summary>
    /// Stores a value. Every cell of the <see cref="Lifetime"/> × <see cref="ShareMode"/> matrix
    /// is durable (EventStore-backed). <see cref="Lifetime.Run"/> writes go to a per-run stream
    /// and are cleared when a new run starts; <see cref="Lifetime.Permanent"/> writes survive
    /// across runs.
    /// </summary>
    /// <typeparam name="T">The data type.</typeparam>
    /// <param name="key">The data key.</param>
    /// <param name="value">The value to store.</param>
    /// <param name="lifetime">Run-scoped (cleared on new run) vs Permanent (survives runs).</param>
    /// <param name="share">Program-private vs Global (visible to all programs).</param>
    /// <returns>True if the value was stored successfully.</returns>
    /// <remarks>
    /// Default interface implementation returns <c>false</c> so that implementations authored
    /// before 1.11.2 (when this signature was introduced) remain source-compatible.
    /// Hosts must override to provide actual data storage.
    /// </remarks>
    bool SetData<T>(string key, T value,
                    Lifetime lifetime = Lifetime.Permanent,
                    ShareMode share = ShareMode.Global)
        => false;
}
