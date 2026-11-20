# TurborelTransaction

[Contents](README.md)

## NAME

`TurborelTransaction`: a transaction of a connection, of an isolation
level, its savepoints, ended by commit or rollback.

## SYNOPSIS

```csharp
namespace Turborel.Data;

public sealed class TurborelTransaction : DbTransaction
{
    public override IsolationLevel IsolationLevel { get; }
    public override bool SupportsSavepoints { get; }   // true

    public override Task CommitAsync(CancellationToken cancellationToken = default);
    public override Task RollbackAsync(CancellationToken cancellationToken = default);
    public override Task SaveAsync(string savepointName, CancellationToken cancellationToken = default);
    public override Task RollbackAsync(string savepointName, CancellationToken cancellationToken = default);
    public override Task ReleaseAsync(string savepointName, CancellationToken cancellationToken = default);

    // Commit, Rollback, Save and Release wait on the same tasks.
}
```

## DESCRIPTION

`TurborelConnection.BeginTransaction`, or `BeginTransactionAsync`, begins
a transaction: it sets the isolation level asked by SET TRANSACTION and
turns the connection's autocommit off, so the statements after are of
one transaction until it ends.

| `IsolationLevel` | Sent |
|---|---|
| `Unspecified` | nothing: the default, READ COMMITTED unless the setting TRANSACTION.ISOLATION says another |
| `ReadUncommitted` | SET TRANSACTION ISOLATION LEVEL READ UNCOMMITTED |
| `ReadCommitted` | SET TRANSACTION ISOLATION LEVEL READ COMMITTED |
| `RepeatableRead`, `Snapshot` | SET TRANSACTION ISOLATION LEVEL REPEATABLE READ, which reads at one snapshot |
| `Serializable` | SET TRANSACTION ISOLATION LEVEL SERIALIZABLE |

`IsolationLevel` is the level asked. As SQL makes it, a READ UNCOMMITTED
transaction is READ ONLY: a statement that changes data in it fails,
SQLSTATE 25000.

`CommitAsync` commits by COMMIT, `RollbackAsync` rolls back by ROLLBACK;
either ends the transaction, even when it fails, and turns autocommit
back on. `SaveAsync` sets a savepoint by SAVEPOINT, `RollbackAsync` of a
name rolls back to it by ROLLBACK TO SAVEPOINT, and `ReleaseAsync`
releases it by RELEASE SAVEPOINT; a savepoint's name is sent as a
delimited identifier, as given. A transaction disposed before it ends is
rolled back, as is one whose connection is closed.

## ERRORS

| SQLSTATE | SQLCODE | When |
|---|---|---|
| (the statement's) | | The server's failure of SET TRANSACTION, COMMIT or a savepoint's statement; a failing SET TRANSACTION leaves autocommit on and no transaction begun |
| 25000 | | A change in a READ UNCOMMITTED transaction, as the server reports it |
| TB910 | -910 | A transaction begun while one is, or while a reader is open on the connection; an isolation level turborel has not, as `Chaos`; a commit, rollback or savepoint while a reader is open, once the transaction has ended, or of one that is not its connection's, as one left from before the connection was closed |

## EXAMPLES

See [Transactions](../user/05-transactions.md) of the User's Guide.

## STANDARD

ADO.NET's `DbTransaction`, of `System.Data.Common`; RULES.md D2, D17 and
D20. The statements it sends, as Turborel's SQL reference describes
them:
[SET TRANSACTION](https://github.com/turborel/turborel/blob/main/doc/sql/statements/set-transaction.md),
[COMMIT](https://github.com/turborel/turborel/blob/main/doc/sql/statements/commit.md),
[ROLLBACK](https://github.com/turborel/turborel/blob/main/doc/sql/statements/rollback.md),
[SAVEPOINT](https://github.com/turborel/turborel/blob/main/doc/sql/statements/savepoint.md) and
[RELEASE SAVEPOINT](https://github.com/turborel/turborel/blob/main/doc/sql/statements/release-savepoint.md).

## SEE ALSO

[TurborelConnection](02-api-connection.md),
[TurborelCommand](03-api-command.md),
[Statements and Cursors](22-statements.md).
