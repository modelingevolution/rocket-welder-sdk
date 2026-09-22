namespace RocketWelder.SDK.Devices.Robot;

/// <summary>
/// A robot that can weave — oscillate the torch across the seam while ordinary linear/circular moves run. Kept off
/// <see cref="IRobot"/> exactly like <see cref="IStoppableRobot"/>: not every arm supports weaving, so a host must
/// be able to ask "can this one?" by probing <c>robot as IWeavingRobot</c> rather than assume it. A null probe is
/// the signal that a program using a Weave block cannot run on this robot.
///
/// <para>Weaving is a mode-toggle bracket, not a per-move parameter: <see cref="BeginWeave"/> configures the
/// profile and turns weaving on, every following move oscillates, and <see cref="EndWeave"/> turns it off.</para>
/// </summary>
public interface IWeavingRobot
{
    /// <summary>
    /// Configures the weave profile and turns weaving on. A driver applies <paramref name="profile"/> to the
    /// controller (Fairino: <c>WeaveSetPara</c>) and then issues the start method for
    /// <see cref="WeaveProfile.Instruction"/>. Every subsequent move oscillates until <see cref="EndWeave"/>.
    /// </summary>
    /// <param name="profile">The weave configuration to apply.</param>
    /// <returns>0 on success; a non-zero controller error code otherwise.</returns>
    int BeginWeave(WeaveProfile profile);

    /// <summary>
    /// Turns weaving off. Carries no parameters (the end variant does not reconfigure); only the
    /// <paramref name="family"/> is needed so the driver issues the matching end method for the family that was
    /// started. Calling this while weaving is already off is a benign no-op.
    /// </summary>
    /// <param name="family">The instruction family whose end method to issue.</param>
    /// <returns>0 on success; a non-zero controller error code otherwise.</returns>
    int EndWeave(WeaveInstruction family);
}
