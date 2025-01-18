using System;
using System.ComponentModel.DataAnnotations.Schema;
using System.Reflection;

namespace WebAPI.Extensions
{
    public static class PropertyInfoExtensions
    {
        public static string GetColumnName(this PropertyInfo property)
        {
            var columnAttr = property.GetCustomAttribute<ColumnAttribute>();
            return columnAttr?.Name ?? property.Name;
        }

        public static bool IsNumericType(this Type type)
        {
            if (type == null)
                return false;

            Type underlyingType = Nullable.GetUnderlyingType(type) ?? type;

            return Type.GetTypeCode(underlyingType) switch
            {
                TypeCode.Byte => true,
                TypeCode.SByte => true,
                TypeCode.UInt16 => true,
                TypeCode.UInt32 => true,
                TypeCode.UInt64 => true,
                TypeCode.Int16 => true,
                TypeCode.Int32 => true,
                TypeCode.Int64 => true,
                TypeCode.Decimal => true,
                TypeCode.Double => true,
                TypeCode.Single => true,
                _ => false,
            };
        }
    }
}
