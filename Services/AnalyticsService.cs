using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

namespace AkademiTrack.Services
{
    public class AnalyticsService
    {
        private static readonly HttpClient _httpClient = new HttpClient();
        private readonly string _supabaseUrl = "https://zqqxqyxozyydhotfuurc.supabase.co";
        private readonly string _supabaseAnonKey = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJpc3MiOiJzdXBhYmFzZSIsInJlZiI6InpxcXhxeXhvenl5ZGhvdGZ1dXJjIiwicm9sZSI6ImFub24iLCJpYXQiOjE3Njc2NzkyNTcsImV4cCI6MjA4MzI1NTI1N30.AeCL4DFJzggZ68JGCgYai7XDniWIAUMt_5zAkjNe6OA";
        private readonly string _sessionId;
        private bool _sessionStarted = false;

        public AnalyticsService()
        {
            // Generate new anonymous session ID each time app starts
            _sessionId = Guid.NewGuid().ToString();
            
            // Configure HTTP client with correct Supabase headers
            _httpClient.DefaultRequestHeaders.Add("apikey", _supabaseAnonKey);
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_supabaseAnonKey}");

            Debug.WriteLine($"[Analytics] New session created: {_sessionId}");
        }

        public async Task StartSessionAsync()
        {
            if (_sessionStarted) return;

            try
            {
                var sessionData = new
                {
                    id = _sessionId,
                    platform = GetPlatform(),
                    app_version = GetAppVersion()
                };

                var json = JsonSerializer.Serialize(sessionData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await _httpClient.PostAsync($"{_supabaseUrl}/rest/v1/sessions", content);
                
                if (response.IsSuccessStatusCode)
                {
                    _sessionStarted = true;
                    Debug.WriteLine($"[Analytics] ✓ Session started successfully: {_sessionId}");
                }
                else
                {
                    Debug.WriteLine($"[Analytics] Session start failed: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                // Silent fail - no user notification for analytics issues
                Debug.WriteLine($"[Analytics] Session start error: {ex.Message}");
            }
        }

        public async Task TrackEventAsync(string eventName, object? eventData = null)
        {
            try
            {
                var trackingData = new
                {
                    session_id = _sessionId,
                    event_name = eventName,
                    event_data = eventData != null ? JsonSerializer.Serialize(eventData) : null,
                    created_at = DateTime.UtcNow
                };

                var json = JsonSerializer.Serialize(trackingData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Fire and forget - don't wait for response
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var response = await _httpClient.PostAsync($"{_supabaseUrl}/rest/v1/events", content);
                        
                        if (response.IsSuccessStatusCode)
                        {
                            Debug.WriteLine($"[Analytics] ✓ Event tracked: {eventName}");
                        }
                        else
                        {
                            Debug.WriteLine($"[Analytics] Event tracking failed: {response.StatusCode}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Analytics] Event tracking failed: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Analytics] Event tracking error: {ex.Message}");
            }
        }

        public async Task LogErrorAsync(string errorType, string errorMessage, Exception? exception = null)
        {
            try
            {
                var errorData = new
                {
                    session_id = _sessionId,
                    error_type = errorType,
                    error_message = errorMessage,
                    stack_trace = exception?.ToString(),
                    app_version = GetAppVersion(),
                    platform = GetPlatform(),
                    created_at = DateTime.UtcNow
                };

                var json = JsonSerializer.Serialize(errorData);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                // Fire and forget - don't wait for response
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var response = await _httpClient.PostAsync($"{_supabaseUrl}/rest/v1/error_logs", content);
                        
                        if (response.IsSuccessStatusCode)
                        {
                            Debug.WriteLine($"[Analytics] ✓ Error logged: {errorType}");
                        }
                        else
                        {
                            Debug.WriteLine($"[Analytics] Error logging failed: {response.StatusCode}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Analytics] Error logging failed: {ex.Message}");
                    }
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Analytics] Error logging error: {ex.Message}");
            }
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
            // Optional: Track session end
            _ = Task.Run(async () =>
            {
                try
                {
                    await TrackEventAsync("session_end");
                    Debug.WriteLine($"[Analytics] Session ended: {_sessionId}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Analytics] Session end error: {ex.Message}");
                }
            });
        }
    }
}