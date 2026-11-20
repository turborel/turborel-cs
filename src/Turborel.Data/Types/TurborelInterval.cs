// An interval as the protocol carries it: its qualifier, then its units,
// months of a year-month interval and microseconds of a day-time one
// [PROTOCOL Values].

using System;

namespace Turborel.Data.Types;

/// <summary>An interval: its qualifier and its units.</summary>
/// <param name="Qualifier">Its fields and precisions.</param>
/// <param name="Units">Months of a year-month interval, microseconds of a day-time one.</param>
internal readonly record struct TurborelInterval(IntervalQualifier Qualifier, Int128 Units);
