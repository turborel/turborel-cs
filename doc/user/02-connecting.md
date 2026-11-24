# Connecting

[Contents](README.md)

A program reaches a server over TCP, a Unix socket or, on Windows, a
named pipe, and proves who it is by one of the server's mechanisms. This
chapter connects with the low-level client, `NativeConnection`; the
standard API's `TurborelConnection`, built over it, makes the same
connections from a connection string's settings ([Installing](01-installing.md),
[Settings](09-settings.md)). The examples' server listens on port 17435, with TLS, a
Unix socket named `sock`, and the user ANN, whose password is
`ann's password`.

## A first connection

`NativeConnection.ConnectAsync` takes where the server is, a target; the
user; and the mechanism that proves the user. The connection it gives
holds what the server told as it logged in: the session's authorization
identifier, the server's product, each session setting, and the
capabilities granted.

```csharp
using Turborel.Data.Authentication;
using Turborel.Data.Session;
using Turborel.Data.Transport;

await using NativeConnection connection =
    await NativeConnection.ConnectAsync(new TcpTarget("localhost", 17435), "ann", new TrustMechanism());
Console.WriteLine($"{connection.Authorization} on {connection.Server}");
Console.WriteLine($"schema {connection.Setting(StatusNames.Schema)}, time zone {connection.Setting("SESSION.TIME_ZONE")}");
Console.WriteLine(string.Join(" ", connection.Granted));
await connection.TerminateAsync();
```

```
ANN on turborel 0.0.0 (alpha)
schema ANN, time zone +00:00
notices status diagnostics describe cursors ping reset change-user query-attributes connection-attributes multi-statement commit-position copy row-counts generated-values fetch-ends conditions declared-types parameter-modes
```

`trust` proves nothing: the server takes it only from a client of its
own machine, and only while its operator has set SERVER.TRUST_LOCAL ON.
A setting the server tells is read with no round trip, as it last told
it; a statement that changes one is followed by its new value.

`TerminateAsync` ends the session, its transaction committed or rolled
back as SESSION.COMMIT_AT_END says, and the connection. Disposing a
connection closes it without a word to the server.

## Where the server is

| Target | Reaches |
|---|---|
| `new TcpTarget(host, port)` | TCP, port 7422 unless another is given |
| `new TlsTarget(host, port, options)` | TCP, then TLS on the same port |
| `new LocalTarget(path)` | a Unix socket; on Windows a named pipe, such as `\\.\pipe\turborel` |

Over TLS the client trusts only the authorities it is told to: those of
a PEM file, `RootsFile`; the platform's, `SystemRoots`; or both. The
platform's are the files the variables SSL_CERT_FILE and SSL_CERT_DIR
name, when either is set, and else the system's store. The server's
certificate must be issued by one of them, not by itself, and be of the
host's name, or of `ServerName` if it is given.

## Proving who connects

| Mechanism | How |
|---|---|
| `new TrustMechanism()` | as the user named, with no proof |
| `new PasswordMechanism(password)` | the password sent whole |
| `new ScramMechanism(password)` | SCRAM-SHA-256: the client proves it knows the password, and the server that it knows the user's verifier; the password never travels |
| `new CertificateMechanism()` | the certificate TLS presented, as the user its common name names |

A password, and SCRAM, are sent only over TLS, a Unix socket or a named
pipe. Over TLS, SCRAM binds the exchange to the server's certificate, as
SCRAM-SHA-256-PLUS, so a server in the middle cannot relay it; with
`Binding` set to `Require`, a login that is not bound is refused, so each
login below is SCRAM-SHA-256-PLUS. A `KeyCache` given to the mechanism
keeps the keys a password derived, so that a later login to the same
server skips the derivation:

```csharp
using Turborel.Data.Authentication;
using Turborel.Data.Session;
using Turborel.Data.Tls;

var tls = new TlsTarget("localhost", 17435, new TlsOptions { RootsFile = "ca.pem" });
var cache = new KeyCache();
for (int i = 0; i < 2; i++)
{
    var scram = new ScramMechanism("ann's password") { Cache = cache, Binding = ChannelBinding.Require };
    await using NativeConnection connection = await NativeConnection.ConnectAsync(tls, "ann", scram);
    Console.WriteLine($"{connection.Authorization}, by {ScramMechanism.PlusName}");
    await connection.TerminateAsync();
}

Console.WriteLine($"hits and misses: {cache.Lookups}");
```

```
ANN, by scram-sha-256-plus
ANN, by scram-sha-256-plus
hits and misses: (1, 1)
```

A login refused is a `TurborelException`, SQLSTATE 28000; a secret
mechanism over plain TCP is refused before the client connects:

```csharp
using Turborel.Data;
using Turborel.Data.Authentication;
using Turborel.Data.Session;
using Turborel.Data.Tls;
using Turborel.Data.Transport;

var tls = new TlsTarget("localhost", 17435, new TlsOptions { RootsFile = "ca.pem" });
foreach ((Target target, string password) in new (Target, string)[] { (tls, "wrong"), (new TcpTarget("localhost", 17435), "ann's password") })
{
    try
    {
        await using NativeConnection connection =
            await NativeConnection.ConnectAsync(target, "ann", new ScramMechanism(password));
    }
    catch (TurborelException e)
    {
        Console.WriteLine($"SQLCODE {e.SqlCode} (SQLSTATE {e.SqlState}): {e.Message}");
    }
}
```

```
SQLCODE -919 (SQLSTATE 28000): the user or the password is wrong
SQLCODE -919 (SQLSTATE 28000): [DESIGN] scram-sha-256 is sent only over TLS, a Unix socket or a named pipe, never over plain TCP
```

## What a connection asks for, and how long it waits

`ConnectOptions` holds the offer, the capabilities the connection asks
for and the attributes it tells the server, and the timeouts. A
connection asks for every capability but compression unless its program
says otherwise. The connect timeout, 30 seconds unless set, bounds
connecting, TLS and each step of the login; the read timeout, none
unless set, bounds each read and write after. A wait that runs out loses
the connection, SQLSTATE 08006, saying the server did not answer in
time.

```csharp
using Turborel.Data.Authentication;
using Turborel.Data.Session;
using Turborel.Data.Transport;

var options = new ConnectOptions
{
    Offer = Offer.Default.Compressed().WithAttribute(Offer.Application, "payroll"),
    Timeouts = new Timeouts(Connect: TimeSpan.FromSeconds(5), Read: TimeSpan.FromMinutes(1)),
};
await using NativeConnection connection =
    await NativeConnection.ConnectAsync(new LocalTarget("sock"), "ann", new PasswordMechanism("ann's password"), options);
Console.WriteLine($"compressed: {connection.Has("compression")}; read timeout {connection.ReadTimeout}");
await connection.Canceller!.CancelAsync();
await connection.TerminateAsync();
Console.WriteLine($"lost: {connection.IsLost}");
```

```
compressed: True; read timeout 00:01:00
lost: True
```

## Cancelling

A connection's `Canceller` cancels the statement it runs, from another
thread: it connects to the server again and sends the key the server
gave at login, and the statement fails with SQLCODE -920, SQLSTATE
TB920. A cancel when nothing runs does nothing, as above. A cancel gives
up after 10 seconds, or the limit its program gives.

## See also

[Connections](../client/21-connections.md) in the API Reference;
[Users and Authentication](https://github.com/turborel/turborel/blob/main/doc/operator/05-users.md)
and
[Connections](https://github.com/turborel/turborel/blob/main/doc/operator/06-network.md)
in Turborel's manual, for the server's side.
