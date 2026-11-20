// The standard API's reader (RULES.md D2, D18): a query's rows as they
// come, a batch at a time, each value by its typed getter as the .NET
// type AdoValues names, or the driver's own by GetProviderSpecificValue;
// its schema, of each column's name, type and nullability.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Data;
using System.Data.Common;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Turborel.Data.Protocol;
using Turborel.Data.Types;

namespace Turborel.Data;

/// <summary>A query's rows, read as they come.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1010:Generic interface should also be implemented",
    Justification = "R-LINT-3: ADO.NET's DbDataReader is the base, and its enumeration of records is its own.")]
public sealed class TurborelDataReader : DbDataReader, IDbColumnSchemaGenerator
{
    private readonly TurborelConnection _connection;
    private readonly ReaderResults _results;
    private readonly bool _closeConnection;
    private IReadOnlyList<IReadOnlyList<object?>>? _batch;
    private int _row = -1;
    private IReadOnlyList<object?>? _current;
    private bool _closed;
    private bool _hadRows;

    /// <summary>Initializes a new instance of the <see cref="TurborelDataReader"/> class.</summary>
    /// <param name="connection">The connection it reads over.</param>
    /// <param name="results">What it reads.</param>
    /// <param name="closeConnection">Whether closing it closes the connection.</param>
    internal TurborelDataReader(TurborelConnection connection, ReaderResults results, bool closeConnection)
    {
        (_connection, _results, _closeConnection) = (connection, results, closeConnection);
        connection.Reader = this;
    }

    /// <inheritdoc/>
    public override int Depth => 0;

    /// <inheritdoc/>
    public override int FieldCount => _results.Columns.Count;

    /// <summary>Gets a value indicating whether the result has rows, read or not, reading its first batch if it must.</summary>
    public override bool HasRows => _hadRows || HasRowsAsync().GetAwaiter().GetResult();

    /// <inheritdoc/>
    public override bool IsClosed => _closed;

    /// <inheritdoc/>
    public override int RecordsAffected => _results.RecordsAffected;

    /// <inheritdoc/>
    public override object this[int ordinal] => GetValue(ordinal);

    /// <inheritdoc/>
    public override object this[string name] => GetValue(GetOrdinal(name));

    /// <inheritdoc/>
    public override bool Read() => ReadAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc/>
    public override async Task<bool> ReadAsync(CancellationToken cancellationToken)
    {
        ThrowIfClosed();
        while (_batch is null || _row + 1 >= _batch.Count)
        {
            _batch = await _results.NextBatchAsync(cancellationToken).ConfigureAwait(false);
            _row = -1;
            if (_batch is null)
            {
                _current = null;
                return false;
            }
        }

        (_current, _hadRows) = (_batch[++_row], true);
        return true;
    }

    /// <inheritdoc/>
    public override bool NextResult() => NextResultAsync(CancellationToken.None).GetAwaiter().GetResult();

