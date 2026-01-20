using System;

namespace AkademiTrack.Common
{
    /// <summary>
    /// Application-wide constants to replace magic numbers
    /// </summary>
    public static class Constants
    {
        /// <summary>
        /// Time-related constants
        /// </summary>
        public static class Time
        {
            public const int DEFAULT_TIMEOUT_SECONDS = 30;
            public const int SHORT_TIMEOUT_SECONDS = 5;
            public const int LONG_TIMEOUT_SECONDS = 60;
            public const int AUTHENTICATION_TIMEOUT_SECONDS = 15;
            
            public const int REGISTRATION_WINDOW_MINUTES = 15;
            public const int CACHE_DEFAULT_TTL_MINUTES = 15;
            public const int CACHE_SHORT_TTL_MINUTES = 5;
            public const int CACHE_LONG_TTL_MINUTES = 30;
            
            public const int RETRY_DELAY_SECONDS = 30;
            public const int HEARTBEAT_ACTIVE_MINUTES = 5;
            public const int HEARTBEAT_INACTIVE_MINUTES = 10;
            
            public const int LOG_RETENTION_DAYS = 7;
            public const int WAKE_DETECTION_THRESHOLD_MINUTES = 5;
            
            public const int UI_DELAY_SHORT_MS = 50;
            public const int UI_DELAY_MEDIUM_MS = 500;
            public const int UI_DELAY_LONG_MS = 1500;
            public const int UI_DELAY_EXTRA_LONG_MS = 3000;
        }

        /// <summary>
        /// Network and API constants
        /// </summary>
        public static class Network
        {
            public const int MAX_RETRY_ATTEMPTS = 3;
            public const int CIRCUIT_BREAKER_FAILURE_THRESHOLD = 3;
            public const int CIRCUIT_BREAKER_RESET_TIMEOUT_SECONDS = 60;
            
            public const int HTTP_TIMEOUT_SECONDS = 30;
            public const int API_TIMEOUT_SECONDS = 15;
            public const int HEALTH_CHECK_TIMEOUT_MS = 2000;
            public const int ISKOLE_TIMEOUT_MS = 3000;
            
            public const int RATE_LIMIT_MAX_DELAY_MS = 30000;
        }

        /// <summary>
        /// UI and Display constants
        /// </summary>
        public static class Display
        {
            public const int BROWSER_WIDTH = 1920;
            public const int BROWSER_HEIGHT = 1080;
            
            public const double PERCENTAGE_MULTIPLIER = 100.0;
            public const int MAX_LOG_DISPLAY_ENTRIES = 600;
        }

        /// <summary>
        /// Validation constants
        /// </summary>
        public static class Validation
        {
            public const int MIN_PASSWORD_LENGTH = 6;
            public const int MAX_PASSWORD_LENGTH = 128;
            public const int MIN_USERNAME_LENGTH = 3;
            public const int MIN_SCHOOL_NAME_LENGTH = 3;
            public const int MAX_SCHOOL_NAME_LENGTH = 200;
            public const int MIN_EMAIL_LOCAL_PART_LENGTH = 4;
        }

        /// <summary>
        /// Cache constants
        /// </summary>
        public static class Cache
        {
            public const int DEFAULT_MAX_ENTRIES = 1000;
            public const int CLEANUP_INTERVAL_MINUTES = 5;
            
            // Cache TTL for different data types
            public const int ATTENDANCE_SUMMARY_TTL_MINUTES = 10;
            public const int TODAY_SCHEDULE_TTL_MINUTES = 5;
            public const int MONTHLY_DATA_TTL_MINUTES = 15;
            public const int WEEKLY_DATA_TTL_MINUTES = 10;
        }

        /// <summary>
        /// School schedule constants
        /// </summary>
        public static class Schedule
        {
            public const int DEFAULT_START_HOUR = 8;
            public const int DEFAULT_START_MINUTE = 15;
            public const int DEFAULT_END_HOUR = 15;
            public const int DEFAULT_END_MINUTE = 15;
            
            public const int MONDAY_START_HOUR = 9;
            public const int MONDAY_START_MINUTE = 0;
            
            public const int WORK_DAYS_IN_WEEK = 5;
            public const int DAYS_UNTIL_FRIDAY = 4;
            public const int DAYS_UNTIL_MONDAY_FROM_SUNDAY = -6;
        }

        /// <summary>
        /// Memory and performance constants
        /// </summary>
        public static class Performance
        {
            public const int BYTES_TO_KB = 1024;
            public const int BYTES_TO_MB = 1024 * 1024;
            public const int MAX_LOG_ENTRIES = 10000;
            
            public const int ERROR_CONTENT_MAX_LENGTH = 500;
            public const int JSON_MIN_LENGTH = 50;
        }

        /// <summary>
        /// Application metadata
        /// </summary>
        public static class App
        {
            public const string COPYRIGHT_YEAR = "2026";
            public const string COMPANY_NAME = "CyberGutta";
        }

        /// <summary>
        /// File and data constants
        /// </summary>
        public static class Files
        {
            public const string SETTINGS_FILENAME = "settings.json";
            public const string SCHOOL_HOURS_FILENAME = "school_hours.json";
            public const string USER_PARAMS_FILENAME = "user_params.json";
            public const string COOKIES_FILENAME = "cookies.json";
        }
    }
}