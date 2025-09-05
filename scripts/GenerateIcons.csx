#!/usr/bin/env dotnet-script
#r "nuget: MudBlazor, 6.11.0"

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using MudBlazor;

// Script to generate Icons classes for C# and Python from MudBlazor Icons using reflection

// Get all icon categories and their icons using reflection
var iconCategories = GetIconsViaReflection();

GenerateCSharpIcons(iconCategories);
GeneratePythonIcons(iconCategories);

Console.WriteLine();
Console.WriteLine("Icon generation completed successfully!");
Console.WriteLine();
Console.WriteLine("Usage examples:");
Console.WriteLine();
Console.WriteLine("C#:");
Console.WriteLine("  var icon = Icons.Material.Filled.Home;");
Console.WriteLine("  var button = factory.DefineIconButton(\"btn\", Icons.Material.Filled.Settings);");
Console.WriteLine();
Console.WriteLine("Python:");
Console.WriteLine("  from rocketwelder.ui.icons import Icons, Material");
Console.WriteLine("  icon = Material.Filled.HOME");
Console.WriteLine("  button = factory.define_icon_button(\"btn\", Material.Filled.SETTINGS)");

Dictionary<string, Dictionary<string, Dictionary<string, string>>> GetIconsViaReflection()
{
    var result = new Dictionary<string, Dictionary<string, Dictionary<string, string>>>();
    
    // Get the MudBlazor.Icons type
    var iconsType = typeof(MudBlazor.Icons);
    
    // Get all nested classes (Material, Custom, FileFormats, Brands)
    var nestedTypes = iconsType.GetNestedTypes(BindingFlags.Public | BindingFlags.Static);
    
    foreach (var categoryType in nestedTypes)
    {
        var categoryName = categoryType.Name;
        var categoryDict = new Dictionary<string, Dictionary<string, string>>();
        
        // Get subcategories (Filled, Outlined, Rounded, Sharp, TwoTone)
        var subCategories = categoryType.GetNestedTypes(BindingFlags.Public | BindingFlags.Static);
        
        if (subCategories.Length > 0)
        {
            foreach (var subCategoryType in subCategories)
            {
                var subCategoryName = subCategoryType.Name;
                var icons = new Dictionary<string, string>();
                
                // Get all const string fields (the actual icons)
                var iconFields = subCategoryType.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                    .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string));
                
                foreach (var field in iconFields)
                {
                    var iconName = field.Name;
                    var iconValue = field.GetValue(null) as string;
                    if (!string.IsNullOrEmpty(iconValue))
                    {
                        icons[iconName] = iconValue;
                    }
                }
                
                if (icons.Count > 0)
                {
                    categoryDict[subCategoryName] = icons;
                }
            }
        }
        else
        {
            // Handle categories without subcategories (like Custom, FileFormats, Brands)
            var icons = new Dictionary<string, string>();
            
            var iconFields = categoryType.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string));
            
            foreach (var field in iconFields)
            {
                var iconName = field.Name;
                var iconValue = field.GetValue(null) as string;
                if (!string.IsNullOrEmpty(iconValue))
                {
                    icons[iconName] = iconValue;
                }
            }
            
            if (icons.Count > 0)
            {
                categoryDict["Default"] = icons;
            }
        }
        
        if (categoryDict.Count > 0)
        {
            result[categoryName] = categoryDict;
        }
    }
    
    return result;
}

