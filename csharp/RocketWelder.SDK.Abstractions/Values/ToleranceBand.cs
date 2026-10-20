using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text.Json.Serialization;
using ModelingEvolution.JsonParsableConverter;

namespace RocketWelder.SDK.Abstractions.Values;

/// <summary>
/// A tolerance band of two limits in millimetres — <see cref="Accept"/> and <see cref="Review"/> — that turns a
/// measured deviation into a <see cref="Verdict"/> (epic-029 feature-011, design.md §2, requirements FR-1).
/// <para>
/// Invariant: <c>0 ≤ Accept ≤ Review</c>, each finite or <c>+∞</c>. <c>Accept == Review</c> is legal (no review zone).
/// </para>
/// <para>
/// Text form (invariant culture) is the type's internal format: canonical <c>"1.2:3.5"</c>, <c>"inf"</c> for
/// infinity. On input <c>"1.2:"</c> means <c>1.2:inf</c>; <c>inf</c> is case-insensitive; a trailing <c>" mm"</c> /
/// <c>" [mm]"</c> and a comma decimal (<c>"1,2:3,5"</c>) are accepted and never written. Every text
/// <see cref="ToString"/> writes parses back to an equal value, <c>default</c> (<c>"0:0"</c>) included.
/// </para>
/// <para>No implicit conversion from <see cref="string"/> (adr.md ADR-7); call <see cref="Parse"/>.</para>
/// </summary>
[JsonConverter(typeof(JsonParsableConverter<ToleranceBand>))]
public readonly record struct ToleranceBand : IParsable<ToleranceBand>
{
    private const string Infinity = "inf";

    /// <summary>Refusal: the accept limit is above the review limit.</summary>
    private const string OrderRefusal = "accept must not exceed review — write it as accept:review, e.g. 1.2:3.5.";

    /// <summary>Refusal: a limit is not a non-negative number or <c>inf</c> (negative, NaN, <c>-inf</c>, text).</summary>
    private const string LimitRefusal = "a limit is a non-negative number of millimetres or inf, e.g. 1.2:3.5.";

    /// <summary>Refusal: the text is not two limits separated by one colon.</summary>
    private const string ShapeRefusal = "write the band as accept:review in millimetres, e.g. 1.2:3.5 or 1.2:inf.";

    /// <summary><c>inf:inf</c> — every finite deviation is accepted; nothing is reviewed or blocked.</summary>
    public static readonly ToleranceBand Unbounded = new(double.PositiveInfinity, double.PositiveInfinity);

    // -0 is stored as 0 (x + 0.0), so equality, hashing and the written text never see a negative zero.
    private ToleranceBand(double accept, double review)
    {
        Accept = accept + 0.0;
        Review = review + 0.0;
    }

    /// <summary>Below this deviation (mm) the correction is accepted with no operator click. ≥ 0, finite or +∞.</summary>
    public double Accept { get; }

    /// <summary>Up to and including this deviation (mm) the operator reviews; above it the adaptation is blocked.
    /// ≥ <see cref="Accept"/>, finite or +∞.</summary>
    public double Review { get; }

    /// <summary>
    /// Creates a band from two limits, or yields the reason it is refused as one sentence.
    /// </summary>
    public static bool TryCreate(double accept, double review, out ToleranceBand band,
        [NotNullWhen(false)] out string? refusal)
    {
        band = default;
        if (!IsLimit(accept) || !IsLimit(review))
        {
            refusal = LimitRefusal;
            return false;
        }

        if (accept > review)
        {
            refusal = OrderRefusal;
            return false;
        }

        band = new ToleranceBand(accept, review);
        refusal = null;
        return true;
    }

    /// <summary>
    /// The one parser (design.md §2): parses a band, or yields the reason it is refused as one sentence fit to show
    /// to the caller (REST 400 body, a store warning).
    /// </summary>
    public static bool TryParse([NotNullWhen(true)] string? s, out ToleranceBand band,
        [NotNullWhen(false)] out string? refusal)
    {
        band = default;
        var text = StripUnit(s);
        var colon = text.IndexOf(':');
        if (colon <= 0 || colon != text.LastIndexOf(':'))
        {
            refusal = ShapeRefusal;
            return false;
        }

        var acceptText = text[..colon].Trim();          // non-empty: text is trimmed and colon > 0
        var reviewText = text[(colon + 1)..].Trim();
        if (!TryParseLimit(acceptText, out var accept))
        {
            refusal = LimitRefusal;
            return false;
        }

        var review = double.PositiveInfinity;           // "1.2:" = 1.2:inf
        if (reviewText.Length > 0 && !TryParseLimit(reviewText, out review))
        {
            refusal = LimitRefusal;
            return false;
        }

        return TryCreate(accept, review, out band, out refusal);
    }

    /// <inheritdoc cref="IParsable{TSelf}.TryParse"/>
    /// <remarks>The text format is culture-invariant; <paramref name="provider"/> is ignored.</remarks>
    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider, out ToleranceBand result) =>
        TryParse(s, out result, out _);

    /// <inheritdoc cref="IParsable{TSelf}.Parse"/>
    /// <exception cref="FormatException">The text is not a band; the message is the refusal sentence.</exception>
    public static ToleranceBand Parse(string s, IFormatProvider? provider = null) =>
        TryParse(s, out var result, out var refusal) ? result : throw new FormatException(refusal);

    /// <summary>
    /// Judges a measured deviation <paramref name="mm"/> (FR-1.3): NaN → <see cref="Verdict.Block"/>;
    /// <c>mm &lt; Accept</c> → <see cref="Verdict.Accept"/>; <c>mm ≤ Review</c> → <see cref="Verdict.Review"/>;
    /// otherwise <see cref="Verdict.Block"/>.
    /// </summary>
    public Verdict Judge(double mm) =>
        double.IsNaN(mm) ? Verdict.Block
        : mm < Accept ? Verdict.Accept
        : mm <= Review ? Verdict.Review
        : Verdict.Block;

    /// <summary>Canonical invariant text, e.g. <c>"1.2:3.5"</c>, <c>"10:10"</c>, <c>"1.2:inf"</c>, <c>"inf:inf"</c>.</summary>
    public override string ToString() => $"{Format(Accept)}:{Format(Review)}";

    /// <summary>Value equality over (<see cref="Accept"/>, <see cref="Review"/>); <c>-0</c> equals <c>0</c>.</summary>
    public bool Equals(ToleranceBand other) => Accept.Equals(other.Accept) && Review.Equals(other.Review);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Accept, Review);

    private static bool IsLimit(double mm) => mm >= 0;   // false for NaN and every negative, -inf included

    private static string StripUnit(string? s)
    {
        var text = (s ?? string.Empty).Trim();
        if (text.EndsWith("[mm]", StringComparison.OrdinalIgnoreCase)) text = text[..^4];
        else if (text.EndsWith("mm", StringComparison.OrdinalIgnoreCase)) text = text[..^2];
        return text.Trim();
    }

    private static bool TryParseLimit(string text, out double mm)
    {
        if (text.Equals(Infinity, StringComparison.OrdinalIgnoreCase))
        {
            mm = double.PositiveInfinity;
            return true;
        }

        // A comma decimal is accepted on input; the separator is ':' so a comma is never ambiguous. Only the token
        // "inf" spells infinity: "NaN", "Infinity" and overflow ("1e400") are refused here, the sign by TryCreate.
        return double.TryParse(text.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out mm)
               && double.IsFinite(mm);
    }

    private static string Format(double mm) =>
        double.IsPositiveInfinity(mm) ? Infinity : mm.ToString("R", CultureInfo.InvariantCulture);
}
