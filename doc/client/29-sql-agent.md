# The SQL-agent

[Contents](README.md)

## NAME

`SqlAgent`: a program's connections, each named, one current, and the
connection statements CONNECT, SET CONNECTION and DISCONNECT.

## SYNOPSIS

```csharp
namespace Turborel.Data.Agent;

public interface IConnector
{
    Task<NativeConnection> ConnectAsync(string? server, string? user, CancellationToken cancellationToken = default);
}
public sealed class SqlAgent
{
    public SqlAgent(IConnector connector);
    public SqlAgent(IConnector connector, NativeConnection connection);
    public NativeConnection? CurrentConnection { get; }
    public string? CurrentName { get; }
    public Canceller? Canceller { get; }
    public string? Warning { get; }
    public bool IsLost { get; }
    public NativeConnection Current();
    public Task<Outcome> QueryAsync(string sql, CancellationToken cancellationToken = default);
    public Task<Streamed> QueryIntoAsync(string sql, IRowSink sink, CancellationToken cancellationToken = default);
    public Task<Completed?> ConnectionStatementAsync(string sql, CancellationToken cancellationToken = default);
    public Task TerminateAsync();
}
```

## DESCRIPTION

`QueryAsync` runs a connection statement itself, its completion SQLCODE
0; every other statement goes to the current connection. Each name is a
character string literal, its spaces trimmed, read as an identifier, a
regular one in upper case.

`CONNECT TO 'server' [AS 'name'] [USER 'user']` makes a connection
through the connector, to the server the address names, as the user;
named by AS, else by its server, it is made current, the one current
before dormant. `CONNECT TO DEFAULT` makes the default connection.
`SET CONNECTION 'name'`, or `DEFAULT`, makes that connection current.
`DISCONNECT 'name'`, `DEFAULT`, `CURRENT` or `ALL` ends each connection
it names, and none is current after if the current one was; a failure
ending one makes the completion a warning, 01002. The second constructor
makes an agent whose default connection, current, is the one given.
`TerminateAsync` ends every connection.

## ERRORS

| SQLSTATE | SQLCODE | When |
|---|---|---|
| 0A001 | -900 | CONNECT or SET CONNECTION while the current connection's transaction is active |
| 28000 | -104 | A user that is no authorization identifier |
| 2E000 | -919 | A connection's name that is no identifier |
| 08002 | -919 | A name in use |
| 08001 | -919 | A server that cannot be reached |
| 08004 | -919 | A server that refuses the connection |
| 08003 | -919 | No such connection, or no current one |
| 25000 | -917 | DISCONNECT of a connection whose transaction is active |
| 42000 | -102, -103 | A statement that begins as a connection statement and is not one; a name not a character string literal |

## EXAMPLES

See [Pipelines and Pools](../user/08-pipelines-and-pools.md).

## STANDARD

[CLIENT The SQL-agent] of
[The Client Library](https://github.com/turborel/turborel/blob/main/doc/client/02-library.md);
ISO/IEC 9075:1992 §15.

## SEE ALSO

[Connections](21-connections.md).
