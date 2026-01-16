using System;
using System.Net.Mail;
using System.Text.RegularExpressions;

namespace AkademiTrack.Services.Utilities
{
    /// <summary>
    /// Input validation utilities to prevent injection attacks and invalid data
    /// </summary>
    public static class InputValidator
    {
        private static readonly Regex EmailRegex = new Regex(
            @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(1)
        );

        private static readonly Regex SafeStringRegex = new Regex(
            @"^[\w\s@.\-]+$",
            RegexOptions.Compiled,
            TimeSpan.FromSeconds(1)
        );

        /// <summary>
        /// Validate email address
        /// </summary>
        public static bool IsValidEmail(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return false;

            try
            {
                // Use MailAddress for robust validation
                var addr = new MailAddress(email);
                return addr.Address == email && EmailRegex.IsMatch(email);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Validate password strength
        /// </summary>
        public static (bool isValid, string? errorMessage) ValidatePassword(string? password)
        {
            if (string.IsNullOrEmpty(password))
                return (false, "Passord kan ikke være tomt");

            if (password.Length < 4)
                return (false, "Passord må være minst 4 tegn");

            if (password.Length > 128)
                return (false, "Passord kan ikke være lengre enn 128 tegn");

            return (true, null);
        }

        /// <summary>
        /// Validate username
        /// </summary>
        public static (bool isValid, string? errorMessage) ValidateUsername(string? username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return (false, "Brukernavn kan ikke være tomt");

            if (username.Length < 3)
                return (false, "Brukernavn må være minst 3 tegn");

            if (username.Length > 100)
                return (false, "Brukernavn kan ikke være lengre enn 100 tegn");

            return (true, null);
        }

        /// <summary>
        /// Sanitize input to prevent injection attacks
        /// Removes potentially dangerous characters
        /// </summary>
        public static string SanitizeInput(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            try
            {
                // Remove control characters
                input = Regex.Replace(input, @"[\x00-\x1F\x7F]", "", RegexOptions.None, TimeSpan.FromSeconds(1));

                // Remove potentially dangerous characters for SQL/script injection
                input = Regex.Replace(input, @"[<>\"";'`]", "", RegexOptions.None, TimeSpan.FromSeconds(1));

                return input.Trim();
            }
            catch (RegexMatchTimeoutException)
            {
                // If regex times out, return empty string for safety
                return string.Empty;
            }
        }

        /// <summary>
        /// Check if string contains only safe characters (alphanumeric, spaces, @, ., -)
        /// </summary>
        public static bool IsSafeString(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return false;

            try
            {
                return SafeStringRegex.IsMatch(input);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        /// <summary>
        /// Validate school name
        /// </summary>
        public static (bool isValid, string? errorMessage) ValidateSchoolName(string? schoolName)
        {
            if (string.IsNullOrWhiteSpace(schoolName))
                return (false, "Skolenavn kan ikke være tomt");

            if (schoolName.Length < 3)
                return (false, "Skolenavn må være minst 3 tegn");

            if (schoolName.Length > 200)
                return (false, "Skolenavn kan ikke være lengre enn 200 tegn");

            return (true, null);
        }

        /// <summary>
        /// Validate URL
        /// </summary>
        public static bool IsValidUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return false;

            return Uri.TryCreate(url, UriKind.Absolute, out var uriResult)
                && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
        }

        /// <summary>
        /// Truncate string to maximum length
        /// </summary>
        public static string Truncate(string? input, int maxLength)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            if (maxLength < 0)
                throw new ArgumentOutOfRangeException(nameof(maxLength));

            return input.Length <= maxLength ? input : input.Substring(0, maxLength);
        }
    }
}
