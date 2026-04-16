using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace MossadStudio.Services
{
    /// <summary>
    /// Parsed representation of a .d.luau Luau declaration file.
    /// Sent as JSON to Monaco for context-aware IntelliSense.
    /// </summary>
    public class LuauTypeSchema
    {
        /// <summary>Global variables and their types. e.g. "game" -> "EngineService"</summary>
        public Dictionary<string, string> Globals { get; set; } = new(StringComparer.Ordinal);
        
        /// <summary>Detailed definitions for classes, modules, and type aliases.</summary>
        public Dictionary<string, LuauType> Types { get; set; } = new(StringComparer.Ordinal);
    }

    public class LuauType
    {
        public string Name { get; set; } = "";
        public string Kind { get; set; } = "class"; // "class", "module", or "type"
        public List<LuauMember> Members { get; set; } = new();
    }

    public class LuauMember
    {
        public string Label { get; set; } = "";
        public CompletionItemKind Kind { get; set; } = CompletionItemKind.Property;
        public string Detail { get; set; } = "";
        public string Documentation { get; set; } = "";
    }

    /// <summary>
    /// Parses a .d.luau Luau declaration file and produces a LuauTypeSchema.
    /// Also maintains an augmented Luau.xshd for AvalonEdit.
    /// </summary>
    public static class DluauParser
    {
        // Regex patterns

        // declare function foo(...)
        private static readonly Regex RxDeclareFunction =
            new(@"^\s*declare\s+function\s+(\w+)\s*\(([^)]*)\)(?:\s*:\s*(.+))?", RegexOptions.Multiline);

        // declare class Foo
        private static readonly Regex RxDeclareClass =
            new(@"^\s*declare\s+class\s+(\w+)", RegexOptions.Multiline);

        // declare module Foo
        private static readonly Regex RxDeclareModule =
            new(@"^\s*declare\s+module\s+(\w+)", RegexOptions.Multiline);

        // declare variableName: Type
        private static readonly Regex RxDeclareVar =
            new(@"^\s*declare\s+([a-zA-Z_]\w*)\s*:\s*([a-zA-Z_]\w*)", RegexOptions.Multiline);

        // type TypeName = ...
        private static readonly Regex RxTypeAlias =
            new(@"^\s*(?:export\s+)?type\s+(\w+)\s*=\s*(.+)", RegexOptions.Multiline);

        // function foo(self, ...) inside a block
        private static readonly Regex RxBlockMethod =
            new(@"^\s+function\s+(\w+)\s*\(([^)]*)\)(?:\s*:\s*(.+))?", RegexOptions.Multiline);

        // member: Type inside a block
        private static readonly Regex RxBlockProp =
            new(@"^\s+([a-zA-Z_]\w*)\s*:\s*(.+)", RegexOptions.Multiline);

        // Public API

        /// <summary>Holds the last successfully loaded schema.</summary>
        public static LuauTypeSchema CurrentSchema { get; private set; } = new();

        public static LuauTypeSchema ParseFile(string filePath)
        {
            string content = File.ReadAllText(filePath);
            CurrentSchema = Parse(content);
            return CurrentSchema;
        }

        public static List<CompletionItem> GetFlatCompletions(LuauTypeSchema schema)
        {
            var list = new List<CompletionItem>();
            var seen = new HashSet<string>();

            void Add(string label, CompletionItemKind kind, string detail) {
                if (seen.Add(label)) list.Add(new CompletionItem { Label = label, Kind = kind, Detail = detail });
            }

            foreach (var g in schema.Globals) Add(g.Key, CompletionItemKind.Variable, g.Value);
            foreach (var t in schema.Types)
            {
                Add(t.Key, t.Value.Kind == "module" ? CompletionItemKind.Module : CompletionItemKind.Class, t.Value.Kind);
                foreach (var m in t.Value.Members) Add(m.Label, m.Kind, m.Detail);
            }
            return list;
        }

        public static string? BuildAugmentedXshd(LuauTypeSchema? schema = null)
        {
            var s = schema ?? CurrentSchema;
            var flat = GetFlatCompletions(s);
            if (flat.Count == 0) return null;

            try
            {
                var asm = Assembly.GetExecutingAssembly();
                using var stream = asm.GetManifestResourceStream("MossadStudio.Luau.xshd");
                if (stream == null) return null;
                string xshd = new StreamReader(stream).ReadToEnd();

                var kw = new StringBuilder();
                kw.AppendLine();
                kw.AppendLine("        <Keywords color=\"TypeDecl\">");
                foreach (var item in flat)
                {
                    if (Regex.IsMatch(item.Label, @"^\w+$"))
                        kw.AppendLine($"            <Word>{item.Label}</Word>");
                }
                kw.AppendLine("        </Keywords>");

                const string colorInsert = "    <Color name=\"TypeDecl\" foreground=\"#9CDCFE\" />\n";
                xshd = xshd.Replace("<RuleSet>", colorInsert + "    <RuleSet>");
                xshd = xshd.Replace("</RuleSet>", kw + "    </RuleSet>");
                return xshd;
            }
            catch { return null; }
        }

        // Private helpers

        private static LuauTypeSchema Parse(string content)
        {
            var schema = new LuauTypeSchema();

            // 1. Blocks (Classes and Modules)
            // We use a simple approach: if we find declare class/module, we search for the next 'end' at column 0.
            // (Note: Roblox .d.luau files are usually perfectly formatted with 'end' at column 0 for top-level blocks).
            ParseBlocks(content, schema, RxDeclareClass, "class");
            ParseBlocks(content, schema, RxDeclareModule, "module");

            // 2. Global variables
            foreach (Match m in RxDeclareVar.Matches(content))
            {
                schema.Globals[m.Groups[1].Value] = m.Groups[2].Value;
            }

            // 3. Global functions (as variables of type 'function')
            foreach (Match m in RxDeclareFunction.Matches(content))
            {
                string name = m.Groups[1].Value;
                schema.Globals[name] = "function";
                // Add to types so it can have documentation
                if (!schema.Types.ContainsKey(name))
                {
                    schema.Types[name] = new LuauType {
                        Name = name,
                        Kind = "function",
                        Members = new List<LuauMember> {
                            new LuauMember { Label = name, Kind = CompletionItemKind.Function, Detail = m.Groups[0].Value.Trim() }
                        }
                    };
                }
            }

            // 4. Type aliases
            foreach (Match m in RxTypeAlias.Matches(content))
            {
                string name = m.Groups[1].Value;
                schema.Types[name] = new LuauType { Name = name, Kind = "type" };
            }

            return schema;
        }

        private static void ParseBlocks(string content, LuauTypeSchema schema, Regex blockStart, string kind)
        {
            foreach (Match m in blockStart.Matches(content))
            {
                string name = m.Groups[1].Value;
                int start = m.Index + m.Length;
                
                // Find matching 'end' at start of line
                int end = content.IndexOf("\nend", start, StringComparison.Ordinal);
                if (end < 0) end = content.Length;

                string body = content[start..end];
                var lt = new LuauType { Name = name, Kind = kind };

                // Parse methods
                foreach (Match mm in RxBlockMethod.Matches(body))
                {
                    lt.Members.Add(new LuauMember {
                        Label = mm.Groups[1].Value,
                        Kind  = kind == "module" ? CompletionItemKind.Function : CompletionItemKind.Method,
                        Detail = mm.Groups[0].Value.Trim()
                    });
                }

                // Parse properties
                foreach (Match pm in RxBlockProp.Matches(body))
                {
                    string pName = pm.Groups[1].Value;
                    if (pName == "function") continue;
                    lt.Members.Add(new LuauMember {
                        Label = pName,
                        Kind  = CompletionItemKind.Property,
                        Detail = pm.Groups[0].Value.Trim()
                    });
                }

                schema.Types[name] = lt;
            }
        }
    }

    // CompletionItem (Compatibility for AvalonEdit)

    public class CompletionItem
    {
        public string Label         { get; set; } = "";
        public CompletionItemKind Kind { get; set; } = CompletionItemKind.Text;
        public string Detail        { get; set; } = "";
        public string Documentation { get; set; } = "";
    }

    public enum CompletionItemKind
    {
        Text = 0, Method = 1, Function = 2, Constructor = 3, Field = 4, Variable = 5,
        Class = 6, Interface = 7, Module = 8, Property = 9, Unit = 10, Value = 11,
        Enum = 12, Keyword = 13, Snippet = 14, Color = 15, File = 16, Reference = 17,
        Folder = 18, EnumMember = 19, Constant = 20, Struct = 21, Event = 22,
        Operator = 23, TypeParameter = 24,
    }
}
