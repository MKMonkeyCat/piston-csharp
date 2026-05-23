using System;
using System.IO;

namespace Piston.Core.Tests
{
  internal static class TestSettings
  {
    public static Uri ApiBase
    {
      get
      {
        string url = null;

        var env = Environment.GetEnvironmentVariable("PISTON_TEST_API_BASE");
        if (!string.IsNullOrWhiteSpace(env)) url = env;

        if (url == null && TryGetFromDotEnv("PISTON_TEST_API_BASE", out var dotEnvVal))
        {
          url = dotEnvVal;
        }

        url ??= "http://localhost:2000/api/v2";

        if (!url.EndsWith('/')) url += "/";
        if (Uri.TryCreate(url, UriKind.Absolute, out var u)) return u;

        return new Uri("http://localhost:2000/api/v2/");
      }
    }

    private static bool TryGetFromDotEnv(string key, out string value)
    {
      value = null;

      var dir = new DirectoryInfo(AppContext.BaseDirectory);
      for (int depth = 0; dir != null && depth < 8; depth++)
      {
        var candidate = Path.Combine(dir.FullName, ".env");
        if (File.Exists(candidate))
        {
          try
          {
            foreach (var raw in File.ReadAllLines(candidate))
            {
              var line = raw.Trim();
              if (string.IsNullOrEmpty(line) || line.StartsWith("#")) continue;

              var idx = line.IndexOf('=');
              if (idx <= 0) continue;

              var k = line[..idx].Trim();
              var v = line[(idx + 1)..].Trim();

              // remove optional surrounding quotes
              if ((v.StartsWith('"') && v.EndsWith('"')) || (v.StartsWith('\'') && v.EndsWith('\'')))
              {
                v = v[1..(v.Length - 1)];
              }

              if (string.Equals(k, key, StringComparison.Ordinal))
              {
                value = v;
                return true;
              }
            }
          }
          catch
          {
            Console.WriteLine($"Warning: Failed to read .env file at {candidate}");
          }
        }

        dir = dir.Parent;
      }

      return false;
    }
  }
}
