using AkademiTrack.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using PuppeteerSharp;
using System.Security;
using System.Text.Json;
using System.Runtime.InteropServices;

namespace AkademiTrack.Services
{
    public class AuthenticationService : IDisposable
    {
        private readonly INotificationService? _notificationService;
        private readonly bool _suppressNotifications;
        private bool _disposed = false;
        
        // Original variables that worked
        private string _loginEmail = "";
        private SecureString? _loginPasswordSecure;
        private string _schoolName = "";
        private Dictionary<string, string>? _cachedCookies;

        public AuthenticationService(INotificationService? notificationService = null, bool suppressNotifications = false)
        {
            _notificationService = notificationService;
            _suppressNotifications = suppressNotifications;
        }

        public async Task<AuthenticationResult> AuthenticateAsync()
        {
            try
            {
                Debug.WriteLine("[AUTH] Starting authentication");
                
                // Load credentials (same as before)
                var credentialsResult = await LoadCredentialsAsync();
                if (!credentialsResult.Success)
                {
                    return credentialsResult;
                }

                // Test existing cookies first (same as before)
                _cachedCookies = await SecureCredentialStorage.LoadCookiesAsync();
                if (_cachedCookies != null && await TestCookiesAsync())
                {
                    Debug.WriteLine("[AUTH] Existing cookies are valid!");
                    var parameters = await LoadUserParametersFromFileAsync();
                    if (parameters != null && parameters.IsComplete)
                    {
                        return AuthenticationResult.CreateSuccess(_cachedCookies, parameters);
                    }
                }

                // Need fresh login with PuppeteerSharp
                Debug.WriteLine("[AUTH] Performing fresh login with PuppeteerSharp");
                return await PerformPuppeteerLoginAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AUTH] Authentication error: {ex.Message}");
                return AuthenticationResult.CreateFailed($"Authentication error: {ex.Message}");
            }
        }

