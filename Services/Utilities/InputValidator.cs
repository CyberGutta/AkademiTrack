using System;
using System.Net.Mail;
using System.Text.RegularExpressions;
using System.Linq;

namespace AkademiTrack.Services.Utilities
{
    /// <summary>
    /// Comprehensive input validation utilities with security-focused sanitization
    /// </summary>
    public static class InputValidator
    {
        private static readonly Regex EmailRegex = new Regex(
            @"^[^@\s]+@[^@\s]+\.[^@\s]+$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(1)
        );

        private static readonly Regex SafeStringRegex = new Regex(
            @"^[\w\s@.\-æøåÆØÅ]+$",
            RegexOptions.Compiled,
            TimeSpan.FromSeconds(1)
        );

        // SQL injection patterns to detect
        private static readonly string[] SqlInjectionPatterns = 
        {
            "union", "select", "insert", "update", "delete", "drop", "create", "alter",
            "exec", "execute", "sp_", "xp_", "--", "/*", "*/", "@@", "char(", "nchar(",
            "varchar(", "nvarchar(", "alter(", "begin(", "cast(", "cursor(", "declare(",
            "end(", "exec(", "fetch(", "kill(", "open(", "sys", "table", "script",
            "javascript:", "vbscript:", "onload", "onerror", "onclick"
        };

        /// <summary>
        /// Validate email address with comprehensive security checks
        /// </summary>
        public static ValidationResult ValidateEmail(string? email)
        {
            if (string.IsNullOrWhiteSpace(email))
                return ValidationResult.Failed("E-post kan ikke være tom");

            email = email.Trim();

            if (email.Length > 254) // RFC 5321 limit
                return ValidationResult.Failed("E-post er for lang (maks 254 tegn)");

            // Check for dangerous patterns
            if (ContainsSqlInjection(email) || ContainsScriptInjection(email))
                return ValidationResult.Failed("E-post inneholder ugyldige tegn");

            try
            {
                // Use MailAddress for robust validation
                var addr = new MailAddress(email);
                if (addr.Address != email || !EmailRegex.IsMatch(email))
                    return ValidationResult.Failed("Ugyldig e-post format");

                return ValidationResult.Success(SanitizeInput(email));
            }
            catch (FormatException)
            {
                return ValidationResult.Failed("Ugyldig e-post format");
            }
        }

        /// <summary>
        /// Validate password with security requirements
        /// </summary>
        public static ValidationResult ValidatePassword(string? password)
        {
            if (string.IsNullOrEmpty(password))
                return ValidationResult.Failed("Passord kan ikke være tomt");

            if (password.Length < 6)
                return ValidationResult.Failed("Passord må være minst 6 tegn");

            if (password.Length > 128)
                return ValidationResult.Failed("Passord kan ikke være lengre enn 128 tegn");

            // Check for null bytes and control characters
            if (password.Any(c => char.IsControl(c) && c != '\t'))
                return ValidationResult.Failed("Passord inneholder ugyldige tegn");

            return ValidationResult.Success(password);
        }

        /// <summary>
        /// Validate username with security checks
        /// </summary>
        public static ValidationResult ValidateUsername(string? username)
        {
            if (string.IsNullOrWhiteSpace(username))
                return ValidationResult.Failed("Brukernavn kan ikke være tomt");

            username = username.Trim();

            if (username.Length < 3)
                return ValidationResult.Failed("Brukernavn må være minst 3 tegn");

            if (username.Length > 100)
                return ValidationResult.Failed("Brukernavn kan ikke være lengre enn 100 tegn");

            if (ContainsSqlInjection(username) || ContainsScriptInjection(username))
                return ValidationResult.Failed("Brukernavn inneholder ugyldige tegn");

            return ValidationResult.Success(SanitizeInput(username));
        }

        /// <summary>
        /// Validate school name with comprehensive checks
        /// </summary>
        public static ValidationResult ValidateSchoolName(string? schoolName)
        {
            if (string.IsNullOrWhiteSpace(schoolName))
                return ValidationResult.Failed("Skolenavn kan ikke være tomt");

            schoolName = schoolName.Trim();

            if (schoolName.Length < 3)
                return ValidationResult.Failed("Skolenavn må være minst 3 tegn");

            if (schoolName.Length > 200)
                return ValidationResult.Failed("Skolenavn kan ikke være lengre enn 200 tegn");

            if (ContainsSqlInjection(schoolName) || ContainsScriptInjection(schoolName))
                return ValidationResult.Failed("Skolenavn inneholder ugyldige tegn");

            // Allow Norwegian characters in school names
            if (!Regex.IsMatch(schoolName, @"^[a-zA-ZæøåÆØÅ0-9\s\-\.\(\)]+$"))
                return ValidationResult.Failed("Skolenavn inneholder ugyldige tegn");

            return ValidationResult.Success(SanitizeInput(schoolName));
        }

