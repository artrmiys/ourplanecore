using System.Collections.Generic;
using System.Text;
using System.Windows.Input;

namespace OurPlanCore;

public static class KeyboardShortcutKeys
{
    private static readonly Dictionary<char, char> RussianLayoutByLatin = new()
    {
        ['q'] = 'й',
        ['w'] = 'ц',
        ['e'] = 'у',
        ['r'] = 'к',
        ['t'] = 'е',
        ['y'] = 'н',
        ['u'] = 'г',
        ['i'] = 'ш',
        ['o'] = 'щ',
        ['p'] = 'з',
        ['a'] = 'ф',
        ['s'] = 'ы',
        ['d'] = 'в',
        ['f'] = 'а',
        ['g'] = 'п',
        ['h'] = 'р',
        ['j'] = 'о',
        ['k'] = 'л',
        ['l'] = 'д',
        ['z'] = 'я',
        ['x'] = 'ч',
        ['c'] = 'с',
        ['v'] = 'м',
        ['b'] = 'и',
        ['n'] = 'т',
        ['m'] = 'ь',
    };

    public static Key EffectiveKey(KeyEventArgs e) =>
        e.Key switch
        {
            Key.System => e.SystemKey,
            Key.ImeProcessed => e.ImeProcessedKey,
            Key.DeadCharProcessed => e.DeadCharProcessedKey,
            _ => e.Key,
        };

    public static bool Is(KeyEventArgs e, Key key) =>
        EffectiveKey(e) == key;

    public static string SequenceToken(Key key)
    {
        if (key is >= Key.A and <= Key.Z)
            return ((char)('a' + key - Key.A)).ToString();

        if (key is >= Key.D0 and <= Key.D9)
            return ((char)('0' + key - Key.D0)).ToString();

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
            return ((char)('0' + key - Key.NumPad0)).ToString();

        return "";
    }

    public static string EnglishLayoutDisplay(string latinSequence) =>
        (latinSequence ?? "").Trim().ToUpperInvariant();

    public static string RussianLayoutText(string latinSequence)
    {
        var sb = new StringBuilder();
        foreach (char raw in (latinSequence ?? "").ToLowerInvariant())
            sb.Append(RussianLayoutByLatin.TryGetValue(raw, out char mapped) ? mapped : raw);
        return sb.ToString();
    }

    public static string DualLayoutDisplay(string latinSequence)
    {
        string latin = (latinSequence ?? "").Trim();
        string russian = RussianLayoutText(latin);
        return string.IsNullOrWhiteSpace(russian) || string.Equals(latin, russian, System.StringComparison.OrdinalIgnoreCase)
            ? latin.ToUpperInvariant()
            : $"{latin.ToUpperInvariant()} / {russian.ToUpperInvariant()}";
    }
}
