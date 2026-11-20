// An XML value: its serialization as CONTENT [PROTOCOL Values].

namespace Turborel.Data.Types;

/// <summary>A value of an XML type, as its text.</summary>
/// <param name="Text">Its serialization as CONTENT.</param>
internal sealed record TurborelXml(string Text);
