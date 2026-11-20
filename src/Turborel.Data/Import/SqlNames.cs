// Names an import writes into SQL it makes: a table's name read as SQL
// reads a <table name>, its parts delimited so nothing else is taken for
// one, and a heading read as an identifier if it is one, as turborel's
// lexer reads them [CLIENT Importing].

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Text;

namespace Turborel.Data.Import;

/// <summary>The SQL names of an import.</summary>
internal static class SqlNames
{
    /// <summary>The most parts of a table's name: catalog, schema and table.</summary>
    private const int MaxNameParts = 3;

    /// <summary>The most characters of an identifier.</summary>
    private const int MaxIdentifierLength = 128;

    /// <summary>Turborel's reserved words, which no regular identifier is (its lexer's KEY_WORDS).</summary>
    private static readonly FrozenSet<string> Reserved = (
        "ABSOLUTE ACTION ADD ALL ALLOCATE ALTER AND ANY ARE AS ASC ASSERTION AT AUTHORIZATION AVG "
        + "BEGIN BETWEEN BIT BIT_LENGTH BOTH BY "
        + "CASCADE CASCADED CASE CAST CATALOG CHAR CHARACTER CHAR_LENGTH CHARACTER_LENGTH CHECK CLOSE "
        + "COALESCE COLLATE COLLATION COLUMN COMMIT CONNECT CONNECTION CONSTRAINT CONSTRAINTS CONTINUE "
        + "CONVERT CORRESPONDING COUNT CREATE CROSS CURRENT CURRENT_DATE CURRENT_TIME CURRENT_TIMESTAMP "
        + "CURRENT_USER CURSOR "
        + "DATE DAY DEALLOCATE DEC DECIMAL DECLARE DEFAULT DEFERRABLE DEFERRED DELETE DESC DESCRIBE "
        + "DESCRIPTOR DIAGNOSTICS DISCONNECT DISTINCT DOMAIN DOUBLE DROP "
        + "ELSE END ESCAPE EXCEPT EXCEPTION EXEC EXECUTE EXISTS EXTERNAL EXTRACT "
        + "FALSE FETCH FIRST FLOAT FOR FOREIGN FOUND FROM FULL "
        + "GET GLOBAL GO GOTO GRANT GROUP "
        + "HAVING HOUR "
        + "IDENTITY IMMEDIATE IN INDICATOR INITIALLY INNER INPUT INSENSITIVE INSERT INT INTEGER INTERSECT "
        + "INTERVAL INTO IS ISOLATION "
        + "JOIN KEY "
        + "LANGUAGE LAST LEADING LEFT LEVEL LIKE LOCAL LOWER "
        + "MATCH MAX MIN MINUTE MODULE MONTH "
        + "NAMES NATIONAL NATURAL NCHAR NEXT NO NOT NULL NULLIF NUMERIC "
        + "OCTET_LENGTH OF ON ONLY OPEN OPTION OR ORDER OUTER OUTPUT OVERLAPS "
        + "PAD PARTIAL POSITION PRECISION PREPARE PRESERVE PRIMARY PRIOR PRIVILEGES PROCEDURE PUBLIC "
        + "READ REAL REFERENCES RELATIVE RESTRICT REVOKE RIGHT ROLLBACK ROWS "
        + "SCHEMA SCROLL SECOND SECTION SELECT SESSION SESSION_USER SET SIZE SMALLINT SOME SPACE SQL "
        + "SQLCODE SQLERROR SQLSTATE SUBSTRING SUM SYSTEM_USER "
        + "TABLE TEMPORARY THEN TIME TIMESTAMP TIMEZONE_HOUR TIMEZONE_MINUTE TO TRAILING TRANSACTION "
        + "TRANSLATE TRANSLATION TRIM TRUE "
        + "UNION UNIQUE UNKNOWN UPDATE UPPER USAGE USER USING "
        + "VALUE VALUES VARCHAR VARYING VIEW "
        + "WHEN WHENEVER WHERE WITH WORK WRITE YEAR ZONE").Split(' ').ToFrozenSet(StringComparer.Ordinal);

