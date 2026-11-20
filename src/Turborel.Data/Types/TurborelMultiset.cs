// A multiset value: its elements, in no set order [PROTOCOL Values].

using System.Collections.Generic;

namespace Turborel.Data.Types;

/// <summary>A value of a multiset type.</summary>
/// <param name="Elements">Its elements, in no set order.</param>
public sealed record TurborelMultiset(IReadOnlyList<object?> Elements);
