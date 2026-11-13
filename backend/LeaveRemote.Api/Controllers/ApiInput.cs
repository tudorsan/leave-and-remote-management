using System.Globalization;
using System.Text;

namespace LeaveRemote.Api.Controllers;

internal static class ApiInput
{
    public const int BCryptMaxPasswordBytes = 72;

    public static bool HasValidRejectionReason(string? reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) return false;

        var characters = StringInfo.GetTextElementEnumerator(reason.Trim());
        var visibleCount = 0;
        var spaces = 0;
        while (characters.MoveNext())
        {
            foreach (var rune in characters.GetTextElement().EnumerateRunes())
            {
                // Blank braille and Hangul fillers are classified as symbols or letters.
                if (rune.Value is 0x2800 or 0x115F or 0x1160 or 0x3164 or 0xFFA0)
                    continue;
                if (Rune.GetUnicodeCategory(rune) is UnicodeCategory.Control or UnicodeCategory.Format or
                    UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
                    continue;

                if (Rune.IsWhiteSpace(rune))
                {
                    if (visibleCount > 0) spaces++;
                    break;
                }

                visibleCount += spaces + 1;
                if (visibleCount >= 10) return true;
                spaces = 0;
                break;
            }
        }
        return false;
    }

    public static bool FitsBCrypt(string password) =>
        !password.Contains('\0') && Encoding.UTF8.GetByteCount(password) <= BCryptMaxPasswordBytes;

    public static bool TryParseEnumName<TEnum>(string? input, out TEnum value)
        where TEnum : struct, Enum
    {
        var name = input?.Trim();
        return Enum.TryParse(name, ignoreCase: true, out value) &&
            string.Equals(Enum.GetName(value), name, StringComparison.OrdinalIgnoreCase);
    }
}
