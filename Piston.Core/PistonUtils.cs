using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Piston.Core.Models;

namespace Piston.Core
{
  public static class PistonUtils
  {
    public static List<PistonFile> PrepareFilesForSubmission(
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

      return CreateFile(filename ?? "main.txt", sourceCode);
    }

    public static string NormalizeLanguage(string lang)
    {
      if (lang == "clang") return "c";
      if (lang == "c++") return "cpp";
      if (lang == "c#") return "csharp";
      if (lang == "py") return "python";
      if (lang == "js") return "javascript";
      if (lang == "node") return "javascript";

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

    private static List<PistonFile> Build(string language, string code, string filename, bool? autoWrap = null)
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

    private static List<PistonFile> CreateFile(string filename, string content)
    {
      List<PistonFile> list = new List<PistonFile>();

      PistonFile file = new PistonFile
      {
        Name = string.IsNullOrWhiteSpace(filename) ? null : filename,
        Content = content ?? ""
      };

      list.Add(file);
      return list;
    }

    private static string Wrap(string language, string preamble, string body)
    {
      StringBuilder sb = new StringBuilder();

      if (!string.IsNullOrWhiteSpace(preamble)) sb.AppendLine(preamble.TrimEnd());

      if (language == "c") return WrapC(sb.ToString(), body);
      if (language == "cpp") return WrapCpp(sb.ToString(), body);
      if (language == "java") return WrapJava(sb.ToString(), body);
      if (language == "csharp") return WrapCSharp(sb.ToString(), body);

      return body;
    }

    private static string WrapC(string preamble, string body)
    {
      StringBuilder sb = new StringBuilder();

      sb.Append(preamble);
      sb.AppendLine("#include <stdio.h>");
      sb.AppendLine("int main() {");
      sb.AppendLine(body);
      sb.AppendLine("return 0;");
      sb.AppendLine("}");
      return sb.ToString();
    }

    private static string WrapCpp(string preamble, string body)
    {
      StringBuilder sb = new StringBuilder();

      sb.Append(preamble);
      sb.AppendLine("#include <iostream>");
      sb.AppendLine("using namespace std;");
      sb.AppendLine("int main() {");
      sb.AppendLine(body);
      sb.AppendLine("return 0;");
      sb.AppendLine("}");
      return sb.ToString();
    }

    private static string WrapJava(string preamble, string body)
    {
      StringBuilder sb = new StringBuilder();

      sb.Append(preamble);
      sb.AppendLine("public class Main {");
      sb.AppendLine("public static void main(String[] args) {");
      sb.AppendLine(body);
      sb.AppendLine("}");
      sb.AppendLine("}");
      return sb.ToString();
    }

    private static string WrapCSharp(string preamble, string body)
    {
      StringBuilder sb = new StringBuilder();

      sb.Append(preamble);
      sb.AppendLine("using System;");
      sb.AppendLine("class Program {");
      sb.AppendLine("static void Main(string[] args) {");
      sb.AppendLine(body);
      sb.AppendLine("}");
      sb.AppendLine("}");
      return sb.ToString();
    }

    private static string ExtractPreamble(string code, string language)
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

    private static string RemovePreamble(string code, string language)
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
