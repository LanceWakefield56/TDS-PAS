namespace Galenica.Core.Patients;

public enum NhiFormat { Unknown = 0, Old = 1, New = 2 }

public sealed record NhiValidationResult(bool IsValid, NhiFormat Format, bool IsTestNumber, string? Error)
{
    public static NhiValidationResult Fail(string error, NhiFormat format = NhiFormat.Unknown) => new(false, format, false, error);
}

/// <summary>
/// Validates NHI numbers in both the old (AAANNNC) and new (AAANNAX) formats using the
/// HISO 10046 check-character routine. A valid NHI is only well-formed: it does not prove
/// the number belongs to the patient (always confirm against the NHI service).
/// NHIs beginning with 'Z' are reserved for testing.
/// </summary>
public static class NhiValidator
{
    // Letters A–Z excluding I and O map to 1–24.
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private static readonly int[] Weights = [7, 6, 5, 4, 3, 2];

    public static NhiValidationResult Validate(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return NhiValidationResult.Fail("NHI is required.");

        var nhi = input.Trim().ToUpperInvariant();
        if (nhi.Length != 7)
            return NhiValidationResult.Fail("NHI must be 7 characters.");

        for (var i = 0; i < 3; i++)
            if (LetterValue(nhi[i]) == 0)
                return NhiValidationResult.Fail("The first three characters must be letters (not I or O).");

        if (!char.IsAsciiDigit(nhi[3]) || !char.IsAsciiDigit(nhi[4]))
            return NhiValidationResult.Fail("Characters 4 and 5 must be digits.");

        var isTest = nhi[0] == 'Z';
        var format = char.IsAsciiDigit(nhi[5]) ? NhiFormat.Old : NhiFormat.New;

        if (format == NhiFormat.New && LetterValue(nhi[5]) == 0)
            return NhiValidationResult.Fail("Character 6 must be a digit or a letter (not I or O).");

        var sum = 0;
        for (var i = 0; i < 6; i++)
            sum += CharValue(nhi[i]) * Weights[i];

        if (format == NhiFormat.Old)
        {
            if (!char.IsAsciiDigit(nhi[6]))
                return NhiValidationResult.Fail("The check character must be a digit for this NHI format.", format);
            var mod = sum % 11;
            if (mod == 0)
                return NhiValidationResult.Fail("Invalid NHI (check calculation).", format);
            var check = 11 - mod;
            if (check == 10) check = 0;
            return check == nhi[6] - '0'
                ? new NhiValidationResult(true, format, isTest, null)
                : NhiValidationResult.Fail("NHI check digit does not match.", format);
        }
        else
        {
            var mod = sum % 23;
            if (mod == 0)
                return NhiValidationResult.Fail("Invalid NHI (check calculation).", format);
            var expected = 23 - mod; // 1..23 → A..Y
            return LetterValue(nhi[6]) == expected
                ? new NhiValidationResult(true, format, isTest, null)
                : NhiValidationResult.Fail("NHI check letter does not match.", format);
        }
    }

    private static int LetterValue(char c) => Alphabet.IndexOf(c) + 1; // 0 when not a valid letter

    private static int CharValue(char c) => char.IsAsciiDigit(c) ? c - '0' : LetterValue(c);
}
