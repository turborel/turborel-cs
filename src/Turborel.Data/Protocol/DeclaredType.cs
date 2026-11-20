// The type a column was declared with [PROTOCOL Declared types]: of a
// Prepared's or a Description's columns, granted declared-types, by tail
// 19. One tag holds several declared types, as INTEGER and NUMERIC(10,0)
// are both an exact numeric type of tag 2; the declared type tells them
// apart, as turborel-client's Column::declared does.

namespace Turborel.Data.Protocol;

/// <summary>The type a column was declared with.</summary>
/// <param name="Name">Its name, as the Information Schema's DATA_TYPE gives it: INTEGER, DECIMAL, USER-DEFINED of a distinct type, ARRAY.</param>
/// <param name="Written">The whole type as SQL writes it: INTEGER, DECIMAL(9,2), a distinct type's schema and name, INTEGER ARRAY[3].</param>
public sealed record DeclaredType(string Name, string Written);
