using AkademiTrack.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using SeleniumExtras.WaitHelpers;
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

                // Need fresh login with Selenium
                Debug.WriteLine("[AUTH] Performing fresh login with Selenium");
                return await PerformSeleniumLoginAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AUTH] Authentication error: {ex.Message}");
                
                // Track authentication error
                try
                {
                    var analyticsService = Services.DependencyInjection.ServiceContainer.GetService<AnalyticsService>();
                    await analyticsService.LogErrorAsync(
                        "authentication_main_error",
                        ex.Message,
                        ex
                    );
                }
                catch (Exception analyticsEx)
                {
                    Debug.WriteLine($"[Analytics] Failed to log authentication error: {analyticsEx.Message}");
                }
                
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
                
                // Track credential loading failure
                try
                {
                    var analyticsService = Services.DependencyInjection.ServiceContainer.GetService<AnalyticsService>();
                    await analyticsService.LogErrorAsync(
                        "authentication_credential_load_failed",
                        ex.Message,
                        ex
                    );
                }
                catch (Exception analyticsEx)
                {
                    Debug.WriteLine($"[Analytics] Failed to log credential load error: {analyticsEx.Message}");
                }
                
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
                
                // Track cookie test failure
                try
                {
                    var analyticsService = Services.DependencyInjection.ServiceContainer.GetService<AnalyticsService>();
                    await analyticsService.LogErrorAsync(
                        "authentication_cookie_test_failed",
                        ex.Message,
                        ex
                    );
                }
                catch (Exception analyticsEx)
                {
                    Debug.WriteLine($"[Analytics] Failed to log cookie test error: {analyticsEx.Message}");
                }
                
                return false;
            }
        }

        private async Task<AuthenticationResult> PerformSeleniumLoginAsync()
        {
            // Run the entire Selenium automation on a background thread to avoid UI freezing
            return await Task.Run(async () =>
            {
                ChromeDriver? driver = null;

                try
                {
                    Debug.WriteLine("[SELENIUM] Starting Chrome browser on background thread");
                    
                    // Ensure ChromeDriver is available
                    var chromeDriverReady = await ChromeDriverManager.EnsureChromeDriverInstalledAsync();
                    if (!chromeDriverReady)
                    {
                        return new AuthenticationResult 
                        { 
                            Success = false, 
                            ErrorMessage = "Kunne ikke sette opp ChromeDriver. Vennligst start appen på nytt eller kontakt support." 
                        };
                    }

                    // Create Chrome driver (headless for production speed)
                    driver = ChromeDriverManager.CreateChromeDriver(headless: true);
                    
                    // Set timeouts
                    driver.Manage().Timeouts().ImplicitWait = TimeSpan.FromSeconds(10);
                    driver.Manage().Timeouts().PageLoad = TimeSpan.FromSeconds(30);

                    Debug.WriteLine("[SELENIUM] Navigating to login page");
                    driver.Navigate().GoToUrl("https://iskole.net/elev/?ojr=login");
                    Debug.WriteLine($"[SELENIUM] Current URL after navigation: {driver.Url}");
                    Debug.WriteLine($"[SELENIUM] Page title: {driver.Title}");
                    await Task.Delay(800); // Wait for page to load

                    // Click FEIDE button
                    Debug.WriteLine("[SELENIUM] Clicking FEIDE button");
                    var wait = new WebDriverWait(driver, TimeSpan.FromSeconds(15));
                    
                    // Wait for page to be fully loaded
                    wait.Until(d => ((IJavaScriptExecutor)d).ExecuteScript("return document.readyState")?.Equals("complete") == true);
                    
                    // Try multiple selectors for the FEIDE button
                    IWebElement? feideButton = null;
                    try
                    {
                        Debug.WriteLine("[SELENIUM] Trying XPath selector for FEIDE button...");
                        // Try XPath first
                        feideButton = wait.Until(d => d.FindElement(By.XPath("//button[.//span[contains(@class, 'feide_icon')]]")));
                        Debug.WriteLine("[SELENIUM] Found FEIDE button with XPath selector");
                    }
                    catch (Exception ex1)
                    {
                        Debug.WriteLine($"[SELENIUM] XPath selector failed: {ex1.Message}");
                        try
                        {
                            Debug.WriteLine("[SELENIUM] Trying text-based selector for FEIDE button...");
                            // Try alternative selector
                            feideButton = wait.Until(d => d.FindElement(By.XPath("//button[contains(text(), 'Feide') or contains(text(), 'FEIDE')]")));
                            Debug.WriteLine("[SELENIUM] Found FEIDE button with text selector");
                        }
                        catch (Exception ex2)
                        {
                            Debug.WriteLine($"[SELENIUM] Text selector failed: {ex2.Message}");
                            try
                            {
                                Debug.WriteLine("[SELENIUM] Trying CSS selector for FEIDE button...");
                                // Try CSS selector as last resort
                                feideButton = wait.Until(d => d.FindElement(By.CssSelector("button[class*='feide'], button[id*='feide']")));
                                Debug.WriteLine("[SELENIUM] Found FEIDE button with CSS selector");
                            }
                            catch (Exception ex3)
                            {
                                Debug.WriteLine($"[SELENIUM] CSS selector failed: {ex3.Message}");
                                // List all buttons on the page for debugging
                                var allButtons = driver.FindElements(By.TagName("button"));
                                Debug.WriteLine($"[SELENIUM] Found {allButtons.Count} buttons on the page:");
                                for (int i = 0; i < Math.Min(allButtons.Count, 10); i++)
                                {
                                    var btn = allButtons[i];
                                    Debug.WriteLine($"[SELENIUM] Button {i}: text='{btn.Text}', class='{btn.GetAttribute("class")}', id='{btn.GetAttribute("id")}'");
                                }
                                throw new Exception("Could not find FEIDE button with any selector");
                            }
                        }
                    }
                    
                    // Ensure feideButton was found
                    if (feideButton == null)
                    {
                        throw new Exception("Could not find FEIDE button with any selector");
                    }
                    
                    // Scroll to element and ensure it's visible
                    Debug.WriteLine("[SELENIUM] Scrolling to FEIDE button...");
                    ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView(true);", feideButton);
                    await Task.Delay(500); // Wait for scroll to complete
                    
                    // Check if element is displayed and enabled
                    Debug.WriteLine($"[SELENIUM] FEIDE button - Displayed: {feideButton.Displayed}, Enabled: {feideButton.Enabled}");
                    
                    // Wait for element to be clickable
                    Debug.WriteLine("[SELENIUM] Waiting for FEIDE button to be clickable...");
                    wait.Until(ExpectedConditions.ElementToBeClickable(feideButton));
                    
                    Debug.WriteLine("[SELENIUM] Clicking FEIDE button...");
                    feideButton.Click();
                    Debug.WriteLine($"[SELENIUM] Clicked FEIDE button, current URL: {driver.Url}");
                    
                    // Wait for navigation to complete
                    wait.Until(d => d.Url.Contains("feide.no") || d.Url.Contains("dataporten") || d.FindElements(By.Id("orglist")).Count > 0);

                    // Handle organization selection - click search input first to activate the list
                    Debug.WriteLine("[SELENIUM] Selecting organization from list");
                    
                    // Wait for the organization page to be fully loaded
                    wait.Until(d => d.FindElements(By.Id("org_selector_filter")).Count > 0);
                    await Task.Delay(500); // Reduced from 2000ms to 500ms
                    
                    // CLICK THE INPUT FIELD TO ACTIVATE THE SCHOOL LIST (same method as username/password)
                    Debug.WriteLine("[SELENIUM] Clicking search input to activate school list");
                    var searchInput = wait.Until(ExpectedConditions.ElementToBeClickable(By.Id("org_selector_filter")));
                    searchInput.Click();
                    Debug.WriteLine("[SELENIUM] ✅ Clicked search input");
                    await Task.Delay(300); // Reduced from 1000ms to 300ms
                    
                    // Find and click the school by matching org_name attribute (case-insensitive)
                    var schoolNameLower = _schoolName.ToLowerInvariant();
                    Debug.WriteLine($"[SELENIUM] Looking for school: '{_schoolName}' (normalized: '{schoolNameLower}')");
                    
                    // Get all school elements and find the matching one
                    var schoolElements = driver.FindElements(By.CssSelector("li.orglist_item[org_name]"));
                    Debug.WriteLine($"[SELENIUM] After clicking search input, found {schoolElements.Count} schools in the list");
                    
                    // If no schools found, try alternative selectors
                    if (schoolElements.Count == 0)
                    {
                        Debug.WriteLine("[SELENIUM] No schools found with org_name attribute, trying alternative selectors");
                        schoolElements = driver.FindElements(By.CssSelector("li.orglist_item"));
                        Debug.WriteLine($"[SELENIUM] Found {schoolElements.Count} schools with basic selector");
                        
                        if (schoolElements.Count == 0)
                        {
                            schoolElements = driver.FindElements(By.CssSelector("li[class*='org']"));
                            Debug.WriteLine($"[SELENIUM] Found {schoolElements.Count} schools with wildcard selector");
                        }
                    }
                    
                    bool schoolFound = false;
                    
                    Debug.WriteLine($"[SELENIUM] Found {schoolElements.Count} schools in the list");
                    
                    foreach (var element in schoolElements)
                    {
                        try
                        {
                            var orgName = element.GetAttribute("org_name");
                            Debug.WriteLine($"[SELENIUM] Checking school: '{orgName}'");
                            
                            if (!string.IsNullOrEmpty(orgName) && orgName.ToLowerInvariant().Contains(schoolNameLower))
                            {
                                Debug.WriteLine($"✅ [SELENIUM] Found matching school: '{orgName}' - clicking it");
                                
                                // Scroll to element and ensure it's visible
                                ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView(true);", element);
                                await Task.Delay(100); // Reduced from 300ms to 100ms
                                
                                // Wait for element to be clickable
                                wait.Until(ExpectedConditions.ElementToBeClickable(element));
                                element.Click();
                                schoolFound = true;
                                break;
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[SELENIUM] Error checking school element: {ex.Message}");
                            continue;
                        }
                    }
                    
                    if (!schoolFound)
                    {
                        Debug.WriteLine($"[SELENIUM] School '{_schoolName}' not found in list, trying alternative approach");
                        
                        // Try to find by text content instead of attribute
                        try
                        {
                            var schoolByText = driver.FindElement(By.XPath($"//li[@class='orglist_item' and contains(text(), '{_schoolName}')]"));
                            Debug.WriteLine($"✅ [SELENIUM] Found school by text content - clicking it");
                            
                            ((IJavaScriptExecutor)driver).ExecuteScript("arguments[0].scrollIntoView(true);", schoolByText);
                            await Task.Delay(100); // Reduced from 300ms to 100ms
                            wait.Until(ExpectedConditions.ElementToBeClickable(schoolByText));
                            schoolByText.Click();
                            schoolFound = true;
                        }
                        catch
                        {
                            Debug.WriteLine($"[SELENIUM] School not found by any method, falling back to search with typing");
                            // Last resort: type in search box
                            var searchBox = driver.FindElement(By.Id("org_selector_filter"));
                            searchBox.Clear();
                            searchBox.SendKeys(_schoolName);
                            await Task.Delay(200); // Reduced from 500ms to 200ms
                            
                            var matchingSchool = wait.Until(ExpectedConditions.ElementToBeClickable(By.CssSelector("li.orglist_item.match")));
                            matchingSchool.Click();
                            schoolFound = true;
                        }
                    }
                    
                    if (schoolFound)
                    {
                        // Small delay to ensure school selection is registered
                        await Task.Delay(200); // Reduced from 500ms to 200ms
                        Debug.WriteLine("➡️ [SELENIUM] Clicking Continue button to proceed with selected school");
                        var continueButton = wait.Until(ExpectedConditions.ElementToBeClickable(By.Id("selectorg_button")));
                        continueButton.Click();
                    }
                    
                    // Wait for navigation to login form
                    wait.Until(ExpectedConditions.ElementIsVisible(By.Id("username")));
                    await Task.Delay(300); // Reduced from 1000ms to 300ms

                    // Fill login form
                    Debug.WriteLine("[SELENIUM] Filling login form");
                    var usernameField = wait.Until(ExpectedConditions.ElementToBeClickable(By.Id("username")));
                    var passwordField = wait.Until(ExpectedConditions.ElementToBeClickable(By.Id("password")));
                    var submitButton = wait.Until(ExpectedConditions.ElementToBeClickable(By.CssSelector("button[type='submit']")));
                    
                    // Clear and fill username
                    usernameField.Clear();
                    usernameField.SendKeys(_loginEmail);
                    await Task.Delay(100); // Reduced from 200ms to 100ms
                    
                    // Clear and fill password
                    passwordField.Clear();
                    passwordField.SendKeys(SecureStringToString(_loginPasswordSecure));
                    await Task.Delay(100); // Reduced from 200ms to 100ms
                    
                    // Submit the form
                    submitButton.Click();

                    // Wait for success - check for successful login redirect
                    Debug.WriteLine("[SELENIUM] Waiting for login success...");
                    var loginWait = new WebDriverWait(driver, TimeSpan.FromSeconds(45));
                    
                    try
                    {
                        // Wait for either success URL or error condition
                        loginWait.Until(d => 
                            d.Url.Contains("isFeideinnlogget=true") || 
                            d.Url.Contains("error") ||
                            d.FindElements(By.CssSelector(".error, .alert-danger, [class*='error']")).Count > 0
                        );
                    }
                    catch (WebDriverTimeoutException)
                    {
                        Debug.WriteLine("[SELENIUM] Login timeout - checking current state");
                        Debug.WriteLine($"[SELENIUM] Current URL: {driver.Url}");
                        Debug.WriteLine($"[SELENIUM] Page title: {driver.Title}");
                    }
                    
                    var currentUrl = driver.Url;
                    Debug.WriteLine($"[SELENIUM] Final URL: {currentUrl}");
                    
                    if (currentUrl.Contains("isFeideinnlogget=true"))
                    {
                        Debug.WriteLine("[SELENIUM] Login successful");
                        
                        // Wait a bit for the page to fully load
                        await Task.Delay(500); // Reduced from 2000ms to 500ms
                        
                        // Extract cookies and parameters
                        var cookies = ExtractCookies(driver);
                        var parameters = await ExtractUserParametersAsync(driver);

                        if (cookies != null && parameters != null)
                        {
                            await SaveCookiesAndParametersAsync(cookies, parameters);
                            return AuthenticationResult.CreateSuccess(cookies, parameters);
                        }
                    }
                    else
                    {
                        // Check for error messages
                        var errorElements = driver.FindElements(By.CssSelector(".error, .alert-danger, [class*='error']"));
                        if (errorElements.Count > 0)
                        {
                            var errorText = errorElements.First().Text;
                            Debug.WriteLine($"[SELENIUM] Login error detected: {errorText}");
                            return AuthenticationResult.CreateFailed($"Login feilet: {errorText}");
                        }
                    }
                    
                    return AuthenticationResult.CreateFailed("Login feilet - kanskje feil passord eller brukernavn");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[SELENIUM] Error: {ex.Message}");
                    
                    // Track Selenium login failure
                    try
                    {
                        var analyticsService = Services.DependencyInjection.ServiceContainer.GetService<AnalyticsService>();
                        await analyticsService.LogErrorAsync(
                            "authentication_selenium_login_failed",
                            ex.Message,
                            ex
                        );
                    }
                    catch (Exception analyticsEx)
                    {
                        Debug.WriteLine($"[Analytics] Failed to log Selenium error: {analyticsEx.Message}");
                    }
                    
                    // Take a screenshot for debugging
                    try
                    {
                        if (driver != null)
                        {
                            var screenshot = ((ITakesScreenshot)driver).GetScreenshot();
                            var screenshotPath = Path.Combine(Path.GetTempPath(), $"selenium_error_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                            screenshot.SaveAsFile(screenshotPath);
                            Debug.WriteLine($"[SELENIUM] Screenshot saved to: {screenshotPath}");
                        }
                    }
                    catch (Exception screenshotEx)
                    {
                        Debug.WriteLine($"[SELENIUM] Failed to take screenshot: {screenshotEx.Message}");
                    }
                    
                    return AuthenticationResult.CreateFailed($"Browser login error: {ex.Message}");
                }
                finally
                {
                    driver?.Quit();
                    driver?.Dispose();
                }
            });
        }

        private Dictionary<string, string>? ExtractCookies(ChromeDriver driver)
        {
            try
            {
                var cookies = driver.Manage().Cookies.AllCookies;
                return cookies
                    .Where(c => c.Domain?.Contains("iskole.net") == true)
                    .ToDictionary(c => c.Name, c => c.Value);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Cookie extraction failed: {ex.Message}");
                return null;
            }
        }

        private async Task<UserParameters?> ExtractUserParametersAsync(ChromeDriver driver)
        {
            try
            {
                Debug.WriteLine("[PARAMS] Starting parameter extraction from VoUserData API");
                
                // Get cookies from the driver
                var cookies = driver.Manage().Cookies.AllCookies;
                var cookieDict = cookies.Where(c => c.Domain?.Contains("iskole.net") == true)
                                       .ToDictionary(c => c.Name, c => c.Value);
                
                var jsessionId = cookieDict.GetValueOrDefault("JSESSIONID", "");
                if (string.IsNullOrEmpty(jsessionId))
                {
                    Debug.WriteLine("[PARAMS] No JSESSIONID found");
                    return FallbackParameterExtraction();
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
                    return FallbackParameterExtraction();
                }
                
                // Parse the user data response to extract parameters
                var parameters = ExtractParametersFromUserData(content);
                
                if (parameters != null && parameters.IsComplete)
                {
                    Debug.WriteLine($"[PARAMS] Successfully extracted from VoUserData: FylkeId='{parameters.FylkeId}', SkoleId='{parameters.SkoleId}', PlanPeri='{parameters.PlanPeri}'");
                    return parameters;
                }
                
                Debug.WriteLine("[PARAMS] VoUserData didn't provide complete parameters, trying fallback");
                return FallbackParameterExtraction();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PARAMS] VoUserData extraction failed: {ex.Message}");
                Debug.WriteLine($"[PARAMS] Stack trace: {ex.StackTrace}");
                return FallbackParameterExtraction();
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

        private UserParameters? FallbackParameterExtraction()
        {
            try
            {
                Debug.WriteLine("[PARAMS] Starting fallback parameter extraction");
                
                Debug.WriteLine("[PARAMS] All extraction methods failed, using CORRECT hardcoded parameters");
                return new UserParameters 
                { 
                    FylkeId = "00", 
                    SkoleId = "312", 
                    PlanPeri = "2025-26" 
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[PARAMS] Fallback extraction failed: {ex.Message}");
                return new UserParameters { FylkeId = "00", SkoleId = "312", PlanPeri = "2025-26" };
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
        
        public void Dispose()
        {
            if (_disposed) return;
            
            _loginPasswordSecure?.Dispose();
            _disposed = true;
        }
    }
}
