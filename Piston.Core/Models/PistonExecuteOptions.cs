using System.Collections.Generic;
using Newtonsoft.Json;

namespace Piston.Core.Models
{
    public class PistonExecuteOptions
    {
        [JsonProperty("stdin")]
        public string Stdin { get; set; }

        [JsonIgnore]
        public List<string> StdinLines { get; set; }

        [JsonProperty("args")]
        public List<string> Args { get; set; }

        [JsonProperty("run_timeout")]
        public int? RunTimeout { get; set; }

        [JsonProperty("compile_timeout")]
        public int? CompileTimeout { get; set; }

        [JsonProperty("run_memory_limit")]
        public int? RunMemoryLimit { get; set; }

        [JsonProperty("compile_memory_limit")]
        public int? CompileMemoryLimit { get; set; }
    }
}
