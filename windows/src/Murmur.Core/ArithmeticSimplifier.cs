using System.Globalization;
using System.Text.RegularExpressions;

namespace Murmur.Core;

/// <summary>
/// Resolves spoken quantity corrections — the Wispr trick.
/// </summary>
/// <remarks>
/// <para>
/// When people dictate numbers they state a quantity, correct themselves, then often say the
/// arithmetic out loud: "three potatoes no one potato no three minus one potatoes" means
/// "two potatoes". This pass finds correction chains — a quantity, a correction marker
/// (<c>no</c>, <c>no wait</c>, <c>actually</c>), another quantity — keeps the final one,
/// and evaluates arithmetic inside it. Standalone arithmetic ("three minus one potatoes")
/// is resolved the same way. Negations ("no potatoes") and ordinary sentences never match:
/// a correction requires a quantity to follow it.
/// </para>
/// <para>
/// Deliberately narrow. This is a text transformation on the transcript, not a calculator —
/// anything that does not parse cleanly is left exactly as dictated. Results below zero are
/// left alone too: "one minus three potatoes" is probably not a quantity.
/// </para>
/// </remarks>
public static class ArithmeticSimplifier
{
    private const string Units =
        "zero|one|two|three|four|five|six|seven|eight|nine|ten|eleven|twelve|thirteen|fourteen|" +
        "fifteen|sixteen|seventeen|eighteen|nineteen";

    private const string Tens =
        "twenty|thirty|forty|fifty|sixty|seventy|eighty|ninety";

    // Each alternative is wrapped in its own group. Without it, .NET's regex engine
    // truncates a nested alternation to its first member ("thirty one" matched as just
    // "thirty") — a silent data-eating quirk that took a probe to pin down.
    private const string Small =
        $"(?:(?:{Units})|(?:{Tens})(?:[ -]?(?:(?:{Units})|(?:{Tens})))?|(?:\\d+))";

    private const string Number =
        $"{Small}(?:\\s+hundred)?";

    private const string Op =
        "(?:minus|take away|less|plus)";

    private const string Quantity =
        $"{Number}(?:\\s+{Op}\\s+{Number})*";

    // Words that are never a quantity's noun — they sit between a number and what the
    // number really describes ("three please"), or follow it in a statement of arithmetic
    // ("two plus two equals four"), and must not be treated as the unit.
    private const string NounGuard =
        "(?!(?:and|or|the|a|an|of|to|for|with|please|equals|equal|is|are|was|were)\\b)";

