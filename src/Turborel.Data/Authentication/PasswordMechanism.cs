// The client's side of `password`: the password sent whole, so only over
// TLS, a Unix socket or a named pipe; a connection over plain TCP refuses
// it before it sends anything [PROTOCOL Mechanisms] [CLIENT Connecting].

using System.Text;

namespace Turborel.Data.Authentication;

/// <summary>Proves the user by its password, sent whole.</summary>
/// <param name="password">The password.</param>
public sealed class PasswordMechanism(string password) : Mechanism
{
    /// <summary>The mechanism's name [PROTOCOL Mechanisms].</summary>
    public const string MechanismName = "password";

    /// <inheritdoc/>
    public override string Name => MechanismName;

    /// <inheritdoc/>
    public override bool IsSecret => true;

    /// <inheritdoc/>
    public override byte[] Initial() => Encoding.UTF8.GetBytes(password);

    /// <inheritdoc/>
    public override string ToString() => "PasswordMechanism(..)";
}
