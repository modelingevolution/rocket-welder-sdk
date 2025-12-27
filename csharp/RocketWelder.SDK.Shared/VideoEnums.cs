namespace RocketWelder.SDK.Shared;

/// <summary>
/// Video transport protocol.
/// </summary>
[Flags]
public enum VideoTransport
{
    Tcp = 1,
    Udp = 2,
    Shm = 4
}

/// <summary>
/// Video source type.
/// </summary>
public enum VideoSource
{
    Camera,
    File,
    Stream
}

/// <summary>
/// Video codec type.
/// </summary>
public enum VideoCodec
{
    Mjpeg,
    H264
}

/// <summary>
/// Predefined video resolutions.
/// </summary>
public enum VideoResolution
{
    FullHd,
    SubHd,
    Hd,
    Xga,
    Svga
}

/// <summary>
/// Video source API (Linux-specific).
/// </summary>
public enum VideoSourceApi
{
    Libcamera,
    OpenVidCam
}
