namespace RocketWelder.SDK.Localization;

/// <summary>
/// A single-channel (grayscale) image abstraction with the bilinear sampling a
/// sub-pixel normal search needs. This is the SDK-level contract that replaces the
/// concrete, OpenCV-backed image carried by a <see cref="MetrologyView"/> in the
/// consumer: the localization contracts depend only on this interface (Vision +
/// Drawing), so no imaging back-end leaks into this package. The concrete
/// decode/convert/sample implementation lives with the consumer that produces the
/// frames (the consumer's gray-image type implements this surface).
/// </summary>
public interface IGrayImage
{
    /// <summary>Image width in pixels.</summary>
    int Width { get; }

    /// <summary>Image height in pixels.</summary>
    int Height { get; }

    /// <summary>Grayscale intensity at integer pixel (x = column, y = row).</summary>
    double this[int x, int y] { get; }

    /// <summary>Bilinear sample at sub-pixel (u, v) with edge replication
    /// (cv2.BORDER_REPLICATE). u = column, v = row.</summary>
    double SampleBilinear(double u, double v);
}
