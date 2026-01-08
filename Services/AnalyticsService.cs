using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;

namespace AkademiTrack.Services
{
    public class AnalyticsService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly string _supabaseUrl = "https://zqqxqyxozyydhotfuurc.supabase.co";
        private readonly string _supabaseAnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InpxcXhxeXhvenl5ZGhvdGZ1dXJjIiwicm9sZSI6ImFub24iLCJpYXQiOjE3Njc2NzkyNTcsImV4cCI6MjA4MzI1NTI1N30.AeCL4DFJzggZ68JGCgYai7XDniWIAUMt_5zAkjNe6OA";
        
        private readonly string _persistentUserId;  // Persistent across app restarts
        private string? _currentSessionId;          // Current session UUID from Supabase
        private bool _sessionStarted = false;
        
        // Heartbeat system
        private Timer? _heartbeatTimer;
        private bool _automationActive = false;
        private readonly object _timerLock = new object();

        public AnalyticsService()
        {
            // Get or create persistent anonymous user ID
            _persistentUserId = GetOrCreatePersistentUserId();
            
            // Configure HTTP client with correct Supabase headers (only if not already configured)
            if (!_httpClient.DefaultRequestHeaders.Contains("apikey"))
            {
                _httpClient.DefaultRequestHeaders.Add("apikey", _supabaseAnonKey);
                _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_supabaseAnonKey}");
            }