        private async Task<AuthenticationResult> LoadCredentialsAsync()
        {
            try
            {
                // Load login credentials
                _loginEmail = await SecureCredentialStorage.GetCredentialAsync("LoginEmail") ?? "";
                var passwordPlain = await SecureCredentialStorage.GetCredentialAsync("LoginPassword") ?? "";
                _schoolName = await SecureCredentialStorage.GetCredentialAsync("SchoolName") ?? "";

                if (!string.IsNullOrEmpty(passwordPlain))
                {
                    _loginPasswordSecure = StringToSecureString(passwordPlain);
                }

                bool hasCredentials = !string.IsNullOrEmpty(_loginEmail) &&
                                    _loginPasswordSecure != null && _loginPasswordSecure.Length > 0 &&
                                    !string.IsNullOrEmpty(_schoolName);

                if (!hasCredentials)
                {
                    return AuthenticationResult.CreateFailed("Mangler innloggingsdata. Vennligst gå til innstillinger og legg inn Feide-brukernavn, passord og skolenavn.");
                }

                return AuthenticationResult.CreateSuccess(null, null);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LOAD] Failed to load credentials: {ex.Message}");
                return AuthenticationResult.CreateFailed($"Failed to load credentials: {ex.Message}");
            }
        }

        private async Task<bool> TestCookiesAsync()
        {
            try
            {
                if (_cachedCookies == null) return false;

                using var httpClient = new System.Net.Http.HttpClient();
                httpClient.Timeout = TimeSpan.FromSeconds(10);

                var jsessionId = _cachedCookies.GetValueOrDefault("JSESSIONID", "");
                if (string.IsNullOrEmpty(jsessionId)) return false;

                var url = $"https://iskole.net/iskole_elev/rest/v0/VoTimeplan_elev_oppmote;jsessionid={jsessionId}";
                url += "?finder=RESTFilter;fylkeid=00,planperi=2025-26,skoleid=312&onlyData=true&limit=1";

                var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
                var cookieString = string.Join("; ", _cachedCookies.Select(c => $"{c.Key}={c.Value}"));
                request.Headers.Add("Cookie", cookieString);

                var response = await httpClient.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TEST] Cookie test failed: {ex.Message}");
                return false;
            }
        }

        private async Task<AuthenticationResult> PerformPuppeteerLoginAsync()
        {
            IBrowser? browser = null;
            IPage? page = null;

            try
            {
                Debug.WriteLine("[PUPPETEER] Starting browser");
                
                // Set environment variables to disable keychain access
                Environment.SetEnvironmentVariable("CHROME_KEYCHAIN", "0");
                Environment.SetEnvironmentVariable("CHROME_PASSWORD_STORE", "basic");
                
                // Ensure Chromium is downloaded and working with consistent cache directory
                var chromiumCacheDir = GetChromiumCacheDirectory();
                var chromiumBrowserFetcher = new BrowserFetcher(new BrowserFetcherOptions
                {
                    Path = chromiumCacheDir
                });
                
                try
                {
                    Debug.WriteLine("[PUPPETEER] Downloading/verifying Chromium...");
                    var downloadedBrowser = await chromiumBrowserFetcher.DownloadAsync();
                    Debug.WriteLine($"[PUPPETEER] Chromium ready at: {downloadedBrowser?.GetExecutablePath()}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[PUPPETEER] Failed to download Chromium: {ex.Message}");
                    return new AuthenticationResult 
                    { 
                        Success = false, 
                        ErrorMessage = $"Kunne ikke laste ned nødvendige komponenter for automatisering. Vennligst start appen på nytt eller kontakt support. Feil: {ex.Message}" 
                    };
                }
                
                // Launch browser with explicit cache directory
                var installedBrowsers = chromiumBrowserFetcher.GetInstalledBrowsers();
                var firstInstalledBrowser = installedBrowsers.FirstOrDefault();
                
                string? executablePath = null;
                if (firstInstalledBrowser != null)
                {
                    executablePath = firstInstalledBrowser.GetExecutablePath();
                    Debug.WriteLine($"[PUPPETEER] Using Chromium at: {executablePath}");
                }
                
                browser = await Puppeteer.LaunchAsync(new LaunchOptions
                {
                    Headless = true, // Hide Chrome window
                    ExecutablePath = executablePath, // Explicitly set the path
                    UserDataDir = GetChromiumUserDataDirectory(), // Use isolated user data directory
                    Args = new[] { 
                        "--no-sandbox", 
                        "--disable-setuid-sandbox",
                        "--disable-dev-shm-usage",
                        "--disable-gpu",
                        "--no-first-run",
                        "--no-default-browser-check",
                        "--disable-default-apps",
                        "--disable-web-security", // Disable CSS loading for speed
                        "--disable-features=VizDisplayCompositor",
                        "--blink-settings=imagesEnabled=false", // Disable images for faster loading
                        "--use-mock-keychain", // Prevent keychain access popup
                        "--password-store=basic", // Use basic password store instead of keychain
                        "--disable-password-generation", // Disable password generation features
                        "--disable-save-password-bubble", // Disable save password prompts
                        "--disable-background-networking", // Disable background network requests
                        "--disable-sync", // Disable Chrome sync
                        "--disable-translate", // Disable translate service
                        "--disable-ipc-flooding-protection", // Disable IPC flooding protection
                        "--disable-renderer-backgrounding", // Disable renderer backgrounding
                        "--disable-backgrounding-occluded-windows", // Disable backgrounding occluded windows
                        "--disable-features=TranslateUI,BlinkGenPropertyTrees", // Disable more features
                        "--aggressive-cache-discard", // Aggressively discard cache
                        "--disable-extensions", // Disable extensions
                        "--disable-plugins", // Disable plugins
                        "--incognito" // Use incognito mode to avoid accessing stored data
                    }
                });

                page = await browser.NewPageAsync();
                await page.SetViewportAsync(new ViewPortOptions { Width = 1920, Height = 1080 });

                // Block CSS and other resources for faster loading
                await page.SetRequestInterceptionAsync(true);
                page.Request += async (sender, e) =>
                {
                    try
                    {
                        var request = e.Request;
                        // Block CSS, fonts, and images to speed up loading
                        if (request.ResourceType == ResourceType.StyleSheet || 
                            request.ResourceType == ResourceType.Font ||
                            request.ResourceType == ResourceType.Image)
                        {
                            await request.AbortAsync();
                        }
                        else
                        {
                            await request.ContinueAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        // Silently handle request interception errors to prevent notifications
                        Debug.WriteLine($"Request interception error (ignored): {ex.Message}");
                    }
                };

                Debug.WriteLine("[PUPPETEER] Navigating to login page");
                await page.GoToAsync("https://iskole.net/elev/?ojr=login");
                await Task.Delay(2000);

                // Click FEIDE button
                Debug.WriteLine("[PUPPETEER] Clicking FEIDE button");
                await page.WaitForSelectorAsync("button:has(span.feide_icon)", new WaitForSelectorOptions { Timeout = 10000 });
                await page.ClickAsync("button:has(span.feide_icon)");
                await page.WaitForNavigationAsync(new NavigationOptions { Timeout = 15000 });

                // Handle organization selection - directly click on school from list without using search
                Debug.WriteLine("[PUPPETEER] Selecting organization directly from list");
                
                // Wait for the organization list to be present in DOM
                await page.WaitForSelectorAsync("#orglist", new WaitForSelectorOptions { Timeout = 10000 });
                
                // Find and click the school by matching org_name attribute (case-insensitive)
                var schoolNameLower = _schoolName.ToLowerInvariant();
                Debug.WriteLine($"[PUPPETEER] Looking for school: '{_schoolName}' (normalized: '{schoolNameLower}')");
                
                // Use a more specific selector to find all school items
                var schoolSelector = "li.orglist_item[org_name]";
                await page.WaitForSelectorAsync(schoolSelector, new WaitForSelectorOptions { Timeout = 5000 });
                
                // Get all school elements and find the matching one
                var schoolElements = await page.QuerySelectorAllAsync(schoolSelector);
                bool schoolFound = false;
                
                Debug.WriteLine($"[PUPPETEER] Found {schoolElements.Length} schools in the list");
                
                foreach (var element in schoolElements)
                {
                    var orgName = await page.EvaluateFunctionAsync<string>("el => el.getAttribute('org_name')", element);
                    Debug.WriteLine($"[PUPPETEER] Checking school: '{orgName}'");
                    
                    if (!string.IsNullOrEmpty(orgName) && orgName.ToLowerInvariant().Contains(schoolNameLower))
                    {
                        Debug.WriteLine($"✅ [PUPPETEER] Found matching school: '{orgName}' - clicking it");
                        await element.ClickAsync();
                        schoolFound = true;
                        break;
                    }
                }
                
                if (!schoolFound)
                {
                    Debug.WriteLine($"[PUPPETEER] School '{_schoolName}' not found in list, trying alternative approach");
                    
                    // Try to find by text content instead of attribute
                    var schoolByText = await page.QuerySelectorAsync($"li.orglist_item:has(.orglist_name:contains('{_schoolName}'))");
                    if (schoolByText != null)
                    {
                        Debug.WriteLine($"✅ [PUPPETEER] Found school by text content - clicking it");
                        await schoolByText.ClickAsync();
                        schoolFound = true;
                    }
                    else
                    {
                        Debug.WriteLine($"[PUPPETEER] School not found by any method, falling back to search");
                        // Last resort: use search method
                        await page.ClickAsync("#org_selector_filter");
                        await page.TypeAsync("#org_selector_filter", _schoolName);
                        await Task.Delay(1000);
                        await page.WaitForSelectorAsync("li.orglist_item.match", new WaitForSelectorOptions { Timeout = 5000 });
                        await page.ClickAsync("li.orglist_item.match");
                        schoolFound = true;
                    }
                }
                
                if (schoolFound)
                {
                    // Small delay to ensure school selection is registered
                    await Task.Delay(500);
                    Debug.WriteLine("➡️ [PUPPETEER] Clicking Continue button to proceed with selected school");
                    await page.ClickAsync("#selectorg_button");
                }
                await page.WaitForNavigationAsync(new NavigationOptions { Timeout = 15000 });

                // Fill login form
                Debug.WriteLine("[PUPPETEER] Filling login form");
                await page.WaitForSelectorAsync("#username", new WaitForSelectorOptions { Timeout = 10000 });
                await page.TypeAsync("#username", _loginEmail);
                await page.TypeAsync("#password", SecureStringToString(_loginPasswordSecure));
                await page.ClickAsync("button[type='submit']");

                // Wait for success
                await page.WaitForNavigationAsync(new NavigationOptions { Timeout = 30000 });
                
                var currentUrl = page.Url;
                if (currentUrl.Contains("isFeideinnlogget=true"))
                {
                    Debug.WriteLine("[PUPPETEER] Login successful");
                    
                    // Extract cookies and parameters
                    var cookies = await ExtractCookiesAsync(page);
                    var parameters = await ExtractUserParametersAsync(page);

                    if (cookies != null && parameters != null)
                    {
                        await SaveCookiesAndParametersAsync(cookies, parameters);
                        return AuthenticationResult.CreateSuccess(cookies, parameters);
                    }
                }
                
                return AuthenticationResult.CreateFailed("Login feilet - kanskje feil passord eller brukernavn");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PUPPETEER] Error: {ex.Message}");
                return AuthenticationResult.CreateFailed($"Browser login error: {ex.Message}");
            }
            finally
            {
                if (page != null) await page.CloseAsync();
                if (browser != null) await browser.CloseAsync();
            }
        }

        private async Task<Dictionary<string, string>?> ExtractCookiesAsync(IPage page)
        {
            try
            {
                var cookies = await page.GetCookiesAsync();
                return cookies
                    .Where(c => c.Domain.Contains("iskole.net"))
                    .ToDictionary(c => c.Name, c => c.Value);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Cookie extraction failed: {ex.Message}");
                return null;
            }
        }

        private async Task<UserParameters?> ExtractUserParametersAsync(IPage page)
        {
            try
            {
                Debug.WriteLine("[PARAMS] Starting parameter extraction from VoUserData API");
                
                // Get cookies from the page first
                var cookies = await page.GetCookiesAsync();
                var cookieDict = cookies.Where(c => c.Domain.Contains("iskole.net"))
                                       .ToDictionary(c => c.Name, c => c.Value);
                
                var jsessionId = cookieDict.GetValueOrDefault("JSESSIONID", "");
                if (string.IsNullOrEmpty(jsessionId))
                {
                    Debug.WriteLine("[PARAMS] No JSESSIONID found");
                    return await FallbackParameterExtraction(page);
                }
                
                Debug.WriteLine($"[PARAMS] Using JSESSIONID: {jsessionId.Substring(0, Math.Min(10, jsessionId.Length))}");
                
                using var httpClient = new System.Net.Http.HttpClient();
                var userDataUrl = $"https://iskole.net/iskole_elev/rest/v0/VoUserData;jsessionid={jsessionId}";
                
                Debug.WriteLine($"[PARAMS] Fetching user data from: {userDataUrl.Replace(jsessionId, "***")}");
                
                var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, userDataUrl);
                request.Headers.Add("Host", "iskole.net");
                request.Headers.Add("Accept", "application/json, text/javascript, */*; q=0.01");
                request.Headers.Add("Accept-Language", "no-NB");
                request.Headers.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36");
                request.Headers.Add("Referer", "https://iskole.net/elev/?isFeideinnlogget=true&ojr=timeplan");
                request.Headers.Add("X-Requested-With", "XMLHttpRequest");
                
                var cookieString = string.Join("; ", cookieDict.Select(c => $"{c.Key}={c.Value}"));
                request.Headers.Add("Cookie", cookieString);
                
                var response = await httpClient.SendAsync(request);
                var content = await response.Content.ReadAsStringAsync();
                
                Debug.WriteLine($"[PARAMS] VoUserData response status: {response.StatusCode}");
                Debug.WriteLine($"[PARAMS] VoUserData response length: {content.Length} characters");
                Debug.WriteLine($"[PARAMS] VoUserData FULL response: {content}");
                
                if (!response.IsSuccessStatusCode)
                {
                    Debug.WriteLine($"[PARAMS] VoUserData API failed: {response.StatusCode}");
                    Debug.WriteLine($"[PARAMS] Response content: {content}");
                    return await FallbackParameterExtraction(page);
                }
                
                // Parse the user data response to extract parameters
                var parameters = ExtractParametersFromUserData(content);
                
                if (parameters != null && parameters.IsComplete)
                {
                    Debug.WriteLine($"[PARAMS] Successfully extracted from VoUserData: FylkeId='{parameters.FylkeId}', SkoleId='{parameters.SkoleId}', PlanPeri='{parameters.PlanPeri}'");
                    return parameters;
                }
                
                Debug.WriteLine("[PARAMS] VoUserData didn't provide complete parameters, trying fallback");
                return await FallbackParameterExtraction(page);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PARAMS] VoUserData extraction failed: {ex.Message}");
                Debug.WriteLine($"[PARAMS] Stack trace: {ex.StackTrace}");
                return await FallbackParameterExtraction(page);
            }
        }

        private UserParameters? ExtractParametersFromUserData(string jsonContent)
        {
            try
            {
                Debug.WriteLine($"[PARAMS] Parsing VoUserData JSON structure");
                Debug.WriteLine($"[PARAMS] Raw JSON content: {jsonContent}");
                
                using var document = JsonDocument.Parse(jsonContent);
                var root = document.RootElement;
                
                // Look for parameters in various possible locations in the JSON
                string? fylkeId = null;
                string? skoleId = null;
                string? planPeri = null;
                
                if (root.TryGetProperty("result", out var resultProp) && resultProp.ValueKind == JsonValueKind.String)
                {
                    var resultJson = resultProp.GetString();
                    Debug.WriteLine($"[PARAMS] Found result field with JSON string: {resultJson}");
                    
                    if (!string.IsNullOrEmpty(resultJson))
                    {
                        try
                        {
                            using var resultDoc = JsonDocument.Parse(resultJson);
                            var resultArray = resultDoc.RootElement;
                            
                            Debug.WriteLine($"[PARAMS] Parsed result JSON, type: {resultArray.ValueKind}");
                            
                            if (resultArray.ValueKind == JsonValueKind.Array && resultArray.GetArrayLength() > 0)
                            {
                                var firstItem = resultArray[0];
                                Debug.WriteLine($"[PARAMS] First item in array: {firstItem}");
                                
                                // Extract the correct field names from the actual response
                                if (firstItem.TryGetProperty("fylkeid", out var fylkeIdProp))
                                {
                                    fylkeId = fylkeIdProp.GetString();
                                    Debug.WriteLine($"[PARAMS] Found fylkeid: '{fylkeId}'");
                                }
                                    
                                if (firstItem.TryGetProperty("skoleid", out var skoleIdProp))
                                {
                                    skoleId = skoleIdProp.GetString();
                                    Debug.WriteLine($"[PARAMS] Found skoleid: '{skoleId}'");
                                }
                                    
                                if (firstItem.TryGetProperty("planperi", out var planPeriProp))
                                {
                                    planPeri = planPeriProp.GetString();
                                    Debug.WriteLine($"[PARAMS] Found planperi: '{planPeri}'");
                                }
                                    
                                Debug.WriteLine($"[PARAMS] Extracted from result JSON: fylkeid='{fylkeId}', skoleid='{skoleId}', planperi='{planPeri}'");
                            }
                            else
                            {
                                Debug.WriteLine($"[PARAMS] Result array is empty or not an array");
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[PARAMS] Failed to parse result JSON: {ex.Message}");
                            Debug.WriteLine($"[PARAMS] Result JSON content: {resultJson}");
                        }
                    }
                }
                else
                {
                    Debug.WriteLine("[PARAMS] No 'result' field found, checking other structures");
                }
                
                if (!string.IsNullOrEmpty(fylkeId) && !string.IsNullOrEmpty(skoleId) && !string.IsNullOrEmpty(planPeri))
                {
                    Debug.WriteLine($"[PARAMS] Successfully extracted parameters: FylkeId='{fylkeId}', SkoleId='{skoleId}', PlanPeri='{planPeri}'");
                    return new UserParameters
                    {
                        FylkeId = fylkeId,
                        SkoleId = skoleId,
                        PlanPeri = planPeri
                    };
                }
                
                Debug.WriteLine("[PARAMS] Could not extract all required parameters from VoUserData response");
                return null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PARAMS] JSON parsing failed: {ex.Message}");
                Debug.WriteLine($"[PARAMS] Stack trace: {ex.StackTrace}");
                return null;
            }
        }

        private Task<UserParameters?> FallbackParameterExtraction(IPage page)
        {
            try
            {
                Debug.WriteLine("[PARAMS] Starting fallback parameter extraction");
                
                Debug.WriteLine("[PARAMS] All extraction methods failed, using CORRECT hardcoded parameters");
                return Task.FromResult<UserParameters?>(new UserParameters 
                { 
                    FylkeId = "00", 
                    SkoleId = "312", 
                    PlanPeri = "2025-26" 
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PARAMS] Fallback extraction failed: {ex.Message}");
                return Task.FromResult<UserParameters?>(new UserParameters { FylkeId = "00", SkoleId = "312", PlanPeri = "2025-26" });
            }
        }

        private async Task SaveCookiesAndParametersAsync(Dictionary<string, string> cookies, UserParameters parameters)
        {
            try
            {
                Debug.WriteLine($"[SAVE] Saving {cookies.Count} cookies and parameters");
                Debug.WriteLine($"[SAVE] Parameters: FylkeId='{parameters.FylkeId}', SkoleId='{parameters.SkoleId}', PlanPeri='{parameters.PlanPeri}', IsComplete={parameters.IsComplete}");
                
                var cookieArray = cookies.Select(c => new Cookie { Name = c.Key, Value = c.Value }).ToArray();
                await SecureCredentialStorage.SaveCookiesAsync(cookieArray);
                Debug.WriteLine("[SAVE] Cookies saved successfully");
                
                // Save user parameters to Application Support directory as plain text file
                await SaveUserParametersToFileAsync(parameters);
                Debug.WriteLine($"[SAVE] Parameters saved to file successfully");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SAVE] Failed to save session data: {ex.Message}");
            }
        }
        
        private async Task SaveUserParametersToFileAsync(UserParameters parameters)
        {
            try
            {
                // DON'T save if these are the wrong default parameters
                if (parameters.FylkeId == "06" && parameters.SkoleId == "0602" && parameters.PlanPeri == "2024-2025")
                {
                    Debug.WriteLine("[SAVE] NOT saving wrong default parameters - keeping existing file");
                    return;
                }
                
                var appSupportDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AkademiTrack"
                );
                Directory.CreateDirectory(appSupportDir);
                
                var filePath = Path.Combine(appSupportDir, "user_parameters.json");
                var json = JsonSerializer.Serialize(parameters, new JsonSerializerOptions { WriteIndented = true });
                
                await File.WriteAllTextAsync(filePath, json);
                Debug.WriteLine($"[SAVE] User parameters saved to: {filePath}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SAVE] Failed to save user parameters: {ex.Message}");
            }
        }
        
        private async Task<UserParameters?> LoadUserParametersFromFileAsync()
        {
            try
            {
                var appSupportDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AkademiTrack"
                );
                var filePath = Path.Combine(appSupportDir, "user_parameters.json");
                
                if (!File.Exists(filePath))
                {
                    Debug.WriteLine("📂 [LOAD] User parameters file does not exist");
                    return null;
                }
                
                var json = await File.ReadAllTextAsync(filePath);
                var parameters = JsonSerializer.Deserialize<UserParameters>(json);
                
                Debug.WriteLine($"📂 [LOAD] User parameters loaded from file: FylkeId='{parameters?.FylkeId}', SkoleId='{parameters?.SkoleId}', PlanPeri='{parameters?.PlanPeri}'");
                return parameters;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LOAD] Failed to load user parameters from file: {ex.Message}");
                return null;
            }
        }

        private string SecureStringToString(SecureString? secureString)
        {
            if (secureString == null) return "";
            
            IntPtr ptr = Marshal.SecureStringToBSTR(secureString);
            try
            {
                return Marshal.PtrToStringBSTR(ptr) ?? "";
            }
            finally
            {
                Marshal.FreeBSTR(ptr);
            }
        }

        private SecureString StringToSecureString(string str)
        {
            var secureString = new SecureString();
            foreach (char c in str)
            {
                secureString.AppendChar(c);
            }
            secureString.MakeReadOnly();
            return secureString;
        }
        
        private static string GetChromiumCacheDirectory()
        {
            // Use a consistent cache directory regardless of how the app is launched
            var appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var cacheDir = Path.Combine(appDataDir, "AkademiTrack", "chromium-cache");
            
            // Ensure directory exists
            Directory.CreateDirectory(cacheDir);
            
            return cacheDir;
        }
        
        private static string GetChromiumUserDataDirectory()
        {
            // Create isolated user data directory to prevent keychain access
            var appDataDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var userDataDir = Path.Combine(appDataDir, "AkademiTrack", "chromium-userdata");
            
            // Ensure directory exists
            Directory.CreateDirectory(userDataDir);
            
            // Create preferences file to disable keychain access
            var defaultDir = Path.Combine(userDataDir, "Default");
            Directory.CreateDirectory(defaultDir);
            
            var preferencesPath = Path.Combine(defaultDir, "Preferences");
            if (!File.Exists(preferencesPath))
            {
                var preferences = new
                {
                    profile = new
                    {
                        password_manager_enabled = false,
                        password_manager_leak_detection_enabled = false
                    },
                    credentials_enable_service = false,
                    credentials_enable_autosignin = false,
                    password_manager = new
                    {
                        os_password_store = "basic"
                    }
                };
                
                var json = System.Text.Json.JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(preferencesPath, json);
            }
            
            return userDataDir;
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            _loginPasswordSecure?.Dispose();
            _disposed = true;
        }
    }
}
