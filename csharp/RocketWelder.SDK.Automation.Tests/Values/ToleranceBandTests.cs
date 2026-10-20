using System.Globalization;
using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using RocketWelder.SDK.Abstractions.Values;

namespace RocketWelder.SDK.Automation.Tests.Values;

/// <summary>epic-029 feature-011 design.md §2 and §9 — <c>ToleranceBandTests</c> (FR-1).</summary>
public class ToleranceBandTests
{
    private const double Inf = double.PositiveInfinity;

    private const string OrderRefusal = "accept must not exceed review — write it as accept:review, e.g. 1.2:3.5.";
    private const string LimitRefusal = "a limit is a non-negative number of millimetres or inf, e.g. 1.2:3.5.";
    private const string ShapeRefusal = "write the band as accept:review in millimetres, e.g. 1.2:3.5 or 1.2:inf.";

    private static ToleranceBand Band(double accept, double review)
    {
        ToleranceBand.TryCreate(accept, review, out var band, out var refusal).Should().BeTrue(refusal);
        return band;
    }

    // ---- §2 table: accepted input forms ----

    [Theory]
    [InlineData("1.2:3.5")]
    [InlineData(" 1.2 : 3.5 ")]
    [InlineData("1.2:3.5 mm")]
    [InlineData("1.2:3.5 [mm]")]
    [InlineData("1,2:3,5")]
    public void Parse_EquivalentSpellings_YieldTheCanonicalBand(string text)
    {
        var band = ToleranceBand.Parse(text);

        band.Accept.Should().Be(1.2);
        band.Review.Should().Be(3.5);
        band.ToString().Should().Be("1.2:3.5");
    }

    [Fact]
    public void Parse_EmptyReview_MeansInfinity()
    {
        var band = ToleranceBand.Parse("1.2:");

        band.Should().Be(Band(1.2, Inf));
        band.ToString().Should().Be("1.2:inf");
    }

    [Theory]
    [InlineData("inf:inf", Inf, Inf, "inf:inf")]
    [InlineData("INF:Inf", Inf, Inf, "inf:inf")]
    [InlineData("10:10", 10, 10, "10:10")]
    [InlineData("0:0", 0, 0, "0:0")]
    public void Parse_AsWritten_WithCanonicalLowercaseInf(string text, double accept, double review, string canonical)
    {
        var band = ToleranceBand.Parse(text);

        band.Accept.Should().Be(accept);
        band.Review.Should().Be(review);
        band.ToString().Should().Be(canonical);
    }

    [Fact]
    public void Parse_InfInf_IsUnbounded()
    {
        ToleranceBand.Parse("inf:inf").Should().Be(ToleranceBand.Unbounded);
        ToleranceBand.Unbounded.Accept.Should().Be(Inf);
        ToleranceBand.Unbounded.Review.Should().Be(Inf);
    }

    // ---- §2 table: refusals, the three exact sentences ----

    [Fact]
    public void TryParse_AcceptAboveReview_RefusedWithTheOrderSentence()
    {
        ToleranceBand.TryParse("3.5:1.2", out var band, out var refusal).Should().BeFalse();

        band.Should().Be(default(ToleranceBand));
        refusal.Should().Be(OrderRefusal);
    }

    [Theory]
    [InlineData("-1:2")]
    [InlineData("a:2")]
    [InlineData("NaN:2")]
    [InlineData("1:-inf")]
    [InlineData("Infinity:inf")]   // only the token "inf" spells infinity
    [InlineData("1:Infinity")]
    [InlineData("1e400:2")]        // overflow to +inf is refused, not read as inf
    public void TryParse_BadLimit_RefusedWithTheLimitSentence(string text)
    {
        ToleranceBand.TryParse(text, out var band, out var refusal).Should().BeFalse();

        band.Should().Be(default(ToleranceBand));
        refusal.Should().Be(LimitRefusal);
    }

    [Theory]
    [InlineData(":3.5")]
    [InlineData("1.2")]
    [InlineData("1:2:3")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_MalformedShape_RefusedWithTheShapeSentence(string? text)
    {
        ToleranceBand.TryParse(text, out var band, out var refusal).Should().BeFalse();

        band.Should().Be(default(ToleranceBand));
        refusal.Should().Be(ShapeRefusal);
    }

    [Fact]
    public void Parse_Refused_ThrowsFormatExceptionCarryingTheRefusal()
    {
        var act = () => ToleranceBand.Parse("3.5:1.2");

        act.Should().Throw<FormatException>().Which.Message.Should().Be(OrderRefusal);
    }

    [Fact]
    public void IParsableTryParse_AgreesWithTheOneParser()
    {
        ToleranceBand.TryParse("3.5:1.2", CultureInfo.InvariantCulture, out _).Should().BeFalse();
        ToleranceBand.TryParse("1.2:3.5", CultureInfo.InvariantCulture, out var ok).Should().BeTrue();
        ok.Should().Be(Band(1.2, 3.5));
    }

    [Theory]
    [InlineData(3.5, 1.2, OrderRefusal)]
    [InlineData(Inf, 10, OrderRefusal)]
    [InlineData(-1, 1, LimitRefusal)]
    [InlineData(double.NaN, 1, LimitRefusal)]
    [InlineData(1, double.NaN, LimitRefusal)]
    [InlineData(1, double.NegativeInfinity, LimitRefusal)]
    public void TryCreate_InvalidPair_Refused(double accept, double review, string expected)
    {
        ToleranceBand.TryCreate(accept, review, out var band, out var refusal).Should().BeFalse();

        band.Should().Be(default(ToleranceBand));
        refusal.Should().Be(expected);
    }

    // ---- round-trip (persisted-value-type rule: Parse is total over what ToString writes) ----

    [Theory]
    [InlineData(1.2, 3.5, "1.2:3.5")]
    [InlineData(1.2, Inf, "1.2:inf")]
    [InlineData(Inf, Inf, "inf:inf")]
    [InlineData(10, 10, "10:10")]
    [InlineData(0, 0, "0:0")]
    [InlineData(0.1, 0.30000000000000004, "0.1:0.30000000000000004")]
    public void ToString_Canonical_RoundTripsThroughParse(double accept, double review, string canonical)
    {
        var band = Band(accept, review);

        band.ToString().Should().Be(canonical);
        ToleranceBand.Parse(canonical).Should().Be(band);
    }

    [Fact]
    public void ToString_Default_IsZeroZeroAndParsesBack()
    {
        default(ToleranceBand).ToString().Should().Be("0:0");
        ToleranceBand.Parse(default(ToleranceBand).ToString()).Should().Be(default(ToleranceBand));
    }

    [Fact]
    public void ToString_UnderPolishCulture_StaysInvariant()
    {
        var saved = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
            Band(1.2, 3.5).ToString().Should().Be("1.2:3.5");
            ToleranceBand.Parse("1.2:3.5").Should().Be(Band(1.2, 3.5));
        }
        finally
        {
            CultureInfo.CurrentCulture = saved;
        }
    }

