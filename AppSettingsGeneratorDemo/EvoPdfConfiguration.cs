using System.ComponentModel.DataAnnotations;

namespace AppSettingsGeneratorDemo
{
    public class EvoPdfConfiguration
    {
        public bool UseServiceClient { get; set; }
        [Required]
        public string ServiceUri { get; set; }
        public string LicenceKey { get; set; }
    }
}