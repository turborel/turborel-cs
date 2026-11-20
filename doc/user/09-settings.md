# Settings

[Contents](README.md)

A program can connect by settings rather than by a target and a
mechanism of its own: `ConnectionSettings` says where the servers are, as
whom to connect, how, and what a server must be, and `ConnectAsync`
connects as it says. A password the settings give, or the password file,
is proved by SCRAM-SHA-256; with none, the client's certificate if one is
named, else the user is trusted as a local client.

## Where settings come from

Each setting has one name, the same in the program's options, in a
`turborel://` URI and in a service file ([Appendix C](../appendix/c-settings.md)).
They come in layers, and each is taken from the first layer that has it:
the program's own, a `ConnectionOptions` or a URI; the section of the
service file the program or the environment names; the environment's
TURBOREL_ variables, a variable set but empty giving nothing; then the
defaults. A setting that cannot be read, a service that cannot be read,
and settings that name no user are refused, SQLSTATE 08001.

```text
turborel://[user[:password]@][host[:port][,host[:port]]...][/][?setting=value[&setting=value]...]
```

A URI's parts are percent-encoded, so `%2Frun%2Fturborel.sock` is a Unix
socket; an IPv6 address is written in brackets. A service file holds
sections, `[name]`, of lines `setting = value`; `#` begins a comment. A
password file holds lines `host:port:user:password`, `*` matching
anything and `\` escaping the character after it; it is read only if it
is a regular file, or a link to one.

## Several servers, and TLS

`host` may name several servers, tried in the order given or, with
`load_balance=random`, in a random order for each connection; a name is
tried at each address it resolves to. A server that cannot be reached,
refuses the user, or is not of the role `target` asks is passed for the
next; with none kept, the failure names each and why. `read-write` keeps
a server alone or its group's primary, `read-only` a replica; the role is
read from SERVER_READ_ONLY, which the server tells every session.

`tls_mode` says how a connection over TCP uses TLS: `disable`; `prefer`,
TLS if the server takes it, else plain TCP; `require`; `verify-ca`, the
server's certificate issued by an authority trusted; `verify-full`, and
of `server_name`, else the host's name. A Unix socket or named pipe uses
none. `channel_binding=require` refuses settings whose connection SCRAM
cannot bind, SQLSTATE 28000. One key cache keeps the SCRAM keys of every
connection the same settings make.

```csharp
using Turborel.Data;
using Turborel.Data.Configuration;
using Turborel.Data.Session;

ConnectionSettings byUri = ConnectionSettings.FromUri(
    "turborel://ann@127.0.0.1:1,127.0.0.1:17435/?tls_mode=require&application_name=ledger&connect_timeout=5");
await using (NativeConnection connection = await byUri.ConnectAsync())
{
    Console.WriteLine($"{connection.Server}, as {connection.Authorization}, over {connection.Target}");
    Console.WriteLine($"the server is {ServerRole.Describe(await ServerRole.OfAsync(connection))}");
}

File.WriteAllText("services.conf", "# services\n[ledger]\nhost = localhost:17435\nuser = ann\ntls_mode = verify-full\ntls_roots = ca.pem\n");
File.WriteAllText("passwords", "*:17435:ann:ann's password\n");
ConnectionOptions options = new ConnectionOptions()
    .Set(ConnectionOptions.Service, "ledger")
    .Set(ConnectionOptions.ServiceFile, "services.conf")
    .Set(ConnectionOptions.Passfile, "passwords");
ConnectionSettings byService = ConnectionSettings.Resolve(options);
Console.WriteLine(byService);
for (int i = 0; i < 2; i++)
{
    await using NativeConnection connection = await byService.ConnectAsync();
    Console.WriteLine($"{connection.Authorization} over {connection.Target}");
}

Console.WriteLine($"key cache hits and misses: {byService.KeyCache.Lookups}");
try
{
    ConnectionSettings.FromUri("turborel://ann@db/?tls_mode=always");
}
catch (TurborelException e)
{
    Console.WriteLine($"SQLSTATE {e.SqlState}: {e.Message}");
}
```

```
turborel 0.0.0 (alpha), as ANN, over Tls(127.0.0.1:17435)
the server is read-write
ConnectionSettings(TcpHost { Name = localhost, Port = 17435 }, ann, VerifyFull, Any)
ANN over Tls(127.0.0.1:17435)
ANN over Tls(127.0.0.1:17435)
key cache hits and misses: (1, 1)
SQLSTATE 08001: [DESIGN] tls_mode is always, not one of disable, prefer, require, verify-ca, verify-full
```

The example's server listens on 127.0.0.1 alone, so `localhost`'s first
address, ::1, is passed when it has one.

## See also

[Settings](../client/26-settings.md) in the API Reference;
[Appendix C: Settings](../appendix/c-settings.md).
