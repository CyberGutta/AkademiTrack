using System;
using System.Collections.Generic;

namespace AkademiTrack.Services
{
    public class UserParameters
    {
        public string? FylkeId { get; set; }
        public string? PlanPeri { get; set; }
        public string? SkoleId { get; set; }

        public bool IsComplete => !string.IsNullOrEmpty(FylkeId) &&
                                  !string.IsNullOrEmpty(PlanPeri) &&
                                  !string.IsNullOrEmpty(SkoleId);
    }

    public class AuthenticationResult
    {
        public bool Success { get; set; }
        public Dictionary<string, string>? Cookies { get; set; }
        public UserParameters? Parameters { get; set; }
    }

    public class SavedParameterData
    {
        public UserParameters? Parameters { get; set; }
        public DateTime SavedAt { get; set; }
        public string? SchoolYear { get; set; }
    }
}