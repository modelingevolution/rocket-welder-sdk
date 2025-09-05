using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

class Program
{
    static void Main(string[] args)
    {
        var icons = GetIcons();
        GenerateCSharpIcons(icons);
        GeneratePythonIcons(icons);
        
        Console.WriteLine();
        Console.WriteLine("Icon generation completed successfully!");
        Console.WriteLine();
        Console.WriteLine("Usage examples:");
        Console.WriteLine();
        Console.WriteLine("C#:");
        Console.WriteLine("  var icon = Icons.Material.Home;");
        Console.WriteLine("  var button = factory.DefineIconButton(\"btn\", Icons.Material.Settings);");
        Console.WriteLine();
        Console.WriteLine("Python:");
        Console.WriteLine("  from rocketwelder.ui.icons import Icons, Material");
        Console.WriteLine("  icon = Icons.MATERIAL.HOME");
        Console.WriteLine("  # or");
        Console.WriteLine("  icon = Material.HOME");
        Console.WriteLine("  button = factory.define_icon_button(\"btn\", Material.SETTINGS)");
    }
    
    static Dictionary<string, Dictionary<string, string>> GetIcons()
    {
        return new Dictionary<string, Dictionary<string, string>>
        {
            ["Material"] = new Dictionary<string, string>
            {
                // Navigation & Actions
                ["Home"] = "M10,20V14H14V20H19V12H22L12,3L2,12H5V20H10Z",
                ["ArrowBack"] = "M20,11V13H8L13.5,18.5L12.08,19.92L4.16,12L12.08,4.08L13.5,5.5L8,11H20Z",
                ["ArrowForward"] = "M4,11V13H16L10.5,18.5L11.92,19.92L19.84,12L11.92,4.08L10.5,5.5L16,11H4Z",
                ["ArrowUpward"] = "M13,20H11V8L5.5,13.5L4.08,12.08L12,4.16L19.92,12.08L18.5,13.5L13,8V20Z",
                ["ArrowDownward"] = "M11,4H13V16L18.5,10.5L19.92,11.92L12,19.84L4.08,11.92L5.5,10.5L11,16V4Z",
                ["ChevronLeft"] = "M15.41,16.58L10.83,12L15.41,7.41L14,6L8,12L14,18L15.41,16.58Z",
                ["ChevronRight"] = "M8.59,16.58L13.17,12L8.59,7.41L10,6L16,12L10,18L8.59,16.58Z",
                ["ExpandMore"] = "M16.59,8.59L12,13.17L7.41,8.59L6,10L12,16L18,10L16.59,8.59Z",
                ["ExpandLess"] = "M12,8L18,14L16.59,15.41L12,10.83L7.41,15.41L6,14L12,8Z",
                ["Menu"] = "M3,6H21V8H3V6M3,11H21V13H3V11M3,16H21V18H3V16Z",
                ["Close"] = "M19,6.41L17.59,5L12,10.59L6.41,5L5,6.41L10.59,12L5,17.59L6.41,19L12,13.41L17.59,19L19,17.59L13.41,12L19,6.41Z",
                ["MoreVert"] = "M12,8C13.1,8 14,7.1 14,6C14,4.9 13.1,4 12,4C10.9,4 10,4.9 10,6C10,7.1 10.9,8 12,8M12,10C10.9,10 10,10.9 10,12C10,13.1 10.9,14 12,14C13.1,14 14,13.1 14,12C14,10.9 13.1,10 12,10M12,16C10.9,16 10,16.9 10,18C10,19.1 10.9,20 12,20C13.1,20 14,19.1 14,18C14,16.9 13.1,16 12,16Z",
                ["MoreHoriz"] = "M6,10C4.9,10 4,10.9 4,12C4,13.1 4.9,14 6,14C7.1,14 8,13.1 8,12C8,10.9 7.1,10 6,10M12,10C10.9,10 10,10.9 10,12C10,13.1 10.9,14 12,14C13.1,14 14,13.1 14,12C14,10.9 13.1,10 12,10M18,10C16.9,10 16,10.9 16,12C16,13.1 16.9,14 18,14C19.1,14 20,13.1 20,12C20,10.9 19.1,10 18,10Z",
                
                // Common Actions
                ["Add"] = "M19,13H13V19H11V13H5V11H11V5H13V11H19V13Z",
                ["Remove"] = "M19,13H5V11H19V13Z",
                ["Check"] = "M21,7L9,19L3.5,13.5L4.91,12.09L9,16.17L19.59,5.59L21,7Z",
                ["Clear"] = "M19,6.41L17.59,5L12,10.59L6.41,5L5,6.41L10.59,12L5,17.59L6.41,19L12,13.41L17.59,19L19,17.59L13.41,12L19,6.41Z",
                ["Edit"] = "M20.71,7.04C21.1,6.65 21.1,6 20.71,5.63L18.37,3.29C18,2.9 17.35,2.9 16.96,3.29L15.12,5.12L18.87,8.87M3,17.25V21H6.75L17.81,9.93L14.06,6.18L3,17.25Z",
                ["Delete"] = "M19,4H15.5L14.5,3H9.5L8.5,4H5V6H19M6,19A2,2 0 0,0 8,21H16A2,2 0 0,0 18,19V7H6V19Z",
                ["Save"] = "M15,9H5V5H15M12,19A3,3 0 0,1 9,16A3,3 0 0,1 12,13A3,3 0 0,1 15,16A3,3 0 0,1 12,19M17,3H5C3.89,3 3,3.9 3,5V19A2,2 0 0,0 5,21H19A2,2 0 0,0 21,19V7L17,3Z",
                ["Search"] = "M9.5,3A6.5,6.5 0 0,1 16,9.5C16,11.11 15.41,12.59 14.44,13.73L14.71,14H15.5L20.5,19L19,20.5L14,15.5V14.71L13.73,14.44C12.59,15.41 11.11,16 9.5,16A6.5,6.5 0 0,1 3,9.5A6.5,6.5 0 0,1 9.5,3M9.5,5C7,5 5,7 5,9.5C5,12 7,14 9.5,14C12,14 14,12 14,9.5C14,7 12,5 9.5,5Z",
                ["Refresh"] = "M17.65,6.35C16.2,4.9 14.21,4 12,4A8,8 0 0,0 4,12A8,8 0 0,0 12,20C15.73,20 18.84,17.45 19.73,14H17.65C16.83,16.33 14.61,18 12,18A6,6 0 0,1 6,12A6,6 0 0,1 12,6C13.66,6 15.14,6.69 16.22,7.78L13,11H20V4L17.65,6.35Z",
                ["Settings"] = "M12,15.5A3.5,3.5 0 0,1 8.5,12A3.5,3.5 0 0,1 12,8.5A3.5,3.5 0 0,1 15.5,12A3.5,3.5 0 0,1 12,15.5M19.43,12.97C19.47,12.65 19.5,12.33 19.5,12C19.5,11.67 19.47,11.34 19.43,11L21.54,9.37C21.73,9.22 21.78,8.95 21.66,8.73L19.66,5.27C19.54,5.05 19.27,4.96 19.05,5.05L16.56,6.05C16.04,5.66 15.5,5.32 14.87,5.07L14.5,2.42C14.46,2.18 14.25,2 14,2H10C9.75,2 9.54,2.18 9.5,2.42L9.13,5.07C8.5,5.32 7.96,5.66 7.44,6.05L4.95,5.05C4.73,4.96 4.46,5.05 4.34,5.27L2.34,8.73C2.21,8.95 2.27,9.22 2.46,9.37L4.57,11C4.53,11.34 4.5,11.67 4.5,12C4.5,12.33 4.53,12.65 4.57,12.97L2.46,14.63C2.27,14.78 2.21,15.05 2.34,15.27L4.34,18.73C4.46,18.95 4.73,19.03 4.95,18.95L7.44,17.94C7.96,18.34 8.5,18.68 9.13,18.93L9.5,21.58C9.54,21.82 9.75,22 10,22H14C14.25,22 14.46,21.82 14.5,21.58L14.87,18.93C15.5,18.67 16.04,18.34 16.56,17.94L19.05,18.95C19.27,19.03 19.54,18.95 19.66,18.73L21.66,15.27C21.78,15.05 21.73,14.78 21.54,14.63L19.43,12.97Z",
                
                // Status & Feedback
                ["Info"] = "M13,9H11V7H13M13,17H11V11H13M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2Z",
                ["Warning"] = "M13,13H11V7H13M13,17H11V15H13M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2Z",
                ["Error"] = "M12,2C17.53,2 22,6.47 22,12C22,17.53 17.53,22 12,22C6.47,22 2,17.53 2,12C2,6.47 6.47,2 12,2M15.59,7L12,10.59L8.41,7L7,8.41L10.59,12L7,15.59L8.41,17L12,13.41L15.59,17L17,15.59L13.41,12L17,8.41L15.59,7Z",
                ["Success"] = "M12,2A10,10 0 0,1 22,12A10,10 0 0,1 12,22A10,10 0 0,1 2,12A10,10 0 0,1 12,2M11,16.5L18,9.5L16.59,8.09L11,13.67L7.91,10.59L6.5,12L11,16.5Z",
                ["Help"] = "M10,19H13V22H10V19M12,2A6,6 0 0,1 18,8C18,10.38 16.83,12.06 15.24,13.12C14.28,13.74 13.5,14.64 13.5,15.67V16H10.5V15.67C10.5,13.75 11.88,12.1 13.39,11.06C14.29,10.42 15,9.46 15,8A3,3 0 0,0 12,5A3,3 0 0,0 9,8H6A6,6 0 0,1 12,2Z",
                ["HelpOutline"] = "M11,18H13V16H11V18M12,2A10,10 0 0,0 2,12A10,10 0 0,0 12,22A10,10 0 0,0 22,12A10,10 0 0,0 12,2M12,20C7.59,20 4,16.41 4,12C4,7.59 7.59,4 12,4C16.41,4 20,7.59 20,12C20,16.41 16.41,20 12,20M12,6A4,4 0 0,0 8,10H10A2,2 0 0,1 12,8A2,2 0 0,1 14,10C14,12 11,11.75 11,15H13C13,12.75 16,12.5 16,10A4,4 0 0,0 12,6Z",
                
                // Files & Folders
                ["Folder"] = "M10,4H4C2.89,4 2,4.89 2,6V18A2,2 0 0,0 4,20H20A2,2 0 0,0 22,18V8C22,6.89 21.1,6 20,6H12L10,4Z",
                ["FolderOpen"] = "M19,20H4C2.89,20 2,19.1 2,18V6C2,4.89 2.89,4 4,4H10L12,6H19A2,2 0 0,1 21,8H21L4,8V18L6.14,10H23.21L20.93,18.5C20.7,19.37 19.92,20 19,20Z",
                ["File"] = "M13,9V3.5L18.5,9M6,2C4.89,2 4,2.89 4,4V20A2,2 0 0,0 6,22H18A2,2 0 0,0 20,20V8L14,2H6Z",
                ["Download"] = "M5,20H19V18H5M19,9H15V3H9V9H5L12,16L19,9Z",
                ["Upload"] = "M9,16V10H5L12,3L19,10H15V16H9M5,20V18H19V20H5Z",
                
                // User & Account  
                ["Person"] = "M12,4A4,4 0 0,1 16,8A4,4 0 0,1 12,12A4,4 0 0,1 8,8A4,4 0 0,1 12,4M12,14C16.42,14 20,15.79 20,18V20H4V18C4,15.79 7.58,14 12,14Z",
                ["Group"] = "M16,13C15.71,13 15.38,13 15.03,13.05C16.19,13.89 17,15 17,16.5V19H23V16.5C23,14.17 18.33,13 16,13M8,13C5.67,13 1,14.17 1,16.5V19H15V16.5C15,14.17 10.33,13 8,13M8,11A3,3 0 0,0 11,8A3,3 0 0,0 8,5A3,3 0 0,0 5,8A3,3 0 0,0 8,11M16,11A3,3 0 0,0 19,8A3,3 0 0,0 16,5A3,3 0 0,0 13,8A3,3 0 0,0 16,11Z",
                ["Lock"] = "M12,17A2,2 0 0,0 14,15C14,13.89 13.1,13 12,13A2,2 0 0,0 10,15A2,2 0 0,0 12,17M18,8A2,2 0 0,1 20,10V20A2,2 0 0,1 18,22H6A2,2 0 0,1 4,20V10C4,8.89 4.9,8 6,8H7V6A5,5 0 0,1 12,1A5,5 0 0,1 17,6V8H18M12,3A3,3 0 0,0 9,6V8H15V6A3,3 0 0,0 12,3Z",
                ["ExitToApp"] = "M10,17L15,12L10,7V10H4V14H10V17M10,2H19A2,2 0 0,1 21,4V20A2,2 0 0,1 19,22H10A2,2 0 0,1 8,20V18H10V20H19V4H10V6H8V4A2,2 0 0,1 10,2Z",
                
                // Social
                ["Favorite"] = "M12,21.35L10.55,20.03C5.4,15.36 2,12.27 2,8.5C2,5.41 4.42,3 7.5,3C9.24,3 10.91,3.81 12,5.08C13.09,3.81 14.76,3 16.5,3C19.58,3 22,5.41 22,8.5C22,12.27 18.6,15.36 13.45,20.03L12,21.35Z",
                ["Star"] = "M12,17.27L18.18,21L16.54,13.97L22,9.24L14.81,8.62L12,2L9.19,8.62L2,9.24L7.45,13.97L5.82,21L12,17.27Z",
                ["ThumbUp"] = "M23,10C23,8.89 22.1,8 21,8H14.68L15.64,3.43C15.66,3.33 15.67,3.22 15.67,3.11C15.67,2.7 15.5,2.32 15.23,2.05L14.17,1L7.59,7.58C7.22,7.95 7,8.45 7,9V19A2,2 0 0,0 9,21H18C18.83,21 19.54,20.5 19.84,19.78L22.86,12.73C22.95,12.5 23,12.26 23,12V10M1,21H5V9H1V21Z",
                
                // Misc
                ["Visibility"] = "M12,9A3,3 0 0,0 9,12A3,3 0 0,0 12,15A3,3 0 0,0 15,12A3,3 0 0,0 12,9M12,17A5,5 0 0,1 7,12A5,5 0 0,1 12,7A5,5 0 0,1 17,12A5,5 0 0,1 12,17M12,4.5C7.14,4.5 2.78,7.5 1,12C2.78,16.5 7.14,19.5 12,19.5C16.86,19.5 21.22,16.5 23,12C21.22,7.5 16.86,4.5 12,4.5Z",
                ["Sync"] = "M12,18A6,6 0 0,1 6,12C6,11 6.25,10.03 6.7,9.2L5.24,7.74C4.46,8.97 4,10.43 4,12A8,8 0 0,0 12,20V23L16,19L12,15M12,4V1L8,5L12,9V6A6,6 0 0,1 18,12C18,13 17.75,13.97 17.3,14.8L18.76,16.26C19.54,15.03 20,13.57 20,12A8,8 0 0,0 12,4Z",
                ["Link"] = "M16,6H13V7.9H16C18.26,7.9 20.1,9.73 20.1,12A4.1,4.1 0 0,1 16,16.1H13V18H16A6,6 0 0,0 22,12A6,6 0 0,0 16,6M3.9,12C3.9,9.73 5.74,7.9 8,7.9H11V6H8A6,6 0 0,0 2,12A6,6 0 0,0 8,18H11V16.1H8C5.74,16.1 3.9,14.26 3.9,12M8,13H16V11H8V13Z",
                ["AttachFile"] = "M16.5,6V17.5A4,4 0 0,1 12.5,21.5A4,4 0 0,1 8.5,17.5V5A2.5,2.5 0 0,1 11,2.5A2.5,2.5 0 0,1 13.5,5V15.5A1,1 0 0,1 12.5,16.5A1,1 0 0,1 11.5,15.5V6H10V15.5A2.5,2.5 0 0,0 12.5,18A2.5,2.5 0 0,0 15,15.5V5A4,4 0 0,0 11,1A4,4 0 0,0 7,5V17.5A5.5,5.5 0 0,0 12.5,23A5.5,5.5 0 0,0 18,17.5V6H16.5Z"
            },
            ["Custom"] = new Dictionary<string, string>
            {
                // Add custom icons here if needed
            }
        };
    }
    
