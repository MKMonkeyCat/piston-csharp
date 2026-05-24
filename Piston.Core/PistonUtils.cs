using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Piston.Core.Models;
using System.Linq;

namespace Piston.Core
{
    public static class PistonUtils
    {
        private static readonly Regex SafePattern = new Regex(@"^[a-zA-Z0-9._-]{1,128}$", RegexOptions.Compiled);

        private class LangConfig
        {
            public string DefaultName { get; set; }
            public HashSet<string> ValidExtensions { get; set; }
        }

        private static readonly Dictionary<string, HashSet<string>> NeedCompilationLanguageMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase)
        {
            { "c",          new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".c", ".h" } },
            { "cpp",        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cpp", ".cc", ".cxx", ".h", ".hpp", ".hh" } },
            { "java",       new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".java" } },
            { "csharp",     new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".cs" } },
            // { "rust",       new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".rs" } },
            // { "php",        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".php" } }
        };

        public static PistonFile PrepareFileForSubmission(
          string language,
          string sourceCode,
          string filename = null,
          bool? autoWrap = null)
        {
            if (language == null) language = "";
            if (sourceCode == null) sourceCode = "";

            language = NormalizeLanguage(language.Trim().ToLowerInvariant());

            if (language == "c" || language == "cpp" || language == "java" || language == "csharp")
            {
                return Build(language, sourceCode, filename, autoWrap);
            }
            if (language == "python") return CreateFile(filename ?? "main.py", sourceCode);
            if (language == "javascript") return CreateFile(filename ?? "main.js", sourceCode);

            return CreateFile(filename, sourceCode);
        }

        public static string NormalizeLanguage(string lang)
        {
            if (lang == "clang") return "c";
            if (lang == "c++") return "cpp";
            if (lang == "c#" || lang == "cs") return "csharp";
            if (lang == "py") return "python";
            if (lang == "js" || lang == "node") return "javascript";

            return lang;
        }

        public static string DefaultFilename(string language)
        {
            if (language == "c") return "main.c";
            if (language == "cpp") return "main.cpp";
            if (language == "java") return "Main.java";
            if (language == "csharp") return "Program.cs";
            if (language == "python") return "main.py";
            if (language == "javascript") return "main.js";

            return "main.txt";
        }

        public static bool ShouldUseBase64Encoding(string language, string filename)
        {
            if (string.IsNullOrWhiteSpace(language) || string.IsNullOrWhiteSpace(filename)) return false;

            language = NormalizeLanguage(language.Trim().ToLowerInvariant());

            if (NeedCompilationLanguageMap.TryGetValue(language, out var extensions))
            {
                string fileExt = Path.GetExtension(filename).ToLowerInvariant();
                // If the file extension is not in the allowed list for the language, use Base64 encoding
                return !extensions.Contains(fileExt);
            }

            return false;
        }

        public static string ValidateFilename(string filename)
        {
            if (string.IsNullOrWhiteSpace(filename)) throw new ArgumentException("filename empty");
            if (filename.Length > 128) throw new ArgumentException("filename too long");
            if (filename.Any(char.IsControl)) throw new ArgumentException("control characters not allowed");

            filename = filename.Normalize(NormalizationForm.FormKC);
            filename = filename.Trim();

            if (filename == "." || filename == "..") throw new ArgumentException("invalid filename");
            if (filename.Contains('\0')) throw new ArgumentException("null byte not allowed");
            if (filename.Contains('/') || filename.Contains('\\')) throw new ArgumentException("path separator not allowed");
            if (Path.IsPathRooted(filename)) throw new ArgumentException("rooted path not allowed");
            if (!string.Equals(filename, Path.GetFileName(filename), StringComparison.Ordinal)) throw new ArgumentException("invalid filename structure");
            if (!SafePattern.IsMatch(filename)) throw new ArgumentException("invalid charset");

            return filename;
        }

        private static PistonFile Build(string language, string code, string filename, bool? autoWrap = null)
        {
            // Auto-wrap if no main method is detected,
            // unless the user has explicitly opted out via autoWrap=false or a WRAP: OFF directive.
            // directive > function arg > default(true)
            bool withAutoWrap = GetTopWrapDirective(code, language) ?? autoWrap ?? true;
            if (withAutoWrap && !HasMain(language, code))
            {
                string preamble = Dedup(ExtractPreamble(code, language));
                string body = RemovePreamble(code, language);

                code = Wrap(language, preamble, body);
            }

            return CreateFile(filename ?? DefaultFilename(language), code);
        }

        private static bool? GetTopWrapDirective(string code, string language)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;

            string firstLine = (code.IndexOf('\n') is int idx && idx != -1 ? code.Substring(0, idx) : code).Trim();

            // TODO - support more languages and comment styles as needed?
            string pattern = (language == "python" || language == "py")
                ? @"^#\s*PISTON-WRAP\s*:\s*(ON|OFF|TRUE|FALSE)"
                : @"^(?://|/\*)\s*PISTON-WRAP\s*:\s*(ON|OFF|TRUE|FALSE)";

            var match = Regex.Match(firstLine, pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                string val = match.Groups[1].Value.ToUpperInvariant();
                return val == "ON" || val == "TRUE";
            }

            return null;
        }

        private static PistonFile CreateFile(string filename, string content)
        {
            PistonFile file = new PistonFile
            {
                Name = ValidateFilename(filename),
                Content = content ?? ""
            };

            return file;
        }

        public static string Wrap(string language, string preamble, string body)
        {
            preamble = string.IsNullOrWhiteSpace(preamble) ? "" : preamble.TrimEnd() + "\n";

            if (language == "c") return WrapC(preamble, body);
            if (language == "cpp") return WrapCpp(preamble, body);
            if (language == "java") return WrapJava(preamble, body);
            if (language == "csharp") return WrapCSharp(preamble, body);

            return body;
        }

        private static string WrapC(string preamble, string body)
        {
            return string.Concat(preamble,
                "#include <stdio.h>\n",
                "int main() {\n",
                body,
                "\nreturn 0;\n",
                "}\n"
            );
        }

        private static string WrapCpp(string preamble, string body)
        {
            return string.Concat(preamble,
                "#include <iostream>\n",
                "using namespace std;\n",
                "int main() {\n",
                body,
                "\nreturn 0;\n",
                "}\n"
            );
        }

        private static string WrapJava(string preamble, string body)
        {
            return string.Concat(preamble,
                "public class Main {\n",
                "public static void main(String[] args) {\n",
                body,
                "\n}\n",
                "}\n"
            );
        }

        private static string WrapCSharp(string preamble, string body)
        {
            return string.Concat(preamble,
                "using System;\n",
                "class Program {\n",
                "static void Main(string[] args) {\n",
                body,
                "\n}\n",
                "}\n"
            );
        }

        public static string ExtractPreamble(string code, string language)
        {
            StringBuilder sb = new StringBuilder();

            string[] lines = code.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                if (IsPreamble(line, language)) sb.AppendLine(line.TrimEnd());
            }

            return sb.ToString();
        }

        public static string RemovePreamble(string code, string language)
        {
            List<string> result = new List<string>();

            string[] lines = code.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];

                if (IsPreamble(line, language)) continue;

                result.Add(line);
            }

            return string.Join("\n", result.ToArray());
        }

        private static readonly Regex CLikeRegex = new Regex("^(#include|#define|using\\s+namespace|typedef)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex CSharpRegex = new Regex("^(using\\s+[A-Za-z0-9_.]+|namespace\\s+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex JavaRegex = new Regex("^(package\\s+|import\\s+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static bool IsPreamble(string line, string language)
        {
            if (string.IsNullOrWhiteSpace(line)) return false;

            if (language == "c" || language == "cpp") return CLikeRegex.IsMatch(line);
            if (language == "csharp") return CSharpRegex.IsMatch(line);
            if (language == "java") return JavaRegex.IsMatch(line);

            return false;
        }

        private static string Dedup(string preamble)
        {
            if (string.IsNullOrWhiteSpace(preamble)) return preamble;

            HashSet<string> seen = new HashSet<string>();
            StringBuilder sb = new StringBuilder();

            string[] lines = preamble.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string l = lines[i].Trim();

                if (string.IsNullOrWhiteSpace(l)) continue;
                if (seen.Contains(l)) continue;

                seen.Add(l);
                sb.AppendLine(l);
            }

            return sb.ToString();
        }

        private static bool HasMain(string language, string code)
        {
            if (string.IsNullOrWhiteSpace(code)) return false;

            if (language == "c" || language == "cpp")
            {
                return Regex.IsMatch(code, "\\b(int|void)\\s+main\\s*\\([^)]*\\)\\s*\\{", RegexOptions.IgnoreCase);
            }
            if (language == "java") return Regex.IsMatch(code, "public\\s+static\\s+void\\s+main\\s*\\(", RegexOptions.IgnoreCase);
            if (language == "csharp") return Regex.IsMatch(code, "static\\s+(void|int)\\s+Main\\s*\\(", RegexOptions.IgnoreCase);

            return false;
        }
    }
}
