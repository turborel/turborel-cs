// The client's side of `trust`: nothing to prove, so a server takes it only
// from a client of its own machine, with SERVER.TRUST_LOCAL ON
// [PROTOCOL Mechanisms] [CLIENT Connecting].

namespace Turborel.Data.Authentication;

/// <summary>Asks the server to trust the user as it names itself.</summary>
public sealed class TrustMechanism : Mechanism
{
    /// <summary>The mechanism's name [PROTOCOL Mechanisms].</summary>
    public const string MechanismName = "trust";

    /// <inheritdoc/>
    public override string Name => MechanismName;

    /// <inheritdoc/>
    public override byte[] Initial() => [];
}
