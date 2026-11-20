// An SQL-agent [CLIENT The SQL-agent] [ISO/IEC 9075:1992 §15], as
// turborel-client's: a program's connections to servers, each named, one
// current, and the statements that make, choose and end them, CONNECT,
// SET CONNECTION and DISCONNECT, which it runs itself; every other
// statement goes to the current connection's session. A connector the
// program gives makes a connection to the server an address names, as the
// user a statement names.

using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Import;
using Turborel.Data.Session;

namespace Turborel.Data.Agent;

/// <summary>What makes an agent's connections.</summary>
public interface IConnector
{
    /// <summary>Makes a connection to the server <paramref name="server"/> names, an address, or for none the default server; as <paramref name="user"/>, or for none the user the connector takes.</summary>
    /// <param name="server">The server's address, or null.</param>
    /// <param name="user">The user, or null.</param>
    /// <param name="cancellationToken">Cancels the connecting.</param>
    /// <returns>The connection.</returns>
    /// <exception cref="TurborelException">The server cannot be reached, or refuses the connection.</exception>
    public Task<NativeConnection> ConnectAsync(string? server, string? user, CancellationToken cancellationToken = default);
}

/// <summary>An SQL-agent's connections, each named, one current.</summary>
public sealed class SqlAgent
{
    private readonly IConnector _connector;
    private readonly List<(string? Name, NativeConnection Connection)> _held = [];
    private int? _current;

    /// <summary>Initializes a new instance of the <see cref="SqlAgent"/> class, with no connection.</summary>
    /// <param name="connector">What makes its connections.</param>
    public SqlAgent(IConnector connector)
    {
        Misuse.ThrowIfNull(connector);
        _connector = connector;
    }

    /// <summary>Initializes a new instance of the <see cref="SqlAgent"/> class whose default connection, current, is <paramref name="connection"/>: as a client begins, connected.</summary>
    /// <param name="connector">What makes its connections.</param>
    /// <param name="connection">Its default connection.</param>
    public SqlAgent(IConnector connector, NativeConnection connection)
        : this(connector)
    {
        Misuse.ThrowIfNull(connection);
        _held.Add((null, connection));
        _current = 0;
    }

    /// <summary>Gets the current connection, if there is one, to read what its server last told it.</summary>
    public NativeConnection? CurrentConnection => _current is int i ? _held[i].Connection : null;

    /// <summary>Gets the current connection's name; null for the default one, or for no connection.</summary>
    public string? CurrentName => _current is int i ? _held[i].Name : null;

    /// <summary>Gets what cancels the current connection's statement, if any.</summary>
    public Canceller? Canceller => CurrentConnection?.Canceller;

    /// <summary>Gets the SQLSTATE of the warning the current connection's last statement completed with, if it did.</summary>
    public string? Warning => CurrentConnection?.Warning;

    /// <summary>Gets a value indicating whether the current connection is lost; none is not.</summary>
    public bool IsLost => CurrentConnection?.IsLost ?? false;

    /// <summary>Gives the current connection.</summary>
    /// <returns>The connection.</returns>
    /// <exception cref="TurborelException">SQLSTATE 08003 if there is none.</exception>
    public NativeConnection Current() =>
        CurrentConnection ?? throw Exception("08003", "[ISO/IEC 9075:1992 §15.3 GR9] there is no current connection");

    /// <summary>Runs <paramref name="sql"/>: a connection statement itself, its completion; any other in the current connection's session.</summary>
    /// <param name="sql">The statement.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>What it gave.</returns>
    /// <exception cref="TurborelException">A connection statement's, as each rule says, or the statement's.</exception>
    public async Task<Outcome> QueryAsync(string sql, CancellationToken cancellationToken = default) =>
        await ConnectionStatementAsync(sql, cancellationToken).ConfigureAwait(false) is { } done
            ? new DoneOutcome(done)
            : await Current().QueryAsync(sql, cancellationToken).ConfigureAwait(false);

    /// <summary>Runs <paramref name="sql"/> as <see cref="QueryAsync"/> does, a query's rows given to <paramref name="sink"/>.</summary>
    /// <param name="sql">The statement.</param>
    /// <param name="sink">What takes the rows.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The rows sent, or a completion.</returns>
    /// <exception cref="TurborelException">As <see cref="QueryAsync"/>'s.</exception>
    public async Task<Streamed> QueryIntoAsync(string sql, IRowSink sink, CancellationToken cancellationToken = default) =>
        await ConnectionStatementAsync(sql, cancellationToken).ConfigureAwait(false) is { } done
            ? new Streamed(null, done)
            : await Current().QueryIntoAsync(sql, sink, cancellationToken).ConfigureAwait(false);

    /// <summary>Runs <paramref name="sql"/>, if a connection statement: its completion; null if it is another.</summary>
    /// <param name="sql">The statement.</param>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The completion, or null.</returns>
    /// <exception cref="TurborelException">
    /// The statement's: SQLSTATE 0A001 to leave an active transaction; 28000 for a user that is no
    /// authorization identifier; 2E000 for a name that is no identifier; 08002 for a name in use;
    /// 08001 for a server that cannot be reached, 08004 for one that refuses; 08003 for no such
    /// connection; 25000 to end one whose transaction is active.
    /// </exception>
    public async Task<Completed?> ConnectionStatementAsync(string sql, CancellationToken cancellationToken = default)
    {
        Misuse.ThrowIfNull(sql);
        if (ConnectionStatement.Read(sql) is not { } statement)
        {
            return null;
        }

        return statement.Verb switch
        {
            ConnectionVerb.Connect => await ConnectAsync(statement.Server, statement.Name, statement.User, cancellationToken).ConfigureAwait(false),
            ConnectionVerb.ConnectDefault => await ConnectAsync(null, null, null, cancellationToken).ConfigureAwait(false),
            ConnectionVerb.Set => SetConnection(statement.Name),
            _ => await DisconnectAsync(statement, cancellationToken).ConfigureAwait(false),
        };
    }

