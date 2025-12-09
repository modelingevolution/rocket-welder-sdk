using System;
using System.Runtime.InteropServices;

namespace RocketWelder.SDK
{
    /// <summary>
    /// Frame metadata prepended to each frame in zerobuffer shared memory.
    /// This structure is 24 bytes, 8-byte aligned.
    ///
    /// Layout:
    ///   [0-7]   frame_number    - Sequential frame index (0-based)
    ///   [8-15]  timestamp_ns    - GStreamer PTS in nanoseconds (UInt64.MaxValue if unavailable)
    ///   [16-17] width           - Frame width in pixels
    ///   [18-19] height          - Frame height in pixels
    ///   [20-21] format          - Pixel format (GstVideoFormat enum value)
    ///   [22-23] reserved        - Alignment padding (must be 0)
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
        /// Sequential frame index (0-based, increments per frame).
        /// </summary>
        public readonly ulong FrameNumber;

        /// <summary>
        /// GStreamer PTS in nanoseconds.
        /// UInt64.MaxValue indicates timestamp is unavailable.
        /// </summary>
        public readonly ulong TimestampNs;

        /// <summary>
        /// Frame width in pixels.
        /// </summary>
        public readonly ushort Width;

        /// <summary>
        /// Frame height in pixels.
        /// </summary>
        public readonly ushort Height;

        /// <summary>
        /// Pixel format (GstVideoFormat enum value).
        /// Common values: 15=RGB, 16=BGR, 11=RGBA, 12=BGRA, 2=I420, 23=NV12, 25=GRAY8
        /// </summary>
        public readonly ushort Format;

        /// <summary>
        /// Reserved for future use (must be 0).
        /// </summary>
        public readonly ushort Reserved;

        /// <summary>
        /// Creates a new FrameMetadata instance.
        /// </summary>
        public FrameMetadata(ulong frameNumber, ulong timestampNs, ushort width, ushort height, ushort format)
        {
            FrameNumber = frameNumber;
            TimestampNs = timestampNs;
            Width = width;
            Height = height;
            Format = format;
            Reserved = 0;
        }

        /// <summary>
        /// Gets whether the timestamp is available.
        /// </summary>
        public bool HasTimestamp => TimestampNs != TimestampUnavailable;

        /// <summary>
        /// Gets the timestamp as a TimeSpan, or null if unavailable.
        /// </summary>
        public TimeSpan? Timestamp => HasTimestamp
            ? TimeSpan.FromTicks((long)(TimestampNs / 100)) // 1 tick = 100 ns
            : null;

        /// <summary>
        /// Gets the format as a GstVideoFormat name.
        /// </summary>
        public string FormatName => Format switch
        {
            0 => "UNKNOWN",
            2 => "I420",
            11 => "RGBA",
            12 => "BGRA",
            13 => "ARGB",
            14 => "ABGR",
            15 => "RGB",
            16 => "BGR",
            23 => "NV12",
            25 => "GRAY8",
            _ => $"FORMAT_{Format}"
        };

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
            return $"Frame {FrameNumber}: {Width}x{Height} {FormatName} @ {timestamp}";
        }
    }
}