    /// <inheritdoc/>
    public override async Task<bool> NextResultAsync(CancellationToken cancellationToken)
    {
        ThrowIfClosed();
        (_batch, _current, _row, _hadRows) = (null, null, -1, false);
        return await _results.NextResultAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override void Close() => CloseAsync().GetAwaiter().GetResult();

    /// <summary>Closes the reader, reading what is left of its statement, so the connection takes the next.</summary>
    /// <returns>The task.</returns>
    public override async Task CloseAsync()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _connection.Reader = null;
        await _results.CloseAsync(CancellationToken.None).ConfigureAwait(false);
        if (_closeConnection)
        {
            await _connection.CloseAsync().ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public override async ValueTask DisposeAsync()
    {
        await CloseAsync().ConfigureAwait(false);
        await base.DisposeAsync().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public override string GetName(int ordinal) => Column(ordinal).Name ?? string.Empty;

    /// <inheritdoc/>
    public override int GetOrdinal(string name)
    {
        for (int i = 0; i < FieldCount; i++)
        {
            if (string.Equals(GetName(i), name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        throw Misuse.Of($"[DESIGN] the result has no column {name}");
    }

    /// <inheritdoc/>
    public override string GetDataTypeName(int ordinal) => TypeName(Column(ordinal).Type);

    /// <inheritdoc/>
    [return: System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
    public override Type GetFieldType(int ordinal) => AdoValues.FieldType(Column(ordinal).Type);

    /// <inheritdoc/>
    [return: System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicFields | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicProperties)]
    public override Type GetProviderSpecificFieldType(int ordinal) => AdoValues.ProviderType(Column(ordinal).Type);

    /// <inheritdoc/>
    public override object GetValue(int ordinal) => Raw(ordinal) is { } raw ? AdoValues.FromWire(raw) : DBNull.Value;

    /// <inheritdoc/>
    public override object GetProviderSpecificValue(int ordinal) => Raw(ordinal) ?? DBNull.Value;

    /// <inheritdoc/>
    public override int GetValues(object[] values)
    {
        Misuse.ThrowIfNull(values);
        int n = Math.Min(values.Length, FieldCount);
        for (int i = 0; i < n; i++)
        {
            values[i] = GetValue(i);
        }

        return n;
    }

    /// <inheritdoc/>
    public override bool IsDBNull(int ordinal) => Raw(ordinal) is null;

    /// <inheritdoc/>
    public override T GetFieldValue<T>(int ordinal) => Raw(ordinal) switch
    {
        T raw => raw,
        null => throw Misuse.Of($"[DESIGN] column {ordinal} is null"),
        { } raw => AdoValues.FromWire(raw) is T value ? value : Within(ordinal, n => Changed<T>(ordinal, n)),
    };

    /// <inheritdoc/>
    public override bool GetBoolean(int ordinal) => Typed<bool>(ordinal);

    /// <inheritdoc/>
    public override byte GetByte(int ordinal) => Within(ordinal, n => checked((byte)n));

    /// <inheritdoc/>
    public override short GetInt16(int ordinal) => Within(ordinal, n => checked((short)n));

    /// <inheritdoc/>
    public override int GetInt32(int ordinal) => Within(ordinal, n => checked((int)n));

    /// <inheritdoc/>
    public override long GetInt64(int ordinal) => Within(ordinal, n => checked((long)n));

    /// <inheritdoc/>
    public override decimal GetDecimal(int ordinal) => Number(ordinal);

    /// <inheritdoc/>
    public override double GetDouble(int ordinal) => Raw(ordinal) is double d ? d : (double)Number(ordinal);

    /// <inheritdoc/>
    public override float GetFloat(int ordinal) => (float)GetDouble(ordinal);

    /// <inheritdoc/>
    public override string GetString(int ordinal) => Raw(ordinal) switch
    {
        TurborelLargeObject { Kind: LargeObjectKind.Character, Data: { } held } => System.Text.Encoding.UTF8.GetString(held.Span),
        { } raw => AdoValues.FromWire(raw) as string ?? throw Misuse.Of($"[DESIGN] column {ordinal} is no character string"),
        null => throw Misuse.Of($"[DESIGN] column {ordinal} is null"),
    };

    /// <inheritdoc/>
    public override char GetChar(int ordinal) => GetString(ordinal) is [char c, ..] ? c : throw Misuse.Of($"[DESIGN] column {ordinal} is empty");

    /// <inheritdoc/>
    public override DateTime GetDateTime(int ordinal) => GetValue(ordinal) switch
    {
        DateTime d => d,
        DateTimeOffset o => o.DateTime,
        _ => throw Misuse.Of($"[DESIGN] column {ordinal} is no timestamp or date"),
    };

    /// <inheritdoc/>
    public override Guid GetGuid(int ordinal) => Typed<Guid>(ordinal);

    /// <inheritdoc/>
    public override long GetBytes(int ordinal, long dataOffset, byte[]? buffer, int bufferOffset, int length)
    {
        byte[] bytes = Raw(ordinal) switch
        {
            byte[] b => b,
            TurborelLargeObject { Data: { } held } => held.ToArray(),
            _ => throw Misuse.Of($"[DESIGN] column {ordinal} is no binary string held whole"),
        };
        return Copy(bytes, dataOffset, buffer, bufferOffset, length);
    }

    /// <inheritdoc/>
    public override long GetChars(int ordinal, long dataOffset, char[]? buffer, int bufferOffset, int length) =>
        Copy(GetString(ordinal).ToCharArray(), dataOffset, buffer, bufferOffset, length);

    /// <inheritdoc/>
    public override IEnumerator GetEnumerator() => new DbEnumerator(this, closeReader: false);

    /// <summary>Gives each column's schema: its name, place, type, nullability, precision, scale and length.</summary>
    /// <returns>The columns.</returns>
    public ReadOnlyCollection<DbColumn> GetColumnSchema() => new([.. _results.Columns.Select((c, i) => (DbColumn)new Schema(c, i))]);

    /// <inheritdoc/>
    [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage(
        "Trimming",
        "IL2111:Method with DynamicallyAccessedMembersAttribute is accessed via reflection",
        Justification = "ADO.NET's schema table holds each column's Type, as its DataType column is; the types are the driver's own and the BCL's, each kept by the code that reads it.")]
    public override DataTable GetSchemaTable()
    {
        var table = new DataTable("SchemaTable") { Locale = CultureInfo.InvariantCulture };
        _ = table.Columns.Add("ColumnName", typeof(string));
        _ = table.Columns.Add("ColumnOrdinal", typeof(int));
        _ = table.Columns.Add("ColumnSize", typeof(int));
        _ = table.Columns.Add("NumericPrecision", typeof(int));
        _ = table.Columns.Add("NumericScale", typeof(int));
        _ = table.Columns.Add("DataType", typeof(Type));
        _ = table.Columns.Add("DataTypeName", typeof(string));
        _ = table.Columns.Add("AllowDBNull", typeof(bool));
        foreach (DbColumn c in GetColumnSchema())
        {
            _ = table.Rows.Add(c.ColumnName, c.ColumnOrdinal, OrNull(c.ColumnSize), OrNull(c.NumericPrecision), OrNull(c.NumericScale), c.DataType, c.DataTypeName, c.AllowDBNull);
        }

        return table;
    }

    /// <summary>Gives a data type's name, as SQL writes it.</summary>
    /// <param name="type">The type.</param>
    /// <returns>The name.</returns>
    internal static string TypeName(DataType type) => type switch
    {
        CharacterType c => string.Create(CultureInfo.InvariantCulture, $"{(c.Varying ? "CHARACTER VARYING" : "CHARACTER")}({c.Length}{(c.Units == CharacterUnits.Octets ? " OCTETS" : string.Empty)})"),
        ExactType e => string.Create(CultureInfo.InvariantCulture, $"DECIMAL({e.Precision}, {e.Scale})"),
        ApproximateType => "DOUBLE PRECISION",
        BinaryType b => string.Create(CultureInfo.InvariantCulture, $"{(b.Varying ? "BINARY VARYING" : "BINARY")}({b.Length})"),
        DatetimeType d => DatetimeName(d),
        IntervalType i => $"INTERVAL {i.Qualifier}",
        BooleanType => "BOOLEAN",
        DecFloatType f => string.Create(CultureInfo.InvariantCulture, $"DECFLOAT({f.Precision})"),
        ArrayType a => string.Create(CultureInfo.InvariantCulture, $"{TypeName(a.Element)} ARRAY[{a.MaxCardinality}]"),
        LargeObjectType l => string.Create(CultureInfo.InvariantCulture, $"{(l.Kind == LargeObjectKind.Character ? "CHARACTER LARGE OBJECT" : "BINARY LARGE OBJECT")}({l.Length})"),
        MultisetType m => $"{TypeName(m.Element)} MULTISET",
        RowType r => $"ROW({string.Join(", ", r.Fields.Select(f => $"{f.Name} {TypeName(f.Type)}"))})",
        StructuredType s => $"{s.Name.Schema}.{s.Name.Name}",
        ReferenceType r => $"REF({r.Referenced.Name.Schema}.{r.Referenced.Name.Name})",
        JsonType => "JSON",
        XmlType => "XML",
        UuidType => "UUID",
        PluginType p => p.Name,
        _ => type.ToString(),
    };

    /// <summary>Refuses, as GetData asks: a column holds no nested reader.</summary>
    /// <param name="ordinal">The column.</param>
    /// <returns>Never.</returns>
    /// <exception cref="TurborelException">SQLSTATE TB910, always.</exception>
    protected override DbDataReader GetDbDataReader(int ordinal) => throw Misuse.Of("[DESIGN] a column of turborel's holds no nested reader");

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Close();
        }

        base.Dispose(disposing);
    }

    private static string DatetimeName(DatetimeType d)
    {
        string name = d.Moment switch { DatetimeMoment.Date => "DATE", DatetimeMoment.Time => "TIME", _ => "TIMESTAMP" };
        return d.Moment == DatetimeMoment.Date
            ? name
            : string.Create(CultureInfo.InvariantCulture, $"{name}({d.Precision}){(d.Zoned ? " WITH TIME ZONE" : string.Empty)}");
    }

    private static object OrNull(int? value) => value is int n ? n : DBNull.Value;

    // A number as another numeric type, of a type it cannot be refused.
    private static T Changed<T>(int ordinal, decimal n)
    {
        try
        {
            return (T)Convert.ChangeType(n, typeof(T), CultureInfo.InvariantCulture);
        }
        catch (InvalidCastException)
        {
            throw Misuse.Of($"[DESIGN] column {ordinal} is no {typeof(T).Name}");
        }
    }

    private static long Copy<T>(T[] source, long dataOffset, T[]? buffer, int bufferOffset, int length)
    {
        if (buffer is null)
        {
            return source.Length;
        }

        if (dataOffset < 0 || bufferOffset < 0 || length < 0 || bufferOffset > buffer.Length || length > buffer.Length - bufferOffset)
        {
            throw Misuse.Of("[DESIGN] the offsets and length are past the buffer, or below 0");
        }

        int start = (int)Math.Min(dataOffset, source.Length);
        int n = Math.Min(length, source.Length - start);
        Array.Copy(source, start, buffer, bufferOffset, n);
        return n;
    }

    private async Task<bool> HasRowsAsync()
    {
        while (!_closed && (_batch is null || _row + 1 >= _batch.Count))
        {
            IReadOnlyList<IReadOnlyList<object?>>? next = await _results.NextBatchAsync(CancellationToken.None).ConfigureAwait(false);
            if (next is null)
            {
                return false;
            }

            (_batch, _row) = (next, -1);
        }

        _hadRows = !_closed;
        return _hadRows;
    }

    private Column Column(int ordinal) => ordinal >= 0 && ordinal < FieldCount
        ? _results.Columns[ordinal]
        : throw Misuse.Of(string.Create(CultureInfo.InvariantCulture, $"[DESIGN] the result has no column {ordinal}"));

    private object? Raw(int ordinal)
    {
        IReadOnlyList<object?> row = _current ?? throw Misuse.Of("[DESIGN] the reader is on no row: Read first");
        return row[Column(ordinal) is not null ? ordinal : 0];
    }

    private T Typed<T>(int ordinal) => Raw(ordinal) is T value ? value : throw Misuse.Of($"[DESIGN] column {ordinal} is no {typeof(T).Name}");

    // A number's value as a decimal, of an exact or approximate number.
    private decimal Number(int ordinal) => Raw(ordinal) switch
    {
        TurborelDecimal d => d.TryToDecimal(out decimal m) ? m : throw Past(ordinal, d),
        double x => Decimal(ordinal, x),
        TurborelDecFloat f => decimal.TryParse(f.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out decimal m) ? m : throw Past(ordinal, f),
        null => throw Misuse.Of($"[DESIGN] column {ordinal} is null"),
        _ => throw Misuse.Of($"[DESIGN] column {ordinal} is no number"),
    };

    // The failure of a number past the type a getter gives.
    private static TurborelException Past(int ordinal, object value) =>
        new(-301, "22003", string.Create(CultureInfo.InvariantCulture, $"[DESIGN] column {ordinal}'s {value} is past the type asked for"));

    // A double as a decimal: one past a decimal's range, or no number, refused.
    private static decimal Decimal(int ordinal, double x)
    {
        try
        {
            return (decimal)x;
        }
        catch (OverflowException)
        {
            throw Past(ordinal, x);
        }
    }

    // A number converted to the type a getter gives, one past it refused.
    private T Within<T>(int ordinal, Func<decimal, T> convert)
    {
        decimal n = Number(ordinal);
        try
        {
            return convert(n);
        }
        catch (OverflowException)
        {
            throw Past(ordinal, n);
        }
    }

    private void ThrowIfClosed()
    {
        if (_closed)
        {
            throw Misuse.Of("[DESIGN] the reader is closed");
        }
    }

    // A column's schema.
    private sealed class Schema : DbColumn
    {
        internal Schema(Column column, int ordinal)
        {
            ColumnName = column.Name ?? string.Empty;
            ColumnOrdinal = ordinal;
            AllowDBNull = !column.NotNull;
            DataType = AdoValues.FieldType(column.Type);
            DataTypeName = TypeName(column.Type);
            (NumericPrecision, NumericScale, ColumnSize) = column.Type switch
            {
                ExactType e => ((int?)e.Precision, (int?)e.Scale, (int?)null),
                ApproximateType a => ((int?)a.Precision, null, null),
                CharacterType c => (null, null, (int?)c.Length),
                BinaryType b => (null, null, (int?)b.Length),
                _ => (null, null, null),
            };
        }
    }
}
