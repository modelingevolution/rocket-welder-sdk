using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("RocketWelder.SDK.Tests")]
[assembly: InternalsVisibleTo("ModelingEvolution.RocketWelder.Tests")]
[assembly: InternalsVisibleTo("RocketWelder.SDK.Automation")]
// TEMPORARY: removed in IT-3e (video sever). KeyPointsProvider (moved to SDK.Runtime)
// calls the internal KeyPointsFrameHandle ctor until the handles move into SDK.Vision.
[assembly: InternalsVisibleTo("RocketWelder.SDK.Runtime")]