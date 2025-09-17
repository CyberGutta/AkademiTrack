using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace AkademiTrack.ViewModels
{
    public class LoginWindowViewModel : ViewModelBase, INotifyPropertyChanged
    {
        private readonly HttpClient _httpClient;
        private string _email = string.Empty;
        private string _password = string.Empty;
        private string _errorMessage = string.Empty;
        private bool _isLoading = false;

        // Supabase configuration
        private string _supabaseUrl = "https://eghxldvyyioolnithndr.supabase.co";
        private string _supabaseKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6ImVnaHhsZHZ5eWlvb2xuaXRobmRyIiwicm9sZSI6ImFub24iLCJpYXQiOjE3NTc2NjAyNzYsImV4cCI6MjA3MzIzNjI3Nn0.NAP799HhYrNkKRpSzXFXT0vyRd_OD-hkW8vH4VbOE8k";

        // Google OAuth configuration
        private const string GoogleClientId = "856198108446-v408cg5425bj46acfu90g7nlufj28md0.apps.googleusercontent.com";
        private const string GoogleAuthUrl = "https://accounts.google.com/o/oauth2/v2/auth";
        private const string GoogleTokenUrl = "https://oauth2.googleapis.com/token";
        private const string RedirectUri = "http://localhost:8080/callback";

        public event PropertyChangedEventHandler PropertyChanged;
        public event EventHandler<bool> LoginCompleted;

        protected virtual void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public LoginWindowViewModel()
        {
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30);

            LoginCommand = new SimpleCommand(async () => await LoginAsync(), () => CanLogin);
            GoogleLoginCommand = new SimpleCommand(async () => await GoogleLoginAsync(), () => !IsLoading);
            ExitCommand = new SimpleCommand(ExitAsync);
        }

        public string Email
        {
            get => _email;
            set
            {
                _email = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanLogin));
                ((SimpleCommand)LoginCommand).RaiseCanExecuteChanged();
            }
        }

        public string Password
        {
            get => _password;
            set
            {
                _password = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanLogin));
                ((SimpleCommand)LoginCommand).RaiseCanExecuteChanged();
            }
        }

        public string ErrorMessage
        {
            get => _errorMessage;
            set
            {
                _errorMessage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasError));
            }
        }

        public bool HasError => !string.IsNullOrEmpty(_errorMessage);

        public bool IsLoading
        {
            get => _isLoading;
            set
            {
                _isLoading = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CanLogin));
                OnPropertyChanged(nameof(LoginButtonText));
                ((SimpleCommand)LoginCommand).RaiseCanExecuteChanged();
                ((SimpleCommand)GoogleLoginCommand).RaiseCanExecuteChanged();
            }
        }

        public bool CanLogin => !IsLoading && !string.IsNullOrWhiteSpace(Email) && !string.IsNullOrWhiteSpace(Password);

        public string LoginButtonText => IsLoading ? "Logger inn..." : "Logg inn";

        public ICommand LoginCommand { get; }
        public ICommand GoogleLoginCommand { get; }
        public ICommand ExitCommand { get; }

        private async Task LoginAsync()
        {
            if (!CanLogin) return;

            IsLoading = true;
            ErrorMessage = string.Empty;

            try
            {
                bool isAuthenticated = await AuthenticateWithSupabaseAsync(Email, Password);

                if (isAuthenticated)
                {
                    await SaveActivationStatusAsync();
                    LoginCompleted?.Invoke(this, true);
                }
                else
                {
                    ErrorMessage = "Ugyldig e-post eller passord. Sjekk at du har en gyldig konto.";
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Innlogging feilet: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Login error: {ex}");
            }
            finally
            {
                IsLoading = false;
            }
        }

        private async Task GoogleLoginAsync()
        {
            if (IsLoading) return;

            IsLoading = true;
            ErrorMessage = string.Empty;
            HttpListener httpListener = null;

            try
            {
                // Generate PKCE values for security
                var codeVerifier = GenerateCodeVerifier();
                var codeChallenge = GenerateCodeChallenge(codeVerifier);
                var state = GenerateRandomString(32);

                System.Diagnostics.Debug.WriteLine($"Code verifier: {codeVerifier}");
                System.Diagnostics.Debug.WriteLine($"Code challenge: {codeChallenge}");
                System.Diagnostics.Debug.WriteLine($"State: {state}");

                // Start local HTTP server to handle the callback
                httpListener = new HttpListener();
                httpListener.Prefixes.Add("http://localhost:8080/");

                try
                {
                    httpListener.Start();
                    System.Diagnostics.Debug.WriteLine("HTTP listener started on localhost:8080");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to start HTTP listener: {ex.Message}");
                    ErrorMessage = "Could not start local server. Please check if port 8080 is available.";
                    return;
                }

                // Build the Google OAuth URL
                var authUrl = BuildGoogleAuthUrl(codeChallenge, state);
                System.Diagnostics.Debug.WriteLine($"Auth URL: {authUrl}");

                // Open the browser
                OpenBrowser(authUrl);

                // Wait for the callback with a timeout
                var callbackTask = httpListener.GetContextAsync();
                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(5)); // 5-minute timeout

                var completedTask = await Task.WhenAny(callbackTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    ErrorMessage = "Login timed out. Please try again.";
                    return;
                }

                var context = callbackTask.Result;
                var response = context.Response;

                try
                {
                    // Parse the callback URL
                    var query = context.Request.Url.Query;
                    System.Diagnostics.Debug.WriteLine($"Received callback query: {query}");

                    var queryParams = ParseQueryString(query);

                    var code = queryParams["code"];
                    var returnedState = queryParams["state"];
                    var error = queryParams["error"];
                    var errorDescription = queryParams["error_description"];

                    System.Diagnostics.Debug.WriteLine($"Authorization code: {!string.IsNullOrEmpty(code)}");
                    System.Diagnostics.Debug.WriteLine($"State match: {returnedState == state}");
                    System.Diagnostics.Debug.WriteLine($"Error: {error}");
                    System.Diagnostics.Debug.WriteLine($"Error description: {errorDescription}");

                    // Send response to browser
                    var responseString = error != null
                        ? $"<html><body><h2>Login failed</h2><p>Error: {error}</p><p>{errorDescription}</p><p>You can close this window.</p></body></html>"
                        : "<html><body><h2>Login successful!</h2><p>You can close this window and return to the application.</p></body></html>";

                    var buffer = Encoding.UTF8.GetBytes(responseString);
                    response.ContentLength64 = buffer.Length;
                    response.StatusCode = 200;
                    response.ContentType = "text/html";
                    response.Headers.Add("Access-Control-Allow-Origin", "*");
                    await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
                    response.Close();

                    if (error != null)
                    {
                        ErrorMessage = $"Google login error: {error} - {errorDescription}";
                        return;
                    }

                    if (returnedState != state)
                    {
                        ErrorMessage = "Security error: Invalid state parameter";
                        return;
                    }

                    if (string.IsNullOrEmpty(code))
                    {
                        ErrorMessage = "No authorization code received";
                        return;
                    }

                    // Exchange authorization code for tokens
                    var googleTokens = await ExchangeCodeForTokensAsync(code, codeVerifier);

                    if (googleTokens == null || string.IsNullOrEmpty(googleTokens.AccessToken))
                    {
                        ErrorMessage = "Failed to get Google access token";
                        return;
                    }

                    System.Diagnostics.Debug.WriteLine($"Access token received: {!string.IsNullOrEmpty(googleTokens.AccessToken)}");
                    System.Diagnostics.Debug.WriteLine($"ID token received: {!string.IsNullOrEmpty(googleTokens.IdToken)}");

                    // Get user info from Google
                    var userInfo = await GetGoogleUserInfoAsync(googleTokens.AccessToken);

                    if (userInfo == null)
                    {
                        ErrorMessage = "Failed to get user information from Google";
                        return;
                    }

                    System.Diagnostics.Debug.WriteLine($"User info - Email: {userInfo.Email}, Name: {userInfo.Name}");

                    // Authenticate with Supabase using Google token
                    var supabaseAuth = await AuthenticateWithSupabaseUsingGoogleAsync(googleTokens.AccessToken, googleTokens.IdToken);

                    if (supabaseAuth)
                    {
                        await SaveActivationStatusAsync(userInfo.Email, "Google");
                        LoginCompleted?.Invoke(this, true);
                    }
                    else
                    {
                        ErrorMessage = "Failed to authenticate with Supabase using Google account";
                    }
                }
                finally
                {
                    try
                    {
                        response?.Close();
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                ErrorMessage = $"Google login failed: {ex.Message}";
                System.Diagnostics.Debug.WriteLine($"Google login error: {ex}");
            }
            finally
            {
                try
                {
                    httpListener?.Stop();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error stopping HTTP listener: {ex.Message}");
                }
                IsLoading = false;
            }
        }

        private string BuildGoogleAuthUrl(string codeChallenge, string state)
        {
            var parameters = new[]
            {
                $"client_id={Uri.EscapeDataString(GoogleClientId)}",
                $"redirect_uri={Uri.EscapeDataString(RedirectUri)}",
                "response_type=code",
                "scope=openid email profile",
                $"code_challenge={Uri.EscapeDataString(codeChallenge)}",
                "code_challenge_method=S256",
                $"state={Uri.EscapeDataString(state)}"
            };

            return $"{GoogleAuthUrl}?{string.Join("&", parameters)}";
        }

        private async Task<GoogleTokenResponse> ExchangeCodeForTokensAsync(string code, string codeVerifier)
        {
            try
            {
                var parameters = new Dictionary<string, string>
        {
            {"client_id", GoogleClientId},
            {"client_secret", "SKIBIDI-S-kEbN6B0K59hOzptxNzOXuVPpWB"}, // Use your actual client secret here
            {"code", code},
            {"grant_type", "authorization_code"},
            {"redirect_uri", RedirectUri},
            {"code_verifier", codeVerifier}
        };

                var formContent = new FormUrlEncodedContent(parameters);

                System.Diagnostics.Debug.WriteLine($"Token exchange request:");
                System.Diagnostics.Debug.WriteLine($"URL: {GoogleTokenUrl}");
                foreach (var param in parameters)
                {
                    if (param.Key != "client_secret")
                        System.Diagnostics.Debug.WriteLine($"{param.Key}: {param.Value}");
                    else
                        System.Diagnostics.Debug.WriteLine($"{param.Key}: [REDACTED]");
                }

                var response = await _httpClient.PostAsync(GoogleTokenUrl, formContent);
                var responseContent = await response.Content.ReadAsStringAsync();

                System.Diagnostics.Debug.WriteLine($"Google token response status: {response.StatusCode}");
                System.Diagnostics.Debug.WriteLine($"Google token response: {responseContent}");

                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        var tokenResponse = JsonSerializer.Deserialize<GoogleTokenResponse>(responseContent, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (tokenResponse != null && !string.IsNullOrEmpty(tokenResponse.AccessToken))
                        {
                            System.Diagnostics.Debug.WriteLine("Successfully parsed token response");
                            return tokenResponse;
                        }
                        else
                        {
                            System.Diagnostics.Debug.WriteLine("Token response was null or missing access token");
                            return null;
                        }
                    }
                    catch (JsonException ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to parse token response: {ex.Message}");
                        return null;
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Google token exchange failed: {response.StatusCode} - {response.ReasonPhrase}");

                    // Try to parse error response
                    try
                    {
                        var errorResponse = JsonSerializer.Deserialize<GoogleErrorResponse>(responseContent, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (errorResponse != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"Google error: {errorResponse.Error} - {errorResponse.ErrorDescription}");
                        }
                    }
                    catch
                    {
                        System.Diagnostics.Debug.WriteLine("Could not parse error response");
                    }

                    return null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception in ExchangeCodeForTokensAsync: {ex}");
                return null;
            }
        }

        public class GoogleErrorResponse
        {
            public string Error { get; set; }

            [JsonPropertyName("error_description")]
            public string ErrorDescription { get; set; }
        }

        private async Task<GoogleUserInfo> GetGoogleUserInfoAsync(string accessToken)
        {
            try
            {
                var request = new HttpRequestMessage(HttpMethod.Get, "https://www.googleapis.com/oauth2/v2/userinfo");
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

                var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                System.Diagnostics.Debug.WriteLine($"Google user info response: {responseContent}");

                if (response.IsSuccessStatusCode)
                {
                    return JsonSerializer.Deserialize<GoogleUserInfo>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Failed to get Google user info: {response.StatusCode}");
                    return null;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error getting Google user info: {ex}");
                return null;
            }
        }

        private async Task<bool> AuthenticateWithSupabaseUsingGoogleAsync(string googleAccessToken, string googleIdToken)
        {
            try
            {
                // First, try using the ID token method (recommended by Supabase)
                var payload = new
                {
                    provider = "google",
                    id_token = googleIdToken
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Post, $"{_supabaseUrl}/auth/v1/token?grant_type=id_token")
                {
                    Content = content
                };

                request.Headers.Add("apikey", _supabaseKey);
                request.Headers.Add("Authorization", $"Bearer {_supabaseKey}");

                System.Diagnostics.Debug.WriteLine($"Supabase auth request URL: {request.RequestUri}");
                System.Diagnostics.Debug.WriteLine($"Supabase auth payload: {json}");

                var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                System.Diagnostics.Debug.WriteLine($"Supabase Google auth response status: {response.StatusCode}");
                System.Diagnostics.Debug.WriteLine($"Supabase Google auth response: {responseContent}");

                if (response.IsSuccessStatusCode)
                {
                    try
                    {
                        var authResponse = JsonSerializer.Deserialize<SupabaseAuthResponse>(responseContent, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        bool success = authResponse != null && !string.IsNullOrEmpty(authResponse.AccessToken);
                        System.Diagnostics.Debug.WriteLine($"Supabase auth success: {success}");
                        return success;
                    }
                    catch (JsonException ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to parse Supabase auth response: {ex.Message}");
                        return false;
                    }
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Supabase Google auth failed: {response.StatusCode} - {response.ReasonPhrase}");

                    // Try to parse error response
                    try
                    {
                        var errorResponse = JsonSerializer.Deserialize<SupabaseErrorResponse>(responseContent, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        if (errorResponse != null)
                        {
                            System.Diagnostics.Debug.WriteLine($"Supabase error: {errorResponse.Error} - {errorResponse.ErrorDescription ?? errorResponse.Message}");
                        }
                    }
                    catch
                    {
                        System.Diagnostics.Debug.WriteLine("Could not parse Supabase error response");
                    }

                    return false;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception in AuthenticateWithSupabaseUsingGoogleAsync: {ex}");
                return false;
            }
        }

        private void OpenBrowser(string url)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    Process.Start("open", url);
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                {
                    Process.Start("xdg-open", url);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to open browser: {ex}");
            }
        }

        // Helper method to parse query strings without HttpUtility
        private NameValueCollection ParseQueryString(string query)
        {
            var result = new NameValueCollection();
            if (string.IsNullOrEmpty(query)) return result;

            if (query.StartsWith("?"))
                query = query.Substring(1);

            var pairs = query.Split('&');
            foreach (var pair in pairs)
            {
                var keyValue = pair.Split('=');
                if (keyValue.Length == 2)
                {
                    var key = Uri.UnescapeDataString(keyValue[0]);
                    var value = Uri.UnescapeDataString(keyValue[1]);
                    result.Add(key, value);
                }
            }
            return result;
        }

        // PKCE helper methods
        private string GenerateCodeVerifier()
        {
            var bytes = new byte[32];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        private string GenerateCodeChallenge(string codeVerifier)
        {
            using (var sha256 = SHA256.Create())
            {
                var hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(codeVerifier));
                return Convert.ToBase64String(hash)
                    .TrimEnd('=')
                    .Replace('+', '-')
                    .Replace('/', '_');
            }
        }

        private string GenerateRandomString(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var bytes = new byte[length];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(bytes);
            }
            return new string(bytes.Select(b => chars[b % chars.Length]).ToArray());
        }

        // Original Supabase authentication method (unchanged)
        private async Task<bool> AuthenticateWithSupabaseAsync(string email, string password)
        {
            try
            {
                var payload = new
                {
                    email = email,
                    password = password
                };

                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var request = new HttpRequestMessage(HttpMethod.Post, $"{_supabaseUrl}/auth/v1/token?grant_type=password")
                {
                    Content = content
                };

                request.Headers.Add("apikey", _supabaseKey);
                request.Headers.Add("Authorization", $"Bearer {_supabaseKey}");
                request.Headers.Add("X-Client-Info", "supabase-csharp/0.0.1");

                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

                System.Diagnostics.Debug.WriteLine($"Making request to: {request.RequestUri}");
                System.Diagnostics.Debug.WriteLine($"Request payload: {json}");

                var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();

                System.Diagnostics.Debug.WriteLine($"Auth Response Status: {response.StatusCode}");
                System.Diagnostics.Debug.WriteLine($"Auth Response Content: {responseContent}");

                if (response.IsSuccessStatusCode)
                {
                    var authResponse = JsonSerializer.Deserialize<SupabaseAuthResponse>(responseContent, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    bool success = !string.IsNullOrEmpty(authResponse?.AccessToken);
                    System.Diagnostics.Debug.WriteLine($"Authentication success: {success}");
                    System.Diagnostics.Debug.WriteLine($"Access token received: {!string.IsNullOrEmpty(authResponse?.AccessToken)}");
                    return success;
                }
                else
                {
                    System.Diagnostics.Debug.WriteLine($"Authentication failed with status: {response.StatusCode}");
                    System.Diagnostics.Debug.WriteLine($"Response headers: {string.Join(", ", response.Headers.Select(h => $"{h.Key}: {string.Join(", ", h.Value)}"))}");

                    try
                    {
                        var errorResponse = JsonSerializer.Deserialize<SupabaseErrorResponse>(responseContent, new JsonSerializerOptions
                        {
                            PropertyNameCaseInsensitive = true
                        });

                        string errorMsg = errorResponse?.ErrorDescription ?? errorResponse?.Message ?? errorResponse?.Error ?? "Authentication failed";
                        System.Diagnostics.Debug.WriteLine($"Parsed error message: {errorMsg}");
                        ErrorMessage = ConvertErrorMessage(errorMsg);
                    }
                    catch (Exception parseEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"Failed to parse error response: {parseEx.Message}");
                        System.Diagnostics.Debug.WriteLine($"Raw error response: {responseContent}");
                        ErrorMessage = "Autentisering feilet - sjekk innloggingsdetaljene dine";
                    }

                    return false;
                }
            }
            catch (HttpRequestException httpEx)
            {
                System.Diagnostics.Debug.WriteLine($"HTTP error during authentication: {httpEx.Message}");
                ErrorMessage = "Nettverksfeil - sjekk internettforbindelsen din";
                return false;
            }
            catch (TaskCanceledException timeoutEx)
            {
                System.Diagnostics.Debug.WriteLine($"Request timeout during authentication: {timeoutEx.Message}");
                ErrorMessage = "Forespørsel tidsavbrudd - prøv igjen";
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Unexpected error during authentication: {ex}");
                ErrorMessage = "Innlogging feilet - prøv igjen senere";
                return false;
            }
        }

        private string ConvertErrorMessage(string errorMessage)
        {
            if (string.IsNullOrEmpty(errorMessage))
                return "Innlogging feilet - sjekk e-post og passord";

            return errorMessage.ToLower() switch
            {
                var msg when msg.Contains("invalid login credentials") || msg.Contains("invalid_grant") => "Ugyldig e-post eller passord",
                var msg when msg.Contains("email not confirmed") => "E-post ikke bekreftet - sjekk e-posten din",
                var msg when msg.Contains("user not found") => "Bruker ikke funnet",
                var msg when msg.Contains("invalid email") => "Ugyldig e-postadresse",
                var msg when msg.Contains("password") && msg.Contains("wrong") => "Passord er feil",
                var msg when msg.Contains("too many requests") || msg.Contains("rate") => "For mange forsøk - vent litt før du prøver igjen",
                var msg when msg.Contains("network") => "Nettverksfeil - sjekk internettforbindelsen",
                var msg when msg.Contains("timeout") => "Tidsavbrudd - prøv igjen",
                _ => $"Innlogging feilet: {errorMessage}"
            };
        }

        private async Task SaveActivationStatusAsync(string email = null, string provider = "Email")
        {
            try
            {
                var activationData = new
                {
                    IsActivated = true,
                    ActivatedAt = DateTime.UtcNow,
                    Email = email ?? Email,
                    Provider = provider
                };

                var json = JsonSerializer.Serialize(activationData, new JsonSerializerOptions { WriteIndented = true });

                string appDataDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AkademiTrack"
                );

                System.IO.Directory.CreateDirectory(appDataDir);
                string activationPath = System.IO.Path.Combine(appDataDir, "activation.json");

                await System.IO.File.WriteAllTextAsync(activationPath, json);

                System.Diagnostics.Debug.WriteLine($"Activation status saved to: {activationPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save activation status: {ex.Message}");
            }
        }

        private async Task ExitAsync()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }
    }

    // Google OAuth response models
    public class GoogleTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; }

        [JsonPropertyName("id_token")]
        public string IdToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; }
    }

    public class GoogleUserInfo
    {
        public string Id { get; set; }
        public string Email { get; set; }
        public string Name { get; set; }
        public string GivenName { get; set; }
        public string FamilyName { get; set; }
        public string Picture { get; set; }
        public string Locale { get; set; }
    }

    // Existing Supabase models (unchanged)
    public class SupabaseAuthResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; }

        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; }

        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("token_type")]
        public string TokenType { get; set; }

        [JsonPropertyName("user")]
        public SupabaseUser User { get; set; }
    }

    public class SupabaseUser
    {
        public string Id { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? LastSignInAt { get; set; }
        public DateTime? EmailConfirmedAt { get; set; }
        public DateTime? PhoneConfirmedAt { get; set; }
        public SupabaseUserMetadata UserMetadata { get; set; }
        public SupabaseUserMetadata AppMetadata { get; set; }
    }

    public class SupabaseUserMetadata
    {
        // Custom user data properties can be added here as needed
    }

    public class SupabaseErrorResponse
    {
        public string Error { get; set; }
        public string ErrorDescription { get; set; }
        public string Message { get; set; }
    }
}