    // A token of a name: an identifier, regular or delimited, a key word, or a period.
    private enum Kind
    {
        Identifier,
        KeyWord,
        Period,
    }

    /// <summary>Writes a name as a delimited identifier, a quote in it written twice.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The delimited identifier.</returns>
    internal static string Delimited(string name) => $"\"{name.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";

    /// <summary>Reads <paramref name="text"/> as a table's name, catalog, schema and table, and writes it as SQL of delimited identifiers.</summary>
    /// <param name="text">The name, as SQL writes it.</param>
    /// <returns>The name, delimited; null if it is not a table's name.</returns>
    internal static string? TableName(string text)
    {
        List<(Kind Kind, string Text)>? tokens = Tokens(text);
        if (tokens is null || tokens.Count == 0)
        {
            return null;
        }

        var parts = new List<string>();
        for (int i = 0; i < tokens.Count; i += 2)
        {
            bool periodAfter = i + 1 < tokens.Count;
            if (tokens[i].Kind != Kind.Identifier || (periodAfter && tokens[i + 1].Kind != Kind.Period) || parts.Count == MaxNameParts)
            {
                return null;
            }

            parts.Add(Delimited(tokens[i].Text));
            if (periodAfter && i + 2 >= tokens.Count)
            {
                return null;
            }
        }

        return string.Join(".", parts);
    }

    /// <summary>The column a heading names: the identifier it is, if it is one, a regular one, or a key word, in upper case; else its name as written.</summary>
    /// <param name="heading">The heading.</param>
    /// <returns>The column's name.</returns>
    internal static string ColumnName(string heading) =>
        Tokens(heading) is [(Kind.Identifier or Kind.KeyWord, string name)] ? name : heading;

    // The tokens of a name, separators dropped; null if a character starts none.
    private static List<(Kind Kind, string Text)>? Tokens(string text)
    {
        var tokens = new List<(Kind, string)>();
        int at = 0;
        while (at < text.Length)
        {
            char c = text[at];
            if (char.IsWhiteSpace(c))
            {
                at++;
            }
            else if (c == '.')
            {
                tokens.Add((Kind.Period, "."));
                at++;
            }
            else if (c == '"')
            {
                (string? name, at) = DelimitedIdentifier(text, at);
                if (name is null)
                {
                    return null;
                }

                tokens.Add((Kind.Identifier, name));
            }
            else if (char.IsLetter(c))
            {
                ((Kind Kind, string Text)? word, at) = RegularWord(text, at);
                if (word is not { } token)
                {
                    return null;
                }

                tokens.Add(token);
            }
            else
            {
                return null;
            }
        }

        return tokens;
    }

    // A regular identifier, in upper case, or a reserved word, from its
    // first letter, and where it ends; null if it is too long.
    private static ((Kind Kind, string Text)? Word, int End) RegularWord(string text, int at)
    {
        int start = at;
        while (at < text.Length && (char.IsLetterOrDigit(text[at]) || text[at] == '_'))
        {
            at++;
        }

        string word = text[start..at].ToUpperInvariant();
        if (Reserved.Contains(word))
        {
            return ((Kind.KeyWord, word), at);
        }

        // Its letters and digits are counted, its underscores not.
        bool fits = word.Replace("_", string.Empty, StringComparison.Ordinal).Length <= MaxIdentifierLength;
        return (fits ? (Kind.Identifier, word) : null, at);
    }

    // A delimited identifier from its opening quote: its name, a doubled
    // quote one, and where it ends; null if it is not closed, empty or too long.
    private static (string? Name, int End) DelimitedIdentifier(string text, int at)
    {
        var name = new StringBuilder();
        for (int i = at + 1; i < text.Length; i++)
        {
            if (text[i] != '"')
            {
                _ = name.Append(text[i]);
                continue;
            }

            if (i + 1 < text.Length && text[i + 1] == '"')
            {
                _ = name.Append('"');
                i++;
                continue;
            }

            bool fits = name.Length is > 0 and <= MaxIdentifierLength;
            return (fits ? name.ToString() : null, i + 1);
        }

        return (null, text.Length);
    }
}
