using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Whisperr;

/// <summary>
/// ISO 4217 minor units for catalog prices.
/// </summary>
public static class WhisperrMoney
{
    // The ISO 4217 minor units that are not 2. The same table as the Whisperr server.
    private static readonly Dictionary<string, int> MinorDigits = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        ["BIF"] = 0, ["CLP"] = 0, ["DJF"] = 0, ["GNF"] = 0, ["ISK"] = 0, ["JPY"] = 0, ["KMF"] = 0, ["KRW"] = 0, ["PYG"] = 0,
        ["RWF"] = 0, ["UGX"] = 0, ["VND"] = 0, ["VUV"] = 0, ["XAF"] = 0, ["XOF"] = 0, ["XPF"] = 0,
        ["BHD"] = 3, ["IQD"] = 3, ["JOD"] = 3, ["KWD"] = 3, ["LYD"] = 3, ["OMR"] = 3, ["TND"] = 3,
        ["CLF"] = 4, ["UYW"] = 4,
    };

    private static readonly Regex Currency = new Regex("^[A-Za-z]{3}$", RegexOptions.CultureInvariant);
    private static readonly Regex DecimalText = new Regex(@"^[0-9]+(\.[0-9]+)?$", RegexOptions.CultureInvariant);

    // 2^53 - 1, so the value is exact in every Whisperr SDK.
    private const decimal MaxMinor = 9007199254740991m;

    /// <summary>
    /// The number of minor-unit digits for an ISO 4217 code (2 unless listed).
    /// </summary>
    public static int CurrencyExponent(string currency)
    {
        if (currency == null || !Currency.IsMatch(currency))
        {
            throw new ArgumentException($"invalid currency \"{currency}\": expected an ISO 4217 code", nameof(currency));
        }

        return MinorDigits.TryGetValue(currency.ToUpperInvariant(), out var digits) ? digits : 2;
    }

    /// <summary>
    /// Converts a price in major units to integer minor units:
    /// <c>ToMinorUnits(15.00m, "USD")</c> is 1500, <c>ToMinorUnits(1500m, "JPY")</c> is 1500,
    /// <c>ToMinorUnits(1.234m, "KWD")</c> is 1234. An amount with more precision than the
    /// currency has, or a negative amount, throws <see cref="ArgumentException"/>: money is never rounded.
    /// </summary>
    public static long ToMinorUnits(decimal amount, string currency)
    {
        var exponent = CurrencyExponent(currency);
        if (amount < 0 || amount > MaxMinor)
        {
            throw InvalidAmount(amount.ToString(CultureInfo.InvariantCulture));
        }

        var scaled = amount;
        for (var i = 0; i < exponent; i++)
        {
            scaled *= 10;
        }

        if (scaled != decimal.Truncate(scaled) || scaled > MaxMinor)
        {
            throw InvalidAmount(amount.ToString(CultureInfo.InvariantCulture));
        }

        return (long)scaled;
    }

    /// <summary>
    /// Converts a decimal string in major units (for example <c>"15.00"</c>) to integer minor units.
    /// See <see cref="ToMinorUnits(decimal, string)"/>.
    /// </summary>
    public static long ToMinorUnits(string amount, string currency)
    {
        CurrencyExponent(currency);
        var text = amount?.Trim() ?? string.Empty;
        if (!DecimalText.IsMatch(text) || !decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
        {
            throw InvalidAmount(amount ?? string.Empty);
        }

        return ToMinorUnits(value, currency);
    }

    private static ArgumentException InvalidAmount(string amount)
    {
        return new ArgumentException(
            $"invalid amount \"{amount}\": expected a non-negative decimal with at most the currency's minor digits",
            nameof(amount));
    }
}
