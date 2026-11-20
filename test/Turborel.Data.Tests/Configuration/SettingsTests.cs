// Connection settings as turborel-client's settings tests read them
// (RULES.md R-TEST-9): the URI, the service file, the password file, the
// environment, and the order the layers are laid in [CLIENT Where
// settings come from] [CLIENT The connection URI] [CLIENT The service
// file] [CLIENT The password file].

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Turborel.Data.Authentication;
using Turborel.Data.Configuration;
using Turborel.Data.Tests.Support;
using Xunit;

namespace Turborel.Data.Tests.Configuration;

/// <summary>Tests of <see cref="ConnectionOptions"/> and <see cref="ConnectionSettings"/>.</summary>
public sealed class SettingsTests
{
    private static readonly Func<string, string?> NoEnvironment = _ => null;

    /// <summary>
    /// A URI gives its user, password, hosts each of its port, an IPv6
    /// address in brackets, a local socket percent-encoded, and its query's
    /// settings; the password is never shown.
    /// </summary>
    [Fact]
    [Verifies("[CLIENT The connection URI]")]
    [Verifies("[CLIENT Where settings come from]")]
    public void UriGivesItsUserHostsAndSettings()
    {
        const string Uri = "turborel://ann:p%40ss@db1:7000,[::1]:7001,db3,%2Frun%2Fs.sock/?tls_mode=verify-full&target=read-write&connect_timeout=5";
        var options = ConnectionOptions.FromUri(Uri);
        Assert.Equal(("ann", "p@ss"), (options.Get(ConnectionOptions.User), options.Get(ConnectionOptions.Password)));

        var settings = ConnectionSettings.ResolveIn(options, NoEnvironment);
        Assert.Equal([new TcpHost("db1", 7000), new TcpHost("::1", 7001), new TcpHost("db3", ServerHost.DefaultPort), new LocalHost("/run/s.sock")], settings.Hosts);
        Assert.Equal((TlsMode.VerifyFull, TargetRole.ReadWrite, TimeSpan.FromSeconds(5)), (settings.TlsMode, settings.Target, settings.ConnectTimeout));
        Assert.DoesNotContain("p@ss", $"{options} {settings}", StringComparison.Ordinal);
        Assert.Equal([new TcpHost(ServerHost.DefaultName, ServerHost.DefaultPort)], ConnectionSettings.ResolveIn(ConnectionOptions.FromUri("turborel://ann@/"), NoEnvironment).Hosts);
    }

    /// <summary>
    /// A URI of another scheme, of a path, of a key that names no setting, of
    /// a pair without <c>=</c>, or of a bad <c>%</c> is refused, SQLSTATE 08001;
    /// so is a word no setting takes, a port that is no number, and no user.
    /// </summary>
    [Fact]
    [Verifies("[CLIENT The connection URI]")]
    [Verifies("[CLIENT Where settings come from]")]
    public void MalformedUriOrSettingIsRefused()
    {
        foreach (string uri in new[] { "other://a@b", "turborel://a@b/db", "turborel://a@b/?colour=red", "turborel://a@b/?tls_mode", "turborel://a%4@b", "turborel://a%FF@b" })
        {
            Assert.Equal("08001", Assert.Throws<TurborelException>(() => ConnectionOptions.FromUri(uri)).SqlState);
        }

        foreach (string uri in new[] { "turborel://a@b/?tls_mode=always", "turborel://a@b:port", "turborel://a@b/?connect_timeout=-1", "turborel://b" })
        {
            var options = ConnectionOptions.FromUri(uri);
            TurborelException refused = Assert.Throws<TurborelException>(() => ConnectionSettings.ResolveIn(options, NoEnvironment));
            Assert.Equal((-919, "08001"), (refused.SqlCode, refused.SqlState));
        }
    }

