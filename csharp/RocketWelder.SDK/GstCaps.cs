using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModelingEvolution.JsonParsableConverter;

namespace RocketWelder.SDK
{
    /// <summary>
    /// Represents GStreamer caps for video format information
    /// </summary>
    [JsonConverter(typeof(JsonParsableConverter<GstCaps>))]
    public readonly record struct GstCaps(
        int Width,
        int Height,
        string Format,
        DepthType Depth,
        int Channels,
        int BytesPerPixel,
        int? FramerateNum = null,
        int? FramerateDen = null,
        string? Interlace = null,
        string? Colorimetry = null,
        string? CapsString = null
    ) : IParsable<GstCaps>
    {
        private static readonly ILogger<GstCaps> _logger = NullLogger<GstCaps>.Instance;
        
        /// <summary>
        /// Calculate the expected frame size in bytes
        /// </summary>
        public int FrameSize => Width * Height * BytesPerPixel;
        
        /// <summary>
        /// Get framerate as double (FPS)
        /// </summary>
        public double? FrameRate => 
            (FramerateNum.HasValue && FramerateDen.HasValue && FramerateDen.Value > 0) 
                ? (double)FramerateNum.Value / FramerateDen.Value 
                : null;
        
        /// <summary>
        /// Parse video format from GStreamer caps string (internal helper)
        /// Example: "video/x-raw, format=(string)RGB, width=(int)640, height=(int)480, framerate=(fraction)30/1"
        /// </summary>
        private static GstCaps? ParseCapsString(string caps)
        {
            if (string.IsNullOrWhiteSpace(caps))
                return null;
            
            try
            {
                // Check if it's a video caps
                if (!caps.StartsWith("video/x-raw"))
                    return null;
                
                // Parse width
                var widthMatch = Regex.Match(caps, @"width=\(int\)(\d+)");
                if (!widthMatch.Success)
                    return null;
                int width = int.Parse(widthMatch.Groups[1].Value);
                
                // Parse height
                var heightMatch = Regex.Match(caps, @"height=\(int\)(\d+)");
                if (!heightMatch.Success)
                    return null;
                int height = int.Parse(heightMatch.Groups[1].Value);
                
                // Parse format
                var formatMatch = Regex.Match(caps, @"format=\(string\)(\w+)");
                string format = formatMatch.Success ? formatMatch.Groups[1].Value : "RGB";
                
                // Parse framerate (optional)
                int? framerateNum = null, framerateDen = null;
                var framerateMatch = Regex.Match(caps, @"framerate=\(fraction\)(\d+)/(\d+)");
                if (framerateMatch.Success)
                {
                    framerateNum = int.Parse(framerateMatch.Groups[1].Value);
                    framerateDen = int.Parse(framerateMatch.Groups[2].Value);
                }
                
                // Parse interlace mode (optional)
                var interlaceMatch = Regex.Match(caps, @"interlace-mode=\(string\)(\w+)");
                string? interlace = interlaceMatch.Success ? interlaceMatch.Groups[1].Value : null;
                
                // Parse colorimetry (optional)
                var colorimetryMatch = Regex.Match(caps, @"colorimetry=\(string\)([\w:]+)");
                string? colorimetry = colorimetryMatch.Success ? colorimetryMatch.Groups[1].Value : null;
                
                // Map format to OpenCV MatType
                var (matType, channels, bytesPerPixel) = MapGStreamerFormatToEmgu(format);
                
                return new GstCaps(
                    width, 
                    height, 
                    format, 
                    matType, 
                    channels, 
                    bytesPerPixel,
                    framerateNum,
                    framerateDen,
                    interlace,
                    colorimetry,
                    caps);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse caps: {Caps}", caps);
                return null;
            }
        }
        
        /// <summary>
        /// Create GstCaps from simple parameters
        /// </summary>
        public static GstCaps FromSimple(int width, int height, string format = "RGB")
        {
            var (matType, channels, bytesPerPixel) = MapGStreamerFormatToEmgu(format);
            return new GstCaps(width, height, format, matType, channels, bytesPerPixel);
        }
        
        /// <summary>
        /// Map GStreamer format strings to Emgu CV DepthType
        /// Reference: https://gstreamer.freedesktop.org/documentation/video/video-format.html
        /// </summary>
        private static (DepthType depth, int channels, int bytesPerPixel) MapGStreamerFormatToEmgu(string format)
        {
            return format?.ToUpperInvariant() switch
            {
                // RGB formats
                "RGB" => (DepthType.Cv8U, 3, 3),
                "BGR" => (DepthType.Cv8U, 3, 3),
                "RGBA" => (DepthType.Cv8U, 4, 4),
                "BGRA" => (DepthType.Cv8U, 4, 4),
                "ARGB" => (DepthType.Cv8U, 4, 4),
                "ABGR" => (DepthType.Cv8U, 4, 4),
                "RGBx" => (DepthType.Cv8U, 4, 4),  // RGB with padding
                "BGRx" => (DepthType.Cv8U, 4, 4),  // BGR with padding
                "xRGB" => (DepthType.Cv8U, 4, 4),  // RGB with padding
                "xBGR" => (DepthType.Cv8U, 4, 4),  // BGR with padding
                
                // 16-bit RGB formats
                "RGB16" => (DepthType.Cv16U, 3, 6),
                "BGR16" => (DepthType.Cv16U, 3, 6),
                
                // Grayscale formats
                "GRAY8" => (DepthType.Cv8U, 1, 1),
                "GRAY16_LE" => (DepthType.Cv16U, 1, 2),
                "GRAY16_BE" => (DepthType.Cv16U, 1, 2),
                
                // YUV planar formats (we'll treat as 8-bit single channel for simplicity)
                "I420" => (DepthType.Cv8U, 1, 1),  // Y plane only for now
                "YV12" => (DepthType.Cv8U, 1, 1),  // Y plane only for now
                "NV12" => (DepthType.Cv8U, 1, 1),  // Y plane only for now
                "NV21" => (DepthType.Cv8U, 1, 1),  // Y plane only for now
                
                // YUV packed formats
                "YUY2" => (DepthType.Cv8U, 2, 2),  // YUYV packed
                "UYVY" => (DepthType.Cv8U, 2, 2),  // UYVY packed
                "YVYU" => (DepthType.Cv8U, 2, 2),  // YVYU packed
                
                // Bayer formats (raw sensor data)
                "BGGR" => (DepthType.Cv8U, 1, 1),
                "RGGB" => (DepthType.Cv8U, 1, 1),
                "GRBG" => (DepthType.Cv8U, 1, 1),
                "GBRG" => (DepthType.Cv8U, 1, 1),
                
                // Default to RGB if unknown
                _ => (DepthType.Cv8U, 3, 3)
            };
        }
        
        /// <summary>
        /// Create Emgu CV Mat with proper format
        /// </summary>
        public unsafe Mat CreateMat(ReadOnlySpan<byte> data)
        {
            if (data.Length != FrameSize)
            {
                throw new ArgumentException(
                    $"Data size mismatch. Expected {FrameSize} bytes for {Width}x{Height} {Format}, got {data.Length}");
            }
            
            // Pin the span and create Mat with zero-copy
            fixed (byte* ptr = data)
            {
                // Create Mat wrapping the existing data
                var mat = new Mat(Height, Width, Depth, Channels, (IntPtr)ptr, Width * Channels);
                return mat;
            }
        }
        
        /// <summary>
        /// Create Emgu CV Mat wrapping existing pointer (zero-copy)
        /// </summary>
        public unsafe Mat CreateMat(byte* dataPtr)
        {
            // Create Mat wrapping the existing data pointer
            return new Mat(Height, Width, Depth, Channels, (IntPtr)dataPtr, Width * Channels);
        }
        
        public override string ToString()
        {
            // If we have the original caps string, return it for perfect round-tripping
            if (!string.IsNullOrEmpty(CapsString))
                return CapsString;
            
            // Otherwise build a simple display string
            var fps = FrameRate.HasValue ? $" @ {FrameRate:F2}fps" : "";
            return $"{Width}x{Height} {Format}{fps}";
        }
        
        // IParsable<VideoFormat> implementation
        public static GstCaps Parse(string s, IFormatProvider? provider)
        {
            if (!TryParse(s, provider, out var result))
                throw new FormatException($"Invalid video format string: {s}");
            return result;
        }
        
        public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, [MaybeNullWhen(false)] out GstCaps result)
        {
            result = default;
            
            if (string.IsNullOrWhiteSpace(s))
                return false;
            
            // Try to parse as caps string first
            if (s.StartsWith("video/x-raw"))
            {
                var parsed = ParseCapsString(s);
                if (parsed.HasValue)
                {
                    result = parsed.Value;
                    return true;
                }
            }
            
            // Try simple format: "640x480 RGB" or "640x480 RGB @ 30.00fps"
            var match = Regex.Match(s, @"^(\d+)x(\d+)\s+(\w+)(?:\s+@\s+([\d.]+)fps)?$");
            if (!match.Success) return false;

            int width = int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
            int height = int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture);
            string format = match.Groups[3].Value;
                
            result = FromSimple(width, height, format);
            return true;

        }
    }
}