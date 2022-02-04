using System.Collections.Generic;

namespace MQTTClient.Discovery
{
    public class DiscoveryInfo
    {
        public string name { get; set; }

        public string unique_id { get; set; }
        
        public DeviceDiscoveryInfo device { get; set; }
        
        public string icon { get; set; }
    }

    public class DeviceDiscoveryInfo
    {
        public string name { get; set; }
        
        public List<string> identifiers { get; set; }
        
        public List<string> connections { get; set; }
        
        public string manufacturer { get; set; }
        
        public string sw_version { get; set; }
    }
}