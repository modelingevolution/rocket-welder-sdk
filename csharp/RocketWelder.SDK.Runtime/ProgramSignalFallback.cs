using System.Runtime.CompilerServices;
using ModelingEvolution.Drawing;
using ModelingEvolution.Signals;

namespace RocketWelder.SDK.Runtime;

/// <summary>
/// The <see cref="IProgramContext.Signal"/> default: what a program gets on a host that predates Epic 091
/// (or a host that chose not to publish program signals). The channel is a real, working
/// <see cref="WritableSignal{T}"/> — <c>Set</c>, <c>Value</c>, <c>HasValue</c>, <c>Subscribe</c> all behave —
/// it is simply not registered anywhere, so writes latch and go nowhere.
///
/// <para>
/// A default interface method holds no state, yet FR-2 requires that repeated declarations of the same
/// name return the same instance (a program may declare inside its loop). The channels are therefore
/// memoised per context instance here, keyed weakly so a finished run's context — and its channels —
/// can be collected with it.
/// </para>
/// </summary>
internal static class ProgramSignalFallback
{
    private static readonly ConditionalWeakTable<IProgramContext, Dictionary<string, WritableSignal<float>>> Channels = new();

    public static WritableSignal<float> GetOrCreate(IProgramContext ctx, string name, string? unit, Frequency<float>? cadence)
    {
        ProgramSignalName.Validate(name);
        var channels = Channels.GetValue(ctx, _ => new Dictionary<string, WritableSignal<float>>(StringComparer.Ordinal));
        lock (channels)
        {
            if (channels.TryGetValue(name, out var existing))
                return existing;

            // "local" is the authority of a channel that no host owns. It is NOT a catalog key: a host that
            // publishes program signals overrides Signal() and mints program://{programId}/{name} instead.
            var signal = new WritableSignal<float>(new SignalMetadata(name, new Uri($"program://local/{name}"), unit, cadence));
            channels[name] = signal;
            return signal;
        }
    }
}
