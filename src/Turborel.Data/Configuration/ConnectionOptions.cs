// Connection settings as text [CLIENT Where settings come from]: a layer
// of settings each named by one of Keys, as a program, a URI, a service's
// section or the environment gives them. The layers are laid one over
// another, each setting taken from the first that has it, then read into
// ConnectionSettings. A connection URI [CLIENT The connection URI] is read
// into a layer, its parts percent-encoded (RFC 3986 §2.1).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace Turborel.Data.Configuration;

/// <summary>A layer of connection settings, each by its name.</summary>
public sealed class ConnectionOptions
{
    /// <summary>The servers.</summary>
    public const string Host = "host";

    /// <summary>The port of each host that carries none.</summary>
    public const string Port = "port";

    /// <summary>The user to connect as.</summary>
    public const string User = "user";

    /// <summary>The user's password.</summary>
    public const string Password = "password";

    /// <summary>How TLS is used.</summary>
    public const string TlsMode = "tls_mode";

    /// <summary>Whether SCRAM binds its channel.</summary>
    public const string ChannelBinding = "channel_binding";

    /// <summary>The program's name.</summary>
    public const string ApplicationName = "application_name";

    /// <summary>Whole seconds each step of connecting waits.</summary>
    public const string ConnectTimeout = "connect_timeout";

    /// <summary>The section of the service file to read.</summary>
    public const string Service = "service";

    /// <summary>The service file.</summary>
    public const string ServiceFile = "service_file";

    /// <summary>The password file.</summary>
    public const string Passfile = "passfile";

    /// <summary>What a server must be.</summary>
    public const string Target = "target";

    /// <summary>Whether the hosts are tried in order.</summary>
    public const string LoadBalance = "load_balance";

    /// <summary>A PEM file of the certificate authorities trusted.</summary>
    public const string TlsRoots = "tls_roots";

    /// <summary>Whether the platform's authorities are trusted too.</summary>
    public const string TlsSystemRoots = "tls_system_roots";

    /// <summary>A PEM file of the client's certificate chain, to present.</summary>
    public const string TlsCertificate = "tls_certificate";

    /// <summary>A PEM file of the client certificate's key.</summary>
    public const string TlsKey = "tls_key";

    /// <summary>The name the server's certificate is checked against.</summary>
    public const string ServerName = "server_name";

    /// <summary>The scheme a connection URI begins with.</summary>
    public const string Scheme = "turborel://";

    private readonly SortedDictionary<string, string> _values = new(StringComparer.Ordinal);

    /// <summary>Gets every setting's name.</summary>
    public static IReadOnlyList<string> Keys { get; } =
    [
        Host, Port, User, Password, TlsMode, ChannelBinding, ApplicationName, ConnectTimeout, Service,
        ServiceFile, Passfile, Target, LoadBalance, TlsRoots, TlsSystemRoots, TlsCertificate, TlsKey, ServerName,
    ];

    /// <summary>Gets the environment's variables, each with the setting it gives.</summary>
    public static IReadOnlyList<(string Variable, string Key)> Environment { get; } =
    [
        ("TURBOREL_HOST", Host), ("TURBOREL_PORT", Port), ("TURBOREL_USER", User), ("TURBOREL_PASSWORD", Password),
        ("TURBOREL_TLS_MODE", TlsMode), ("TURBOREL_APPLICATION_NAME", ApplicationName), ("TURBOREL_CONNECT_TIMEOUT", ConnectTimeout),
        ("TURBOREL_SERVICE", Service), ("TURBOREL_SERVICE_FILE", ServiceFile), ("TURBOREL_PASSFILE", Passfile),
    ];

    /// <summary>Gets a value indicating whether none is set.</summary>
    public bool IsEmpty => _values.Count == 0;

    /// <summary>Gives the settings the environment gives, <paramref name="variable"/> reading each variable: an empty one gives none.</summary>
    /// <param name="variable">Reads a variable, null if it is not set.</param>
    /// <returns>The settings.</returns>
    public static ConnectionOptions FromEnvironment(Func<string, string?> variable)
    {
        Misuse.ThrowIfNull(variable);
        var options = new ConnectionOptions();
        foreach ((string name, string key) in Environment)
        {
            if (variable(name) is { Length: > 0 } value)
            {
                options._values[key] = value;
            }
        }

        return options;
    }

