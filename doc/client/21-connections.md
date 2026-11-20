# Connections

[Contents](README.md)

## NAME

`NativeConnection`, the low-level client's connection: its targets,
mechanisms, TLS, offer, timeouts, what it is told, the control of its
session, cancel and end. Its statements are on the page
[Statements and Cursors](22-statements.md).

## SYNOPSIS

```csharp
namespace Turborel.Data.Session;

public sealed partial class NativeConnection : IAsyncDisposable
{
    public static Task<NativeConnection> ConnectAsync(Target target, string user, Mechanism mechanism,
        ConnectOptions? options = null, CancellationToken cancellationToken = default);
    public static Task<NativeConnection> OpenAsync(Stream stream, string user, Mechanism mechanism,
        Offer? offer = null, CancellationToken cancellationToken = default);
    public static Task<NativeConnection> OpenWithinAsync(Stream stream, string user, Mechanism mechanism,
        ConnectOptions? options = null, CancellationToken cancellationToken = default);

    public string Authorization { get; }
    public string Server { get; }
    public Target? Target { get; }
    public IReadOnlyList<string> Granted { get; }
    public IReadOnlyDictionary<string, string> Settings { get; }
    public string? Setting(string name);
    public bool Has(string capability);
    public bool InTransaction { get; }
    public bool IsLost { get; }
    public uint MaxBody { get; set; }
    public TimeSpan? ReadTimeout { get; }
    public void SetReadTimeout(TimeSpan? within);
    public Canceller? Canceller { get; }
    public Task TerminateAsync(CancellationToken cancellationToken = default);
    public ValueTask DisposeAsync();

    public Action<TurborelNotice>? NoticeHandler { get; set; }
    public IReadOnlyList<TurborelNotice> TakeNotices();
    public void SetQueryAttributes(IReadOnlyList<KeyValuePair<string, string>> attributes);
    public Task PingAsync(CancellationToken cancellationToken = default);
    public Task ResetAsync(CancellationToken cancellationToken = default);
    public Task ChangeUserAsync(string user, Mechanism mechanism, CancellationToken cancellationToken = default);
}

public sealed record ConnectOptions { Offer Offer; Timeouts Timeouts; }
public sealed record Offer
{
    public static Offer Default { get; }
    public static Offer None();
    public Offer Compressed();
    public Offer WithAttribute(string name, string value);
    public IReadOnlyList<string> Capabilities { get; init; }
    public IReadOnlyList<KeyValuePair<string, string>> Attributes { get; init; }
    public int CompressFrom { get; init; }
}
public sealed class Canceller
{
    public static readonly TimeSpan CancelWithin;
    public Task CancelAsync(TimeSpan? within = null, CancellationToken cancellationToken = default);
}
public static class StatusNames { Authorization, Schema, ReadOnly, ServerVersion, ServerReadOnly, Catalog, Owner }

namespace Turborel.Data.Transport;

public abstract class Target;
public sealed class TcpTarget(string host, int port = TcpTarget.DefaultPort) : Target;
public sealed class LocalTarget(string path) : Target;
public sealed record Timeouts(TimeSpan? Connect, TimeSpan? Read)
{
    public static readonly TimeSpan ConnectWithin, TerminateWithin;
    public static Timeouts Default { get; }
}

namespace Turborel.Data.Tls;

public sealed class TlsTarget(string host, int port, TlsOptions options) : Target;
public sealed record TlsOptions
{
    string? RootsFile; bool SystemRoots; string? CertificateFile; string? KeyFile;
    string? ServerName; bool VerifyAuthority = true; bool VerifyName = true;
}
public sealed record TlsChannel(ReadOnlyMemory<byte>? Certificate);

namespace Turborel.Data.Authentication;

public abstract class Mechanism;
public sealed class TrustMechanism : Mechanism;
public sealed class PasswordMechanism(string password) : Mechanism;
public sealed class CertificateMechanism : Mechanism;
public sealed class ScramMechanism(string password) : Mechanism
{
    public bool InTheClear { get; set; }
    public ChannelBinding Binding { get; set; }
    public KeyCache? Cache { get; set; }
}
public enum ChannelBinding { Prefer, Disable, Require }
public sealed class KeyCache
{
    public const int Entries = 64;
    public int Count { get; }
    public (long Hits, long Misses) Lookups { get; }
    public void Clear();
}
```

## DESCRIPTION

The low-level client is asynchronous alone: each method that waits on
the server returns a task and takes a `CancellationToken`. The standard
API gives each its synchronous form (RULES.md D15).

### Connecting

`ConnectAsync` reaches `target`, sends Hello as `user` with the
mechanism's first message, answers each Challenge by the mechanism, and
returns once the server sends Ready. A Status the server sends before
Ready is kept; Ready gives the session's authorization identifier,
`Authorization`, the server's product, `Server`, the capabilities
granted, `Granted`, in the order of the protocol's table, and the key a
cancel sends, which the connection never shows.

