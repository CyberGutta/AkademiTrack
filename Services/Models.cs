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
        public string? ErrorMessage { get; set; }

        public static AuthenticationResult CreateSuccess(Dictionary<string, string>? cookies, UserParameters? parameters)
        {
            return new AuthenticationResult { Success = true, Cookies = cookies, Parameters = parameters };
        }

        public static AuthenticationResult CreateFailed(string errorMessage)
        {
            return new AuthenticationResult { Success = false, ErrorMessage = errorMessage };
        }
    }

    public class SavedParameterData
    {
        public UserParameters? Parameters { get; set; }
        public DateTime SavedAt { get; set; }
        public string? SchoolYear { get; set; }
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public string? Message { get; set; }
        public string? Level { get; set; }
        public string FormattedMessage => $"[{Timestamp:HH:mm:ss}] [{Level}] {Message}";
    }

    public class NotificationEntry
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string? Title { get; set; }
        public string? Message { get; set; }
        public string? Level { get; set; }
        public bool IsVisible { get; set; } = true;
        public TimeSpan? Duration { get; set; }
    }
}