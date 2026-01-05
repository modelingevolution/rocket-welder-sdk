using System;

namespace RocketWelder.SDK.Protocols;

/// <summary>
/// Generic container for delta-encoded streaming data.
/// Used by Reader/Source classes where IsDelta is needed for decoding.
/// </summary>
/// <typeparam name="T">The item type (e.g., KeyPoint, SegmentationInstance)</typeparam>
/// <param name="FrameId">The frame identifier.</param>
/// <param name="IsDelta">True if this frame contains delta-encoded values relative to previous frame.</param>
/// <param name="Items">The items in this frame. Caller owns the backing memory.</param>
public readonly record struct DeltaFrame<T>(ulong FrameId, bool IsDelta, ReadOnlyMemory<T> Items)
    where T : struct;
