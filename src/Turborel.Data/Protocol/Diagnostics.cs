// What a failure or a notice says beyond its SQLSTATE and message, tags 7
// to 15 of its tail, written only with `diagnostics` granted
// [PROTOCOL Errors and notices] [PROTOCOL The tail].


namespace Turborel.Data.Protocol;

/// <summary>A failure's or a notice's severity, and what it is about; each null if not told.</summary>
internal sealed record Diagnostics
{
    /// <summary>The tags of the diagnostics, 7 to 15, in order.</summary>
    internal static readonly byte[] Tags =
    [
        TailTags.Severity, TailTags.Detail, TailTags.Hint, TailTags.Context, TailTags.Schema,
        TailTags.Table, TailTags.Column, TailTags.DataType, TailTags.Constraint,
    ];

    /// <summary>Gets ERROR, FATAL, WARNING or NOTICE (tag 7).</summary>
    internal string? Severity { get; init; }

    /// <summary>Gets more than the message says, as the key a constraint refused (tag 8).</summary>
    internal string? Detail { get; init; }

    /// <summary>Gets what may be done about it (tag 9).</summary>
    internal string? Hint { get; init; }

    /// <summary>Gets the trigger or routine running, as <c>trigger S.T</c> (tag 10).</summary>
    internal string? Context { get; init; }

    /// <summary>Gets the schema of what it is about (tag 11).</summary>
    internal string? Schema { get; init; }

    /// <summary>Gets the table or view it is about (tag 12).</summary>
    internal string? Table { get; init; }

    /// <summary>Gets the column it is about (tag 13).</summary>
    internal string? Column { get; init; }

    /// <summary>Gets the data type of the value it is about (tag 14).</summary>
    internal string? DataType { get; init; }

    /// <summary>Gets the constraint broken (tag 15).</summary>
    internal string? Constraint { get; init; }

    /// <summary>Reads a tail of diagnostics alone, to the end of the body.</summary>
    /// <param name="r">The reader, at the tail.</param>
    /// <returns>The diagnostics; null if the tail is empty.</returns>
    internal static Diagnostics? Read(WireReader r)
    {
        Diagnostics? found = null;
        var tail = default(TailReader);
        while (tail.Next(r, Tags) is byte tag)
        {
            found = With(found ?? new Diagnostics(), tag, r.ReadString());
        }

        return found;
    }

    /// <summary>Writes each field told, after its tag.</summary>
    /// <param name="w">The writer.</param>
    internal void Write(WireWriter w)
    {
        string?[] fields = Fields();
        for (int i = 0; i < fields.Length; i++)
        {
            if (fields[i] is string field)
            {
                w.WriteU8(Tags[i]);
                w.WriteString(field);
            }
        }
    }

    private static Diagnostics With(Diagnostics d, byte tag, string value) => tag switch
    {
        TailTags.Severity => d with { Severity = value },
        TailTags.Detail => d with { Detail = value },
        TailTags.Hint => d with { Hint = value },
        TailTags.Context => d with { Context = value },
        TailTags.Schema => d with { Schema = value },
        TailTags.Table => d with { Table = value },
        TailTags.Column => d with { Column = value },
        TailTags.DataType => d with { DataType = value },
        _ => d with { Constraint = value },
    };

    private string?[] Fields() => [Severity, Detail, Hint, Context, Schema, Table, Column, DataType, Constraint];
}
