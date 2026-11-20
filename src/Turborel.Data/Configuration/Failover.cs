// Connecting as settings say, to one of several servers [CLIENT Several
// servers] [CLIENT TLS by mode], as turborel-client's failover does: each
// host tried in the order given, or in an order chosen at random for each
// connection, and each address a host's name resolves to in turn, every
// step within the connect timeout. A server reached, logged in to and of
// the role asked is kept; another is left and the next tried. No server
// kept, the failure says why each was not.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Authentication;
using Turborel.Data.Session;
using Turborel.Data.Tls;
using Turborel.Data.Transport;

namespace Turborel.Data.Configuration;

/// <summary>Connects as settings say.</summary>
internal static class Failover
{
    /// <summary>The multiplier of the generator that orders hosts at random (Knuth's MMIX linear congruential generator).</summary>
    private const ulong Multiplier = 6_364_136_223_846_793_005;

    /// <summary>Its increment (MMIX's).</summary>
    private const ulong Increment = 1_442_695_040_888_963_407;

    /// <summary>The high bits of its state taken for each draw, the low bits of such a generator being the weakest.</summary>
    private const int DrawShift = 33;

    /// <summary>Connects as <paramref name="settings"/> say.</summary>
    /// <param name="settings">The settings.</param>
    /// <param name="cancellationToken">Cancels the connecting.</param>
    /// <returns>The connection.</returns>
    internal static async Task<NativeConnection> ConnectAsync(ConnectionSettings settings, CancellationToken cancellationToken)
    {
        ServerHost[] hosts = [.. settings.Hosts];
        if (settings.LoadBalance == LoadBalance.Random)
        {
            Shuffle(hosts, Seed());
        }

        TlsOptions? tls = TlsOf(settings);
        var tried = new List<(string At, TurborelException Failure)>();
        foreach (ServerHost host in hosts)
        {
            foreach ((string at, Target? target, TurborelException? unresolved) in await TargetsAsync(host, settings.Tls.ServerName, tls, cancellationToken).ConfigureAwait(false))
            {
                try
                {
                    return target is null ? throw unresolved! : await AttemptAsync(settings, host, target, cancellationToken).ConfigureAwait(false);
                }
                catch (TurborelException failure)
                {
                    tried.Add((at, failure));
                }
            }
        }

        throw NoneKept(tried);
    }

    /// <summary>Orders <paramref name="items"/> as drawn from <paramref name="seed"/> (Fisher and Yates, as Durstenfeld gives it): each order as likely, for a seed drawn at random.</summary>
    /// <typeparam name="T">What is ordered.</typeparam>
    /// <param name="items">The items.</param>
    /// <param name="seed">The seed.</param>
    internal static void Shuffle<T>(T[] items, ulong seed)
    {
        ulong state = seed;
        for (int i = items.Length - 1; i > 0; i--)
        {
            state = unchecked((state * Multiplier) + Increment);
            ulong drawn = (state >> DrawShift) % ((ulong)i + 1);
            (items[i], items[(int)drawn]) = (items[(int)drawn], items[i]);
        }
    }

    /// <summary>Gives the failure of a connection none of <paramref name="tried"/> gave.</summary>
    /// <param name="tried">Each address tried, and why it was not kept.</param>
    /// <returns>The failure.</returns>
    internal static TurborelException NoneKept(List<(string At, TurborelException Failure)> tried)
    {
        if (tried.Count == 1)
        {
            return tried[0].Failure;
        }

        bool refused = tried.Count > 0 && tried[^1].Failure.SqlState.StartsWith("28", StringComparison.Ordinal);
        string message = tried.Count == 0
            ? "[DESIGN] no server is named"
            : $"[DESIGN] no server was kept: {string.Join("; ", tried.Select(t => $"{t.At}: {t.Failure.Message}"))}";
        return new TurborelException(-919, refused ? "28000" : "08001", message);
    }

    // A seed of the system's randomness.
    private static ulong Seed() => BitConverter.ToUInt64(RandomNumberGenerator.GetBytes(sizeof(ulong)));

    // How TLS is used, as the settings' mode says; null of disable. The
    // files are checked as the TLS is made, before any connection.
    private static TlsOptions? TlsOf(ConnectionSettings settings)
    {
        TlsFiles files = settings.Tls;
        if (settings.TlsMode == TlsMode.Disable)
        {
            return null;
        }

        // As turborel-client's TLS: the authorities read only where they are
        // checked, verify-ca and verify-full.
        bool verifies = SettingWords.Verifies(settings.TlsMode);
        if (verifies && files.Roots is { } roots)
        {
            _ = Pem.Certificates(roots);
        }

        if (verifies && files.Roots is null && !files.SystemRoots)
        {
            throw Protocol.ConnectionFailure.Of("[DESIGN] no certificate authority is trusted: name a file of them, or the platform's");
        }

        if (files.Certificate is not null && files.Key is null)
        {
            throw Protocol.ConnectionFailure.Of("[DESIGN] a client certificate is presented with its key, and none is named");
        }

        var options = new TlsOptions
        {
            RootsFile = files.Roots,
            SystemRoots = files.SystemRoots,
            CertificateFile = files.Certificate,
            KeyFile = files.Key,
            VerifyAuthority = verifies,
            VerifyName = settings.TlsMode == TlsMode.VerifyFull,
        };

        // The certificate presented is read before any connection, so one
        // that cannot be read fails it, under prefer too, not the TLS alone.
        _ = ClientCertificate.Of(options);
        return options;
    }

