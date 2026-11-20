// The standard API's parameters (RULES.md D2, D18): each the value of a
// statement's `?`, by its place in the collection, its name the program's
// own; its value any .NET value AdoValues maps, or the driver's own types.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;

namespace Turborel.Data;

/// <summary>A value of a statement's dynamic parameter, a <c>?</c>, by its place.</summary>
public sealed class TurborelParameter : DbParameter
{
    private DbType? _dbType;

    /// <summary>Initializes a new instance of the <see cref="TurborelParameter"/> class, of no value.</summary>
    public TurborelParameter()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="TurborelParameter"/> class of a name and a value.</summary>
    /// <param name="parameterName">The program's name for it.</param>
    /// <param name="value">Its value.</param>
    public TurborelParameter(string? parameterName, object? value)
    {
        ParameterName = parameterName ?? string.Empty;
        Value = value;
    }

    /// <summary>Gets or sets the parameter's type, as told, else as its value says.</summary>
    public override DbType DbType
    {
        get => _dbType ?? Infer(Value);
        set => _dbType = value;
    }

    /// <summary>Gets or sets its direction: input alone, as a statement's <c>?</c> takes a value.</summary>
    public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;

    /// <inheritdoc/>
    public override bool IsNullable { get; set; }

    /// <inheritdoc/>
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string ParameterName { get; set; } = string.Empty;

    /// <inheritdoc/>
    [System.Diagnostics.CodeAnalysis.AllowNull]
    public override string SourceColumn { get; set; } = string.Empty;

    /// <inheritdoc/>
    public override bool SourceColumnNullMapping { get; set; }

    /// <inheritdoc/>
    public override object? Value { get; set; }

    /// <inheritdoc/>
    public override int Size { get; set; }

    /// <inheritdoc/>
    public override void ResetDbType() => _dbType = null;

    // The type a value is of.
    private static DbType Infer(object? value) => value switch
    {
        string or char => DbType.String,
        int => DbType.Int32,
        long => DbType.Int64,
        short => DbType.Int16,
        byte => DbType.Byte,
        bool => DbType.Boolean,
        decimal or Types.TurborelDecimal => DbType.Decimal,
        double => DbType.Double,
        float => DbType.Single,
        byte[] => DbType.Binary,
        Guid => DbType.Guid,
        DateTime => DbType.DateTime2,
        DateTimeOffset => DbType.DateTimeOffset,
        DateOnly => DbType.Date,
        TimeOnly or TimeSpan => DbType.Time,
        _ => DbType.Object,
    };
}

/// <summary>The parameters of a command, in the order of its <c>?</c> marks.</summary>
[System.Diagnostics.CodeAnalysis.SuppressMessage(
    "Design",
    "CA1010:Generic interface should also be implemented",
    Justification = "R-LINT-3: ADO.NET's DbParameterCollection is the base, and its list is its own.")]
public sealed class TurborelParameterCollection : DbParameterCollection
{
    private readonly List<TurborelParameter> _items = [];

    /// <inheritdoc/>
    public override int Count => _items.Count;

    /// <inheritdoc/>
    public override object SyncRoot => ((ICollection)_items).SyncRoot;

    /// <summary>Adds a parameter of a value, named for the program alone.</summary>
    /// <param name="parameterName">The program's name for it.</param>
    /// <param name="value">Its value.</param>
    /// <returns>The parameter.</returns>
    public TurborelParameter AddWithValue(string? parameterName, object? value)
    {
        var parameter = new TurborelParameter(parameterName, value);
        _items.Add(parameter);
        return parameter;
    }

    /// <inheritdoc/>
    public override int Add(object value)
    {
        _items.Add(Of(value));
        return _items.Count - 1;
    }

    /// <inheritdoc/>
    public override void AddRange(Array values)
    {
        Misuse.ThrowIfNull(values);
        _items.AddRange(values.Cast<object>().Select(Of));
    }

    /// <inheritdoc/>
    public override void Clear() => _items.Clear();

    /// <inheritdoc/>
    public override bool Contains(object value) => value is TurborelParameter p && _items.Contains(p);

    /// <inheritdoc/>
    public override bool Contains(string value) => IndexOf(value) >= 0;

    /// <inheritdoc/>
    public override void CopyTo(Array array, int index)
    {
        Misuse.ThrowIfNull(array);
        _ = Place(index, array.Length - _items.Count);
        for (int i = 0; i < _items.Count; i++)
        {
            try
            {
                array.SetValue(_items[i], index + i);
            }
            catch (InvalidCastException)
            {
                throw Misuse.Of($"[DESIGN] an array of {array.GetType().GetElementType()?.Name} holds no parameter");
            }
        }
    }

    /// <inheritdoc/>
    public override IEnumerator GetEnumerator() => _items.GetEnumerator();

    /// <inheritdoc/>
    public override int IndexOf(object value) => value is TurborelParameter p ? _items.IndexOf(p) : -1;

    /// <inheritdoc/>
    public override int IndexOf(string parameterName) => _items.FindIndex(p => p.ParameterName == parameterName);

    /// <inheritdoc/>
    public override void Insert(int index, object value) => _items.Insert(Place(index, _items.Count), Of(value));

    /// <inheritdoc/>
    public override void Remove(object value) => _items.Remove(Of(value));

    /// <inheritdoc/>
    public override void RemoveAt(int index) => _items.RemoveAt(Place(index, _items.Count - 1));

    /// <inheritdoc/>
    public override void RemoveAt(string parameterName) => _items.RemoveAt(Named(parameterName));

    /// <summary>Gives each parameter's value as the protocol carries it, in order.</summary>
    /// <returns>The values.</returns>
    /// <exception cref="TurborelException">SQLSTATE TB910 for a parameter not an input.</exception>
    internal IReadOnlyList<object?> Values() => [.. _items.Select(p => p.Direction == ParameterDirection.Input
        ? AdoValues.ToWire(p.Value)
        : throw Misuse.Of($"[DESIGN] a parameter is an input alone: {p.ParameterName} is {p.Direction}"))];

    /// <inheritdoc/>
    protected override DbParameter GetParameter(int index) => _items[Place(index, _items.Count - 1)];

    /// <inheritdoc/>
    protected override DbParameter GetParameter(string parameterName) => _items[Named(parameterName)];

    /// <inheritdoc/>
    protected override void SetParameter(int index, DbParameter value) => _items[Place(index, _items.Count - 1)] = Of(value);

    /// <inheritdoc/>
    protected override void SetParameter(string parameterName, DbParameter value) => _items[Named(parameterName)] = Of(value);

    /// <summary>Checks a place in a collection.</summary>
    /// <param name="index">The place.</param>
    /// <param name="last">The last place it may be.</param>
    /// <returns>The place.</returns>
    /// <exception cref="TurborelException">SQLSTATE TB910 for a place past the collection.</exception>
    internal static int Place(int index, int last) => index >= 0 && index <= last
        ? index
        : throw Misuse.Of(System.FormattableString.Invariant($"[DESIGN] the collection has no place {index}"));

    // The place of the parameter of a name.
    private int Named(string parameterName) => IndexOf(parameterName) is int i and >= 0 ? i : throw Misuse.Of($"[DESIGN] no parameter is named {parameterName}");

    private static TurborelParameter Of(object value) =>
        value as TurborelParameter ?? throw Misuse.Of($"[DESIGN] a parameter of turborel's is a TurborelParameter, not {value?.GetType().Name ?? "null"}");
}
