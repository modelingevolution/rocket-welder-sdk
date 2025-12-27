namespace RocketWelder.SDK.Shared;

/// <summary>
/// Video resolution (width x height).
/// </summary>
public readonly record struct Resolution(int Width, int Height) : IParsable<Resolution>
{
    public static readonly Resolution FullHd = new(1920, 1080);
    public static readonly Resolution SubHd = new(1456, 1088);
    public static readonly Resolution Hd = new(1280, 720);
    public static readonly Resolution Xga = new(1024, 768);
    public static readonly Resolution Svga = new(800, 600);

    public static explicit operator Resolution(VideoResolution r) => r switch
    {
        VideoResolution.FullHd => FullHd,
        VideoResolution.SubHd => SubHd,
        VideoResolution.Hd => Hd,
        VideoResolution.Xga => Xga,
        VideoResolution.Svga => Svga,
        _ => throw new NotImplementedException()
    };

    public override string ToString() => $"{Width}x{Height}";

    public static Resolution Parse(string s, IFormatProvider? provider)
    {
        var segments = s.Split('x');
        return new Resolution(int.Parse(segments[0]), int.Parse(segments[1]));
    }

    public static bool TryParse(string? s, out Resolution result) =>
        TryParse(s, null, out result);

    public static bool TryParse(string? s, IFormatProvider? provider, out Resolution result)
    {
        if (s == null)
        {
            result = default;
            return false;
        }
        var segments = s.Split('x');
        if (segments.Length != 2)
        {
            result = default;
            return false;
        }
        if (int.TryParse(segments[0], out var w) && int.TryParse(segments[1], out var h))
        {
            result = new Resolution(w, h);
            return true;
        }
        result = default;
        return false;
    }
}
