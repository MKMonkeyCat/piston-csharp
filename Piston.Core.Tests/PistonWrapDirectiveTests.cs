using System;
using System.Diagnostics;
using System.Linq;
using Xunit;

namespace Piston.Core.Tests
{
    public class PistonWrapDirectiveTests
    {
        [Fact]
        public void NoMain_NoDirective_DefaultWraps()
        {
            var code = "printf(\"hello\n\");";
            var file = PistonUtils.PrepareFileForSubmission("c", code, null, null);
            Assert.NotNull(file);
            var content = file.Content.Replace("\r\n", "\n");
            Assert.Contains("#include <stdio.h>", content);
            Assert.Contains("int main()", content);
        }

        [Fact]
        public void NoMain_FirstLineDirectiveOff_DoesNotWrap()
        {
            var code = "// PISTON-WRAP: OFF\nprintf(\"x\n\");";
            var file = PistonUtils.PrepareFileForSubmission("c", code, null, null);
            var content = file.Content.Replace("\r\n", "\n");
            // should be returned unchanged (no wrapper)
            Assert.DoesNotContain("int main()", content);
            Assert.Equal(code, content);
        }

        [Fact]
        public void NoMain_AutoWrapFalse_DoesNotWrap()
        {
            var code = "printf(\"x\n\");";
            var file = PistonUtils.PrepareFileForSubmission("c", code, null, false);
            var content = file.Content.Replace("\r\n", "\n");
            Assert.DoesNotContain("int main()", content);
            Assert.Equal(code, content);
        }

        [Fact]
        public void HasMain_NoDirective_DoesNotWrap()
        {
            var code = "#include <stdio.h>\nint main() { printf(\"ok\n\"); }";
            var file = PistonUtils.PrepareFileForSubmission("c", code, null, null);
            var content = file.Content.Replace("\r\n", "\n");
            // should be unchanged because it already has a main
            Assert.Equal(code, content);
        }

        [Fact]
        public void HasMain_FirstLineDirectiveOn_StillDoesNotWrap()
        {
            var code = "// PISTON-WRAP: ON\n#include <stdio.h>\nint main() { return 5; }";
            var file = PistonUtils.PrepareFileForSubmission("c", code, null, null);
            var content = file.Content.Replace("\r\n", "\n");
            // current behavior: directive ON does not force-wrap an already-main program
            Assert.Equal(code, content);
        }

        [Fact]
        public void DirectiveNotOnFirstLine_Ignored()
        {
            var code = "/* comment */\n// PISTON-WRAP: OFF\nprintf(\"x\n\");";
            var file = PistonUtils.PrepareFileForSubmission("c", code, null, null);
            var content = file.Content.Replace("\r\n", "\n");
            // implementation currently checks only the first line, so directive on second line is ignored and wrapping occurs
            Assert.Contains("int main()", content);
        }

        [Fact]
        public void BenchmarkWrapDirectiveParsing()
        {
            string codeOn = "// PISTON-WRAP: ON\nprintf(\"x\n\");";
            string codeOff = "// PISTON-WRAP: OFF\nprintf(\"x\n\");";
            string codeNoDirective = "printf(\"x\n\");";

            for (int i = 0; i < 100000; i++)
            {
                PistonUtils.ExtractPreamble(codeOn, "c");
                PistonUtils.ExtractPreamble(codeOff, "c");
                PistonUtils.ExtractPreamble(codeNoDirective, "c");
            }
        }

        [Fact]
        public void BenchmarkWrapCode()
        {
            const int iterations = 1_000_000;

            string code = string.Join("\n", Enumerable.Repeat("printf(\"x\n\");", 100));

            Warmup(code);

            Benchmark("C", iterations, () => PistonUtils.Wrap("c", "", code));
            Benchmark("CPP", iterations, () => PistonUtils.Wrap("cpp", "", code));
            Benchmark("JAVA", iterations, () => PistonUtils.Wrap("java", "", code));
            Benchmark("CSHARP", iterations, () => PistonUtils.Wrap("csharp", "", code));
        }

        private static void Warmup(string code)
        {
            for (int i = 0; i < 10000; i++) PistonUtils.Wrap("c", "", code);
        }

        private static void Benchmark(
            string name,
            int iterations,
            Func<string> func)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Stopwatch sw = Stopwatch.StartNew();

            int total = 0;
            for (int i = 0; i < iterations; i++) total += func().Length;

            sw.Stop();

            Console.WriteLine(
              $"{name,-10} {sw.ElapsedMilliseconds,6} ms | checksum={total}"
            );
        }
    }
}
