using System.Buffers;
using Emgu.CV;
using Emgu.CV.CvEnum;
using ModelingEvolution.Mjpeg;

namespace RocketWelder.SDK.Automation;

/// <summary>
/// Zero-copy RAII wrapper: pins pooled <see cref="FrameImage"/> memory and wraps it in a <see cref="Mat"/>.
/// Dispose releases the Mat, unpins memory, and returns the buffer to <see cref="MemoryPool{T}"/>.
/// </summary>
public readonly struct MatFrame : IDisposable
{
    private readonly FrameImage _frame;
    private readonly MemoryHandle _pin;

    /// <summary>
    /// The OpenCV Mat backed by the pinned pooled memory. Do NOT dispose this directly;
    /// dispose the <see cref="MatFrame"/> instead.
    /// </summary>
    public Mat Mat { get; }

    /// <summary>Frame width in pixels.</summary>
    public int Width => _frame.Header.Width;

    /// <summary>Frame height in pixels.</summary>
    public int Height => _frame.Header.Height;

    /// <summary>
    /// Creates a MatFrame by pinning the FrameImage's pooled buffer and wrapping it in a Mat.
    /// Takes ownership of the FrameImage — do NOT dispose the FrameImage separately.
    /// </summary>
    public MatFrame(FrameImage frame)
    {
        _frame = frame;
        _pin = frame.GetWritableData().Pin();
        var h = frame.Header;
        unsafe
        {
            Mat = new Mat(h.Height, h.Width, DepthType.Cv8U, 1,
                         (IntPtr)_pin.Pointer, h.Stride);
        }
    }

    /// <summary>
    /// Disposes the Mat, unpins memory, and returns the buffer to the pool.
    /// </summary>
    public void Dispose()
    {
        Mat.Dispose();
        _pin.Dispose();
        _frame.Dispose();
    }
}