    /// <summary>
    /// A setting the program gives prevails over the service's, which prevails
    /// over the environment's, which prevails over the default; the
    /// environment names the service and its file when the program does not;
    /// an empty variable gives nothing.
    /// </summary>
    [Fact]
    [Verifies("[CLIENT Where settings come from]")]
    [Verifies("[CLIENT The service file]")]
    public void SettingsAreLaidProgramServiceEnvironmentDefault()
    {
        string file = Scratch("# servers\n[prod]\nhost = prod-db\nport = 7500\ntls_mode = require\n\n[test]\nhost = test-db\n");
        try
        {
            var env = new Dictionary<string, string>
            {
                ["TURBOREL_SERVICE"] = "prod",
                ["TURBOREL_SERVICE_FILE"] = file,
                ["TURBOREL_USER"] = "env-user",
                ["TURBOREL_PORT"] = "7600",
                ["TURBOREL_TLS_MODE"] = "disable",
                ["TURBOREL_APPLICATION_NAME"] = string.Empty,
            };
            ConnectionOptions explicitOptions = new ConnectionOptions().Set(ConnectionOptions.TlsMode, "verify-ca");

            var settings = ConnectionSettings.ResolveIn(explicitOptions, name => env.GetValueOrDefault(name));
            Assert.Equal([new TcpHost("prod-db", 7500)], settings.Hosts);
            Assert.Equal((TlsMode.VerifyCa, "env-user", null, ChannelBinding.Prefer), (settings.TlsMode, settings.User, settings.ApplicationName, settings.ChannelBinding));
            _ = explicitOptions.Set(ConnectionOptions.Service, "test");
            Assert.Equal([new TcpHost("test-db", 7600)], ConnectionSettings.ResolveIn(explicitOptions, name => env.GetValueOrDefault(name)).Hosts);
        }
        finally
        {
            File.Delete(file);
        }
    }

    /// <summary>
    /// A service file's section is its lines' settings; a section not there, a
    /// setting before any section, a line without <c>=</c>, a key that names no
    /// setting, and a service or service file set by a service are refused,
    /// naming the line.
    /// </summary>
    [Fact]
    [Verifies("[CLIENT The service file]")]
    public void ServiceFileIsReadBySection()
    {
        ConnectionOptions options = SettingsFiles.Service("[a]\nuser=ann\n[b]\nuser = bob\n  host =  h  \n", "b", "services.conf");
        Assert.Equal(("bob", "h"), (options.Get(ConnectionOptions.User), options.Get(ConnectionOptions.Host)));
        Assert.Contains("has no service b", Assert.Throws<TurborelException>(() => SettingsFiles.Service("[a]\nuser=ann\n", "b", "services.conf")).Message, StringComparison.Ordinal);
        foreach ((string text, string why) in new[]
        {
            ("user=ann\n", "line 1: a setting comes before any section"),
            ("[a]\nuser\n", "line 2: a setting is <key> = <value>"),
            ("[a]\ncolour=red\n", "line 2: [DESIGN] colour is no connection setting"),
            ("[a]\nservice=b\n", "line 2: a service cannot set service"),
            ("[a\n", "line 1: a section's name ends with ]"),
        })
        {
            Assert.Contains(why, Assert.Throws<TurborelException>(() => SettingsFiles.Service(text, "a", "services.conf")).Message, StringComparison.Ordinal);
        }

        string missing = Path.Combine(Path.GetTempPath(), $"turborel-cs-no-service-{Guid.NewGuid():N}");
        Assert.Equal("08001", Assert.Throws<TurborelException>(() => ConnectionSettings.Service(missing, "s")).SqlState);
    }

    /// <summary>
    /// The password file gives the password of its first line matching host,
    /// port and user, <c>*</c> matching any, <c>\:</c> and <c>\\</c> taken as
    /// they are, an escaped <c>\*</c> a star; comments and short lines skipped;
    /// the settings' own password prevails; a file that cannot be read gives none.
    /// </summary>
    [Fact]
    [Verifies("[CLIENT The password file]")]
    public void PasswordFileMatchesByHostPortAndUser()
    {
        const string Text = "# passwords\nshort:line\ndb1:7422:ann:first\n*:*:ann:any\\:host\ndb\\:2:*:bob:c:\\\\d\n\\*:1:carl:star\n";
        Assert.Equal("first", SettingsFiles.Password(Text, ("db1", "7422", "ann")));
        Assert.Equal("any:host", SettingsFiles.Password(Text, ("db9", "1", "ann")));
        Assert.Equal("c:\\d", SettingsFiles.Password(Text, ("db:2", "7", "bob")));
        Assert.Null(SettingsFiles.Password(Text, ("x", "1", "carl")));
        Assert.Equal("star", SettingsFiles.Password(Text, ("*", "1", "carl")));
        Assert.Null(SettingsFiles.Password(Text, ("db1", "7422", "nobody")));
        string file = Scratch(Text);
        ConnectionOptions explicitOptions = new ConnectionOptions().Set(ConnectionOptions.User, "ann").Set(ConnectionOptions.Passfile, file).Set(ConnectionOptions.Host, "db1");
        var settings = ConnectionSettings.ResolveIn(explicitOptions, NoEnvironment);
        Assert.Equal("first", settings.PasswordFor(new TcpHost("db1", 7422)));
        Assert.Equal("given", ConnectionSettings.ResolveIn(explicitOptions.Set(ConnectionOptions.Password, "given"), NoEnvironment).PasswordFor(new TcpHost("db1", 7422)));
        File.Delete(file);
        Assert.Null(settings.PasswordFor(new TcpHost("db1", 7422)));
        Assert.Null(SettingsFiles.ReadRegular(Path.GetTempPath()));
    }

