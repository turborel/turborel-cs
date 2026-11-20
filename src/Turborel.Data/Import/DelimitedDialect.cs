// How delimited text is written [CLIENT Importing]: CSV as RFC 4180 §2
// defines it, or tab-separated text; its delimiter, whether a header
// names the fields, the text of a null, and the longest record.

using System.Globalization;

namespace Turborel.Data.Import;

/// <summary>How delimited text is written.</summary>
public sealed record DelimitedDialect
{
    /// <summary>The longest record read by default, in bytes: the protocol's longest body, as a longer one could not be sent.</summary>
    public const int DefaultMaxRecord = 16 * 1024 * 1024;

    private const byte Comma = (byte)',';
    private const byte Tab = (byte)'\t';

    private DelimitedDialect(byte delimiter, bool quoted)
    {
        Delimiter = delimiter;
        Quoted = quoted;
    }

    /// <summary>Gets a value indicating whether the first record names the fields [RFC 4180 §2 rule 3].</summary>
    public bool Header { get; private init; }

    /// <summary>Gets what a field not enclosed in quotes is written as to be null; empty by default.</summary>
    public string Null { get; private init; } = string.Empty;

    /// <summary>Gets the longest record, in bytes, its line break counted.</summary>
    public int MaxRecord { get; private init; } = DefaultMaxRecord;

    /// <summary>Gets the byte fields are split at.</summary>
    internal byte Delimiter { get; private init; }

    /// <summary>Gets a value indicating whether fields may be enclosed in quotes, as CSV's are.</summary>
    internal bool Quoted { get; }

    /// <summary>Gives CSV: fields split at commas, enclosed in quotes as they need; no header; an empty field null [RFC 4180 §2].</summary>
    /// <returns>The dialect.</returns>
    public static DelimitedDialect Csv() => new(Comma, quoted: true);

    /// <summary>Gives tab-separated text: fields split at tabs, never quoted; no header; an empty field null.</summary>
    /// <returns>The dialect.</returns>
    public static DelimitedDialect Tsv() => new(Tab, quoted: false);

    /// <summary>Gives the dialect, its fields split at <paramref name="delimiter"/>.</summary>
    /// <param name="delimiter">One ASCII character other than a quote, CR or LF.</param>
    /// <returns>The dialect.</returns>
    /// <exception cref="TurborelException">SQLSTATE TB910 if the delimiter is not one such character.</exception>
    public DelimitedDialect WithDelimiter(char delimiter) => delimiter is < (char)0x80 and not '"' and not '\r' and not '\n'
        ? this with { Delimiter = (byte)delimiter }
        : throw Misuse.Of(string.Create(CultureInfo.InvariantCulture, $"[DESIGN] a delimiter is one ASCII character, not a quote or a line break: '{delimiter}'"));

    /// <summary>Gives the dialect, its first record naming the fields.</summary>
    /// <returns>The dialect.</returns>
    public DelimitedDialect WithHeader() => this with { Header = true };

    /// <summary>Gives the dialect, a field written as <paramref name="text"/>, not in quotes, null.</summary>
    /// <param name="text">The text of a null.</param>
    /// <returns>The dialect.</returns>
    public DelimitedDialect WithNull(string text) => this with { Null = text };

    /// <summary>Gives the dialect, a record at most <paramref name="bytes"/> long, its line break counted.</summary>
    /// <param name="bytes">The longest record.</param>
    /// <returns>The dialect.</returns>
    public DelimitedDialect WithMaxRecord(int bytes) => this with { MaxRecord = bytes };
}
