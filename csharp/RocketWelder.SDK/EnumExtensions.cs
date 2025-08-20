using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace RocketWelder.SDK;

/// <summary>
/// Extension methods for working with enums, particularly flag enums.
/// </summary>
public static class EnumExtensions
{
    /// <summary>
    /// Parses a string representation of a flags enum, supporting the '+' operator for combining flags.
    /// Example: "Mjpeg+Http" or "Read+Write+Execute"
    /// </summary>
    /// <typeparam name="TEnum">The enum type (must have Flags attribute)</typeparam>
    /// <param name="value">The string value to parse</param>
    /// <param name="ignoreCase">Whether to ignore case when parsing</param>
    /// <param name="result">The parsed enum value</param>
    /// <returns>True if parsing succeeded, false otherwise</returns>
    public static bool TryParseFlags<TEnum>(string value, bool ignoreCase, out TEnum result) where TEnum : struct, Enum
    {
        result = default;
        
        if (string.IsNullOrWhiteSpace(value))
            return false;

        // Check if the enum type has the Flags attribute
        var enumType = typeof(TEnum);
        var hasFlagsAttribute = Attribute.IsDefined(enumType, typeof(FlagsAttribute));
        
        // If it's not a flags enum, just use standard parsing
        if (!hasFlagsAttribute)
        {
            return Enum.TryParse<TEnum>(value, ignoreCase, out result);
        }

        // Split by '+' to handle combined flags
        var parts = value.Split('+');
        
        // Parse each part and combine them
        var combinedValue = 0;
        foreach (var part in parts)
        {
            var trimmedPart = part.Trim();
            if (string.IsNullOrEmpty(trimmedPart))
                continue;
                
            if (!Enum.TryParse<TEnum>(trimmedPart, ignoreCase, out var partValue))
                return false;
                
            combinedValue |= Convert.ToInt32(partValue);
        }
        
        result = (TEnum)Enum.ToObject(enumType, combinedValue);
        return true;
    }
    
    /// <summary>
    /// Parses a string representation of a flags enum, supporting the '+' operator for combining flags.
    /// Throws FormatException if parsing fails.
    /// </summary>
    /// <typeparam name="TEnum">The enum type (must have Flags attribute)</typeparam>
    /// <param name="value">The string value to parse</param>
    /// <param name="ignoreCase">Whether to ignore case when parsing</param>
    /// <returns>The parsed enum value</returns>
    public static TEnum ParseFlags<TEnum>(string value, bool ignoreCase = true) where TEnum : struct, Enum
    {
        if (TryParseFlags<TEnum>(value, ignoreCase, out var result))
            return result;
            
        throw new FormatException($"Unable to parse '{value}' as {typeof(TEnum).Name}");
    }
    
    /// <summary>
    /// Converts a flags enum to a string representation using '+' as separator.
    /// Example: Protocol.Mjpeg | Protocol.Http becomes "Mjpeg+Http"
    /// </summary>
    /// <typeparam name="TEnum">The enum type</typeparam>
    /// <param name="value">The enum value</param>
    /// <param name="separator">The separator to use (default is "+")</param>
    /// <returns>String representation of the flags</returns>
    public static string ToFlagsString<TEnum>(this TEnum value, string separator = "+") where TEnum : struct, Enum
    {
        var enumType = typeof(TEnum);
        var hasFlagsAttribute = Attribute.IsDefined(enumType, typeof(FlagsAttribute));
        
        if (!hasFlagsAttribute)
        {
            return value.ToString();
        }
        
        var enumValue = Convert.ToInt64(value);
        if (enumValue == 0)
        {
            // Handle the zero/None case
            return Enum.GetName(enumType, value) ?? "0";
        }
        
        var names = new List<string>();
        foreach (var enumMember in Enum.GetValues<TEnum>())
        {
            var memberValue = Convert.ToInt64(enumMember);
            
            // Skip zero value and composite values
            if (memberValue == 0 || !IsPowerOfTwo(memberValue))
                continue;
                
            if ((enumValue & memberValue) == memberValue)
            {
                names.Add(Enum.GetName(enumType, enumMember) ?? memberValue.ToString());
            }
        }
        
        return names.Count > 0 ? string.Join(separator, names) : value.ToString();
    }
    
    /// <summary>
    /// Checks if a number is a power of two (single bit flag)
    /// </summary>
    private static bool IsPowerOfTwo(long x)
    {
        return x > 0 && (x & (x - 1)) == 0;
    }
    
    /// <summary>
    /// Gets the description from the DescriptionAttribute of an enum value.
    /// Falls back to the enum name if no description is found.
    /// </summary>
    /// <typeparam name="TEnum">The enum type</typeparam>
    /// <param name="value">The enum value</param>
    /// <returns>The description or enum name</returns>
    public static string GetDescription<TEnum>(this TEnum value) where TEnum : struct, Enum
    {
        var type = value.GetType();
        var name = Enum.GetName(type, value);
        
        if (name == null)
            return value.ToString();
            
        var field = type.GetField(name);
        if (field == null)
            return name;
            
        var attribute = field.GetCustomAttribute<DescriptionAttribute>();
        return attribute?.Description ?? name;
    }
}