    /// <summary>
    /// As turborel-client reads them: a password file's lines end at LF or CR
    /// LF alone; an empty or unnamed password file, or one of no length,
    /// gives no password; a connect timeout longer than any wait is timed by
    /// is no limit; a TLS file named empty cannot be read, SQLSTATE 08006.
    /// </summary>
    [Fact]
    [Verifies("[CLIENT The password file]")]
    [Verifies("[CLIENT Where settings come from]")]
    [Verifies("[CLIENT TLS by mode]")]
    public async Task FilesAndTimesAreReadAsTurborelClientReadsThem()
    {
        Assert.Equal("pa\u2028ss\fword", SettingsFiles.Password("db1:7422:ann:pa\u2028ss\fword\r\n", ("db1", "7422", "ann")));
        string empty = Scratch(string.Empty);
        foreach (string passfile in new[] { string.Empty, empty })
        {
            ConnectionOptions options = new ConnectionOptions().Set(ConnectionOptions.User, "ann").Set(ConnectionOptions.Passfile, passfile);
            Assert.Null(ConnectionSettings.ResolveIn(options, NoEnvironment).PasswordFor(new TcpHost("db1", 7422)));
        }

        File.Delete(empty);
        ConnectionOptions forever = new ConnectionOptions().Set(ConnectionOptions.User, "ann").Set(ConnectionOptions.ConnectTimeout, "99999999999999999");
        Assert.Null(ConnectionSettings.ResolveIn(forever, NoEnvironment).ConnectTimeout);
        foreach ((string mode, string key, string why) in new[] { ("verify-full", ConnectionOptions.TlsRoots, "cannot read"), ("prefer", ConnectionOptions.TlsCertificate, "cannot present") })
        {
            ConnectionOptions unnamed = new ConnectionOptions().Set(ConnectionOptions.User, "ann").Set(ConnectionOptions.Host, "127.0.0.1:1")
                .Set(ConnectionOptions.TlsMode, mode).Set(key, string.Empty).Set(ConnectionOptions.TlsKey, string.Empty);
            TurborelException refused = await Assert.ThrowsAsync<TurborelException>(() => ConnectionSettings.ResolveIn(unnamed, NoEnvironment).ConnectAsync(TestContext.Current.CancellationToken));
            Assert.Equal("08006", refused.SqlState);
            Assert.Contains(why, refused.Message, StringComparison.Ordinal);
        }

        // Under require the authorities named are not read: none is checked.
        ConnectionOptions unread = new ConnectionOptions().Set(ConnectionOptions.User, "ann").Set(ConnectionOptions.Host, "127.0.0.1:1")
            .Set(ConnectionOptions.TlsMode, "require").Set(ConnectionOptions.TlsRoots, "/nonexistent");
        TurborelException unreached = await Assert.ThrowsAsync<TurborelException>(() => ConnectionSettings.ResolveIn(unread, NoEnvironment).ConnectAsync(TestContext.Current.CancellationToken));
        Assert.DoesNotContain("cannot read", unreached.Message, StringComparison.Ordinal);
    }

