using System;
using Daihon;

namespace Yuukei.Runtime
{
    /// <summary>
    /// DaihonValue と CLR オブジェクト間の相互変換を行うユーティリティ。
    /// 永続化対象の型判定も担当する。
    /// </summary>
    internal static class DaihonValueUtility
    {
        public static DaihonValue ToDaihonValue(object value)
        {
            if (value == null)
            {
                return DaihonValue.None;
            }

            return value switch
            {
                bool boolValue => DaihonValue.FromBoolean(boolValue),
                string stringValue => DaihonValue.FromString(stringValue),
                byte byteValue => DaihonValue.FromNumber(byteValue),
                sbyte sbyteValue => DaihonValue.FromNumber(sbyteValue),
                short shortValue => DaihonValue.FromNumber(shortValue),
                ushort ushortValue => DaihonValue.FromNumber(ushortValue),
                int intValue => DaihonValue.FromNumber(intValue),
                uint uintValue => DaihonValue.FromNumber(uintValue),
                long longValue => DaihonValue.FromNumber(longValue),
                ulong ulongValue => DaihonValue.FromNumber(ulongValue),
                float floatValue => DaihonValue.FromNumber(floatValue),
                double doubleValue => DaihonValue.FromNumber(doubleValue),
                decimal decimalValue => DaihonValue.FromNumber((double)decimalValue),
                _ => throw new DaihonRuntimeException($"永続化できない値の型です: {value.GetType().Name}")
            };
        }

        public static object ToPersistentObject(DaihonValue value)
        {
            return value.Type switch
            {
                DaihonValue.ValueType.Boolean => value.AsBoolean(),
                DaihonValue.ValueType.Number => value.AsNumber(),
                DaihonValue.ValueType.String => value.AsString(),
                _ => throw new DaihonRuntimeException("永続化できるのは bool / number / string のみです。")
            };
        }

        public static bool IsPersistentType(object value)
        {
            return value is bool
                || value is string
                || value is byte
                || value is sbyte
                || value is short
                || value is ushort
                || value is int
                || value is uint
                || value is long
                || value is ulong
                || value is float
                || value is double
                || value is decimal;
        }

        public static Type GetPersistentType(object value)
        {
            if (value is bool)
            {
                return typeof(bool);
            }

            if (value is string)
            {
                return typeof(string);
            }

            if (IsPersistentType(value))
            {
                return typeof(double);
            }

            return value?.GetType() ?? typeof(void);
        }
    }
}
