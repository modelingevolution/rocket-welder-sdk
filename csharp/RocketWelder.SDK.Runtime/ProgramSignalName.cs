using System.Text.RegularExpressions;

namespace RocketWelder.SDK.Runtime;

/// <summary>
/// The one rule for a program signal channel name (Epic 091 FR-4). A name becomes the last path segment
/// of the channel's catalog URI (<c>program://{programId}/{name}</c>), and that URI is reconstructed from
/// the WebSocket route path on the host — so the name must survive a URL round trip <b>unescaped</b>.
/// Escaping is not enough: the route percent-decodes the captured path and <c>System.Uri</c> re-escapes
/// none of the sub-delimiters, so an escaped <c>&amp;</c>/<c>+</c>/<c>=</c>/<c>:</c> would come back as a
/// different key than the one in the catalog. Restricting the name to the URI unreserved set makes the
/// round trip correct by construction.
/// </summary>
public static partial class ProgramSignalName
{
    /// <summary>Longest accepted name.</summary>
    public const int MaxLength = 64;

    // \z, not $: in .NET `$` also matches before a trailing newline, so "gap\n" would pass, build the same
    // Uri as "gap" (System.Uri trims it) and silently replace that channel's catalog row.
    [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._~-]{0,63}\z")]
    private static partial Regex Pattern();

    /// <summary>
    /// True when <paramref name="name"/> is 1–64 characters of letters, digits, <c>-</c>, <c>.</c>, <c>_</c>
    /// or <c>~</c>, starts with a letter or digit, and is neither <c>.</c> nor <c>..</c>.
    /// </summary>
    public static bool IsValid(string? name)
        => name is not null && name is not "." and not ".." && Pattern().IsMatch(name);

    /// <summary>
    /// Throws <see cref="ArgumentException"/> naming the offending characters when the name is not valid.
    /// The message is what the program author sees at the declaration call site.
    /// </summary>
    public static void Validate(string? name, string paramName = "name")
    {
        if (IsValid(name)) return;
        if (string.IsNullOrEmpty(name))
            throw new ArgumentException("A signal channel name must not be empty.", paramName);
        if (name.Length > MaxLength)
            throw new ArgumentException(
                $"Signal channel name '{name}' is {name.Length} characters; the limit is {MaxLength}.", paramName);
        if (name is "." or "..")
            throw new ArgumentException($"'{name}' is not a usable signal channel name.", paramName);

        var offending = name
            .Where(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '.' or '_' or '~'))
            .Distinct()
            .Select(c => $"'{c}'");
        var reason = !char.IsAsciiLetterOrDigit(name[0])
            ? "it must start with a letter or digit"
            : $"it contains {string.Join(", ", offending)}";
        throw new ArgumentException(
            $"Signal channel name '{name}' cannot be addressed over the signal WebSocket: {reason}. " +
            "Use only letters, digits, '-', '.', '_' or '~'.", paramName);
    }
}