    /// <summary>
    /// A correction chain: quantity noun (correction quantity noun)+, keeping the last.
    /// </summary>
    private static readonly Regex ChainRegex = new(
        $@"(?<![\w-])(?<q>{Quantity})\s+{NounGuard}(?<n>[a-z]+)" +
        $@"(?:,{{0,3}}\s*(?:no(?: wait)?|actually)\s*,{{0,3}}\s*(?<q>{Quantity})\s+{NounGuard}(?<n>[a-z]+))+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// A standalone arithmetic quantity ("three minus one potatoes"). Chains are consumed
    /// first; this catches the rest.
    /// </summary>
    private static readonly Regex StandaloneRegex = new(
        $@"(?<![\w-])(?<q>{Number}\s+{Op}\s+{Number}(?:\s+{Op}\s+{Number})*)\s+{NounGuard}(?<n>[a-z]+)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Matches one number inside a quantity, at a given position.</summary>
    private static readonly Regex NumberRegex = new(
        $@"\s*{Number}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Matches one operator inside a quantity, at a given position.</summary>
    private static readonly Regex OpRegex = new(
        $@"\s*{Op}",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly Dictionary<string, int> NumberWords = new(StringComparer.OrdinalIgnoreCase)
    {
        ["zero"] = 0, ["one"] = 1, ["two"] = 2, ["three"] = 3, ["four"] = 4, ["five"] = 5,
        ["six"] = 6, ["seven"] = 7, ["eight"] = 8, ["nine"] = 9, ["ten"] = 10, ["eleven"] = 11,
        ["twelve"] = 12, ["thirteen"] = 13, ["fourteen"] = 14, ["fifteen"] = 15, ["sixteen"] = 16,
        ["seventeen"] = 17, ["eighteen"] = 18, ["nineteen"] = 19, ["twenty"] = 20, ["thirty"] = 30,
        ["forty"] = 40, ["fifty"] = 50, ["sixty"] = 60, ["seventy"] = 70, ["eighty"] = 80,
        ["ninety"] = 90,
    };

    /// <summary>
    /// Simplifies correction chains and arithmetic quantities in <paramref name="text"/>.
    /// </summary>
    public static string Simplify(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;

        text = ChainRegex.Replace(text, Resolve);
        text = StandaloneRegex.Replace(text, Resolve);
        return text;
    }

    private static string Resolve(Match match)
    {
        var quantity = match.Groups["q"].Value;
        var noun = match.Groups["n"].Value;

        // Never touch text that does not parse, or results below zero.
        var result = Compute(quantity);
        if (result is null || result < 0) return match.Value;

        // "one" singularises; everything else keeps the unit the user said.
        return result == 1
            ? $"1 {Singularize(noun)}"
            : $"{result} {noun}";
    }

    /// <summary>Evaluates "three minus one", "twenty one take away three", etc.</summary>
    private static int? Compute(string quantity)
    {
        var pos = 0;
        var number = NumberRegex.Match(quantity, pos);
        if (!number.Success) return null;

        var total = ParseNumber(number.Value.Trim());
        if (total is null) return null;
        pos = number.Index + number.Length;

        while (pos < quantity.Length)
        {
            var op = OpRegex.Match(quantity, pos);
            if (!op.Success || op.Index != pos) return null;

            var next = NumberRegex.Match(quantity, op.Index + op.Length);
            if (!next.Success || next.Index != op.Index + op.Length) return null;

            var value = ParseNumber(next.Value.Trim());
            if (value is null) return null;

            total = op.Value.Trim().Equals("minus", StringComparison.OrdinalIgnoreCase)
                 || op.Value.Trim().Equals("take away", StringComparison.OrdinalIgnoreCase)
                 || op.Value.Trim().Equals("less", StringComparison.OrdinalIgnoreCase)
                ? total - value
                : total + value;

            pos = next.Index + next.Length;
        }

        return total;
    }

    /// <summary>Parses "3", "twenty one", "two hundred", "one hundred twenty three".</summary>
    private static int? ParseNumber(string token)
    {
        if (int.TryParse(token, NumberStyles.None, CultureInfo.InvariantCulture, out var digits))
        {
            return digits;
        }

        var total = 0;
        var sawAny = false;

        foreach (var part in token.Split([' ', '-'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Equals("hundred", StringComparison.OrdinalIgnoreCase))
            {
                total = total == 0 ? 100 : total * 100;
                sawAny = true;
                continue;
            }

            if (!NumberWords.TryGetValue(part, out var value)) return null;

            if (value >= 20)
            {
                // Tens never follow a unit within the same word: "one twenty" is invalid.
                if (total % 100 != 0) return null;
                total += value;
            }
            else
            {
                // A unit may follow a ten ("twenty one") but not another unit ("one two").
                if (total % 10 != 0) return null;
                total += value;
            }

            sawAny = true;
        }

        return sawAny ? total : null;
    }

    /// <summary>A small English singulariser for the result "1 …".</summary>
    private static string Singularize(string noun) => noun switch
    {
        _ when noun.EndsWith("ies", StringComparison.Ordinal) => noun[..^3] + "y",
        _ when noun.EndsWith("ses", StringComparison.Ordinal)
            || noun.EndsWith("xes", StringComparison.Ordinal)
            || noun.EndsWith("zes", StringComparison.Ordinal)
            || noun.EndsWith("ches", StringComparison.Ordinal)
            || noun.EndsWith("shes", StringComparison.Ordinal)
            || noun.EndsWith("oes", StringComparison.Ordinal) => noun[..^2],
        _ when noun.EndsWith('s') => noun[..^1],
        _ => noun,
    };
}
