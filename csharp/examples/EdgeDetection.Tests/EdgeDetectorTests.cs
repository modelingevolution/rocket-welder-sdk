using System.Drawing;
using Emgu.CV;
using Emgu.CV.CvEnum;
using Emgu.CV.Structure;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace EdgeDetection.Tests;

public sealed class EdgeDetectorTests
{
    private readonly ITestOutputHelper _output;

    public EdgeDetectorTests(ITestOutputHelper output) => _output = output;


    private const int Width = 1024;
    private const int Height = 768;

    // OpenCV's CvInvoke.Rectangle with thickness=-1 fills pixels INCLUSIVELY on both
    // ends — so Rectangle(x=200, y=200, w=600, h=400) fills pixels x ∈ [200..800],
    // y ∈ [200..600] (601 × 401 pixels). The geometric black/white boundaries sit
    // between pixels, at the half-pixel grid lines: x = 199.5 / 800.5, y = 199.5 / 600.5.
    // CornerSubPix refines toward those gradient-peak locations.
    private static readonly PointF TopLeftTrue = new(199.5f, 199.5f);
    private static readonly PointF TopRightTrue = new(800.5f, 199.5f);
    private static readonly PointF BottomRightTrue = new(800.5f, 600.5f);
    private static readonly PointF BottomLeftTrue = new(199.5f, 600.5f);

    // Pixel-grid corners — what CvInvoke.Rectangle takes and what the README documents.
    private const int RectX = 200;
    private const int RectY = 200;
    private const int RectW = 600;
    private const int RectH = 400;

    private static readonly EdgeDetectionOptions DefaultOptions = new()
    {
        ClassId = 1,
        CannyThreshold1 = 50,
        CannyThreshold2 = 150,
        MinContourArea = 100.0,
        MaxContourArea = 1_000_000.0, // generous — the test square is ~240k px²
        MinVertices = 4,
    };

    private static Mat BuildSyntheticCube()
    {
        // 1024×768 BGR white background with a black axis-aligned filled rectangle.
        var frame = new Mat(Height, Width, DepthType.Cv8U, 3);
        frame.SetTo(new MCvScalar(255, 255, 255));
        CvInvoke.Rectangle(
            frame,
            new Rectangle(RectX, RectY, RectW, RectH),
            new MCvScalar(0, 0, 0),
            thickness: -1); // filled
        return frame;
    }

    [Fact]
    public void Detect_Should_Find_Exactly_One_Contour_For_Clean_Cube_Face()
    {
        // Arrange
        using var frame = BuildSyntheticCube();

        // Act
        var detected = EdgeDetector.Detect(frame, DefaultOptions);

        // Assert
        detected.Should().HaveCount(1, "the synthetic image contains exactly one square");
    }

    [Fact]
    public void Detect_Should_Refine_Vertices_To_Within_Half_Pixel_Of_True_Corners()
    {
        // Arrange
        using var frame = BuildSyntheticCube();

        // Act
        var detected = EdgeDetector.Detect(frame, DefaultOptions);

        // Assert
        detected.Should().HaveCount(1);
        var contour = detected[0];

        // Note: FindContours(External + ApproxSimple) returns up to 8 vertices for a
        // filled axis-aligned rectangle — Canny's 1-px edge thickness produces two
        // very-close vertices at each of the 4 geometric corners. We accept that and
        // verify each returned vertex lies near a true corner.
        contour.RefinedPoints.Should().HaveCountGreaterThanOrEqualTo(4,
            "the rectangle has 4 geometric corners; FindContours may emit duplicate vertices at each");
        contour.RefinedPoints.Should().HaveCountLessThanOrEqualTo(8,
            "ChainApproxSimple should not emit more than 8 vertices for an axis-aligned rectangle");

        // Each refined vertex must be within 0.5 px of one of the four true corners.
        // True corners sit on the half-pixel grid because OpenCV's Rectangle fills
        // inclusively on both ends: pixel range [200..800]×[200..600] → boundaries at
        // (199.5, 199.5) etc.
        var trueCorners = new[] { TopLeftTrue, TopRightTrue, BottomRightTrue, BottomLeftTrue };
        double maxResidual = 0;
        foreach (var refined in contour.RefinedPoints)
        {
            var minDistance = trueCorners
                .Select(c => DistanceTo(refined, c))
                .Min();
            if (minDistance > maxResidual) maxResidual = minDistance;
            minDistance.Should().BeLessThan(0.5,
                $"refined vertex ({refined.X:F3}, {refined.Y:F3}) must lie within 0.5 px of a true corner");
        }
        _output.WriteLine($"Achieved keypoint precision (max residual across {contour.RefinedPoints.Length} vertices): {maxResidual:F4} px");
    }

