// The client's side of `certificate`: nothing sent, the certificate the
// client presents over TLS proving the user its common name names
// [PROTOCOL Mechanisms] [CLIENT Connecting].

namespace Turborel.Data.Authentication;

/// <summary>Proves the user by the certificate presented over TLS.</summary>
public sealed class CertificateMechanism : Mechanism
{
    /// <summary>The mechanism's name [PROTOCOL Mechanisms].</summary>
    public const string MechanismName = "certificate";

    /// <inheritdoc/>
    public override string Name => MechanismName;

    /// <inheritdoc/>
    public override byte[] Initial() => [];
}
