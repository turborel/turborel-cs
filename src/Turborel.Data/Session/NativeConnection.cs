// A connection to a server over the native protocol: one session of the
// server's [CLIENT Connecting]. It opens over a target, TCP, a local socket
// or pipe, or TLS, or over a stream the program opened; logs in by a
// mechanism, Challenge by Answer until Ready [PROTOCOL Opening a
// connection]; and ends by Terminate. Every failure is a TurborelException,
// and one that loses the connection leaves it taking no more.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Authentication;
using Turborel.Data.Protocol;
using Turborel.Data.Tls;
using Turborel.Data.Transport;

namespace Turborel.Data.Session;

/// <summary>A connection to a turborel server: one session of the server's.</summary>
public sealed partial class NativeConnection : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly bool _bounded;
    private readonly Told _told = new();
    private TurborelException? _lost;
    private ReadOnlyMemory<byte> _key;

    private NativeConnection(Stream stream, Target? target, bool bounded)
    {
        _stream = stream;
        Target = target;
        _bounded = bounded;
    }

    /// <summary>Gets the session's authorization identifier, as Ready told it.</summary>
    public string Authorization { get; private set; } = string.Empty;

    /// <summary>Gets the server's product, as <c>turborel 0.0.0 (alpha)</c>.</summary>
    public string Server { get; private set; } = string.Empty;

    /// <summary>Gets where the connection reached its server; null of a stream the program gave.</summary>
    public Target? Target { get; }

    /// <summary>Gets the capabilities granted, in the order of the protocol's table.</summary>
    public IReadOnlyList<string> Granted => _told.Granted;

    /// <summary>Gets the session's settings, and what else the server tells by Status, as it last told them.</summary>
    public IReadOnlyDictionary<string, string> Settings => _told.Settings;

    /// <summary>Gets a value indicating whether the connection is lost: it takes no more.</summary>
    public bool IsLost => _lost is not null;

    /// <summary>Gets a value indicating whether the session's transaction is active, as the server said after the last statement.</summary>
    public bool InTransaction { get; private set; }

    /// <summary>Gets or sets the longest reply's body taken, 16 MiB unless the program says; a longer one loses the connection.</summary>
    public uint MaxBody { get; set; } = FrameHeader.MaxBody;

    /// <summary>Gets what cancels the statement this connection runs, from another thread; null of a stream the program gave.</summary>
    public Canceller? Canceller => Target is null ? null : new Canceller(Target, _key);

    /// <summary>Gets how long each read or write waits after the login; null for no limit.</summary>
    public TimeSpan? ReadTimeout { get; private set; }

    /// <summary>Connects to <paramref name="target"/> as <paramref name="user"/>, proved by <paramref name="mechanism"/>.</summary>
    /// <param name="target">Where the server is.</param>
    /// <param name="user">The user, read by the server as an identifier.</param>
    /// <param name="mechanism">What proves the user.</param>
    /// <param name="options">What it asks for and how long it waits; the defaults if null.</param>
    /// <param name="cancellationToken">Cancels the connecting.</param>
    /// <returns>The connection, logged in.</returns>
    /// <exception cref="TurborelException">
    /// SQLSTATE 28000 before anything is sent for a secret mechanism over plain
    /// TCP; the server's refusal; a connection failure, 08006, if none is made in time.
    /// </exception>
    public static async Task<NativeConnection> ConnectAsync(
        Target target,
        string user,
        Mechanism mechanism,
        ConnectOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(mechanism);
        options ??= ConnectOptions.Default;
        if (target.IsPlainTcp)
        {
            RefuseInTheClear(mechanism);
        }

        (Stream stream, TlsChannel? channel) = await target.OpenAsync(options.Timeouts.Connect, cancellationToken).ConfigureAwait(false);
        if (channel is not null)
        {
            mechanism.Bind(channel);
        }

        var connection = new NativeConnection(stream, target, target.BoundsReads);
        await connection.LogInAsync((user, mechanism), options, cancellationToken).ConfigureAwait(false);
        return connection;
    }

    /// <summary>
    /// Opens a connection over <paramref name="stream"/>, as the program opened it.
    /// The program answers for whether it keeps a secret mechanism's bytes
    /// private, and binds a TLS stream to the mechanism first, by
    /// <see cref="Mechanism.Bind"/>; the stream's waits are its own.
    /// </summary>
    /// <param name="stream">The stream to the server.</param>
    /// <param name="user">The user.</param>
    /// <param name="mechanism">What proves the user.</param>
    /// <param name="offer">What it asks for; the default if null.</param>
    /// <param name="cancellationToken">Cancels the opening.</param>
    /// <returns>The connection, logged in.</returns>
    /// <exception cref="TurborelException">The server's refusal; a connection failure.</exception>
    public static async Task<NativeConnection> OpenAsync(
        Stream stream,
        string user,
        Mechanism mechanism,
        Offer? offer = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(mechanism);
        var connection = new NativeConnection(stream, null, bounded: false);
        var options = new ConnectOptions { Offer = offer ?? Offer.Default, Timeouts = new Timeouts(null, null) };
        await connection.LogInAsync((user, mechanism), options, cancellationToken).ConfigureAwait(false);
        return connection;
    }

    /// <summary>The value of the setting <paramref name="name"/> as the server last told it, with no round trip.</summary>
    /// <param name="name">The setting's name, as SESSION.TIME_ZONE.</param>
    /// <returns>Its value, as SQL writes it; null if the server never told it.</returns>
    public string? Setting(string name) => _told.Settings.TryGetValue(name, out string? value) ? value : null;

    /// <summary>Whether <paramref name="capability"/> was granted.</summary>
    /// <param name="capability">The capability's name.</param>
    /// <returns>Whether it was.</returns>
    public bool Has(string capability) => _told.Has(capability);

    /// <summary>Sets how long each read or write waits from now on; null for no limit.</summary>
    /// <param name="within">The limit.</param>
    /// <exception cref="TurborelException">A connection failure for a stream the program gave, or a named pipe, which its maker bounds.</exception>
    public void SetReadTimeout(TimeSpan? within)
    {
        ReadTimeout = _bounded ? within : throw ConnectionFailure.Of("[DESIGN] this connection's stream is bounded by its maker");
    }

    /// <summary>Ends the session, its transaction as SESSION.COMMIT_AT_END says, and the connection.</summary>
    /// <param name="cancellationToken">Cancels the wait.</param>
    /// <returns>The task.</returns>
    /// <exception cref="TurborelException">
    /// A failing commit's failure, as 40002, which the server sends before it
    /// closes; a connection failure if the server does not close within the
    /// read timeout, else 60 seconds.
    /// </exception>
    public async Task TerminateAsync(CancellationToken cancellationToken = default)
    {
        await SendAsync(new Request.Terminate(), cancellationToken).ConfigureAwait(false);
        TimeSpan? within = _bounded ? ReadTimeout ?? Timeouts.TerminateWithin : null;
        Reply? last = await Waits.Within("receive", within, LastReplyAsync, cancellationToken).ConfigureAwait(false);
        await DisposeAsync().ConfigureAwait(false);
        _ = last switch
        {
            null or Reply.Notice or Reply.Status or Reply.Pong => true,
            Reply.Error error => throw error.Failure.ToException(),
            _ => throw ConnectionFailure.Of("[DESIGN] the server answered Terminate"),
        };
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        _lost ??= ConnectionFailure.Of("[DESIGN] the connection is closed");
        await _stream.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override string ToString() => $"NativeConnection({Target}, {Authorization}, lost {IsLost})";

    /// <summary>Fails before anything is sent if <paramref name="capability"/> was not granted.</summary>
    /// <param name="capability">The capability a method needs.</param>
    /// <exception cref="TurborelException">A connection failure, as the server would fail it, saying which.</exception>
    internal void Needs(string capability)
    {
        if (!_told.Has(capability))
        {
            throw ConnectionFailure.Of($"[DESIGN] the server did not grant {capability}, which this needs");
        }
    }

    private static void RefuseInTheClear(Mechanism mechanism)
    {
        if (mechanism.IsSecret)
        {
            throw ScramFailure.Credentials(
                $"[DESIGN] {mechanism.Name} is sent only over TLS, a Unix socket or a named pipe, never over plain TCP");
        }
    }
}
