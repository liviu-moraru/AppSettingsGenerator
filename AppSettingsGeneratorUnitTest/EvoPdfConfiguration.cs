using System;
using System.ComponentModel.DataAnnotations;

namespace AppSettingsGeneratorUnitTest
{
    public class EvoPdfConfiguration
    {
        public bool UseServiceClient { get; set; }

        [Required]
        public string ServiceUri { get; set; }

        public string LicenceKey { get; set; }

        public Guid UniqueId { get; set; }
    }
}