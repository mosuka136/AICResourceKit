using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace AICResourceKit.Contracts
{
    internal static class ContractValue
    {
        internal static Dictionary<string, object> Object(object value)
        {
            return value as Dictionary<string, object> ?? throw new InvalidDataException("Expected JSON object.");
        }

        internal static object Get(Dictionary<string, object> value, string key)
        {
            if (value == null) throw new InvalidDataException("Expected JSON object.");
            return value.TryGetValue(key, out var result) ? result : null;
        }

        internal static string String(Dictionary<string, object> value, string key, string fallback = null)
        {
            var item = Get(value, key);
            if (item == null)
                return fallback;
            return item as string ?? throw new InvalidDataException("Expected string: " + key);
        }

        internal static List<object> Array(object value)
        {
            return value as List<object> ?? throw new InvalidDataException("Expected JSON array.");
        }

        internal static double Number(object value)
        {
            if (!(value is float || value is double || value is int || value is long))
                throw new InvalidDataException("Expected number.");
            double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            if (double.IsInfinity(number) || double.IsNaN(number))
                throw new InvalidDataException("Non-finite number.");
            return number;
        }

        internal static int Integer(object value)
        {
            double number = Number(value);
            if (number != Math.Truncate(number) || number < int.MinValue || number > int.MaxValue)
                throw new InvalidDataException("Expected integer.");
            return (int)number;
        }
    }
}
