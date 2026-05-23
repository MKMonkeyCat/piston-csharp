using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Xunit;

namespace Piston.Core.Tests
{
  public class PistonUtilsFileTests
  {
    private static readonly PistonClient _client = new(
      new HttpClient() { BaseAddress = TestSettings.ApiBase }
    );

    public static IEnumerable<object[]> GetTestFiles()
    {
      var baseDir = AppContext.BaseDirectory;
      var dataDir = Path.Combine(baseDir, "TestData");
      if (!Directory.Exists(dataDir)) yield break;

      foreach (var file in Directory.EnumerateFiles(dataDir, "*", SearchOption.AllDirectories))
      {
        // skip expected files (we only want inputs)
        if (file.EndsWith(".expected", StringComparison.OrdinalIgnoreCase)) continue;
        yield return new object[] { file };
      }
    }

    [Theory]
    [MemberData(nameof(GetTestFiles))]
    public async Task PrepareFilesForSubmission_FromFile_DoesNotThrow(string filePath)
    {
      var content = File.ReadAllText(filePath);
      var ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();

      var files = PistonUtils.PrepareFilesForSubmission(ext, content, Path.GetFileName(filePath));
      Assert.NotNull(files);
      Assert.NotEmpty(files);

      var expectedPath = filePath + ".expected";
      if (File.Exists(expectedPath))
      {
        var actual = files[0].Content.Replace("\r\n", "\n");
        var expected = File.ReadAllText(expectedPath).Replace("\r\n", "\n");

        var result = await _client.ExecuteAsync(ext, "*", content);
        if (result.Run.Code != 0)
        {
          Console.WriteLine($"Execution failed for {filePath} with code {result.Run.Code} - Stderr: {result.Run.Stderr}");
        }

        Assert.Equal(expected, actual);
      }
      else
      {
        foreach (var f in files)
        {
          Assert.NotNull(f.Content);
        }
      }
    }
  }
}