    // ---- -0 equals 0 (hand-written Equals + GetHashCode) ----

    [Fact]
    public void NegativeZero_EqualsZero_InEqualsAndHash()
    {
        var fromNegative = Band(-0.0, -0.0);
        var parsed = ToleranceBand.Parse("-0:0");

        fromNegative.Should().Be(default(ToleranceBand));
        (fromNegative == default).Should().BeTrue();
        fromNegative.GetHashCode().Should().Be(default(ToleranceBand).GetHashCode());
        parsed.Should().Be(default(ToleranceBand));
        parsed.GetHashCode().Should().Be(default(ToleranceBand).GetHashCode());
        fromNegative.ToString().Should().Be("0:0");
    }

    [Fact]
    public void Equals_DistinguishesDifferentLimits()
    {
        Band(1, 2).Should().NotBe(Band(1, 3));
        Band(1, 2).Should().NotBe(Band(0, 2));
        Band(1, 2).Should().Be(ToleranceBand.Parse("1:2"));
    }

    // ---- Judge (FR-1.3) ----

    [Theory]
    [InlineData(0.0, Verdict.Accept)]
    [InlineData(1.19, Verdict.Accept)]
    [InlineData(1.2, Verdict.Review)]      // d == accept → Review
    [InlineData(2.0, Verdict.Review)]
    [InlineData(3.5, Verdict.Review)]      // d == review → Review
    [InlineData(3.51, Verdict.Block)]      // d > review → Block
    [InlineData(Inf, Verdict.Block)]
    [InlineData(double.NaN, Verdict.Block)]
    public void Judge_Boundaries(double mm, Verdict expected)
    {
        Band(1.2, 3.5).Judge(mm).Should().Be(expected);
    }

    [Fact]
    public void Judge_NoReviewZone_AcceptsBelowReviewsAtTheLimitBlocksAbove()
    {
        var band = Band(10, 10);

        band.Judge(9.99).Should().Be(Verdict.Accept);
        band.Judge(10).Should().Be(Verdict.Review);
        band.Judge(10.01).Should().Be(Verdict.Block);
    }

    [Fact]
    public void Judge_Unbounded_AcceptsEveryFiniteDeviation_ButNaNBlocks()
    {
        ToleranceBand.Unbounded.Judge(1e9).Should().Be(Verdict.Accept);
        ToleranceBand.Unbounded.Judge(double.NaN).Should().Be(Verdict.Block);
    }

    [Fact]
    public void Verdict_IsOrderedBestToWorst()
    {
        ((int)Verdict.Accept).Should().Be(0);
        ((int)Verdict.Review).Should().Be(1);
        ((int)Verdict.Block).Should().Be(2);
    }

    // ---- no implicit conversion from string (ADR-7) ----

    [Fact]
    public void Type_DeclaresNoImplicitConversion()
    {
        var statics = typeof(ToleranceBand).GetMethods(BindingFlags.Public | BindingFlags.Static);

        // Control: the query sees the type's public statics (rule 4 of "a test must distinguish…").
        statics.Should().Contain(m => m.Name == nameof(ToleranceBand.Parse));
        typeof(ToleranceBand).GetMethod("op_Implicit").Should().BeNull();
        statics.Should().NotContain(m => m.Name == "op_Implicit");
    }

    // ---- JSON via JsonParsableConverter ----

    [Fact]
    public void Json_RoundTripsAsTheCanonicalString()
    {
        var band = Band(1.2, Inf);

        JsonSerializer.Serialize(band).Should().Be("\"1.2:inf\"");
        JsonSerializer.Deserialize<ToleranceBand>("\"1.2:inf\"").Should().Be(band);
        JsonSerializer.Serialize(default(ToleranceBand)).Should().Be("\"0:0\"");
        JsonSerializer.Deserialize<ToleranceBand>("\"0:0\"").Should().Be(default(ToleranceBand));
    }

    [Fact]
    public void Json_NullableBand_RoundTripsNullAndValue()
    {
        JsonSerializer.Deserialize<ToleranceBand?>("null").Should().BeNull();
        JsonSerializer.Deserialize<ToleranceBand?>("\"5:20\"").Should().Be(Band(5, 20));
        JsonSerializer.Serialize<ToleranceBand?>(Band(5, 20)).Should().Be("\"5:20\"");
    }
}
