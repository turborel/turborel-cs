// Connection settings from outside the program [CLIENT Connecting by
// settings] [CLIENT Where settings come from]: where the servers are, as
// whom to connect, how, and what a server must be, read from layers of
// ConnectionOptions: the program's own, or a connection URI's, then a
// service's section of the service file, then the environment, then the
// defaults, each setting taken from the first layer that has it. A
// password none of them gives is looked up in the password file.
// ConnectAsync connects as they say (Failover).

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Authentication;
using Turborel.Data.Session;
using Turborel.Data.Transport;

namespace Turborel.Data.Configuration;

/// <summary>The files a connection's TLS is made of.</summary>
/// <param name="Roots">A PEM file of the authorities trusted.</param>
/// <param name="SystemRoots">Whether the platform's authorities are trusted too.</param>
/// <param name="Certificate">A PEM file of the client's certificate chain, to present.</param>
/// <param name="Key">A PEM file of its private key.</param>
/// <param name="ServerName">The name the server's certificate is checked against, if not the host's.</param>
public sealed record TlsFiles(string? Roots, bool SystemRoots, string? Certificate, string? Key, string? ServerName);

/// <summary>How to connect, as the settings say.</summary>
public sealed record ConnectionSettings : Pooling.IConnectionSource
{
    /// <summary>The service file read when none is named, in the home directory.</summary>
    public const string DefaultServiceFile = ".turborel_service.conf";

    /// <summary>The password file read when none is named, in the home directory.</summary>
    public const string DefaultPassfile = ".turborel_pass";

    /// <summary>Gets the servers, tried in turn.</summary>
    public required IReadOnlyList<ServerHost> Hosts { get; init; }

    /// <summary>Gets the user to connect as.</summary>
    public required string User { get; init; }

    /// <summary>Gets the user's password, if given; else the password file's.</summary>
    public string? Password { get; init; }

    /// <summary>Gets how TLS is used.</summary>
    public TlsMode TlsMode { get; init; } = TlsMode.Prefer;

    /// <summary>Gets whether SCRAM binds its channel.</summary>
    public ChannelBinding ChannelBinding { get; init; } = ChannelBinding.Prefer;

    /// <summary>Gets the program's name, as it tells the server.</summary>
    public string? ApplicationName { get; init; }

    /// <summary>Gets how long each step of connecting waits; null for no limit.</summary>
    public TimeSpan? ConnectTimeout { get; init; } = Timeouts.ConnectWithin;

    /// <summary>Gets the password file, if any.</summary>
    public string? Passfile { get; init; }

    /// <summary>Gets what a server must be.</summary>
    public TargetRole Target { get; init; }

    /// <summary>Gets whether the servers are tried in order.</summary>
    public LoadBalance LoadBalance { get; init; }

    /// <summary>Gets the files TLS is made of.</summary>
    public TlsFiles Tls { get; init; } = new(null, false, null, null, null);

    /// <summary>Gets the SCRAM keys of its connections, so a reconnection skips their derivation; shared by its copies.</summary>
    public KeyCache KeyCache { get; init; } = new();

    /// <summary>Gives the settings <paramref name="explicitOptions"/> gives, then the service it or the environment names, then the environment, then the defaults.</summary>
    /// <param name="explicitOptions">The program's own settings.</param>
    /// <returns>The settings.</returns>
    /// <exception cref="TurborelException">SQLSTATE 08001 if a setting cannot be read, a service named cannot be read, or no user is named.</exception>
    public static ConnectionSettings Resolve(ConnectionOptions explicitOptions) =>
        ResolveIn(explicitOptions, System.Environment.GetEnvironmentVariable);

