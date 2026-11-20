// A prepared statement's number and one of its cursors', as Execute,
// Fetch, Close and Describe name them; cursor 0 unless `cursors` was
// granted [PROTOCOL Execute 'E'] [PROTOCOL Describe 'Y'].

namespace Turborel.Data.Protocol;

/// <summary>A statement, and a cursor of it.</summary>
/// <param name="Statement">The client's number for the statement.</param>
/// <param name="Cursor">The cursor's number, 0 unless <c>cursors</c> was granted.</param>
internal readonly record struct StatementCursor(uint Statement, uint Cursor = 0);
