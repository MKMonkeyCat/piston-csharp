using Xunit;

namespace Piston.Core.Tests
{
  public class PistonWrapDirectiveTests
  {
    [Fact]
    public void NoMain_NoDirective_DefaultWraps()
    {
      var code = "printf(\"hello\n\");";
      var files = PistonUtils.PrepareFilesForSubmission("c", code, null, null);
      Assert.NotNull(files);
      Assert.Single(files);
      var content = files[0].Content.Replace("\r\n", "\n");
      Assert.Contains("#include <stdio.h>", content);
      Assert.Contains("int main()", content);
    }

    [Fact]
    public void NoMain_FirstLineDirectiveOff_DoesNotWrap()
    {
      var code = "// PISTON-WRAP: OFF\nprintf(\"x\n\");";
      var files = PistonUtils.PrepareFilesForSubmission("c", code, null, null);
      Assert.Single(files);
      var content = files[0].Content.Replace("\r\n", "\n");
      // should be returned unchanged (no wrapper)
      Assert.DoesNotContain("int main()", content);
      Assert.Equal(code, content);
    }

    [Fact]
    public void NoMain_AutoWrapFalse_DoesNotWrap()
    {
      var code = "printf(\"x\n\");";
      var files = PistonUtils.PrepareFilesForSubmission("c", code, null, false);
      Assert.Single(files);
      var content = files[0].Content.Replace("\r\n", "\n");
      Assert.DoesNotContain("int main()", content);
      Assert.Equal(code, content);
    }

    [Fact]
    public void HasMain_NoDirective_DoesNotWrap()
    {
      var code = "#include <stdio.h>\nint main() { printf(\"ok\n\"); }";
      var files = PistonUtils.PrepareFilesForSubmission("c", code, null, null);
      Assert.Single(files);
      var content = files[0].Content.Replace("\r\n", "\n");
      // should be unchanged because it already has a main
      Assert.Equal(code, content);
    }

    [Fact]
    public void HasMain_FirstLineDirectiveOn_StillDoesNotWrap()
    {
      var code = "// PISTON-WRAP: ON\n#include <stdio.h>\nint main() { return 5; }";
      var files = PistonUtils.PrepareFilesForSubmission("c", code, null, null);
      Assert.Single(files);
      var content = files[0].Content.Replace("\r\n", "\n");
      // current behavior: directive ON does not force-wrap an already-main program
      Assert.Equal(code, content);
    }

    [Fact]
    public void DirectiveNotOnFirstLine_Ignored()
    {
      var code = "/* comment */\n// PISTON-WRAP: OFF\nprintf(\"x\n\");";
      var files = PistonUtils.PrepareFilesForSubmission("c", code, null, null);
      Assert.Single(files);
      var content = files[0].Content.Replace("\r\n", "\n");
      // implementation currently checks only the first line, so directive on second line is ignored and wrapping occurs
      Assert.Contains("int main()", content);
    }
  }
}
