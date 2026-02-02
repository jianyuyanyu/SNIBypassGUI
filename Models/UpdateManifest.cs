using Newtonsoft.Json;
using System.Collections.Generic;

namespace SNIBypassGUI.Models
{
    public class UpdateManifest
    {
        [JsonProperty("version")]
        public string Version { get; set; }

        [JsonProperty("timestamp")]
        public long Timestamp { get; set; }

        [JsonProperty("executable")]
        public ExecutableInfo Executable { get; set; }

        [JsonProperty("assets")]
        public List<AssetInfo> Assets { get; set; }
    }

    public class ExecutableInfo
    {
        [JsonProperty("update_required")]
        public bool UpdateRequired { get; set; }

        [JsonProperty("hash")]
        public string Hash { get; set; }

        [JsonProperty("parts")]
        public List<string> Parts { get; set; }
    }

    public class AssetInfo
    {
        [JsonProperty("path")]
        public string Path { get; set; }

        [JsonProperty("url")]
        public string Url { get; set; }

        [JsonProperty("hash")]
        public string Hash { get; set; }
    }
}
