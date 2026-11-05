using System.Globalization;
using System.Numerics;

namespace AutoUpdater.Models;

/// Упрощённый, но совместимый с основными правилами SemVer 2.0.0 класс версии.
/// Формат: MAJOR.MINOR.PATCH[-PRERELEASE][+BUILD].
public sealed class SemVer : IComparable<SemVer>
{
    private readonly List<string> _preReleaseIdentifiers;
    private readonly string _originalValue;

    private SemVer(
        BigInteger major,
        BigInteger minor,
        BigInteger patch,
        List<string> preReleaseIdentifiers,
        string originalValue)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        _preReleaseIdentifiers = preReleaseIdentifiers;
        _originalValue = originalValue;
    }

    public BigInteger Major { get; }
    public BigInteger Minor { get; }
    public BigInteger Patch { get; }

    public static SemVer Parse(string value)
    {
        if (!TryParse(value, out SemVer? result) || result == null)
        {
            throw new FormatException("Некорректная версия SemVer: " + value);
        }

        return result;
    }

    public static bool TryParse(string? value, out SemVer? result)
    {
        result = null;

        if (string.IsNullOrWhiteSpace(value) || value.Trim() != value)
        {
            return false;
        }

        string versionPart = value;
        string buildPart = "";

        // Отделяем необязательные метаданные сборки после символа +.
        int plusIndex = versionPart.IndexOf('+');
        if (plusIndex >= 0)
        {
            if (versionPart.IndexOf('+', plusIndex + 1) >= 0)
            {
                return false;
            }

            buildPart = versionPart[(plusIndex + 1)..];
            versionPart = versionPart[..plusIndex];

            if (!AreIdentifiersValid(buildPart, false))
            {
                return false;
            }
        }

        // Отделяем предварительную версию, например beta.1.
        string preReleasePart = "";
        int dashIndex = versionPart.IndexOf('-');
        if (dashIndex >= 0)
        {
            preReleasePart = versionPart[(dashIndex + 1)..];
            versionPart = versionPart[..dashIndex];

            if (!AreIdentifiersValid(preReleasePart, true))
            {
                return false;
            }
        }

        string[] coreParts = versionPart.Split('.');
        if (coreParts.Length != 3)
        {
            return false;
        }

        if (!TryParseCoreNumber(coreParts[0], out BigInteger major) ||
            !TryParseCoreNumber(coreParts[1], out BigInteger minor) ||
            !TryParseCoreNumber(coreParts[2], out BigInteger patch))
        {
            return false;
        }

        List<string> preReleaseIdentifiers = new();
        if (preReleasePart.Length > 0)
        {
            preReleaseIdentifiers.AddRange(preReleasePart.Split('.'));
        }

        result = new SemVer(major, minor, patch, preReleaseIdentifiers, value);
        return true;
    }

    public int CompareTo(SemVer? other)
    {
        if (other == null)
        {
            return 1;
        }

        int result = Major.CompareTo(other.Major);
        if (result != 0) return result;

        result = Minor.CompareTo(other.Minor);
        if (result != 0) return result;

        result = Patch.CompareTo(other.Patch);
        if (result != 0) return result;

        // Версия без предварительной части новее такой же версии с beta/alpha/rc.
        bool thisIsRelease = _preReleaseIdentifiers.Count == 0;
        bool otherIsRelease = other._preReleaseIdentifiers.Count == 0;

        if (thisIsRelease && otherIsRelease) return 0;
        if (thisIsRelease) return 1;
        if (otherIsRelease) return -1;

        int commonLength = Math.Min(
            _preReleaseIdentifiers.Count,
            other._preReleaseIdentifiers.Count);

        for (int index = 0; index < commonLength; index++)
        {
            string left = _preReleaseIdentifiers[index];
            string right = other._preReleaseIdentifiers[index];

            bool leftIsNumber = IsNumericIdentifier(left);
            bool rightIsNumber = IsNumericIdentifier(right);

            if (leftIsNumber && rightIsNumber)
            {
                BigInteger leftNumber = BigInteger.Parse(left, CultureInfo.InvariantCulture);
                BigInteger rightNumber = BigInteger.Parse(right, CultureInfo.InvariantCulture);
                result = leftNumber.CompareTo(rightNumber);
            }
            else if (leftIsNumber)
            {
                // Числовой идентификатор имеет меньший приоритет, чем текстовый.
                result = -1;
            }
            else if (rightIsNumber)
            {
                result = 1;
            }
            else
            {
                result = string.CompareOrdinal(left, right);
            }

            if (result != 0)
            {
                return result;
            }
        }

        // Если общая часть совпала, более короткий список идентификаторов старше.
        return _preReleaseIdentifiers.Count.CompareTo(other._preReleaseIdentifiers.Count);
    }

    public override string ToString()
    {
        return _originalValue;
    }

    private static bool TryParseCoreNumber(string value, out BigInteger number)
    {
        number = BigInteger.Zero;

        if (value.Length == 0)
        {
            return false;
        }

        if (value.Length > 1 && value[0] == '0')
        {
            return false;
        }

        foreach (char symbol in value)
        {
            if (symbol < '0' || symbol > '9')
            {
                return false;
            }
        }

        return BigInteger.TryParse(
            value,
            NumberStyles.None,
            CultureInfo.InvariantCulture,
            out number);
    }

    private static bool AreIdentifiersValid(string value, bool forbidLeadingZeroInNumbers)
    {
        if (value.Length == 0)
        {
            return false;
        }

        string[] identifiers = value.Split('.');

        foreach (string identifier in identifiers)
        {
            if (identifier.Length == 0)
            {
                return false;
            }

            bool isNumeric = true;

            foreach (char symbol in identifier)
            {
                bool isAllowed =
                    (symbol >= '0' && symbol <= '9') ||
                    (symbol >= 'A' && symbol <= 'Z') ||
                    (symbol >= 'a' && symbol <= 'z') ||
                    symbol == '-';

                if (!isAllowed)
                {
                    return false;
                }

                if (symbol < '0' || symbol > '9')
                {
                    isNumeric = false;
                }
            }

            if (forbidLeadingZeroInNumbers && isNumeric &&
                identifier.Length > 1 && identifier[0] == '0')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsNumericIdentifier(string value)
    {
        foreach (char symbol in value)
        {
            if (symbol < '0' || symbol > '9')
            {
                return false;
            }
        }

        return true;
    }
}
