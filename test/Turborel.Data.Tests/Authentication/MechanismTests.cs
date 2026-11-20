// The mechanisms of no exchange: trust, password and certificate, their
// names, their first bytes, which is secret, and that none answers a
// challenge [PROTOCOL Mechanisms] [CLIENT Connecting].

using System.Text;
using Turborel.Data.Authentication;
using Turborel.Data.Tests.Support;
using Turborel.Data.Tls;
using Xunit;

namespace Turborel.Data.Tests.Authentication;

/// <summary>Tests of <see cref="TrustMechanism"/>, <see cref="PasswordMechanism"/> and <see cref="CertificateMechanism"/>.</summary>
public sealed class MechanismTests
{
    /// <summary>
    /// trust and certificate send nothing, password the password's UTF-8,
    /// which alone is secret; none answers a challenge, binds or keeps anything.
    /// </summary>
    [Fact]
    [Verifies("[PROTOCOL Mechanisms]")]
    public void MechanismsOfNoExchange()
    {
        var password = new PasswordMechanism("pâss");
        Mechanism[] all = [new TrustMechanism(), password, new CertificateMechanism()];

        Assert.Equal(["trust", "password", "certificate"], [.. System.Array.ConvertAll(all, m => m.Name)]);
        Assert.Equal([false, true, false], [.. System.Array.ConvertAll(all, m => m.IsSecret)]);
        Assert.Equal(Encoding.UTF8.GetBytes("pâss"), password.Initial());
        Assert.Empty(all[0].Initial());
        Assert.Empty(all[2].Initial());
        Assert.Equal("PasswordMechanism(..)", password.ToString());
        foreach (Mechanism mechanism in all)
        {
            mechanism.Bind(new TlsChannel(null));
            mechanism.CheckReady();
            mechanism.Refused();
            Assert.Contains("has no answer", Wire.Fails(() => mechanism.Answer([1])).Message, System.StringComparison.Ordinal);
        }
    }
}
