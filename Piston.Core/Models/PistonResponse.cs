using System.Collections.Generic;
using Newtonsoft.Json;

namespace Piston.Core.Models
{
    public class PistonRuntime
    {
        [JsonProperty("language")]
        public string Language { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("aliases")]
        public List<string> Aliases { get; set; }

        [JsonProperty("runtime")]
        public string RuntimeName { get; set; }
    }

    public class PistonPackage
    {
        [JsonProperty("language")]
        public string Language { get; set; }

        [JsonProperty("language_version")]
        public string LanguageVersion { get; set; }

        [JsonProperty("installed")]
        public bool Installed { get; set; }
    }

    public class PistonResult
    {
        [JsonProperty("language")]
        public string Language { get; set; }

        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("run")]
        public PistonStageResult Run { get; set; }

        [JsonProperty("compile")]
        public PistonStageResult Compile { get; set; }
    }

    public class PistonStageResult
    {
        [JsonProperty("stdout")]
        public string Stdout { get; set; }

        [JsonProperty("stderr")]
        public string Stderr { get; set; }

        [JsonProperty("code")]
        public int? Code { get; set; }

        [JsonProperty("signal")]
        public string Signal { get; set; }

        [JsonProperty("output")]
        public string Output { get; set; }
    }

    public class PistonWsMessage
    {
        [JsonProperty("type")]
        public string Type { get; set; }

        [JsonProperty("stream")]
        public string Stream { get; set; }

        [JsonProperty("data")]
        public string Data { get; set; }

        [JsonProperty("stage")]
        public string Stage { get; set; }

        [JsonProperty("code")]
        public int? Code { get; set; }

        [JsonProperty("signal")]
        public string Signal { get; set; }
    }
}