    /// <summary>Ends every connection, as the client ends.</summary>
    /// <returns>The task.</returns>
    /// <exception cref="TurborelException">The first failure ending one; every one is ended still.</exception>
    public async Task TerminateAsync()
    {
        TurborelException? first = null;
        foreach ((_, NativeConnection connection) in _held)
        {
            try
            {
                await connection.TerminateAsync().ConfigureAwait(false);
            }
            catch (TurborelException e)
            {
                first ??= e;
            }
        }

        _held.Clear();
        _current = null;
        if (first is not null)
        {
            throw first;
        }
    }

    // A connection exception of state, its SQLCODE by its class.
    private static TurborelException Exception(string state, string message)
    {
        int code = state[..2] switch
        {
            "28" => -104,
            "0A" => -900,
            _ => -919,
        };
        return new TurborelException(code, state, message);
    }

    // Refuses to leave the current connection's active transaction: there
    // are no transactions of more than one server [§15.1 GR1, §15.2 GR1].
    private void NotInTransaction()
    {
        if (CurrentConnection?.InTransaction == true)
        {
            throw Exception("0A001", "[ISO/IEC 9075:1992 §15.1 GR1, §15.2 GR1] the current connection's transaction is active, and none spans servers");
        }
    }

    private int? Find(string? name)
    {
        int i = _held.FindIndex(h => h.Name == name);
        return i < 0 ? null : i;
    }

    // CONNECT TO server [AS name] [USER user], or TO DEFAULT for no server:
    // the connection made current, the one current before dormant.
    private async Task<Completed> ConnectAsync(SimpleValue? server, SimpleValue? name, SimpleValue? user, CancellationToken cancellationToken)
    {
        NotInTransaction();
        string? userText = user?.Text();
        if (userText is not null && SqlNames.Identifier(userText) is null)
        {
            throw Exception("28000", $"[ISO/IEC 9075:1992 §15.1 GR3] '{userText}' is not an <authorization identifier>");
        }

        string? address = server?.Text();
        string? named = name?.Text() ?? address;
        string? identified = named is null
            ? null
            : SqlNames.Identifier(named) ?? throw Exception("2E000", $"[ISO/IEC 9075:1992 §15.1 GR6] '{named}' is not an <identifier>");
        if (Find(identified) is not null)
        {
            string what = identified is null ? "the default connection" : $"the connection {identified}";
            throw Exception("08002", $"[ISO/IEC 9075:1992 §15.1 GR7] {what} is established");
        }

        NativeConnection connection;
        try
        {
            connection = await _connector.ConnectAsync(address, userText, cancellationToken).ConfigureAwait(false);
        }
        catch (TurborelException failure)
        {
            string state = failure.SqlState == "08006" ? "08001" : "08004";
            throw Exception(state, $"[ISO/IEC 9075:1992 §15.1 GR10, GR11] {failure.Message}");
        }

        _held.Add((identified, connection));
        _current = _held.Count - 1;
        return Completed.Of(0, "00000");
    }

    // SET CONNECTION name, or DEFAULT for none: that connection current.
    private Completed SetConnection(SimpleValue? name)
    {
        NotInTransaction();
        string? text = name?.Text();
        string? identified = text is null ? null : SqlNames.Identifier(text) ?? text;
        _current = Find(identified)
            ?? throw Exception("08003", $"[ISO/IEC 9075:1992 §15.2 GR2] there is {(identified is null ? "no default connection" : $"no connection {identified}")}");
        return Completed.Of(0, "00000");
    }

    // DISCONNECT: each connection it names ended; none current after, if
    // the current one was; an error ending one a warning, 01002.
    private async Task<Completed> DisconnectAsync(ConnectionStatement statement, CancellationToken cancellationToken)
    {
        TurborelException Missing(string what) => Exception("08003", $"[ISO/IEC 9075:1992 §15.3 GR1-GR3] there is {what}");
        int[] ended = statement.Object switch
        {
            DisconnectObject.All => [.. Enumerable.Range(0, _held.Count)],
            DisconnectObject.Current => [_current ?? throw Missing("no current connection")],
            DisconnectObject.Default => [Find(null) ?? throw Missing("no default connection")],
            _ => [Named(statement.Name!, Missing)],
        };
        if (ended.Any(i => _held[i].Connection.InTransaction))
        {
            throw new TurborelException(-917, "25000", "[ISO/IEC 9075:1992 §15.3 GR6] a connection's transaction is active");
        }

        string? current = _current is int c ? _held[c].Name : null;
        bool wasCurrent = _current is int k && ended.Contains(k);
        bool warned = false;
        foreach (int i in ended.OrderDescending())
        {
            try
            {
                await _held[i].Connection.TerminateAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (TurborelException)
            {
                warned = true;
            }

            _held.RemoveAt(i);
        }

        _current = wasCurrent || _current is null ? null : Find(current);
        return Completed.Of(0, warned ? "01002" : "00000");
    }

    private int Named(SimpleValue name, System.Func<string, TurborelException> missing)
    {
        string text = name.Text();
        string identified = SqlNames.Identifier(text) ?? text;
        return Find(identified) ?? throw missing($"no connection {identified}");
    }
}