void GenerateCSharpIcons(Dictionary<string, Dictionary<string, Dictionary<string, string>>> iconCategories)
{
    var sb = new StringBuilder();
    sb.AppendLine("// Generated file - do not edit manually");
    sb.AppendLine("// Generated from MudBlazor Icons using reflection");
    sb.AppendLine("// MudBlazor version: 6.11.0");
    sb.AppendLine();
    sb.AppendLine("namespace RocketWelder.SDK.Ui;");
    sb.AppendLine();
    sb.AppendLine("/// <summary>");
    sb.AppendLine("/// Material Design icon SVG strings for use with IconButton controls.");
    sb.AppendLine("/// Extracted from MudBlazor.Icons via reflection.");
    sb.AppendLine("/// </summary>");
    sb.AppendLine("public static class Icons");
    sb.AppendLine("{");
    
    foreach (var category in iconCategories.OrderBy(c => c.Key))
    {
        sb.AppendLine($"    public static class {category.Key}");
        sb.AppendLine("    {");
        
        foreach (var subCategory in category.Value.OrderBy(s => s.Key))
        {
            if (subCategory.Key != "Default")
            {
                sb.AppendLine($"        public static class {subCategory.Key}");
                sb.AppendLine("        {");
                
                foreach (var icon in subCategory.Value.OrderBy(i => i.Key))
                {
                    // Escape the string properly for C#
                    var escapedValue = icon.Value.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    sb.AppendLine($"            public const string {icon.Key} = \"{escapedValue}\";");
                }
                
                sb.AppendLine("        }");
                sb.AppendLine();
            }
            else
            {
                // Direct icons without subcategory
                foreach (var icon in subCategory.Value.OrderBy(i => i.Key))
                {
                    var escapedValue = icon.Value.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    sb.AppendLine($"        public const string {icon.Key} = \"{escapedValue}\";");
                }
            }
        }
        
        sb.AppendLine("    }");
        sb.AppendLine();
    }
    
    sb.AppendLine("}");
    
    var outputPath = Path.Combine(Directory.GetCurrentDirectory(), "../csharp/RocketWelder.SDK/Ui/Icons.cs");
    File.WriteAllText(outputPath, sb.ToString());
    Console.WriteLine($"C# Icons class generated at: {outputPath}");
    Console.WriteLine($"Total categories: {iconCategories.Count}");
    Console.WriteLine($"Total icons: {iconCategories.Sum(c => c.Value.Sum(s => s.Value.Count))}");
}

void GeneratePythonIcons(Dictionary<string, Dictionary<string, Dictionary<string, string>>> iconCategories)
{
    var sb = new StringBuilder();
    sb.AppendLine("\"\"\"Material Design icon SVG strings for use with IconButton controls.\"\"\"");
    sb.AppendLine();
    sb.AppendLine("# Generated file - do not edit manually");
    sb.AppendLine("# Generated from MudBlazor Icons using reflection");
    sb.AppendLine("# MudBlazor version: 6.11.0");
    sb.AppendLine();
    
    foreach (var category in iconCategories.OrderBy(c => c.Key))
    {
        sb.AppendLine();
        sb.AppendLine($"class {category.Key}:");
        sb.AppendLine($"    \"\"\"{category.Key} icons from MudBlazor.\"\"\"");
        sb.AppendLine();
        
        foreach (var subCategory in category.Value.OrderBy(s => s.Key))
        {
            if (subCategory.Key != "Default")
            {
                sb.AppendLine($"    class {subCategory.Key}:");
                sb.AppendLine($"        \"\"\"{subCategory.Key} variant of {category.Key} icons.\"\"\"");
                sb.AppendLine();
                
                foreach (var icon in subCategory.Value.OrderBy(i => i.Key))
                {
                    var pythonName = ConvertToPythonName(icon.Key);
                    // Escape the string properly for Python
                    var escapedValue = icon.Value.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    sb.AppendLine($"        {pythonName} = \"{escapedValue}\"");
                }
                sb.AppendLine();
            }
            else
            {
                // Direct icons without subcategory
                foreach (var icon in subCategory.Value.OrderBy(i => i.Key))
                {
                    var pythonName = ConvertToPythonName(icon.Key);
                    var escapedValue = icon.Value.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    sb.AppendLine($"    {pythonName} = \"{escapedValue}\"");
                }
            }
        }
    }
    
    sb.AppendLine();
    sb.AppendLine();
    sb.AppendLine("# Convenience class with all icon categories");
    sb.AppendLine("class Icons:");
    sb.AppendLine("    \"\"\"All available icon categories.\"\"\"");
    sb.AppendLine();
    
    foreach (var category in iconCategories.OrderBy(c => c.Key))
    {
        var pythonCategoryName = ConvertToPythonName(category.Key);
        sb.AppendLine($"    {pythonCategoryName} = {category.Key}");
    }
    
    var outputPath = Path.Combine(Directory.GetCurrentDirectory(), "../python/rocketwelder/ui/icons.py");
    File.WriteAllText(outputPath, sb.ToString());
    Console.WriteLine($"Python Icons module generated at: {outputPath}");
}

string ConvertToPythonName(string name)
{
    // Convert PascalCase to UPPER_SNAKE_CASE for Python constants
    var result = Regex.Replace(name, "([a-z])([A-Z])", "$1_$2");
    result = Regex.Replace(result, "([A-Z])([A-Z][a-z])", "$1_$2");
    return result.ToUpper();
}