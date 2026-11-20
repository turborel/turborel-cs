// An array value: its elements, in order [PROTOCOL Values].

using System.Collections.Generic;

namespace Turborel.Data.Types;

/// <summary>A value of an array type.</summary>
/// <param name="Elements">Its elements, in order.</param>
internal sealed record TurborelArray(IReadOnlyList<object?> Elements);
