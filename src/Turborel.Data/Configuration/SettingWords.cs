// The values of connection settings that are one of a few words [CLIENT
// Where settings come from] [CLIENT TLS by mode] [CLIENT Several servers]:
// how TLS is used, whether SCRAM binds its channel, what a server must be
// to be taken, and whether servers are tried in order, each written in
// lower case, as a setting, the environment and a URI give it.

using System.Collections.Generic;
using System.Linq;
using Turborel.Data.Authentication;

namespace Turborel.Data.Configuration;

/// <summary>How a connection over TCP uses TLS; a Unix socket or named pipe uses none whatever it says.</summary>
public enum TlsMode
{
    /// <summary>Never: plain TCP.</summary>
    Disable,

    /// <summary>TLS if the server takes it, else plain TCP; its certificate not checked.</summary>
    Prefer,

    /// <summary>TLS or no connection; the server's certificate not checked.</summary>
    Require,

    /// <summary>TLS, the server's certificate signed by an authority trusted, its name not checked.</summary>
    VerifyCa,

    /// <summary>TLS, the server's certificate signed by an authority trusted and of the server's name.</summary>
    VerifyFull,
}

/// <summary>What a server must be for a connection to it to be kept.</summary>
public enum TargetRole
{
    /// <summary>Any server.</summary>
    Any,

    /// <summary>One that takes changes: a server alone, or its group's primary.</summary>
    ReadWrite,

    /// <summary>One that takes none: a replica following its group's log.</summary>
    ReadOnly,
}

/// <summary>Whether servers are tried in the order given.</summary>
public enum LoadBalance
{
    /// <summary>In order.</summary>
    Disable,

    /// <summary>In an order chosen at random for each connection, so connections spread over them.</summary>
    Random,
}

/// <summary>The words each kind of setting takes.</summary>
internal static class SettingWords
{
    private static readonly (string Word, TlsMode Value)[] TlsModes =
        [("disable", TlsMode.Disable), ("prefer", TlsMode.Prefer), ("require", TlsMode.Require), ("verify-ca", TlsMode.VerifyCa), ("verify-full", TlsMode.VerifyFull)];

    private static readonly (string Word, ChannelBinding Value)[] Bindings =
        [("disable", ChannelBinding.Disable), ("prefer", ChannelBinding.Prefer), ("require", ChannelBinding.Require)];

    private static readonly (string Word, TargetRole Value)[] Roles =
        [("any", TargetRole.Any), ("read-write", TargetRole.ReadWrite), ("read-only", TargetRole.ReadOnly)];

    private static readonly (string Word, LoadBalance Value)[] Balances = [("disable", LoadBalance.Disable), ("random", LoadBalance.Random)];

    private static readonly (string Word, bool Value)[] Booleans = [("true", true), ("false", false)];

    /// <summary>Reads a TLS mode.</summary>
    /// <param name="key">The setting's name.</param>
    /// <param name="text">Its value.</param>
    /// <returns>The mode.</returns>
    internal static TlsMode Tls(string key, string text) => Word(key, text, TlsModes);

    /// <summary>Reads a channel binding.</summary>
    /// <param name="key">The setting's name.</param>
    /// <param name="text">Its value.</param>
    /// <returns>The binding.</returns>
    internal static ChannelBinding Binding(string key, string text) => Word(key, text, Bindings);

    /// <summary>Reads a target role.</summary>
    /// <param name="key">The setting's name.</param>
    /// <param name="text">Its value.</param>
    /// <returns>The role.</returns>
    internal static TargetRole Role(string key, string text) => Word(key, text, Roles);

    /// <summary>Reads a load balance.</summary>
    /// <param name="key">The setting's name.</param>
    /// <param name="text">Its value.</param>
    /// <returns>The choice.</returns>
    internal static LoadBalance Balance(string key, string text) => Word(key, text, Balances);

    /// <summary>Reads a truth: true or false.</summary>
    /// <param name="key">The setting's name.</param>
    /// <param name="text">Its value.</param>
    /// <returns>The truth.</returns>
    internal static bool Boolean(string key, string text) => Word(key, text, Booleans);

    /// <summary>Whether the server's certificate is checked against the authorities trusted.</summary>
    /// <param name="mode">The mode.</param>
    /// <returns>Whether it is.</returns>
    internal static bool Verifies(TlsMode mode) => mode is TlsMode.VerifyCa or TlsMode.VerifyFull;

    // The value the word text is, among words.
    private static T Word<T>(string key, string text, IReadOnlyList<(string Word, T Value)> words)
    {
        foreach ((string word, T value) in words)
        {
            if (word == text)
            {
                return value;
            }
        }

        throw ConnectionOptions.Invalid($"[DESIGN] {key} is {text}, not one of {string.Join(", ", words.Select(w => w.Word))}");
    }
}