`OpenAsync` logs in over a stream the program opened, as a TLS stream
of its own. Such a connection has no `Target`, so no `Canceller`, and
its reads are bounded by the stream's maker: `SetReadTimeout` is refused.
`OpenWithinAsync` logs in over a stream the program opened as
`OpenAsync` does, and bounds it as its options' `Timeouts` say, as a
connection the driver makes: `SetReadTimeout` changes it, and
`TerminateAsync` waits as below. A mechanism bound to its TLS channel by
`Bind`, before the stream is given, carries SCRAM-SHA-256-PLUS over it.

| Target | Reaches |
|---|---|
| `TcpTarget` | TCP at `host`, port 7422 unless another is given |
| `TlsTarget` | TCP at `host`, then TLS begun on the same port |
| `LocalTarget` | the Unix socket at `path`; on Windows the named pipe of that name, as `\\.\pipe\turborel` |

### Mechanisms

| Mechanism | Name | Sends |
|---|---|---|
| `TrustMechanism` | `trust` | nothing: the server takes the user named, if its SERVER.TRUST_LOCAL lets it |
| `PasswordMechanism` | `password` | the password's UTF-8, whole |
| `CertificateMechanism` | `certificate` | nothing: the TLS client certificate's common name names the user |
| `ScramMechanism` | `scram-sha-256`, or `scram-sha-256-plus` when bound | SCRAM-SHA-256's exchange (RFC 5802, RFC 7677) |

A password and SCRAM are secret: over plain TCP the connection refuses
them before it connects, SQLSTATE 28000, unless `InTheClear` is set on
SCRAM, for a server whose SERVER.SCRAM_WITHOUT_TLS is ON. SCRAM checks
the server's signature before its last, empty Answer, and the connection
is refused, SQLSTATE 08006, if Ready comes before the server has proved
it knows the user's verifier.

Over TLS, SCRAM binds the exchange by `tls-server-end-point`, the hash
of the certificate the server presented, over TLS 1.2 and TLS 1.3 alike:
.NET's TLS gives no keying material to export, so `tls-exporter` is not
used (RULES.md D8). A certificate whose signature names no hash, or two,
defines no binding; SCRAM then says it could bind, and the server offered
none. `Binding` chooses: `Prefer` binds where it can, `Disable` never
binds, and `Require` refuses, SQLSTATE 28000, an exchange that is not
bound. A channel binds the login it is given to alone: a mechanism used
again binds afresh, so over a Unix socket after TLS it is SCRAM-SHA-256.

`Cache`, a `KeyCache`, keeps the keys a password derived after a login
the server proved, found again by the salt, the iterations and a tag of
the password under a random secret of the cache's own, so a later login
of the same salt and iterations skips the derivation. It holds at most
64 entries, the oldest dropped first, and drops an entry a refused login
used. It is held in the process's memory alone. `Lookups` counts its
hits and misses.

### TLS

`TlsOptions` names the authorities trusted: a PEM file, `RootsFile`; the
platform's, `SystemRoots`, the files SSL_CERT_FILE and SSL_CERT_DIR
name in place of the system's store when either variable is set; or
both. With neither, a connection that verifies the authority is
refused. The server's certificate must be issued by one of them, not by
itself, and of `ServerName`, or else of the host's name.
`VerifyAuthority` and `VerifyName` turn either check off. The TLS
version is the platform's choice: .NET's TLS speaks TLS 1.3 on Linux and
on Windows 11 and Windows Server 2022 or later, and TLS 1.2 at most on
macOS.
`CertificateFile` and `KeyFile` name the PEM files of a certificate
chain and its key, which the client presents.

### What a connection asks for, and what it is told

`Offer.Default` asks for every capability but compression. `Offer.None()`
asks for none, and the connection then sees the protocol's first form,
byte for byte; `Compressed()` adds compression, which begins after Ready:
a frame of `CompressFrom` bytes or more, its header included, 1024 unless
set, goes compressed where it comes out shorter;
`WithAttribute` adds a connection attribute, such as `Offer.Application`,
which every server reads, granted or not. `Has` tells whether a
capability was granted; a method that needs one not granted fails before
it sends anything.

`Settings` holds each session setting as the server last told it, at
login and after each statement that changed it; `Setting` reads one, or
null. `StatusNames` names those the server tells besides the settings a
session sets for itself. `InTransaction` tells whether a transaction is
open, as the server last said.

### Notices

A Notice the server sends, before the reply of the statement that raised
it, goes to `NoticeHandler` as it comes; with no handler the last 64 are
kept, the oldest dropped first, and `TakeNotices` takes them.

### Controlling the session