    static void GenerateCSharpIcons(Dictionary<string, Dictionary<string, string>> icons)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// Generated file - do not edit manually");
        sb.AppendLine("// Generated from MudBlazor icons subset");
        sb.AppendLine();
        sb.AppendLine("namespace RocketWelder.SDK.Ui;");
        sb.AppendLine();
        sb.AppendLine("/// <summary>");
        sb.AppendLine("/// Material Design icon paths for use with IconButton controls.");
        sb.AppendLine("/// Icons are SVG path data strings compatible with 24x24 viewBox.");
        sb.AppendLine("/// </summary>");
        sb.AppendLine("public static class Icons");
        sb.AppendLine("{");
        
        foreach (var category in icons)
        {
            if (category.Value.Count == 0) continue;
            
            sb.AppendLine($"    public static class {category.Key}");
            sb.AppendLine("    {");
            
            foreach (var icon in category.Value)
            {
                sb.AppendLine($"        /// <summary>{icon.Key} icon</summary>");
                sb.AppendLine($"        public const string {icon.Key} = \"{icon.Value}\";");
                sb.AppendLine();
            }
            
            sb.AppendLine("    }");
            sb.AppendLine();
        }
        
        sb.AppendLine("}");
        
        var outputPath = Path.Combine(Directory.GetCurrentDirectory(), "../../csharp/RocketWelder.SDK/Ui/Icons.cs");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        File.WriteAllText(outputPath, sb.ToString());
        Console.WriteLine($"C# Icons class generated at: {outputPath}");
    }
    
    static void GeneratePythonIcons(Dictionary<string, Dictionary<string, string>> icons)
    {
        var sb = new StringBuilder();
        sb.AppendLine("\"\"\"Material Design icon paths for use with IconButton controls.\"\"\"");
        sb.AppendLine();
        sb.AppendLine("# Generated file - do not edit manually");
        sb.AppendLine("# Generated from MudBlazor icons subset");
        sb.AppendLine();
        
        foreach (var category in icons)
        {
            if (category.Value.Count == 0) continue;
            
            sb.AppendLine();
            sb.AppendLine($"class {category.Key}:");
            sb.AppendLine($"    \"\"\"Material Design {category.Key} icons.\"\"\"");
            sb.AppendLine();
            
            foreach (var icon in category.Value)
            {
                var pythonName = ConvertToPythonName(icon.Key);
                sb.AppendLine($"    {pythonName} = \"{icon.Value}\"");
            }
        }
        
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("# Convenience class with all icons as class attributes");
        sb.AppendLine("class Icons:");
        sb.AppendLine("    \"\"\"All available icons with nested categories.\"\"\"");
        sb.AppendLine();
        
        foreach (var category in icons)
        {
            if (category.Value.Count == 0) continue;
            sb.AppendLine($"    {ConvertToPythonName(category.Key)} = {category.Key}");
        }
        
        var outputPath = Path.Combine(Directory.GetCurrentDirectory(), "../../python/rocketwelder/ui/icons.py");
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));
        File.WriteAllText(outputPath, sb.ToString());
        Console.WriteLine($"Python Icons module generated at: {outputPath}");
    }
    
    static string ConvertToPythonName(string name)
    {
        // Handle special cases
        if (name == "Material") return "MATERIAL";
        if (name == "Custom") return "CUSTOM";
        
        // Convert PascalCase to UPPER_SNAKE_CASE for constants
        var result = Regex.Replace(name, "([a-z])([A-Z])", "$1_$2");
        return result.ToUpper();
    }
}