            Debug.WriteLine($"[Analytics] Persistent User ID: {_persistentUserId}");
        }

        public async Task StartSessionAsync()
        {
            if (_sessionStarted) return;

            try
            {
                Debug.WriteLine($"[Analytics] Starting session for user: {_persistentUserId}");
                
                // First, check if there's an existing session for this user
                var existingSessionId = await GetExistingSessionIdAsync();
                
                if (!string.IsNullOrEmpty(existingSessionId))
                {
                    // Update existing session
                    Debug.WriteLine($"[Analytics] Found existing session {existingSessionId}, updating it");
                    _currentSessionId = existingSessionId;
                    await TrackActionAsync("app_opened");
                    _sessionStarted = true;
                    Debug.WriteLine($"[Analytics] ✓ Existing session updated for user: {_persistentUserId}");
                    
                    // Start heartbeat timer for existing session too
                    StartHeartbeatTimer();
                }
                else
                {
                    // Create new session
                    Debug.WriteLine($"[Analytics] No existing session found, creating new one");
                    
                    var sessionData = new
                    {
                        user_id = _persistentUserId,
                        platform = GetPlatform(),
                        app_version = GetAppVersion(),
                        started_at = DateTime.UtcNow,
                        last_action = "app_opened",
                        last_action_at = DateTime.UtcNow
                    };

                    var json = JsonSerializer.Serialize(sessionData);
                    Debug.WriteLine($"[Analytics] Session data: {json}");
                    
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    var url = $"{_supabaseUrl}/rest/v1/sessions";
                    
                    Debug.WriteLine($"[Analytics] Posting to: {url}");

                    // Use Prefer: return=representation to get the created session back
                    var request = new HttpRequestMessage(HttpMethod.Post, url);
                    request.Content = content;
                    request.Headers.Add("Prefer", "return=representation");

                    var response = await _httpClient.SendAsync(request);
                    var responseContent = await response.Content.ReadAsStringAsync();
                    
                    Debug.WriteLine($"[Analytics] Response status: {response.StatusCode}");
                    Debug.WriteLine($"[Analytics] Response content: {responseContent}");
                    
                    if (response.IsSuccessStatusCode)
                    {
                        // Parse the response to get the session ID
                        try
                        {
                            if (!string.IsNullOrEmpty(responseContent))
                            {
                                var sessionResponse = JsonSerializer.Deserialize<JsonElement[]>(responseContent);
                                if (sessionResponse != null && sessionResponse.Length > 0 && 
                                    sessionResponse[0].TryGetProperty("id", out var idElement))
                                {
                                    _currentSessionId = idElement.GetString();
                                    Debug.WriteLine($"[Analytics] ✓ New session created with ID: {_currentSessionId}");
                                }
                                else
                                {
                                    Debug.WriteLine($"[Analytics] Warning: Session created but no ID returned in response");
                                }
                            }
                        }
                        catch (Exception parseEx)
                        {
                            Debug.WriteLine($"[Analytics] Failed to parse session response: {parseEx.Message}");
                            Debug.WriteLine($"[Analytics] Response content was: {responseContent}");
                        }
                        
                        _sessionStarted = true;
                        Debug.WriteLine($"[Analytics] ✓ New session started successfully for user: {_persistentUserId}");
                    }
                    else
                    {
                        Debug.WriteLine($"[Analytics] Session start failed: {response.StatusCode} - {responseContent}");
                    }
                }

                // Start heartbeat timer after successful session creation
                if (_sessionStarted && !string.IsNullOrEmpty(_currentSessionId))
                {
                    StartHeartbeatTimer();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Analytics] Session start error: {ex.Message}");
                Debug.WriteLine($"[Analytics] Stack trace: {ex.StackTrace}");
            }
        }

        private async Task<string?> GetExistingSessionIdAsync()
        {
            try
            {
                var url = $"{_supabaseUrl}/rest/v1/sessions?user_id=eq.{_persistentUserId}&select=id&order=started_at.desc&limit=1";
                Debug.WriteLine($"[Analytics] Checking for existing session: {url}");
                
                var response = await _httpClient.GetAsync(url);
                var responseContent = await response.Content.ReadAsStringAsync();
                
                Debug.WriteLine($"[Analytics] Existing session check response: {response.StatusCode}");
                Debug.WriteLine($"[Analytics] Response content: {responseContent}");
                
                if (response.IsSuccessStatusCode && !string.IsNullOrEmpty(responseContent))
                {
                    var sessions = JsonSerializer.Deserialize<JsonElement[]>(responseContent);
                    if (sessions != null && sessions.Length > 0 && 
                        sessions[0].TryGetProperty("id", out var idElement))
                    {
                        var sessionId = idElement.GetString();
                        Debug.WriteLine($"[Analytics] Found existing session: {sessionId}");
                        return sessionId;
                    }
                }
                
                Debug.WriteLine($"[Analytics] No existing session found");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Analytics] Error checking for existing session: {ex.Message}");
                return null;
            }
        }

        public async Task TrackActionAsync(string action)
        {
            try
            {
                if (string.IsNullOrEmpty(_currentSessionId))
                {
                    Debug.WriteLine($"[Analytics] No session ID available for action tracking. Session started: {_sessionStarted}");
                    return;
                }

                Debug.WriteLine($"[Analytics] Tracking action '{action}' for user: {_persistentUserId}, session: {_currentSessionId}");
                
                // Update session with new action
                var updateData = new
                {
                    last_action = action,
                    last_action_at = DateTime.UtcNow
                };

                var json = JsonSerializer.Serialize(updateData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var url = $"{_supabaseUrl}/rest/v1/sessions?id=eq.{_currentSessionId}";
                
                Debug.WriteLine($"[Analytics] Updating session {_currentSessionId} with action '{action}': {json}");
                Debug.WriteLine($"[Analytics] PATCH URL: {url}");

                var request = new HttpRequestMessage(new HttpMethod("PATCH"), url);
                request.Content = content;

                var response = await _httpClient.SendAsync(request);
                var responseContent = await response.Content.ReadAsStringAsync();
                
                Debug.WriteLine($"[Analytics] Action tracking response: {response.StatusCode}");
                Debug.WriteLine($"[Analytics] Response content: {responseContent}");
                
                if (response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[Analytics] ✓ Action '{action}' tracked successfully");
                }
                else
                {
                    Debug.WriteLine($"[Analytics] Action tracking failed: {response.StatusCode} - {responseContent}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Analytics] Action tracking error: {ex.Message}");
                Debug.WriteLine($"[Analytics] Stack trace: {ex.StackTrace}");
            }
        }

        // Convenience methods for specific actions
        public async Task TrackAutomationStartAsync() 
        {
            try
            {
                _automationActive = true;
                await TrackActionAsync("automation_started");
                RestartHeartbeatTimer(); // Switch to faster heartbeat
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Analytics] TrackAutomationStartAsync failed: {ex.Message}");
            }
        }
        
        public async Task TrackAutomationStopAsync() 
        {
            try
            {
                _automationActive = false;
                await TrackActionAsync("automation_stopped");
                RestartHeartbeatTimer(); // Switch back to slower heartbeat
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Analytics] TrackAutomationStopAsync failed: {ex.Message}");
            }
        }
        
        public async Task TrackAppClosedAsync() 
        {
            try
            {
                await TrackActionAsync("app_closed");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Analytics] TrackAppClosedAsync failed: {ex.Message}");
            }
        }

        private void StartHeartbeatTimer()
        {
            try
            {
                lock (_timerLock)
                {
                    _heartbeatTimer?.Dispose();
                    
                    // Start with 10-minute interval (app is open but automation not active)
                    var interval = TimeSpan.FromMinutes(10);
                    _heartbeatTimer = new Timer(HeartbeatCallback, null, interval, interval);
                    
                    Debug.WriteLine($"[Analytics] Heartbeat timer started with {interval.TotalMinutes} minute interval");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Analytics] StartHeartbeatTimer failed: {ex.Message}");
            }
        }

        private void RestartHeartbeatTimer()
        {
            try
            {
                lock (_timerLock)
                {
                    _heartbeatTimer?.Dispose();
                    
                    // Choose interval based on automation status
                    var interval = _automationActive ? TimeSpan.FromMinutes(5) : TimeSpan.FromMinutes(10);
                    _heartbeatTimer = new Timer(HeartbeatCallback, null, interval, interval);
                    
                    Debug.WriteLine($"[Analytics] Heartbeat timer restarted with {interval.TotalMinutes} minute interval (automation: {_automationActive})");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Analytics] RestartHeartbeatTimer failed: {ex.Message}");
            }
        }

        private async void HeartbeatCallback(object? state)
        {
            try
            {
                if (string.IsNullOrEmpty(_currentSessionId))
                {
                    Debug.WriteLine("[Analytics] No session ID for heartbeat");
                    return;
                }

                var action = _automationActive ? "automation_active" : "app_active";
                Debug.WriteLine($"[Analytics] Sending heartbeat: {action}");
                
                await TrackActionAsync(action);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Analytics] Heartbeat error: {ex.Message}");
            }
        }

        public async Task LogErrorAsync(string errorType, string errorMessage, Exception? exception = null)
        {
            try
            {
                if (string.IsNullOrEmpty(_currentSessionId))
                {
                    Debug.WriteLine($"[Analytics] No session ID available for error logging. Session started: {_sessionStarted}");
                    return;
                }

                Debug.WriteLine($"[Analytics] Logging error: {errorType} - {errorMessage}");
                Debug.WriteLine($"[Analytics] Using session ID: {_currentSessionId}");
                
                var errorData = new
                {
                    session_id = _currentSessionId,
                    user_id = _persistentUserId,
                    error_type = errorType,
                    error_message = errorMessage,
                    stack_trace = exception?.ToString(),
                    app_version = GetAppVersion(),
                    platform = GetPlatform(),
                    created_at = DateTime.UtcNow
                };

                var json = JsonSerializer.Serialize(errorData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                Debug.WriteLine($"[Analytics] Error data: {json}");

                var url = $"{_supabaseUrl}/rest/v1/error_logs";
                Debug.WriteLine($"[Analytics] POST URL: {url}");

                var response = await _httpClient.PostAsync(url, content);
                var responseContent = await response.Content.ReadAsStringAsync();
                
                Debug.WriteLine($"[Analytics] Error log response: {response.StatusCode}");
                Debug.WriteLine($"[Analytics] Error log content: {responseContent}");
                
                if (response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[Analytics] ✓ Error logged: {errorType}");
                }
                else
                {
                    Debug.WriteLine($"[Analytics] Error logging failed: {response.StatusCode} - {responseContent}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Analytics] Error logging error: {ex.Message}");
                Debug.WriteLine($"[Analytics] Stack trace: {ex.StackTrace}");
            }
        }

        private static string GetOrCreatePersistentUserId()
        {
            try
            {
                var appDataDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AkademiTrack"
                );
                Directory.CreateDirectory(appDataDir);
                
                var userIdFile = Path.Combine(appDataDir, "anonymous_user_id.txt");
                
                // Try to load existing user ID
                if (File.Exists(userIdFile))
                {
                    var existingId = File.ReadAllText(userIdFile).Trim();
                    if (!string.IsNullOrEmpty(existingId))
                    {
                        Debug.WriteLine($"[Analytics] Loaded existing user ID: {existingId}");
                        return existingId;
                    }
                }
                
                // Generate new persistent user ID
                var newUserId = GenerateAnonymousUserId();
                File.WriteAllText(userIdFile, newUserId);
                
                Debug.WriteLine($"[Analytics] Created new persistent user ID: {newUserId}");
                return newUserId;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Analytics] Error managing user ID: {ex.Message}");
                // Fallback to session-based ID if file operations fail
                return Guid.NewGuid().ToString();
            }
        }

        /// <summary>
        /// Get the persistent anonymous user ID for this installation
        /// </summary>
        public string GetPersistentUserId() => _persistentUserId;

        /// <summary>
        /// Get the current session ID
        /// </summary>
        public string? GetSessionId() => _currentSessionId;

        private static string GenerateAnonymousUserId()
        {
            // Generate a longer, more unique anonymous ID
            // Format: AT-{timestamp}-{random}
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString("x");
            var random = Guid.NewGuid().ToString("N")[..12]; // First 12 chars
            return $"AT-{timestamp}-{random}";
        }

        private static string GetPlatform()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return "Windows";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return "macOS";
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return "Linux";
            else
                return "Unknown";
        }

        private static string GetAppVersion()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;
                return version?.ToString() ?? "Unknown";
            }
            catch
            {
                return "Unknown";
            }
        }

        public void Dispose()
        {
            try
            {
                lock (_timerLock)
                {
                    _heartbeatTimer?.Dispose();
                    _heartbeatTimer = null;
                }
                Debug.WriteLine($"[Analytics] Analytics service disposed for user: {_persistentUserId}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Analytics] Dispose failed: {ex.Message}");
            }
        }
    }
}