    // The targets of a host, each named: its local socket, or each address
    // its name resolves to, over TLS as tls says, the server's certificate
    // of server_name, else the host's.
    private static async Task<List<(string At, Target? Target, TurborelException? Unresolved)>> TargetsAsync(
        ServerHost host,
        string? serverName,
        TlsOptions? tls,
        CancellationToken cancellationToken)
    {
        if (host is LocalHost local)
        {
            return [(local.Path, new LocalTarget(local.Path), null)];
        }

        var tcp = (TcpHost)host;
        IPAddress[] addresses;
        try
        {
            addresses = IPAddress.TryParse(tcp.Name, out IPAddress? literal)
                ? [literal]
                : await Dns.GetHostAddressesAsync(tcp.Name, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception e) when (e is SocketException or ArgumentException)
        {
            // This host's failure alone: the next is tried, as turborel-client's failover tries it.
            return [($"{tcp.Name}:{tcp.Port}", null, IoFailure.Of("resolve the name", e, timedOut: false))];
        }
        catch (OperationCanceledException e)
        {
            throw IoFailure.Of("resolve the name", new OperationCanceledException("the program cancelled the wait", e), timedOut: false);
        }

        TlsOptions? named = tls is null ? null : tls with { ServerName = serverName ?? tcp.Name };
        return [.. addresses.Select(a => ($"{tcp.Name} ({new IPEndPoint(a, tcp.Port)})", named is null ? (Target)new TcpTarget(a.ToString(), tcp.Port) : new TlsTarget(a.ToString(), tcp.Port, named), (TurborelException?)null))];
    }

    // A connection to host over target, logged in and of the role asked;
    // under prefer, over plain TCP if TLS cannot be begun.
    private static async Task<NativeConnection> AttemptAsync(ConnectionSettings settings, ServerHost host, Target target, CancellationToken cancellationToken)
    {
        TimeSpan? within = settings.ConnectTimeout;
        (Target reached, (Stream Stream, TlsChannel? Channel) opened) = settings.TlsMode == TlsMode.Prefer && target is TlsTarget secured
            ? await PreferringAsync(secured, within, cancellationToken).ConfigureAwait(false)
            : (target, await target.OpenAsync(within, cancellationToken).ConfigureAwait(false));
        NativeConnection connection;
        try
        {
            Mechanism mechanism = Proof(settings, host, reached);
            if (reached.IsPlainTcp)
            {
                NativeConnection.RefuseInTheClear(mechanism);
            }

            var options = new ConnectOptions { Offer = OfferOf(settings), Timeouts = new Timeouts(within, null) };
            connection = await NativeConnection.LogInOverAsync(reached, opened, (settings.User, mechanism), options, cancellationToken).ConfigureAwait(false);
        }
        catch (TurborelException)
        {
            await opened.Stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        await ServerRole.CheckAsync(connection, settings.Target, cancellationToken).ConfigureAwait(false);
        return connection;
    }

    // The stream to a TLS target, or, its handshake failing, to the same
    // address over plain TCP; a server that cannot be reached is not tried twice.
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "R-LINT-3: the socket passes to the stream, which owns and disposes it.")]
    private static async Task<(Target Target, (Stream Stream, TlsChannel? Channel) Opened)> PreferringAsync(
        TlsTarget target,
        TimeSpan? within,
        CancellationToken cancellationToken)
    {
        Socket socket = await TcpTarget.ConnectAsync(target.Host, target.Port, within, cancellationToken).ConfigureAwait(false);
        try
        {
            (Stream stream, TlsChannel channel) = await TlsHandshake.SecureAsync(new NetworkStream(socket, ownsSocket: true), target.Host, target.Options, within, cancellationToken).ConfigureAwait(false);
            return (target, (stream, channel));
        }
        catch (TurborelException)
        {
            var plain = new TcpTarget(target.Host, target.Port);
            return (plain, await plain.OpenAsync(within, cancellationToken).ConfigureAwait(false));
        }
    }

    // What proves the user at host over target: SCRAM of a password, else
    // the certificate named, else trust; a binding required refused where
    // none can be.
    private static Mechanism Proof(ConnectionSettings settings, ServerHost host, Target target)
    {
        Mechanism proof = settings.PasswordFor(host) is { } password
            ? new ScramMechanism(password) { Binding = settings.ChannelBinding, Cache = settings.KeyCache }
            : settings.Tls.Certificate is not null ? new CertificateMechanism() : new TrustMechanism();
        if (settings.ChannelBinding == ChannelBinding.Require && !(proof is ScramMechanism && target is TlsTarget))
        {
            throw new TurborelException(-919, "28000", "[DESIGN] channel binding is required: SCRAM over TLS alone binds one");
        }

        return proof;
    }

    // What a connection of the settings asks for: every capability but
    // compression, and the program's name, if given, as the attribute application.
    private static Offer OfferOf(ConnectionSettings settings) =>
        settings.ApplicationName is { } name ? Offer.Default.WithAttribute(Offer.Application, name) : Offer.Default;
}
