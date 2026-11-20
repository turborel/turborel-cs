# Settings

[Contents](README.md)

## NAME

`ConnectionOptions` and `ConnectionSettings`: connection settings from
the program, a URI, the service file, the environment and the password
file; connecting by them to one of several servers, by TLS mode and role.

## SYNOPSIS

```csharp
namespace Turborel.Data.Configuration;

public sealed class ConnectionOptions
{
    public const string Host = "host", Port = "port", User = "user", Password = "password", TlsMode = "tls_mode",
        ChannelBinding = "channel_binding", ApplicationName = "application_name", ConnectTimeout = "connect_timeout",
        Service = "service", ServiceFile = "service_file", Passfile = "passfile", Target = "target",
        LoadBalance = "load_balance", TlsRoots = "tls_roots", TlsSystemRoots = "tls_system_roots",
        TlsCertificate = "tls_certificate", TlsKey = "tls_key", ServerName = "server_name";
    public const string Scheme = "turborel://";
    public static IReadOnlyList<string> Keys { get; }
    public static IReadOnlyList<(string Variable, string Key)> Environment { get; }
    public static ConnectionOptions FromEnvironment(Func<string, string?> variable);
    public static ConnectionOptions FromUri(string uri);
    public ConnectionOptions Set(string key, string value);
    public string? Get(string key);
    public bool IsEmpty { get; }
    public ConnectionOptions Over(ConnectionOptions lower);
}

public sealed record ConnectionSettings
{
    public const string DefaultServiceFile = ".turborel_service.conf";
    public const string DefaultPassfile = ".turborel_pass";
    public required IReadOnlyList<ServerHost> Hosts { get; init; }
    public required string User { get; init; }
    public string? Password { get; init; }
    public TlsMode TlsMode { get; init; }
    public ChannelBinding ChannelBinding { get; init; }
    public string? ApplicationName { get; init; }
    public TimeSpan? ConnectTimeout { get; init; }
    public string? Passfile { get; init; }
    public TargetRole Target { get; init; }
    public LoadBalance LoadBalance { get; init; }
    public TlsFiles Tls { get; init; }
    public KeyCache KeyCache { get; init; }
    public static ConnectionSettings Resolve(ConnectionOptions explicitOptions);
    public static ConnectionSettings ResolveIn(ConnectionOptions explicitOptions, Func<string, string?> variable);
    public static ConnectionSettings FromUri(string uri);
    public static ConnectionOptions Service(string file, string name);
    public string? PasswordFor(ServerHost host);
    public Task<NativeConnection> ConnectAsync(CancellationToken cancellationToken = default);
}

public abstract record ServerHost { public const int DefaultPort = 7422; public const string DefaultName = "localhost"; }
public sealed record TcpHost(string Name, int Port) : ServerHost;
public sealed record LocalHost(string Path) : ServerHost;
public sealed record TlsFiles(string? Roots, bool SystemRoots, string? Certificate, string? Key, string? ServerName);
public enum TlsMode { Disable, Prefer, Require, VerifyCa, VerifyFull }
public enum TargetRole { Any, ReadWrite, ReadOnly }
public enum LoadBalance { Disable, Random }
public enum ServerRoleKind { ReadWrite, ReadOnly, Unknown }
public static class ServerRole
{
    public const string RoleQuery = "SELECT ROLE FROM INFORMATION_SCHEMA.REPLICAS WHERE IS_SELF = 'YES'";
    public static Task<ServerRoleKind> OfAsync(NativeConnection connection, CancellationToken cancellationToken = default);
    public static string Describe(ServerRoleKind role);
}
```

## DESCRIPTION

### Layers

A `ConnectionOptions` is a layer of settings, each by its name;
`Set` refuses a name that is no setting's. `Over` lays one layer over
another, each setting taken from the upper where it has one.
`ConnectionSettings.Resolve` lays the program's options over the section
of the service file they or the environment name, over the environment,
and reads them, the defaults for the rest; `ResolveIn` reads the
environment through a function the program gives. `FromEnvironment` reads
the variables of `Environment`, each the setting of its name, a variable
set but empty giving nothing. `FromUri` reads a connection URI, its user,
password, hosts and settings percent-encoded, an IPv6 address in
brackets; another scheme, a path, a setting not one of `Keys`, a pair
without `=`, or a `%` not followed by two hexadecimal digits is refused.

`host` names servers, comma-separated, each `name`, `name:port`,
`[address]` or `[address]:port`, or a path, a Unix socket's or a named
pipe's; a name of two colons or more is an IPv6 address unbracketed.
`port` gives one port for all hosts that carry none, or one each.
`connect_timeout` is whole seconds, 0 for no limit.

### Files

`Service` reads a section of the service file: lines `setting = value`
under `[name]`, `#` comments and blank lines skipped; a section cannot
set `service` or `service_file`; a malformed line is refused, its file
and line named, as is a file with no section of the name.
`PasswordFor` gives the settings' password, else the password file's, of
its first line `host:port:user:password` matching the server, its port
and the user, a field `*` written plain matching anything, `\` taking the
character after it as it is; a Unix socket is matched by its path and
the default port. The file is read only if it is a regular file, or a
link to one; .NET's portable library reads no file's owner or
permissions, so its owner answers for keeping it private.

### Connecting

`ConnectAsync` tries each host in turn, or in a random order for each
connection with `LoadBalance.Random`, and each address a name resolves
to, each step within the connect timeout. A server is proved to by
SCRAM-SHA-256 of a password, binding as `ChannelBinding` says, with the
settings' `KeyCache`, shared by every connection they make; else by the
certificate named, else by trust. A secret mechanism over plain TCP is
refused before anything is sent. `ApplicationName` is sent as the
connection attribute `application`. A server unreachable, refusing the
user, or not of the role `Target` asks is passed for the next. With none
kept, the failure is the one server's, or names each address and why.

`TlsMode` says how TCP uses TLS: `Prefer` and `Require` take any
certificate, `VerifyCa` one issued by an authority trusted, `VerifyFull`
one also of `ServerName`, else the host's name. `Prefer` connects again
over plain TCP to a server whose handshake fails. The verify modes need
an authority trusted, a certificate its key, checked before any
connection. A Unix socket or named pipe uses no TLS.
`ChannelBinding.Require` refuses a connection SCRAM over TLS cannot bind.

`ServerRole.OfAsync` gives the role a connection's session sees: as
SERVER_READ_ONLY last told it, else from INFORMATION_SCHEMA.REPLICAS,
asked as a statement of its own transaction; a server alone, which has
no such table, takes changes.

## ERRORS

| SQLSTATE | SQLCODE | When |
|---|---|---|
| 08001 | -919 | A setting that cannot be read; a service file unread, malformed or without the section; no user; no server kept, naming each and why; a server not of the role asked |
| 28000 | -919 | A secret mechanism over plain TCP; a channel binding required that cannot be; no server kept, the last refusing the user |
| 08006 | -919 | The verify modes with no authority trusted; a certificate with no key; a name that resolves to nothing; a server that cannot be reached |

## EXAMPLES

See [Settings](../user/09-settings.md).

## STANDARD

[CLIENT Connecting by settings], [CLIENT Where settings come from],
[CLIENT The connection URI], [CLIENT The service file], [CLIENT The
password file], [CLIENT Several servers], [CLIENT TLS by mode] and
[CLIENT The SCRAM key cache] of
[Settings, Pipelines and Pools](https://github.com/turborel/turborel/blob/main/doc/client/02-library-connections.md).

## SEE ALSO

[Connections](21-connections.md), [Appendix C: Settings](../appendix/c-settings.md).
