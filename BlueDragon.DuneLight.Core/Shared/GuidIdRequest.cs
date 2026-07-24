using System;
using System.ComponentModel.DataAnnotations;

namespace BlueDragon.DuneLight.Core.Shared
{
    public class GuidIdRequest
    {
        [Required]
        public Guid? Id { get; set; }
    }
}