        /// <summary>
        /// Enhanced input sanitization with security focus
        /// </summary>
        public static string SanitizeInput(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            try
            {
                // Remove control characters except tab, newline, carriage return
                input = Regex.Replace(input, @"[\x00-\x08\x0B\x0C\x0E-\x1F\x7F]", "", 
                    RegexOptions.None, TimeSpan.FromSeconds(1));

                // Remove potentially dangerous characters for SQL/script injection
                input = Regex.Replace(input, @"[<>\"";'`\x00]", "", 
                    RegexOptions.None, TimeSpan.FromSeconds(1));

                // Normalize whitespace
                input = Regex.Replace(input, @"\s+", " ", 
                    RegexOptions.None, TimeSpan.FromSeconds(1));

                return input.Trim();
            }
            catch (RegexMatchTimeoutException)
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// </summary>
        private static bool ContainsSqlInjection(string input)
        {
            if (string.IsNullOrEmpty(input))
                return false;

            var lowerInput = input.ToLowerInvariant();
            return SqlInjectionPatterns.Any(pattern => lowerInput.Contains(pattern));
        }

        /// <summary>
        /// </summary>
        private static bool ContainsScriptInjection(string input)
        {
            if (string.IsNullOrEmpty(input))
                return false;

            var lowerInput = input.ToLowerInvariant();
            var scriptPatterns = new[] { "javascript:", "vbscript:", "data:", "onload", "onerror", "onclick", "onmouseover" };
            
            return scriptPatterns.Any(pattern => lowerInput.Contains(pattern));
        }

        /// <summary>
        /// </summary>
        public static bool IsSafeString(string? input)
        {
            if (string.IsNullOrEmpty(input))
                return false;

            try
            {
                return SafeStringRegex.IsMatch(input) && 
                       !ContainsSqlInjection(input) && 
                       !ContainsScriptInjection(input);
            }
            catch (RegexMatchTimeoutException)
            {
                return false;
            }
        }

        /// <summary>
        /// Validate URL with security checks
        /// </summary>
        public static ValidationResult ValidateUrl(string? url)
        {
            if (string.IsNullOrWhiteSpace(url))
                return ValidationResult.Failed("URL kan ikke være tom");

            url = url.Trim();

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uriResult))
                return ValidationResult.Failed("Ugyldig URL format");

            if (uriResult.Scheme != Uri.UriSchemeHttp && uriResult.Scheme != Uri.UriSchemeHttps)
                return ValidationResult.Failed("URL må bruke HTTP eller HTTPS");

            // Check for suspicious patterns in URL
            if (ContainsSqlInjection(url) || ContainsScriptInjection(url))
                return ValidationResult.Failed("URL inneholder ugyldige tegn");

            return ValidationResult.Success(uriResult.ToString());
        }

        /// <summary>
        /// Validate numeric input within range
        /// </summary>
        public static ValidationResult ValidateNumeric(string? input, int min = int.MinValue, int max = int.MaxValue)
        {
            if (string.IsNullOrWhiteSpace(input))
                return ValidationResult.Failed("Numerisk verdi kan ikke være tom");

            if (!int.TryParse(input.Trim(), out var value))
                return ValidationResult.Failed("Ugyldig numerisk format");

            if (value < min || value > max)
                return ValidationResult.Failed($"Verdi må være mellom {min} og {max}");

            return ValidationResult.Success(value.ToString());
        }

        /// <summary>
        /// </summary>
        public static string Truncate(string? input, int maxLength)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            if (maxLength < 0)
                throw new ArgumentOutOfRangeException(nameof(maxLength));

            return input.Length <= maxLength ? input : input.Substring(0, maxLength);
        }

        /// <summary>
        /// Validate file path for security
        /// </summary>
        public static ValidationResult ValidateFilePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return ValidationResult.Failed("Filsti kan ikke være tom");

            path = path.Trim();

            // Check for path traversal attempts
            if (path.Contains("..") || path.Contains("~") || path.Contains("//"))
                return ValidationResult.Failed("Ugyldig filsti - path traversal ikke tillatt");

            // Check for invalid path characters
            var invalidChars = System.IO.Path.GetInvalidPathChars();
            if (path.Any(c => invalidChars.Contains(c)))
                return ValidationResult.Failed("Filsti inneholder ugyldige tegn");

            try
            {
                var fullPath = System.IO.Path.GetFullPath(path);
                return ValidationResult.Success(fullPath);
            }
            catch (Exception)
            {
                return ValidationResult.Failed("Ugyldig filsti format");
            }
        }
    }

    /// <summary>
    /// Result of input validation with detailed feedback
    /// </summary>
    public class ValidationResult
    {
        public bool IsValid { get; private set; }
        public string? Value { get; private set; }
        public string? ErrorMessage { get; private set; }

        private ValidationResult(bool isValid, string? value, string? errorMessage)
        {
            IsValid = isValid;
            Value = value;
            ErrorMessage = errorMessage;
        }

        public static ValidationResult Success(string value)
        {
            return new ValidationResult(true, value, null);
        }

        public static ValidationResult Failed(string errorMessage)
        {
            return new ValidationResult(false, null, errorMessage);
        }

        /// <summary>
        /// Throws an exception if validation failed
        /// </summary>
        public void ThrowIfInvalid()
        {
            if (!IsValid)
                throw new ArgumentException(ErrorMessage ?? "Validation failed");
        }

        /// <summary>
        /// Gets the value or throws if invalid
        /// </summary>
        public string GetValueOrThrow()
        {
            ThrowIfInvalid();
            return Value ?? string.Empty;
        }
    }
}