    /// <summary>Gives the settings of the connection URI <paramref name="uri"/>, as <see cref="Resolve"/> lays them.</summary>
    /// <param name="uri">The URI.</param>
    /// <returns>The settings.</returns>
    /// <exception cref="TurborelException">As <see cref="ConnectionOptions.FromUri"/>'s and <see cref="Resolve"/>'s.</exception>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Design",
        "CA1054:URI-like parameters should not be strings",
        Justification = "R-LINT-3: a connection URI of several hosts, or of a socket's path, is no System.Uri.")]
    public static ConnectionSettings FromUri(string uri) => Resolve(ConnectionOptions.FromUri(uri));

    /// <summary>Gives the settings as <see cref="Resolve"/> lays them, <paramref name="variable"/> reading the environment.</summary>
    /// <param name="explicitOptions">The program's own settings.</param>
    /// <param name="variable">Reads a variable of the environment, null if it is not set.</param>
    /// <returns>The settings.</returns>
    /// <exception cref="TurborelException">As <see cref="Resolve"/>'s.</exception>
    public static ConnectionSettings ResolveIn(ConnectionOptions explicitOptions, Func<string, string?> variable)
    {
        Misuse.ThrowIfNull(explicitOptions);
        var environment = ConnectionOptions.FromEnvironment(variable);
        ConnectionOptions named = explicitOptions.Over(environment);
        ConnectionOptions service = named.Get(ConnectionOptions.Service) is { } name
            ? Service(named.Get(ConnectionOptions.ServiceFile) ?? AtHome(DefaultServiceFile) ?? string.Empty, name)
            : new ConnectionOptions();
        return Of(explicitOptions.Over(service).Over(environment));
    }

    /// <summary>Reads the section <paramref name="name"/> of the service file <paramref name="file"/>.</summary>
    /// <param name="file">The service file.</param>
    /// <param name="name">The section.</param>
    /// <returns>Its settings.</returns>
    /// <exception cref="TurborelException">SQLSTATE 08001 if it cannot be read, a line is malformed, or it has no section of the name.</exception>
    public static ConnectionOptions Service(string file, string name)
    {
        Misuse.ThrowIfNull(file);
        string text;
        try
        {
            text = File.ReadAllText(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw ConnectionOptions.Invalid($"[DESIGN] cannot read {file}: {e.Message}");
        }

        return SettingsFiles.Service(text, name, file);
    }

    /// <summary>Gives the password to prove at <paramref name="host"/>: the settings', else the password file's for the host, its port and the user, if the file can be read.</summary>
    /// <param name="host">The server.</param>
    /// <returns>The password, or null.</returns>
    public string? PasswordFor(ServerHost host)
    {
        if (Password is not null)
        {
            return Password;
        }

        if (Passfile is null || SettingsFiles.ReadRegular(Passfile) is not { } text)
        {
            return null;
        }

        (string name, int port) = host switch
        {
            TcpHost tcp => (tcp.Name, tcp.Port),
            LocalHost local => (local.Path, ServerHost.DefaultPort),
            _ => (string.Empty, 0),
        };
        return SettingsFiles.Password(text, (name, port.ToString(CultureInfo.InvariantCulture), User));
    }

    /// <summary>Connects to the first server, of the hosts in the order the settings say, that can be reached, logs the user in and is of the role asked.</summary>
    /// <param name="cancellationToken">Cancels the connecting.</param>
    /// <returns>The connection.</returns>
    /// <exception cref="TurborelException">The one server's failure, if there was one; else SQLSTATE 08001, saying why each was not kept, 28000 where the last refused the user; 28000 for a channel binding required that cannot be.</exception>
    public Task<NativeConnection> ConnectAsync(CancellationToken cancellationToken = default) => Failover.ConnectAsync(this, cancellationToken);

    /// <summary>Writes the settings, never the password.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => $"ConnectionSettings({string.Join(", ", Hosts)}, {User}, {TlsMode}, {Target})";

    /// <summary>Reads a whole number, as Rust reads an unsigned one: digits, a + before them taken.</summary>
    /// <param name="text">The text, trimmed of white space first.</param>
    /// <returns>The number, or null.</returns>
    internal static ulong? Unsigned(string text)
    {
        string trimmed = text.Trim();
        string digits = trimmed.StartsWith('+') ? trimmed[1..] : trimmed;
        return ulong.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out ulong n) ? n : null;
    }

    // The settings options give, laid already, the defaults for the rest.
    private static ConnectionSettings Of(ConnectionOptions options)
    {
        string user = options.Get(ConnectionOptions.User) ?? throw ConnectionOptions.Invalid("[DESIGN] no user is named: by the program, a service, or TURBOREL_USER");
        return new ConnectionSettings
        {
            Hosts = ServerHost.Parse(options.Get(ConnectionOptions.Host) ?? ServerHost.DefaultName, options.Get(ConnectionOptions.Port) ?? string.Empty),
            User = user,
            Password = options.Get(ConnectionOptions.Password),
            TlsMode = Read(options, ConnectionOptions.TlsMode, SettingWords.Tls, TlsMode.Prefer),
            ChannelBinding = Read(options, ConnectionOptions.ChannelBinding, SettingWords.Binding, ChannelBinding.Prefer),
            ApplicationName = options.Get(ConnectionOptions.ApplicationName),
            ConnectTimeout = Read(options, ConnectionOptions.ConnectTimeout, Seconds, Timeouts.ConnectWithin),
            Passfile = options.Get(ConnectionOptions.Passfile) ?? AtHome(DefaultPassfile),
            Target = Read(options, ConnectionOptions.Target, SettingWords.Role, TargetRole.Any),
            LoadBalance = Read(options, ConnectionOptions.LoadBalance, SettingWords.Balance, LoadBalance.Disable),
            Tls = new TlsFiles(
                options.Get(ConnectionOptions.TlsRoots),
                Read(options, ConnectionOptions.TlsSystemRoots, SettingWords.Boolean, false),
                options.Get(ConnectionOptions.TlsCertificate),
                options.Get(ConnectionOptions.TlsKey),
                options.Get(ConnectionOptions.ServerName)),
        };
    }

    private static T Read<T>(ConnectionOptions options, string key, Func<string, string, T> parse, T fallback) =>
        options.Get(key) is { } text ? parse(key, text) : fallback;

    // The time text is, in whole seconds; 0 for no limit.
    private static TimeSpan? Seconds(string key, string text) => Unsigned(text) switch
    {
        null => throw ConnectionOptions.Invalid($"[DESIGN] {key} is {text}, not a whole number of seconds"),
        0 => null,
        ulong n => TimeSpan.FromSeconds(n),
    };

    // The file name in the home directory, if there is one.
    private static string? AtHome(string name)
    {
        string home = System.Environment.GetFolderPath(System.Environment.SpecialFolder.UserProfile);
        return home.Length > 0 ? Path.Combine(home, name) : null;
    }
}
