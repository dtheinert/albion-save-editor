using System.Globalization;

namespace AlbionSaveEditor.Api;

/// <summary>Thrown for invalid input; mapped to 400 {error, field}.</summary>
public sealed class ApiValidationException(string field, string message) : Exception(message)
{
    public string Field { get; } = field;
}

/// <summary>
/// Converts library enums to/from names.
/// Plain enums: the member name, or the decimal number for values the library does not name.
/// Flags enums: array of member names. Bits that no member covers are not listed and are
/// always preserved on write, so a round trip never loses data.
/// </summary>
public static class EnumMapper
{
    public static string ToName<T>(T value) where T : struct, Enum
        => Enum.GetName(value) ?? Convert.ToUInt64(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture);

    public static T ParseName<T>(string? text, T current, string field) where T : struct, Enum
    {
        if (text == null) return current;
        text = text.Trim();
        if (Enum.GetNames<T>().FirstOrDefault(n => string.Equals(n, text, StringComparison.OrdinalIgnoreCase)) is { } name)
            return Enum.Parse<T>(name);
        if (ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong number))
        {
            if (number > MaxValue<T>())
                throw new ApiValidationException(field, $"{number} is out of range (0..{MaxValue<T>()}).");
            return (T)Enum.ToObject(typeof(T), number);
        }
        throw new ApiValidationException(field, $"Unknown value '{text}'. Allowed: {string.Join(", ", Enum.GetNames<T>())}.");
    }

    public static List<string> ToFlagNames<T>(T value) where T : struct, Enum
    {
        ulong v = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
        var names = new List<string>();
        foreach (var (name, bits) in Members<T>())
        {
            if (bits != 0 && (v & bits) == bits) names.Add(name);
        }
        if (names.Count == 0 && v == 0 && Members<T>().FirstOrDefault(m => m.Bits == 0).Name is { } zero)
            names.Add(zero); // e.g. MagicFlags.None
        return names;
    }

    public static T ParseFlags<T>(List<string>? names, T current, string field) where T : struct, Enum
    {
        if (names == null) return current;
        ulong defined = Members<T>().Aggregate(0UL, (acc, m) => acc | m.Bits);
        ulong result = Convert.ToUInt64(current, CultureInfo.InvariantCulture) & ~defined; // keep undocumented bits
        for (int i = 0; i < names.Count; i++)
        {
            string? n = names[i]?.Trim();
            var member = Members<T>().FirstOrDefault(m => string.Equals(m.Name, n, StringComparison.OrdinalIgnoreCase));
            if (member.Name == null)
                throw new ApiValidationException($"{field}[{i}]", $"Unknown flag '{n}'. Allowed: {string.Join(", ", Enum.GetNames<T>())}.");
            result |= member.Bits;
        }
        return (T)Enum.ToObject(typeof(T), result);
    }

    public static IEnumerable<(string Name, ulong Bits)> Members<T>() where T : struct, Enum
        => Enum.GetValues<T>().Select(v => (Enum.GetName(v)!, Convert.ToUInt64(v, CultureInfo.InvariantCulture)));

    private static ulong MaxValue<T>() where T : struct, Enum
        => Type.GetTypeCode(Enum.GetUnderlyingType(typeof(T))) switch
        {
            TypeCode.Byte => byte.MaxValue,
            TypeCode.UInt16 => ushort.MaxValue,
            TypeCode.Int16 => (ulong)short.MaxValue,
            TypeCode.SByte => (ulong)sbyte.MaxValue,
            _ => int.MaxValue,
        };

    /// <summary>Enum metadata for /api/meta.</summary>
    public static object Describe(Type enumType)
    {
        bool flags = enumType.IsDefined(typeof(FlagsAttribute), false);
        var values = Enum.GetValues(enumType).Cast<object>()
            .Select(v => new { name = Enum.GetName(enumType, v)!, value = Convert.ToInt64(v, CultureInfo.InvariantCulture) })
            .OrderBy(v => v.value)
            .ToList();
        return new { flags, values };
    }
}
