// The live tests' server (RULES.md R-TEST-8): a turboreld the environment
// variable TURBOREL_TEST_TURBORELD names, its path or a command line that
// runs one, as CI's `docker run ... ghcr.io/turborel/turborel:nightly`,
// run in memory on the driver's own port, 17435, with its owner ANN, SCRAM's verifiers of few
// iterations, TRUST_LOCAL, a Unix socket or named pipe, and TLS of the
// tests' own certificates. Without the variable the live tests are
// skipped, so the unit tests stay deterministic (R-TEST-5). CI sets it on
// Linux, from the nightly image.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Turborel.Data.Authentication;
using Turborel.Data.Protocol;
using Turborel.Data.Session;
using Turborel.Data.Transport;
using Xunit;

namespace Turborel.Data.Tests.Server;

/// <summary>The turboreld the live tests run against, one for the whole run.</summary>
public sealed class LiveServer : IAsyncLifetime
{
    /// <summary>The variable naming the turboreld to run.</summary>
    internal const string Variable = "TURBOREL_TEST_TURBORELD";

    /// <summary>The driver's own port for live servers.</summary>
    internal const int Port = 17435;

    /// <summary>The owner's name.</summary>
    internal const string Owner = "ANN";

    /// <summary>The owner's password.</summary>
    internal const string Password = "ann's password";

    /// <summary>How long the server may take to start.</summary>
    private static readonly TimeSpan StartWithin = TimeSpan.FromSeconds(60);

    private Process? _process;

    /// <summary>Gets a value indicating whether a server runs for the live tests.</summary>
    internal bool Available => _process is not null;

    /// <summary>Gets the server's local socket, or on Windows its named pipe.</summary>
    internal string Socket { get; private set; } = string.Empty;

    /// <summary>Gets the tests' certificates.</summary>
    internal TestCertificates? Certificates { get; private set; }

    /// <summary>Gets the tests' own directory, under the system's temporary one.</summary>
    internal string Directory { get; } = System.IO.Directory.CreateTempSubdirectory("turborel-cs-").FullName;

    /// <summary>Skips the calling test if no server runs.</summary>
    internal void Require() => Assert.SkipUnless(Available, $"{Variable} names no turboreld, so the live tests do not run");

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable(Variable) is not { Length: > 0 } turboreld)
        {
            return;
        }

        Certificates = TestCertificates.Make(Directory);
        Socket = OperatingSystem.IsWindows() ? @"\\.\pipe\turborel-cs-test" : Path.Combine(Directory, "s");
        _process = Process.Start(StartInfo(turboreld)) ?? throw new InvalidOperationException("turboreld did not start");
        await _process.StandardInput.WriteLineAsync(Password).ConfigureAwait(false);
        _process.StandardInput.Close();
        // turboreld says where it listens on its standard error.
        Task<string?> listening = ListeningAsync(_process);
        if (await Task.WhenAny(listening, Task.Delay(StartWithin)).ConfigureAwait(false) != listening || await listening is null)
        {
            await DisposeAsync().ConfigureAwait(false);
            throw new InvalidOperationException("turboreld did not listen within its time");
        }

        await MakeOwnersSchemaAsync().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_process is { } process)
        {
            _process = null;
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            await process.WaitForExitAsync().ConfigureAwait(false);
            process.Dispose();
        }

        System.IO.Directory.Delete(Directory, recursive: true);
    }

    // The owner's schema, which a database kept nowhere starts without.
    private static async Task MakeOwnersSchemaAsync()
    {
        NativeConnection owner = await NativeConnection.ConnectAsync(new TcpTarget("127.0.0.1", Port), Owner, new TrustMechanism()).ConfigureAwait(false);
        await owner.SendAsync(new Request.Query($"CREATE SCHEMA AUTHORIZATION {Owner}", Ends: true, []), default).ConfigureAwait(false);
        Reply reply = await owner.ReceiveAsync(default).ConfigureAwait(false);
        await owner.TerminateAsync().ConfigureAwait(false);
        _ = reply as Reply.Complete ?? throw new InvalidOperationException("the owner's schema was not made");
    }

    private static async Task<string?> ListeningAsync(Process process)
    {
        while (await process.StandardError.ReadLineAsync().ConfigureAwait(false) is { } line)
        {
            if (line.Contains("listening at", StringComparison.Ordinal))
            {
                return line;
            }
        }

        return null;
    }

    private ProcessStartInfo StartInfo(string turboreld)
    {
        // A command line's words are split at spaces: the program, then its first arguments.
        string[] words = turboreld.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var start = new ProcessStartInfo(words[0])
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        string[] settings =
        [
            $"SERVER.PORT={Port}", "SERVER.TRUST_LOCAL=ON", $"SERVER.SOCKET={Socket}", "SERVER.PASSWORD_ITERATIONS=10000",
            "SERVER.LOGIN_DELAY_MS=0", "SERVER.TLS=ON", $"SERVER.TLS_CERTIFICATE={Certificates!.ServerFile}",
            $"SERVER.TLS_KEY={Certificates.ServerKeyFile}", $"SERVER.TLS_CLIENT_CA={Certificates.AuthorityFile}",
        ];
        foreach (string argument in words[1..])
        {
            start.ArgumentList.Add(argument);
        }

        foreach (string argument in new[] { "--memory", "--owner", Owner, "--log", Path.Combine(Directory, "log") })
        {
            start.ArgumentList.Add(argument);
        }

        foreach (string setting in settings)
        {
            start.ArgumentList.Add("--set");
            start.ArgumentList.Add(setting);
        }

        return start;
    }
}
