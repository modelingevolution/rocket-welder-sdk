using System;
using System.Runtime.InteropServices;

namespace RocketWelder.SDK
{
    /// <summary>
    /// Frame metadata prepended to each frame in zerobuffer shared memory.
    /// This structure is 24 bytes, 8-byte aligned.
    ///
    /// Layout:
    ///   [0-7]   frame_number      - Sequential frame index (0-based)
    ///   [8-15]  timestamp_ns      - GStreamer PTS in nanoseconds (UInt64.MaxValue if unavailable)
    ///   [16-23] exposure_time_us  - Exposure time in microseconds (UInt64.MaxValue if unavailable)
    ///
    /// Note: Width, height, and format are NOT included here because they are
    /// stream-level properties that never change per-frame. They are stored once
    /// in the ZeroBuffer metadata section as GstCaps (via GstMetadata).
    /// This avoids redundant data and follows single-source-of-truth principle.
    /// </summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public readonly struct FrameMetadata
    {
        /// <summary>
        /// Size of the FrameMetadata structure in bytes.
        /// </summary>
        public const int Size = 24;

        /// <summary>
        /// Value indicating timestamp is unavailable.
        /// </summary>
        public const ulong TimestampUnavailable = ulong.MaxValue;

        /// <summary>
        /// Value indicating exposure time is unavailable.
        /// </summary>
        public const ulong ExposureTimeUnavailable = ulong.MaxValue;

        /// <summary>
        /// Sequential frame index (0-based, increments per frame).
        /// </summary>
        public readonly ulong FrameNumber;

        /// <summary>
        /// GStreamer PTS in nanoseconds.
        /// UInt64.MaxValue indicates timestamp is unavailable.
        /// </summary>
        public readonly ulong TimestampNs;

        /// <summary>
        /// Exposure time in microseconds.
        /// UInt64.MaxValue indicates exposure time is unavailable.
        /// </summary>
        public readonly ulong ExposureTimeUs;

        /// <summary>
        /// Creates a new FrameMetadata instance with exposure time.
        /// </summary>
        public FrameMetadata(ulong frameNumber, ulong timestampNs, ulong exposureTimeUs)
        {
            FrameNumber = frameNumber;
            TimestampNs = timestampNs;
            ExposureTimeUs = exposureTimeUs;
        }

        /// <summary>
        /// Creates a new FrameMetadata instance without exposure time (defaults to unavailable).
        /// </summary>
        public FrameMetadata(ulong frameNumber, ulong timestampNs)
        {
            FrameNumber = frameNumber;
            TimestampNs = timestampNs;
            ExposureTimeUs = ExposureTimeUnavailable;
        }

        /// <summary>
        /// Gets whether the timestamp is available.
        /// </summary>
        public bool HasTimestamp => TimestampNs != TimestampUnavailable;

        /// <summary>
        /// Gets whether the exposure time is available.
        /// </summary>
        public bool HasExposureTime => ExposureTimeUs != ExposureTimeUnavailable;

        /// <summary>
        /// Gets the timestamp as a TimeSpan, or null if unavailable.
        /// </summary>
        public TimeSpan? Timestamp => HasTimestamp
            ? TimeSpan.FromTicks((long)(TimestampNs / 100)) // 1 tick = 100 ns
            : null;

        /// <summary>
        /// Gets the exposure time as a TimeSpan, or null if unavailable.
        /// 1 microsecond = 10 ticks.
        /// </summary>
        public TimeSpan? ExposureTime => HasExposureTime
            ? TimeSpan.FromTicks((long)(ExposureTimeUs * 10))
            : null;

        /// <summary>
        /// Reads FrameMetadata from a pointer.
        /// </summary>
        public static unsafe FrameMetadata FromPointer(IntPtr ptr)
        {
            return *(FrameMetadata*)ptr.ToPointer();
        }

        /// <summary>
        /// Reads FrameMetadata from a span of bytes.
        /// </summary>
        public static FrameMetadata FromSpan(ReadOnlySpan<byte> span)
        {
            if (span.Length < Size)
                throw new ArgumentException($"Span must be at least {Size} bytes", nameof(span));

            return MemoryMarshal.Read<FrameMetadata>(span);
        }

        /// <summary>
        /// Returns a string representation of the metadata.
        /// </summary>
        public override string ToString()
        {
            var timestamp = HasTimestamp
                ? $"{TimestampNs / 1_000_000.0:F3}ms"
                : "N/A";
            var exposure = HasExposureTime
                ? $"{ExposureTimeUs}us"
                : "N/A";
            return $"Frame {FrameNumber} @ {timestamp} exp={exposure}";
        }
    }
}
