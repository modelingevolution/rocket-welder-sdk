namespace RocketWelder.SDK.Devices.Welding;

/// <summary>
/// Why touch sensing was lost. The two cases need different operator text and different handling, and a host must
/// not have to compare exception message strings across package boundaries to tell them apart.
/// </summary>
public enum TouchSensingLostReason
{
    /// <summary>
    /// The welder reports touch sensing off although this host never asked for it — on the iWave, the command bit
    /// was found cleared, which means another fieldbus master wrote it. The welder is still there; the probe must
    /// stop the arm because contact is no longer being sensed.
    /// </summary>
    SwitchedOff = 0,

    /// <summary>
    /// The welder device object itself went away — disposed, replaced, or removed from the host's registry while a
    /// probe was using it. Every outstanding waiter is faulted with this reason before the adapter tears its link
    /// down, so a probe learns of it rather than descending unwatched.
    /// </summary>
    DeviceChanged = 1
}
