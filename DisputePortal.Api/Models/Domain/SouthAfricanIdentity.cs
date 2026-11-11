namespace DisputePortal.Api.Models.Domain;

public static class SouthAfricanIdentity
{
    public static int InferCentury(string? number, DateOnly today) => TryGetBirthDate(number, 2000, today, out _) ? 2000 : 1900;

    public static bool TryGetBirthDate(string? number, int century, DateOnly today, out DateOnly birthDate)
    {
        birthDate = default;
        if (number is not { Length: 13 }
            || number.Any(c => c is < '0' or > '9')
            || century is not (1900 or 2000)
            || number[10] is not ('0' or '1'))
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < number.Length; i++)
        {
            var digit = number[i] - '0';
            if (i % 2 == 1)
            {
                digit *= 2;
            }

            sum += digit > 9 ? digit - 9 : digit;
        }

        if (sum % 10 != 0)
        {
            return false;
        }

        var year = century + (number[0] - '0') * 10 + number[1] - '0';
        var month = (number[2] - '0') * 10 + number[3] - '0';
        var day = (number[4] - '0') * 10 + number[5] - '0';
        if (month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return false;
        }

        birthDate = new DateOnly(year, month, day);
        return birthDate <= today;
    }
}
