// Where a server is, as the setting host names one [CLIENT Where settings
// come from]: a name or address over TCP, of its own port or the port
// setting's, an IPv6 address in brackets, or a path, a Unix socket's or,
// on Windows, a named pipe's.

using System;
using System.Collections.Generic;
using System.Linq;

namespace Turborel.Data.Configuration;

/// <summary>Where a server is: <see cref="TcpHost"/> or <see cref="LocalHost"/>.</summary>
public abstract record ServerHost
{
    /// <summary>The port of the native protocol, where a server listens unless told otherwise.</summary>
    public const int DefaultPort = 7422;

    /// <summary>The host connected to when none is named.</summary>
    public const string DefaultName = "localhost";

    private const char List = ',';

    // Only the kinds below are hosts.
    private protected ServerHost()
    {
    }

    /// <summary>Reads the hosts <paramref name="hosts"/> names, each of its own port, or the port of the same place in <paramref name="ports"/>, or the one port it gives, or the default.</summary>
    /// <param name="hosts">The host setting.</param>
    /// <param name="ports">The port setting.</param>
    /// <returns>The hosts.</returns>
    internal static IReadOnlyList<ServerHost> Parse(string hosts, string ports)
    {
        int[] numbers = [.. ports.Split(List).Where(p => p.Trim().Length > 0).Select(p => PortOf(ConnectionOptions.Port, p))];
        string[] names = [.. hosts.Split(List).Select(n => n.Trim())];
        int Fallback(int i) => i < numbers.Length ? numbers[i] : numbers.Length == 1 ? numbers[0] : DefaultPort;
        return [.. names.Select((name, i) => One(name, Fallback(i)))];
    }

    // The port text is, the value of key.
    private static int PortOf(string key, string text) =>
        ConnectionSettings.Unsigned(text) is ulong port && port <= ushort.MaxValue ? (int)port : throw ConnectionOptions.Invalid($"[DESIGN] {key} is {text}, no port");

    // A path, of a local socket; [address] or [address]:port, of IPv6;
    // name:port; or a name, at port.
    private static ServerHost One(string name, int port)
    {
        if (name.Length == 0)
        {
            throw ConnectionOptions.Invalid("[DESIGN] a host of the list is empty");
        }

        if (name[0] is '/' or '\\')
        {
            return new LocalHost(name);
        }

        if (name[0] == '[')
        {
            int close = name.IndexOf(']', StringComparison.Ordinal);
            string after = close < 0 ? throw ConnectionOptions.Invalid($"[DESIGN] {name} has no ]") : name[(close + 1)..];
            return new TcpHost(name[1..close], after.StartsWith(':') ? PortOf(ConnectionOptions.Host, after[1..]) : port);
        }

        // A name of two colons or more is an IPv6 address unbracketed, of no port.
        int colon = name.IndexOf(':', StringComparison.Ordinal);
        return colon >= 0 && name.IndexOf(':', colon + 1) < 0
            ? new TcpHost(name[..colon], PortOf(ConnectionOptions.Host, name[(colon + 1)..]))
            : new TcpHost(name, port);
    }
}

/// <summary>A server at a host name or address, and port, over TCP.</summary>
/// <param name="Name">The name or address.</param>
/// <param name="Port">The port.</param>
public sealed record TcpHost(string Name, int Port) : ServerHost;

/// <summary>A server at a Unix socket's path; on Windows, a named pipe's.</summary>
/// <param name="Path">The path.</param>
public sealed record LocalHost(string Path) : ServerHost;
