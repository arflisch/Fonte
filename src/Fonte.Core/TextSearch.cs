using System.Globalization;
using System.Text;

namespace Fonte.Core;

public static class TextSearch
{
    /// <summary>Lower case without accents, so "developpe" finds "Développé couché" and "oeuf" finds "Œuf".</summary>
    public static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant().Replace("œ", "oe").Replace("æ", "ae").Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>The words of a search, normalized.</summary>
    public static string[] Words(string query) =>
        Normalize(query).Split([' ', ',', '\'', '-'], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>A word without its plural mark, so "flocons" finds "Flocon d'avoine" and "eggs" finds "Egg".</summary>
    public static string Singular(string word) =>
        word.Length > 3 && (word.EndsWith('s') || word.EndsWith('x')) ? word[..^1] : word;
}
