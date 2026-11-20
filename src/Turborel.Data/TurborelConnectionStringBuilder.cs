// A connection string (RULES.md D19): key=value pairs of the connection
// settings, by the names a URI's query and a service file give them
// [CLIENT Where settings come from], and the pool's own; or a whole
// turborel:// URI. Read into the settings and the pool's configuration
// a TurborelDataSource connects by.

using System;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using Turborel.Data.Configuration;
using Turborel.Data.Pooling;

namespace Turborel.Data;

/// <summary>A connection string of turborel's settings and its pool's.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1010:Generic interface should also be implemented",
    Justification = "R-LINT-3: ADO.NET's DbConnectionStringBuilder is the base, and its collection is its own.")]
public sealed class TurborelConnectionStringBuilder : DbConnectionStringBuilder
{
    /// <summary>Whether connections are pooled: <c>true</c> or <c>false</c>; <c>true</c> by default.</summary>
    public const string Pooling = "pooling";

    /// <summary>The pool's <see cref="PoolConfig.Max"/>.</summary>
    public const string PoolMax = "pool_max";

    /// <summary>The pool's <see cref="PoolConfig.MaxIdle"/>.</summary>
    public const string PoolMaxIdle = "pool_max_idle";

    /// <summary>The pool's <see cref="PoolConfig.IdleTimeout"/>, whole seconds, 0 for none.</summary>
    public const string PoolIdleTimeout = "pool_idle_timeout";

    /// <summary>The pool's <see cref="PoolConfig.MaxLifetime"/>, whole seconds, 0 for none.</summary>
    public const string PoolMaxLifetime = "pool_max_lifetime";

    /// <summary>The pool's <see cref="PoolConfig.Wait"/>, whole seconds.</summary>
    public const string PoolWait = "pool_wait";

    /// <summary>The pool's <see cref="PoolConfig.Check"/>: <c>true</c> or <c>false</c>.</summary>
    public const string PoolCheck = "pool_check";

    private static readonly string[] PoolKeys = [Pooling, PoolMax, PoolMaxIdle, PoolIdleTimeout, PoolMaxLifetime, PoolWait, PoolCheck];

    /// <summary>Initializes a new instance of the <see cref="TurborelConnectionStringBuilder"/> class, empty.</summary>
    public TurborelConnectionStringBuilder()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TurborelConnectionStringBuilder"/> class of a connection string, or of a turborel:// URI.</summary>
    /// <param name="connectionString">The connection string or URI.</param>
    /// <exception cref="TurborelException">SQLSTATE 08001 if a key names no setting, or the URI cannot be read.</exception>
    public TurborelConnectionStringBuilder(string? connectionString)
    {
        if (connectionString is null || !connectionString.StartsWith(ConnectionOptions.Scheme, StringComparison.Ordinal))
        {
            ConnectionString = connectionString ?? string.Empty;
            return;
        }


        var options = ConnectionOptions.FromUri(connectionString);
        foreach (string key in ConnectionOptions.Keys)
        {
            if (options.Get(key) is { } value)
            {
                this[key] = value;
            }
        }
    }

    /// <summary>Gets or sets the connection string, its keys and values; one that cannot be read refused.</summary>
    /// <exception cref="TurborelException">SQLSTATE 08001 if it cannot be read, or a key names no setting.</exception>
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public new string ConnectionString
    {
        get => base.ConnectionString;
        set
        {
            try
            {
                base.ConnectionString = value;
            }
            catch (ArgumentException e)
            {
                throw ConnectionOptions.Invalid($"[DESIGN] the connection string cannot be read: {e.Message}");
            }
        }
    }

    /// <summary>Gets or sets the value of a setting, by its name, null of one not set; a bool is written as the settings write one, <c>true</c> or <c>false</c>.</summary>
    /// <param name="keyword">The setting's name, as a URI's query writes it, or one of the pool's.</param>
    /// <returns>Its value.</returns>
    /// <exception cref="TurborelException">SQLSTATE 08001 if the name is no setting's.</exception>
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override object this[string keyword]
    {
        get => TryGetValue(Known(keyword), out object? value) ? value : null!;
        set => base[Known(keyword)] = value is bool b ? (b ? "true" : "false") : value;
    }

    /// <summary>Gives the connection settings the string names, laid over the environment and the service, as <see cref="ConnectionSettings.Resolve"/> lays them.</summary>
    /// <returns>The settings.</returns>
    /// <exception cref="TurborelException">As <see cref="ConnectionSettings.Resolve"/>'s.</exception>
    public ConnectionSettings ToSettings()
    {
        var options = new ConnectionOptions();
        foreach (string key in ConnectionOptions.Keys.Where(ContainsKey))
        {
            _ = options.Set(key, Text(key));
        }

        return ConnectionSettings.Resolve(options);
    }

    /// <summary>Gives the pool's configuration the string names, its defaults for the rest; null if connections are not pooled.</summary>
    /// <returns>The configuration, or null.</returns>
    /// <exception cref="TurborelException">SQLSTATE 08001 if a value cannot be read.</exception>
    public PoolConfig? ToPoolConfig()
    {
        if (!Truth(Pooling, true))
        {
            return null;
        }

        var defaults = new PoolConfig();
        return new PoolConfig
        {
            Max = Whole(PoolMax) is ulong max ? (int)Math.Min(max, int.MaxValue) : defaults.Max,
            MaxIdle = Whole(PoolMaxIdle) is ulong idle ? (int)Math.Min(idle, int.MaxValue) : defaults.MaxIdle,
            IdleTimeout = Seconds(PoolIdleTimeout, defaults.IdleTimeout),
            MaxLifetime = Seconds(PoolMaxLifetime, defaults.MaxLifetime),
            Wait = Seconds(PoolWait, defaults.Wait) ?? TimeSpan.Zero,
            Check = Truth(PoolCheck, defaults.Check),
        };
    }

    // The name keyword is, in lower case, if it names a setting.
    private static string Known(string keyword)
    {
        Misuse.ThrowIfNull(keyword);
        string key = keyword.Trim();
        return ConnectionOptions.Keys.Concat(PoolKeys).FirstOrDefault(k => string.Equals(k, key, StringComparison.OrdinalIgnoreCase))
            ?? throw ConnectionOptions.Invalid($"[DESIGN] {keyword} is no connection setting");
    }

    private ulong? Whole(string key) => ContainsKey(key)
        ? ConnectionSettings.Unsigned(Text(key)) ?? throw ConnectionOptions.Invalid($"[DESIGN] {key} is {this[key]}, not a whole number")
        : null;

    private TimeSpan? Seconds(string key, TimeSpan? fallback) => Whole(key) switch
    {
        null => fallback,
        0 => null,
        ulong n => ConnectionSettings.Timed(n),
    };

    // A pool's switch, true or false in any case, as .NET writes a bool.
    private bool Truth(string key, bool fallback) => !ContainsKey(key)
        ? fallback
        : bool.TryParse(Text(key), out bool value) ? value : throw ConnectionOptions.Invalid($"[DESIGN] {key} is {this[key]}, neither true nor false");

    // A value as a setting's text.
    private string Text(string key) => Convert.ToString(this[key], CultureInfo.InvariantCulture) ?? string.Empty;
}
