using System.Collections.Generic;
using System.Windows.Media;
using Newtonsoft.Json;

namespace SNIBypassGUI.Models
{
    public class SwitchItem
    {
        public string Id { get; set; }
        public string DisplayName { get; set; }
        public List<string> Links { get; set; } = [];
        public string Favicon { get; set; }
        public string Hosts { get; set; }

        [JsonIgnore]
        public ImageSource FaviconImage { get; set; }
    }
}
