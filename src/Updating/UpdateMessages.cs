#nullable enable
using System;
using System.Linq;
using System.Text;

namespace ModConfigEnhance.Updating;

public sealed class UpdateMessageTerm
{
    public readonly string Id, Source, English, SimplifiedChinese, TraditionalChinese;
    public UpdateMessageTerm(string id, string source, string english, string traditionalChinese)
    { Id = id; Source = source; English = english; SimplifiedChinese = source; TraditionalChinese = traditionalChinese; }
}

// The worker persists a stable message ID and a separately encoded argument. Both
// runtimes share this format; old plain-text queue records remain readable.
public static partial class UpdateMessages
{
    private const string Prefix = "@MCE[";
    public static string Encode(string message)
    {
        if (string.IsNullOrEmpty(message) || message.StartsWith(Prefix, StringComparison.Ordinal)) return message;
        var term = Terms.FirstOrDefault(t => t.Source == message)
            ?? Terms.Where(t => t.Source.Length > 0 && message.StartsWith(t.Source, StringComparison.Ordinal))
                .OrderByDescending(t => t.Source.Length).FirstOrDefault();
        return term == null ? message : Prefix + term.Id + "]" + Convert.ToBase64String(Encoding.UTF8.GetBytes(message.Substring(term.Source.Length)));
    }
    public static bool TryDecode(string message, out UpdateMessageTerm? term, out string argument)
    {
        term = null; argument = "";
        if (!message.StartsWith(Prefix, StringComparison.Ordinal)) return false;
        int end = message.IndexOf(']', Prefix.Length);
        if (end < 0) return false;
        term = Terms.FirstOrDefault(t => t.Id == message.Substring(Prefix.Length, end - Prefix.Length));
        if (term == null) return false;
        try { argument = Encoding.UTF8.GetString(Convert.FromBase64String(message.Substring(end + 1))); return true; }
        catch (FormatException) { term = null; return false; }
    }
}
