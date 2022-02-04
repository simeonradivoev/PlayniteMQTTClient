using System.Collections.Generic;

namespace MQTTClient.Discovery
{
    public class PlayingGameDiscoveryInfo : DiscoveryInfo
    {
        public string json_attributes_topic { get; set; }
        
        public string availability_topic { get; set; }
        
        public string device_class { get; set; }
        
        public string state_topic { get; set; }
    }
    
    public class PlayingGameCoverDiscoveryInfo : DiscoveryInfo
    {
        public string json_attributes_topic { get; set; }
        
        public string availability_topic { get; set; }

        public string topic { get; set; }
    }
    
    public class PlayingGameBackgroundDiscoveryInfo : DiscoveryInfo
    {
        public string json_attributes_topic { get; set; }
        
        public string availability_topic { get; set; }

        public string topic { get; set; }
    }
    
    public class SelectedGameDiscoveryInfo : DiscoveryInfo
    {
        public string json_attributes_topic { get; set; }
        
        public List<string> options { get; set; }
        
        public string availability_topic { get; set; }
        
        public string value_template { get; set; }
        
        public string command_topic { get; set; }
        
        public string state_topic { get; set; }
    }
    
    public class SelectedGameCoverDiscoveryInfo : DiscoveryInfo
    {
        public string json_attributes_topic { get; set; }

        public string availability_topic { get; set; }

        public string topic { get; set; }
    }
    
    public class ActiveViewGameDiscoveryInfo : DiscoveryInfo
    {
        public List<string> options { get; set; }
        
        public string availability_topic { get; set; }
        
        public string command_topic { get; set; }
        
        public string state_topic { get; set; }
    }
}