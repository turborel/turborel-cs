// The files connection settings come from [CLIENT The service file]
// [CLIENT The password file]: a service file of named sections of
// settings, and a password file of passwords by server and user.
//
// A service file holds sections, each [name] on a line of its own, then
// lines key = value, each key a setting's name but service and
// service_file; a line beginning with # is a comment, blank lines are
// skipped. A password file holds lines host:port:user:password, a field *
// matching anything, \ taking the character after it as it is; the first
// line that matches is taken, a line of fewer fields skipped. A password
// file is read only if it is a regular file, or a link to one: .NET's
// portable library reads no file's owner or permissions, so its owner
// answers for keeping it private, as turborel-client says of its own.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Turborel.Data.Configuration;

/// <summary>The service file and the password file.</summary>
internal static class SettingsFiles
{
    private const char Comment = '#';
    private const string Any = "*";
    private const char Escape = '\\';
    private const char Separator = ':';
    private const int Fields = 4;

    /// <summary>Reads the section <paramref name="name"/> of the service file <paramref name="text"/>, read from <paramref name="file"/>.</summary>
    /// <param name="text">The file's text.</param>
    /// <param name="name">The section.</param>
    /// <param name="file">The file, to name in a failure.</param>
    /// <returns>The section's settings.</returns>
    /// <exception cref="TurborelException">SQLSTATE 08001 if a line is malformed or sets no setting, or there is no section of the name.</exception>
    internal static ConnectionOptions Service(string text, string name, string file)
    {
        ConnectionOptions? found = null;
        string? current = null;
        string[] lines = text.ReplaceLineEndings("\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i].Trim();
            int n = i + 1;
            if (line.Length == 0 || line[0] == Comment)
            {
                continue;
            }

            if (line[0] == '[')
            {
                current = line.EndsWith(']') ? line[1..^1].Trim() : throw At(file, n, "a section's name ends with ]");
                continue;
            }

            string section = current ?? throw At(file, n, "a setting comes before any section");
            int equals = line.IndexOf('=', StringComparison.Ordinal);
            string key = equals < 0 ? throw At(file, n, "a setting is <key> = <value>") : line[..equals].Trim();
            if (key is ConnectionOptions.Service or ConnectionOptions.ServiceFile)
            {
                throw At(file, n, $"a service cannot set {key}");
            }

            var options = new ConnectionOptions();
            try
            {
                _ = options.Set(key, line[(equals + 1)..].Trim());
            }
            catch (TurborelException e)
            {
                throw At(file, n, e.Message);
            }

            if (section == name)
            {
                found = options.Over(found ?? new ConnectionOptions());
            }
        }

        return found ?? throw ConnectionOptions.Invalid($"[DESIGN] {file} has no service {name}");
    }

    /// <summary>Gives the password the password file <paramref name="text"/> gives a user at a host and port: that of its first line matching all three.</summary>
    /// <param name="text">The file's text.</param>
    /// <param name="wanted">The host, the port and the user.</param>
    /// <returns>The password, or null.</returns>
    internal static string? Password(string text, (string Host, string Port, string User) wanted)
    {
        foreach (string line in text.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.TrimStart().StartsWith(Comment))
            {
                continue;
            }

            List<(string Value, bool Plain)> fields = FieldsOf(line);
            if (fields.Count == Fields && Matches(fields[0], wanted.Host) && Matches(fields[1], wanted.Port) && Matches(fields[2], wanted.User))
            {
                return fields[3].Value;
            }
        }

        return null;
    }

    /// <summary>Reads the file at <paramref name="path"/>, if it can be read and is a regular file, a link followed to one.</summary>
    /// <param name="path">The file.</param>
    /// <returns>Its text, or null.</returns>
    internal static string? ReadRegular(string path)
    {
        try
        {
            FileSystemInfo info = new FileInfo(path);
            if (info.LinkTarget is not null)
            {
                info = info.ResolveLinkTarget(returnFinalTarget: true) ?? info;
            }

            return info is FileInfo { Exists: true } file && !file.Attributes.HasFlag(FileAttributes.Device) ? File.ReadAllText(file.FullName) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static TurborelException At(string file, int line, string why) =>
        ConnectionOptions.Invalid($"[DESIGN] {file} line {line}: {why}");

    // The fields of a password file's line, unescaped: the first three
    // split at each colon not escaped, the fourth the rest; each with
    // whether it was written with nothing escaped.
    private static List<(string Value, bool Plain)> FieldsOf(string line)
    {
        var fields = new List<(string, bool)>(Fields);
        var field = new StringBuilder();
        bool escaped = false;
        bool plain = true;
        foreach (char c in line)
        {
            if (escaped)
            {
                _ = field.Append(c);
                (escaped, plain) = (false, false);
            }
            else if (c == Escape)
            {
                escaped = true;
            }
            else if (c == Separator && fields.Count < Fields - 1)
            {
                fields.Add((field.ToString(), plain));
                _ = field.Clear();
                plain = true;
            }
            else
            {
                _ = field.Append(c);
            }
        }

        fields.Add((field.ToString(), plain));
        return fields;
    }

    // A * written plain matches anything.
    private static bool Matches((string Value, bool Plain) field, string wanted) =>
        (field.Plain && field.Value == Any) || field.Value == wanted;
}
