using System.Collections.Generic;
using Newtonsoft.Json;

namespace Piston.Core.Models
{
    public class PistonRequest
    {
        [JsonProperty("language")]
        public string Language { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("files")]
        public List<PistonFile> Files { get; set; }

        [JsonProperty("stdin")]
        public string Stdin { get; set; }

        [JsonProperty("args")]
        public List<string> Args { get; set; }

        [JsonProperty("compile_memory_limit")]
        public int? CompileMemoryLimit { get; set; }

        [JsonProperty("run_memory_limit")]
        public int? RunMemoryLimit { get; set; }

        [JsonProperty("run_timeout")]
        public int? RunTimeout { get; set; }

        [JsonProperty("compile_timeout")]
        public int? CompileTimeout { get; set; }
    }

    public class PistonFile
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("content")]
        public string Content { get; set; }

        [JsonProperty("encoding")]
        public string Encoding { get; set; } = "utf8";
    }

    public class PistonPackageRequest
    {
        [JsonProperty("language")]
        public string Language { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }
    }
}
