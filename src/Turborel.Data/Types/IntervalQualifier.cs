// An interval's qualifier: its start and end fields and its precisions,
// as the INTERVAL type writes them [PROTOCOL Data types].

namespace Turborel.Data.Types;

/// <summary>The fields and precisions of an interval type or value.</summary>
/// <param name="Start">The leading field.</param>
/// <param name="End">The last field.</param>
/// <param name="LeadingPrecision">The digits of the leading field.</param>
/// <param name="FractionalPrecision">The digits of a fraction of a second.</param>
public readonly record struct IntervalQualifier(
    IntervalField Start,
    IntervalField End,
    uint LeadingPrecision,
    uint FractionalPrecision)
{
    /// <summary>Gets a value indicating whether it is a year-month qualifier, its units months.</summary>
    internal bool IsYearMonth => End <= IntervalField.Month;

    /// <summary>Writes it as the INTERVAL type does: <c>DAY(2) TO SECOND(6)</c>.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => SqlText.Qualifier(this);
}
