// The tags of a message's tail, the fields version 1 gained after its
// first form, each written after its tag in increasing order of tag
// [PROTOCOL The tail].

namespace Turborel.Data.Protocol;

/// <summary>The tags of the tail's fields, and the bounds of attributes.</summary>
internal static class TailTags
{
    /// <summary>Attributes: a count, then each name and value, strings; of Hello, Query and Execute.</summary>
    internal const byte Attributes = 1;

    /// <summary>Multi: nothing; a Query of several statements.</summary>
    internal const byte Multi = 2;

    /// <summary>Cursor: a <c>u32</c>; of Execute, Fetch and Close.</summary>
    internal const byte Cursor = 3;

    /// <summary>Position: a <c>u64</c>, the commit's log position; of Complete.</summary>
    internal const byte Position = 4;

    /// <summary>More: nothing; another statement's replies follow; of Complete.</summary>
    internal const byte More = 5;

    /// <summary>Rows: a <c>u64</c>, the rows the statement counted; of Complete.</summary>
    internal const byte Rows = 6;

    /// <summary>Severity, the first of the diagnostics, tags 7 to 15, each a string; of a failure and Notice.</summary>
    internal const byte Severity = 7;

    /// <summary>Detail, a diagnostic.</summary>
    internal const byte Detail = 8;

    /// <summary>Hint, a diagnostic.</summary>
    internal const byte Hint = 9;

    /// <summary>Context, a diagnostic.</summary>
    internal const byte Context = 10;

    /// <summary>Schema, a diagnostic.</summary>
    internal const byte Schema = 11;

    /// <summary>Table, a diagnostic.</summary>
    internal const byte Table = 12;

    /// <summary>Column, a diagnostic.</summary>
    internal const byte Column = 13;

    /// <summary>Data type, a diagnostic.</summary>
    internal const byte DataType = 14;

    /// <summary>Constraint, the last of the diagnostics.</summary>
    internal const byte Constraint = 15;

    /// <summary>Generated: a list of strings, the identity values an INSERT made; of Complete.</summary>
    internal const byte Generated = 16;

    /// <summary>Conditions: a list, each a failure as bytes; of Complete.</summary>
    internal const byte Conditions = 17;

    /// <summary>Time: total, queued and locks, each a <c>u64</c>; of Complete.</summary>
    internal const byte Time = 18;

    /// <summary>Declared: each column's declared type, of Prepared and Description [PROTOCOL Declared types].</summary>
    internal const byte Declared = 19;

    /// <summary>Called: a CALL's procedure and each <c>?</c> with its parameter's mode, of Prepared [PROTOCOL Parameter modes].</summary>
    internal const byte Called = 20;

    /// <summary>The most attributes a message takes [PROTOCOL The tail].</summary>
    internal const int MaxAttributes = 32;

    /// <summary>The longest name of an attribute, in bytes; a name has one at least [PROTOCOL The tail].</summary>
    internal const int MaxAttributeName = 64;

    /// <summary>The longest value of an attribute, in bytes [PROTOCOL The tail].</summary>
    internal const int MaxAttributeValue = 1024;
}
