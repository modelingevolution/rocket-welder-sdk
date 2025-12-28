namespace RocketWelder.SDK.Types;

/// <summary>
/// Represents paths to a video recording's data and index files.
/// </summary>
public readonly record struct RecordingPath(string DataPath, string IndexPath)
{
    /// <summary>
    /// Gets the directory containing the recording files.
    /// </summary>
    public string Directory => Path.GetDirectoryName(DataPath)!;
}

/// <summary>
/// Interface for locating video recordings on the filesystem.
/// </summary>
public interface IVideoRecordingLocator
{
    /// <summary>
    /// Resolves the file naming convention used for the recording.
    /// </summary>
    VideoRecordingIdentifier.FileNamingConvention? Resolve(in VideoRecordingIdentifier recording);

    /// <summary>
    /// Checks if the recording exists on disk.
    /// </summary>
    bool Exists(in VideoRecordingIdentifier recording, VideoRecordingIdentifier.FileNamingConvention? convention = null);

    /// <summary>
    /// Checks if the frame exists on disk.
    /// </summary>
    bool Exists(in FrameId frameId);

    /// <summary>
    /// Gets the paths to the recording's data and index files.
    /// </summary>
    RecordingPath GetPath(in VideoRecordingIdentifier recording, VideoRecordingIdentifier.FileNamingConvention? convention = null, string? extension = null);

    /// <summary>
    /// Gets the full folder path for the recording.
    /// </summary>
    string GetFolderFullPath(in VideoRecordingIdentifier recording, VideoRecordingIdentifier.FileNamingConvention? convention = null);

    /// <summary>
    /// Enumerates all recordings.
    /// </summary>
    IEnumerable<VideoRecordingIdentifier> Recording();
}