    [Fact]
    public void Detect_Should_Place_TopLeft_Corner_At_Points0_As_EdgeStart()
    {
        // FR-5.2: Points[0] is the EdgeStart keypoint. With Emgu's FindContours under
        // RetrievalModes.External + ChainApproxSimple, the first vertex of an axis-
        // aligned black filled rectangle is at the top-left geometric corner. The true
        // top-left of pixel range [200..799]×[200..599] is (199.5, 199.5) (the inter-
        // pixel boundary). CornerSubPix refines onto the gradient peak which is
        // co-located with that boundary.
        // Arrange
        using var frame = BuildSyntheticCube();

        // Act
        var detected = EdgeDetector.Detect(frame, DefaultOptions);

        // Assert
        detected.Should().HaveCount(1);
        var p0Refined = detected[0].RefinedPoints[0];
        DistanceTo(p0Refined, TopLeftTrue).Should().BeLessThan(0.5,
            $"Points[0] must be the top-left corner (EdgeStart per FR-5.2); was ({p0Refined.X:F3}, {p0Refined.Y:F3})");

        // The integer-rounded Points[0] (what actually goes on the wire) lands on
        // either side of the half-pixel grid line — accept ±1 px from the pixel-grid
        // value (the wire-format quantization documented in README "Precision floor").
        var p0Int = detected[0].IntPoints[0];
        Math.Abs(p0Int.X - RectX).Should().BeLessThanOrEqualTo(1);
        Math.Abs(p0Int.Y - RectY).Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public void Detect_Should_Populate_Confidence_In_Unit_Interval_And_Non_Zero()
    {
        // Arrange
        // FR-2.5 tiebreaker requires a non-zero Confidence so the consumer can compare.
        // For a convex (square) shape, area/hull-area ≈ 1.0.
        using var frame = BuildSyntheticCube();

        // Act
        var detected = EdgeDetector.Detect(frame, DefaultOptions);

        // Assert
        detected.Should().HaveCount(1);
        var confidence = detected[0].Confidence;

        confidence.Should().BeGreaterThan(0f, "FR-2.5 tiebreaker needs a non-zero quality metric");
        confidence.Should().BeLessThanOrEqualTo(1f, "Confidence is normalized to [0, 1]");
        confidence.Should().BeGreaterThan(0.95f,
            "a convex square contour should produce contour-area/hull-area ≈ 1.0");
    }

    [Fact]
    public void Detect_MinContourArea_Should_Filter_Out_Tiny_Noise_Blobs()
    {
        // Arrange
        // Build a frame with one big square AND many tiny noise dots; the filter
        // should keep the square and discard every noise dot.
        using var frame = BuildSyntheticCube();
        // Sprinkle small black dots (5x5) across the white area — these are below MinContourArea=100.
        for (int y = 50; y < 150; y += 20)
        {
            for (int x = 50; x < 150; x += 20)
            {
                CvInvoke.Rectangle(
                    frame,
                    new Rectangle(x, y, 5, 5),
                    new MCvScalar(0, 0, 0),
                    thickness: -1);
            }
        }

        // Act
        var detected = EdgeDetector.Detect(frame, DefaultOptions);

        // Assert
        detected.Should().HaveCount(1, "MinContourArea must filter out the 25-pixel noise blobs");
    }

    private static double DistanceTo(PointF a, PointF b)
    {
        double dx = a.X - b.X;
        double dy = a.Y - b.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }
}