    /// <summary>Gives the settings the connection URI <paramref name="uri"/> gives.</summary>
    /// <param name="uri">The URI: <c>turborel://[user[:password]@][host[:port][,host[:port]]...][/][?setting=value[&amp;setting=value]...]</c>.</param>
    /// <returns>The settings.</returns>
    /// <exception cref="TurborelException">SQLSTATE 08001 if it is not one: another scheme, a path, a key that names no setting, a pair without <c>=</c>, or bad percent-encoding.</exception>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1054:URI-like parameters should not be strings",
        Justification = "R-LINT-3: a connection URI of several hosts, or of a socket's path, is no System.Uri.")]
    public static ConnectionOptions FromUri(string uri)
    {
        Misuse.ThrowIfNull(uri);
        string rest = uri.StartsWith(Scheme, StringComparison.Ordinal) ? uri[Scheme.Length..] : throw Invalid($"[DESIGN] a connection URI begins with {Scheme}");
        (string before, string query) = Split(rest, '?') ?? (rest, string.Empty);
        (string authority, string path) = Split(before, '/') ?? (before, string.Empty);
        if (path.Length > 0)
        {
            throw Invalid($"[DESIGN] a connection URI names no database, so has no path, not /{path}");
        }

        var options = new ConnectionOptions();
        int at = authority.LastIndexOf('@');
        (string userinfo, string hosts) = at < 0 ? (string.Empty, authority) : (authority[..at], authority[(at + 1)..]);
        if (userinfo.Length > 0)
        {
            (string user, string password) = Split(userinfo, ':') ?? (userinfo, null!);
            options.Set(User, Decode(user));
            if (password is not null)
            {
                options.Set(Password, Decode(password));
            }
        }

        if (hosts.Length > 0)
        {
            options.Set(Host, Decode(hosts));
        }

        foreach (string pair in query.Split('&').Where(p => p.Length > 0))
        {
            (string key, string value) = Split(pair, '=') ?? throw Invalid($"[DESIGN] {pair} in a connection URI has no =");
            options.Set(Decode(key), Decode(value));
        }

        return options;
    }

    /// <summary>Sets <paramref name="key"/> to <paramref name="value"/>.</summary>
    /// <param name="key">The setting's name, one of <see cref="Keys"/>.</param>
    /// <param name="value">Its value.</param>
    /// <returns>These options.</returns>
    /// <exception cref="TurborelException">SQLSTATE 08001 if <paramref name="key"/> names no setting.</exception>
    public ConnectionOptions Set(string key, string value)
    {
        Misuse.ThrowIfNull(key);
        Misuse.ThrowIfNull(value);
        _values[Keys.Contains(key) ? key : throw Invalid($"[DESIGN] {key} is no connection setting")] = value;
        return this;
    }

    /// <summary>Gives the value of <paramref name="key"/>, if set.</summary>
    /// <param name="key">The setting's name.</param>
    /// <returns>Its value, or null.</returns>
    public string? Get(string key) => _values.TryGetValue(key, out string? value) ? value : null;

    /// <summary>Gives these settings laid over <paramref name="lower"/>: each of its kept where these have none.</summary>
    /// <param name="lower">The layer beneath.</param>
    /// <returns>The settings laid.</returns>
    public ConnectionOptions Over(ConnectionOptions lower)
    {
        Misuse.ThrowIfNull(lower);
        var laid = new ConnectionOptions();
        foreach (KeyValuePair<string, string> pair in lower._values.Concat(_values))
        {
            laid._values[pair.Key] = pair.Value;
        }

        return laid;
    }

    /// <summary>Writes the settings, never the password.</summary>
    /// <returns>The text.</returns>
    public override string ToString() =>
        "{" + string.Join(", ", _values.Select(p => $"{p.Key}: {(p.Key == Password ? "..." : p.Value)}")) + "}";

    /// <summary>The failure of a setting that cannot be read, or settings no connection can be made by: SQLCODE -919, SQLSTATE 08001.</summary>
    /// <param name="message">Why.</param>
    /// <returns>The failure.</returns>
    internal static TurborelException Invalid(string message) => new(-919, "08001", message);

    // Text percent-decoded, as UTF-8.
    private static string Decode(string text)
    {
        var octets = new List<byte>(text.Length);
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != '%')
            {
                octets.Add(bytes[i]);
                continue;
            }

            byte octet = 0;
            bool two = i + 2 < bytes.Length
                && byte.TryParse(Encoding.ASCII.GetString(bytes, i + 1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out octet);
            octets.Add(two ? octet : throw Invalid($"[DESIGN] {text} has a % without two hexadecimal digits"));
            i += 2;
        }

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString([.. octets]);
        }
        catch (DecoderFallbackException)
        {
            throw Invalid($"[DESIGN] {text} is not UTF-8 once decoded");
        }
    }

    // Text split at the first of a character, or null if it has none.
    private static (string Before, string After)? Split(string text, char at)
    {
        int i = text.IndexOf(at, StringComparison.Ordinal);
        return i < 0 ? null : (text[..i], text[(i + 1)..]);
    }
}
