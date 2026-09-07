namespace RocketWelder.SDK.Http.Programs;

/// <summary>
/// Wire shape of an edit that returns only the tree's new etag
/// (<c>{ "etag": "sha256:..." }</c>) — remove and move. Unwrapped to
/// <see cref="ProgramEtag"/> by the SDK methods.
/// </summary>
internal sealed record EtagResponse(ProgramEtag Etag);