    /// <summary>Every variable of the environment gives its setting, and the words of each kind are read as named.</summary>
    [Fact]
    [Verifies("[CLIENT Where settings come from]")]
    public void EnvironmentAndWordsAreRead()
    {
        var options = ConnectionOptions.FromEnvironment(_ => "v");
        Assert.All(ConnectionOptions.Environment, pair => Assert.Equal("v", options.Get(pair.Key)));
        Assert.Equal(TlsMode.Prefer, SettingWords.Tls(ConnectionOptions.TlsMode, "prefer"));
        Assert.True(SettingWords.Verifies(TlsMode.VerifyCa) && !SettingWords.Verifies(TlsMode.Require));
        Assert.Equal(ChannelBinding.Require, SettingWords.Binding(ConnectionOptions.ChannelBinding, "require"));
        Assert.Equal(LoadBalance.Random, SettingWords.Balance(ConnectionOptions.LoadBalance, "random"));
        Assert.Contains("any, read-write, read-only", Assert.Throws<TurborelException>(() => SettingWords.Role(ConnectionOptions.Target, "primary")).Message, StringComparison.Ordinal);
        ConnectionOptions both = new ConnectionOptions().Set(ConnectionOptions.Host, "a,b").Set(ConnectionOptions.Port, "1,2").Set(ConnectionOptions.User, "u").Set(ConnectionOptions.TlsSystemRoots, "true");
        var settings = ConnectionSettings.ResolveIn(both, NoEnvironment);
        Assert.Equal([new TcpHost("a", 1), new TcpHost("b", 2)], settings.Hosts);
        Assert.True(settings.Tls.SystemRoots);
        Assert.True(new ConnectionOptions().IsEmpty);
        Assert.Equal("08001", Assert.Throws<TurborelException>(() => new ConnectionOptions().Set("colour", "red")).SqlState);
    }

    /// <summary>
    /// A list of hosts with an empty one, an IPv6 address whose bracket is not
    /// closed or whose port is no number, a port that is no number, and a word
    /// no setting takes are refused, SQLSTATE 08001; several hosts of one port
    /// each take it; client certificates are named by settings.
    /// </summary>
    [Fact]
    [Verifies("[CLIENT Where settings come from]")]
    public void MalformedHostsAndWordsAreRefused()
    {
        foreach ((string key, string value) in new[]
        {
            (ConnectionOptions.Host, "a,,b"), (ConnectionOptions.Host, "[::1"), (ConnectionOptions.Host, "[::1]:x"), (ConnectionOptions.Port, "1,x"),
            (ConnectionOptions.ChannelBinding, "sometimes"), (ConnectionOptions.Target, "sometimes"), (ConnectionOptions.LoadBalance, "sometimes"),
            (ConnectionOptions.TlsSystemRoots, "sometimes"), (ConnectionOptions.Host, "a:70000"),
        })
        {
            ConnectionOptions options = new ConnectionOptions().Set(ConnectionOptions.User, "u").Set(key, value);
            Assert.Equal("08001", Assert.Throws<TurborelException>(() => ConnectionSettings.ResolveIn(options, NoEnvironment)).SqlState);
        }

        ConnectionOptions onePort = new ConnectionOptions().Set(ConnectionOptions.User, "u").Set(ConnectionOptions.Host, "a,b,[::1],fe80::1").Set(ConnectionOptions.Port, "+9");
        Assert.Equal([new TcpHost("a", 9), new TcpHost("b", 9), new TcpHost("::1", 9), new TcpHost("fe80::1", 9)], ConnectionSettings.ResolveIn(onePort, NoEnvironment).Hosts);
        ConnectionOptions presenting = new ConnectionOptions().Set(ConnectionOptions.User, "u").Set(ConnectionOptions.TlsCertificate, "c.pem").Set(ConnectionOptions.TlsKey, "k.pem").Set(ConnectionOptions.ServerName, "db").Set(ConnectionOptions.TlsRoots, "ca.pem");
        Assert.Equal(new TlsFiles("ca.pem", false, "c.pem", "k.pem", "db"), ConnectionSettings.ResolveIn(presenting, NoEnvironment).Tls);
    }

    /// <summary>The password file is matched for a local socket by its path and the default port.</summary>
    [Fact]
    [Verifies("[CLIENT The password file]")]
    public void PasswordFileMatchesALocalSocketByItsPath()
    {
        string file = Scratch($"/run/s.sock:{ServerHost.DefaultPort}:ann:local\n");
        try
        {
            ConnectionOptions options = new ConnectionOptions().Set(ConnectionOptions.User, "ann").Set(ConnectionOptions.Passfile, file);
            Assert.Equal("local", ConnectionSettings.ResolveIn(options, NoEnvironment).PasswordFor(new LocalHost("/run/s.sock")));
        }
        finally
        {
            File.Delete(file);
        }
    }

    private static string Scratch(string text)
    {
        string path = Path.Combine(Path.GetTempPath(), $"turborel-cs-settings-{Guid.NewGuid():N}");
        File.WriteAllText(path, text);
        return path;
    }
}