`PingAsync`, once `ping` is granted, asks whether the server answers,
with no worker of its taken; it may be sent at any point after Ready.
`ResetAsync`, once `reset` is granted, returns the session to its state
at login: its transaction rolled back, told by notice 01T01; its
prepared statements, cursors, a copy, temporary tables and settings
gone, each setting told again; its authentication and cancel key kept.
`ChangeUserAsync`, once `change-user` is granted, logs in again as
`user` by the steps of a login, and renews the session as `ResetAsync`
does, as that user's, under a new cancel key; no transaction is active
after it, and the last statement's warning is gone. A refusal ends the
connection; a SCRAM mechanism's keys are kept in its cache, as
turborel-client keeps them. `SetQueryAttributes`, once
`query-attributes` is granted, sets the attributes each later Query and
Execute carries, read in SQL by QUERY_ATTRIBUTE; none, for none.

### Timeouts

`Timeouts.Connect`, 30 seconds by default, bounds connecting, TLS and
each step of the login; `Timeouts.Read`, none by default, bounds each
read and write after. `SetReadTimeout` changes a connection's. A named
pipe on Windows is bounded only as it is opened: `SetReadTimeout` keeps
its value, and `ReadTimeout` says it, but it bounds no read. A wait that runs out,
or a `CancellationToken` cancelled during one, loses the connection.
`MaxBody` bounds a reply's body, 16 MiB by default; a longer reply loses
the connection. One longer than .NET's longest array is refused as it is
set, SQLSTATE TB910.

A reply whose frame is whole but holds a value the client cannot take,
as damaged JSON or a truth value other than 0 or 1, fails its request
with that value's error, SQLCODE -916, or -900 of JSON past Turborel's
limits. The rest of the request's replies are read past first, so the
next request gets its own, and the connection goes on. A frame that is
itself malformed, cut short or with bytes after its last field, loses
the connection, SQLSTATE 08006 ([Appendix A: Types](../appendix/a-types.md)).

### Ending

`TerminateAsync` sends Terminate: the session ends, its transaction
committed or rolled back as SESSION.COMMIT_AT_END says, and the
connection closes. A Notice or a Status the server sends before closing
is read past; an Error it sends, as of a commit that fails then, is
given. It waits at most the read timeout for the server's close, or 60
seconds without one; over a stream given to `OpenAsync`, as the stream's
maker set it. `DisposeAsync` closes the connection
without a word. A connection lost, by a failure or a timeout, says so by
`IsLost`, and fails each later call.

### Cancelling

`Canceller` gives a cancel of the connection's running statement, from
another thread: `CancelAsync` connects to the target again, sends Cancel
with the key Ready gave as the connection's first and only message, and
reads until the server closes. The statement fails with SQLCODE -920,
SQLSTATE TB920. It gives up after 10 seconds, `CancelWithin`, or the
limit `within` gives.

## ERRORS

| SQLSTATE | SQLCODE | When |
|---|---|---|
| 28000 | -919 | A password or SCRAM over plain TCP, unless SCRAM is asked in the clear; a SCRAM exchange refused; the server's refusal of the login |
| 08004 | -919 | The server refuses the protocol's version |
| 08006 | -919 | No connection made, or no answer, in time; TLS refused, saying why; a reply past `MaxBody`; a malformed frame; Ready before the server proved itself to SCRAM; a read timeout set on a connection over a stream given to `OpenAsync` |
| TB916 | -916 | A value the client cannot take, in a frame read whole: its request's alone, the connection kept |
| 40002 | -913 | `TerminateAsync`: the commit at the session's end failed |
| TB910 | -910 | A null where a value is needed; `MaxBody` longer than .NET's longest array |

A failure the server sends at login is given whole, with its diagnostics,
and ends the connection: one of class 08 but 08003, or of class 28.

## EXAMPLES

See [Connecting](../user/02-connecting.md), whose programs connect by
each target and mechanism, and print what they are told, and
[Transactions](../user/05-transactions.md), which resets a session and
changes its user.

## STANDARD

[PROTOCOL The Native Protocol], [PROTOCOL Opening a connection],
[PROTOCOL Mechanisms], [PROTOCOL Capabilities], [PROTOCOL Hello 'H'],
[PROTOCOL Ready 'R'], [PROTOCOL Status 'T'], [PROTOCOL Error 'E'],
[PROTOCOL Cancel 'Z'], [PROTOCOL Terminate 'X'], [PROTOCOL Ping 'G'],
[PROTOCOL Reset 'W'], [PROTOCOL ChangeUser 'U'] and [PROTOCOL Notice 'N'] of
[The Native Protocol](https://github.com/turborel/turborel/blob/main/doc/client/05-protocol.md);
[CLIENT Connecting], [CLIENT Timeouts], [CLIENT What a connection asks
for], [CLIENT What the server tells], [CLIENT The SCRAM key cache],
[CLIENT Running statements] and [CLIENT Controlling the session] of
[The Client Library](https://github.com/turborel/turborel/blob/main/doc/client/02-library.md).

## SEE ALSO

[Connecting](../user/02-connecting.md), [Errors](30-errors.md),
[Appendix B: Errors](../appendix/b-errors.md).
