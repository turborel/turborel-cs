// A member of an SQL/JSON object: its key and its value, as Turborel's
// stored form keeps it (Turborel's RULES.md D89).

namespace Turborel.Data.Json;

/// <summary>A member of an object.</summary>
/// <param name="Key">Its key.</param>
/// <param name="Value">Its value.</param>
internal sealed record JsonMember(string Key, JsonItem Value);
