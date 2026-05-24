using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Piston.Core.Models;
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

      var file = PistonUtils.PrepareFileForSubmission(ext, content, Path.GetFileName(filePath));
      Assert.NotNull(file);

      var expectedPath = filePath + ".expected";
      if (File.Exists(expectedPath))
      {
        var actual = file.Content.Replace("\r\n", "\n");
        var expected = File.ReadAllText(expectedPath).Replace("\r\n", "\n");

        Assert.Equal(expected, actual);
      }
      else Assert.NotNull(file.Content);
    }

    [Fact]
    public async Task PrepareFilesForSubmission_AllFiles_WithRun_Parallel()
    {
      var baseDir = Path.Combine(AppContext.BaseDirectory, "TestData");
      var filesToTest = Directory.EnumerateFiles(baseDir, "*", SearchOption.AllDirectories);

      var options = new ParallelOptions { MaxDegreeOfParallelism = 8 };

      await Parallel.ForEachAsync(filesToTest, options, async (input, token) =>
      {
        var content = await File.ReadAllTextAsync(input, token);
        var basename = Path.GetFileNameWithoutExtension(input);

        var ext = Path.GetExtension(basename.Contains('.') ? basename : input).TrimStart('.').ToLowerInvariant();

        List<PistonFile> files = [
          PistonUtils.PrepareFileForSubmission(ext, content, null),
          PistonUtils.PrepareFileForSubmission("txt", "A text file", content, null),
        ];

        var result = await _client.ExecuteAsync(ext, "*", files, cancellationToken: token);

        Assert.NotNull(result);
        Assert.NotNull(result.Run);
        Assert.True(result.Run.Code == 0 || result.Run.Code == 1 || result.Compile?.Code == 0 || result.Compile?.Code == 1);
      });
    }
  }
}
