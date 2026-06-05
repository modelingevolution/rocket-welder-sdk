namespace RocketWelder.SDK.Devices.Welding;

/// <summary>
/// Welding process mode at the welder. Vendor-agnostic minimum; concrete vendors map their richer
/// enums onto these. Values not mapped land on <see cref="Unknown"/>.
/// </summary>
public enum WeldingMode
{
    /// <summary>Mode is unknown or could not be mapped from the vendor's representation.</summary>
    Unknown = 0,

    /// <summary>MIG/MAG standard (manual) welding.</summary>
    MigMagStandard,

    /// <summary>MIG/MAG synergic welding.</summary>
    MigMagSynergic,

    /// <summary>Job mode (a stored welding program selected by number).</summary>
    Job,

    /// <summary>TIG welding.</summary>
    Tig,

    /// <summary>MMA (stick) welding.</summary>
    Mma,
}
