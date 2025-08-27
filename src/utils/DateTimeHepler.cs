using System;
using System.Globalization;

namespace WorkDailyReport.Utils
{
    public static class DateTimeHelpers
    {
        /// <summary>
        /// Converte un DateTimeOffset in stringa ISO-8601 round-trip (compatibile con ActivityWatch).
        /// Esempio: 2025-08-27T09:00:00.0000000+02:00
        /// </summary>
        public static string ToIso8601(DateTimeOffset dt) =>
            dt.ToString("O", CultureInfo.InvariantCulture);

        /// <summary>
        /// Esegue parsing sicuro di una stringa ISO-8601 round-trip.
        /// Restituisce null se il formato non è valido.
        /// </summary>
        public static DateTimeOffset? ParseIso8601(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;

            if (DateTimeOffset.TryParseExact(
                    s,
                    "O",  // round-trip format
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var result))
            {
                return result;
            }

            return null;
        }
    }
}
