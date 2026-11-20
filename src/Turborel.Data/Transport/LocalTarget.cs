// A server reached on its own machine: a Unix socket on Linux and macOS,
// a named pipe on Windows, as \\.\pipe\turborel [CLIENT Connecting]. A
// pipe is bounded only as it is opened, while every instance is busy, as
// turborel-client bounds it [CLIENT Timeouts].

using System;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Protocol;
using Turborel.Data.Tls;

namespace Turborel.Data.Transport;

/// <summary>A server reached by a Unix socket, or on Windows a named pipe.</summary>
/// <param name="path">The socket's path, or the pipe's, as <c>\\.\pipe\turborel</c>.</param>
public sealed class LocalTarget(string path) : Target
{
    /// <summary>What a named pipe's path begins with.</summary>
    private const string PipePrefix = @"\\.\pipe\";

    /// <summary>The machine a pipe is opened on: this one.</summary>
    private const string ThisMachine = ".";

    /// <summary>Gets the socket's or the pipe's path.</summary>
    public string Path { get; } = path;

    /// <inheritdoc/>
    internal override bool BoundsReads => !OperatingSystem.IsWindows();

    /// <inheritdoc/>
    public override string ToString() => $"Local({Path})";

    /// <inheritdoc/>
    [System.Diagnostics.CodeAnalysis.SuppressMessage(
        "Reliability",
        "CA2000:Dispose objects before losing scope",
        Justification = "R-LINT-3: the socket passes to the stream, which owns and disposes it.")]
    internal override async Task<(Stream Stream, TlsChannel? Channel)> OpenAsync(TimeSpan? within, CancellationToken cancellationToken)
    {
        if (OperatingSystem.IsWindows())
        {
            return (await OpenPipeAsync(within, cancellationToken).ConfigureAwait(false), null);
        }

        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS() && !OperatingSystem.IsFreeBSD())
        {
            throw ConnectionFailure.Of($"[DESIGN] {Path} names a local socket, and this platform has none");
        }

        Socket socket = await Waits.Within(
            "connect",
            within,
            async token =>
            {
                var unix = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
                try
                {
                    await unix.ConnectAsync(new UnixDomainSocketEndPoint(Path), token).ConfigureAwait(false);
                    return unix;
                }
                catch
                {
                    unix.Dispose();
                    throw;
                }
            },
            cancellationToken).ConfigureAwait(false);
        return (new NetworkStream(socket, ownsSocket: true), null);
    }

    // The server may learn who the client is, but never act as it.
    private async Task<Stream> OpenPipeAsync(TimeSpan? within, CancellationToken cancellationToken)
    {
        string name = Path.StartsWith(PipePrefix, StringComparison.OrdinalIgnoreCase) ? Path[PipePrefix.Length..] : Path;
        var pipe = new NamedPipeClientStream(ThisMachine, name, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        try
        {
            await Waits.Within("connect", within, token => pipe.ConnectAsync(token), cancellationToken).ConfigureAwait(false);
            return pipe;
        }
        catch
        {
            await pipe.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }
}
