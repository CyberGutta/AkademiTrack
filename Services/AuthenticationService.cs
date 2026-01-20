using AkademiTrack.Services.Interfaces;
using AkademiTrack.ViewModels;
using Avalonia.Threading;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text.Json;
using System.Threading.Tasks;

namespace AkademiTrack.Services
{
    public class AuthenticationService : IDisposable
    {
        private IWebDriver? _webDriver;
        private string _loginEmail = "";
        private SecureString? _loginPasswordSecure;
        private string _schoolName = "";
        private Dictionary<string, string>? _cachedCookies;
        private bool _credentialsWereRejected = false;
        private static DateTime? _lastMissingCredentialsNotification;
        private static DateTime? _lastInvalidCredentialsNotification;
        private static readonly TimeSpan NotificationCooldown = TimeSpan.FromMinutes(5);
        private readonly INotificationService? _notificationService;
        private readonly bool _suppressNotifications;
        private bool _disposed = false;

        public AuthenticationService(INotificationService? notificationService = null, bool suppressNotifications = false)
        {
            _notificationService = notificationService;
            _suppressNotifications = suppressNotifications;
        }

        private bool ShouldShowNotification(ref DateTime? lastShown)
        {
            if (lastShown == null || DateTime.Now - lastShown.Value > NotificationCooldown)
            {
                lastShown = DateTime.Now;
                return true;
            }
            return false;
        }

        public async Task<AuthenticationResult> AuthenticateAsync()
        {
            try
            {
                // Reset credentials rejected flag at start of new authentication attempt
                _credentialsWereRejected = false;
                
                // Load credentials
                _loginEmail = await SecureCredentialStorage.GetCredentialAsync("LoginEmail") ?? "";
                var passwordPlain = await SecureCredentialStorage.GetCredentialAsync("LoginPassword") ?? "";
                try
                {
                    _loginPasswordSecure = StringToSecureString(passwordPlain);
                }
                finally
                {
                    // Immediately clear plaintext password from memory
                    passwordPlain = null;
                }
                _schoolName = await SecureCredentialStorage.GetCredentialAsync("SchoolName") ?? "";

                if (_cachedCookies == null)
                {
                    _cachedCookies = await SecureCredentialStorage.LoadCookiesAsync();
                }
                
                if (_cachedCookies != null)
                {
                    Debug.WriteLine("[AUTH] Found cached cookies, testing validity...");
                    if (await TestCookiesAsync(_cachedCookies))
                    {
                        Debug.WriteLine("[AUTH] ✓ Cached cookies are valid!");
                        var parameters = await ExtractParametersFromCookies(_cachedCookies);
                        
                        if (parameters != null && parameters.IsComplete)
                        {
                            Debug.WriteLine("[AUTH] ✓ Parameters are complete - returning success without browser login");
                            return new AuthenticationResult
                            {
                                Success = true,
                                Cookies = _cachedCookies,
                                Parameters = parameters
                            };
                        }
                        else
                        {
                            Debug.WriteLine("[AUTH] ⚠️ Parameters missing or incomplete - will need browser login");
                        }
                    }
                    else
                    {
                        Debug.WriteLine("[AUTH] ❌ Cached cookies are invalid - will need fresh login");
                        _cachedCookies = null;
                    }
                }

                // Need fresh login
                bool hasCredentials = !string.IsNullOrEmpty(_loginEmail) &&
                                    _loginPasswordSecure != null && _loginPasswordSecure.Length > 0 &&
                                    !string.IsNullOrEmpty(_schoolName);

                Debug.WriteLine($"[AUTH] Credential check - Email: {(!string.IsNullOrEmpty(_loginEmail) ? "✓" : "✗")}, Password: {(_loginPasswordSecure != null && _loginPasswordSecure.Length > 0 ? "✓" : "✗")}, School: {(!string.IsNullOrEmpty(_schoolName) ? "✓" : "✗")}");

                if (!hasCredentials)
                {
                    Debug.WriteLine("[AUTH] ❌ Missing credentials - cannot proceed with authentication");

                    try
                    {
                        var settings = await SafeSettingsLoader.LoadSettingsWithAutoRepairAsync();
                        if (settings.InitialSetupCompleted && ShouldShowNotification(ref _lastMissingCredentialsNotification))
                        {
                            if (_notificationService != null)
                            {
                                await _notificationService.ShowNotificationAsync(
                                    "Mangler Innloggingsdata",
                                    "Gå til innstillinger og legg inn Feide-brukernavn, passord og skolenavn.",
                                    NotificationLevel.Warning,
                                    isHighPriority: true
                                );
                            }
                        }
                        else
                        {
                            Debug.WriteLine("[AUTH] Initial setup not completed - suppressing notification");
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[AUTH] Could not check initial setup status: {ex.Message}");
                        // Don't show notification if we can't determine setup status
                    }

                    return new AuthenticationResult
                    {
                        Success = false,
                        ErrorMessage = "Mangler innloggingsdata. Vennligst gå til innstillinger og legg inn Feide-brukernavn, passord og skolenavn."
                    };
                }


                var loginResult = await PerformBrowserLoginAsync(hasCredentials);
                
                if (loginResult.Success)
                {
                    _cachedCookies = loginResult.Cookies;
                    return loginResult;
                }
                
                // Return the specific error message from login attempt
                return new AuthenticationResult {   
                    Success = false, 
                    ErrorMessage = loginResult.ErrorMessage ?? "ERROR: Sjekk om du er på et gyldig nettverk" 

                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[AUTH] Authentication error: {ex.Message}");
                return new AuthenticationResult 
                { 
                    Success = false, 
                    ErrorMessage = $"Teknisk feil: {ex.Message}" 
                };
            }
            finally
            {
                await CleanupWebDriverAsync();
            }
        }

        private async Task<bool> TestCookiesAsync(Dictionary<string, string> cookies)
        {
            return await Task.Run(async () =>
            {
                try
                {
                    using var httpClient = new System.Net.Http.HttpClient();
                    httpClient.Timeout = TimeSpan.FromSeconds(10);

                    var jsessionId = cookies.GetValueOrDefault("JSESSIONID", "");
                    var url = $"https://iskole.net/iskole_elev/rest/v0/VoTimeplan_elev_oppmote;jsessionid={jsessionId}";
                    url += "?finder=RESTFilter;fylkeid=00,planperi=2025-26,skoleid=312&onlyData=true&limit=1";

                    var request = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Get, url);
                    var cookieString = string.Join("; ", cookies.Select(c => $"{c.Key}={c.Value}"));
                    request.Headers.Add("Cookie", cookieString);

                    var response = await httpClient.SendAsync(request);
                    return response.IsSuccessStatusCode;
                }
                catch
                {
                    return false;
                }
            });
        }

        private async Task<UserParameters?> ExtractParametersFromCookies(Dictionary<string, string> cookies)
        {
            try
            {
                var paramFile = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AkademiTrack",
                    "user_parameters.json"
                );

                if (System.IO.File.Exists(paramFile))
                {
                    var json = await System.IO.File.ReadAllTextAsync(paramFile);
                    var savedData = JsonSerializer.Deserialize<SavedParameterData>(json);
                    
                    if (savedData?.Parameters != null && IsCurrentSchoolYear(savedData.Parameters.PlanPeri))
                    {
                        return savedData.Parameters;
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private async Task<AuthenticationResult> PerformBrowserLoginAsync(bool hasCredentials)
        {
            // Wrap entire Selenium operation in Task.Run to prevent UI blocking
            return await Task.Run(async () =>
            {
                try
                {
                    var options = new ChromeOptions();
                    options.AddArgument("--no-sandbox");
                    options.AddArgument("--disable-dev-shm-usage");
                    options.AddArgument("--disable-blink-features=AutomationControlled");
                    options.AddExcludedArgument("enable-automation");
                    options.AddArgument("--disable-gpu");
                    options.AddArgument("--headless");
                    options.AddArgument("--window-size=1920,1080");

                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        var chromeDriverService = ChromeDriverService.CreateDefaultService();
                        chromeDriverService.HideCommandPromptWindow = true;
                        _webDriver = new ChromeDriver(chromeDriverService, options);
                    }
                    else
                    {
                        _webDriver = new ChromeDriver(options);
                    }

                    _webDriver.Navigate().GoToUrl("https://iskole.net/elev/?ojr=login");
                    await Task.Delay(2000);

                    bool loginSuccess = false;

                    if (hasCredentials)
                    {
                        try
                        {
                            Debug.WriteLine("[AUTH] Starting PerformFastAutomaticLoginAsync...");
                            loginSuccess = await PerformFastAutomaticLoginAsync();
                            Debug.WriteLine($"[AUTH] PerformFastAutomaticLoginAsync returned: {loginSuccess}");
                        }
                        catch (InvalidOperationException ex) when (ex.Message == "INVALID_CREDENTIALS")
                        {
                            Debug.WriteLine("[AUTH] ❌ INVALID CREDENTIALS DETECTED - returning failure immediately");
                            return new AuthenticationResult { Success = false };
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[AUTH] Auto-login failed with exception: {ex.Message}");
                            loginSuccess = false;
                        }
                    }

                    if (!loginSuccess)
                    {
                        Debug.WriteLine($"[AUTH] Login was not successful. _credentialsWereRejected = {_credentialsWereRejected}");

                        if (_credentialsWereRejected)
                        {
                            Debug.WriteLine("[AUTH] ❌ CREDENTIALS WERE REJECTED - returning failure immediately");

                            // Note: Notifications must be shown on UI thread
                            if (ShouldShowNotification(ref _lastInvalidCredentialsNotification))
                            {
                                if (_notificationService != null)
                                {
                                    // Schedule notification on UI thread
                                    _ = Dispatcher.UIThread.InvokeAsync(async () =>
                                    {
                                        await _notificationService.ShowNotificationAsync(
                                            "Innlogging Feilet",
                                            "Feil brukernavn eller passord. Sjekk dine Feide-innloggingsdata.",
                                            NotificationLevel.Error,
                                            isHighPriority: true
                                        );
                                    });
                                }
                            }

                            return new AuthenticationResult
                            {
                                Success = false,
                                ErrorMessage = "Feil brukernavn eller passord. Vennligst sjekk dine Feide-innloggingsdata og prøv igjen."
                            };
                        }

                        Debug.WriteLine("[AUTH] ❌ Auto-login failed and no manual login allowed - returning failure");
                        return new AuthenticationResult
                        {
                            Success = false,
                            ErrorMessage = "Innlogging mislyktes. Sjekk nettverksforbindelse og prøv igjen."
                        };
                    }

                    var parameters = await QuickParameterCapture();
                    var cookies = _webDriver.Manage().Cookies.AllCookies;
                    var cookieDict = cookies.ToDictionary(c => c.Name, c => c.Value);

                    await SecureCredentialStorage.SaveCookiesAsync(
                        cookies.Select(c => new Cookie { Name = c.Name, Value = c.Value }).ToArray()
                    );

                    if (parameters != null)
                    {
                        await SaveParametersAsync(parameters);
                    }

                    return new AuthenticationResult
                    {
                        Success = true,
                        Cookies = cookieDict,
                        Parameters = parameters
                    };
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[AUTH] Login error: {ex.Message}");
                    return new AuthenticationResult { Success = false };
                }
            });
        }

        private async Task<bool> PerformFastAutomaticLoginAsync()
        {

            try
            {
                if (_webDriver == null) return false;

                var wait = new WebDriverWait(_webDriver, TimeSpan.FromSeconds(10));

                // Click FEIDE button
                try
                {
                    var feideButton = wait.Until(driver =>
                    {
                        var selectors = new[]
                        {
                    "//span[contains(@class, 'feide_icon')]/ancestor::button",
                    "//button[contains(., 'FEIDE')]",
                    "//button[contains(@class, 'feide')]"
                };

                        foreach (var selector in selectors)
                        {
                            try
                            {
                                var btn = driver.FindElement(By.XPath(selector));
                                if (btn.Displayed && btn.Enabled) return btn;
                            }
                            catch { }
                        }
                        return null;
                    });

                    if (feideButton == null) return false;

                    feideButton.Click();
                    await Task.Delay(1500);
                }
                catch
                {
                    return false;
                }

                // Handle organization selection
                if (!await HandleFastOrganizationSelectionAsync())
                {
                    return false;
                }

                // Fill login form
                Debug.WriteLine("[AUTH] Calling HandleFastFeideLoginFormAsync...");
                var loginResult = await HandleFastFeideLoginFormAsync();
                Debug.WriteLine($"[AUTH] HandleFastFeideLoginFormAsync returned: {loginResult}");
                return loginResult;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> HandleFastOrganizationSelectionAsync()
        {
            try
            {
                if (_webDriver == null) return false;

                var wait = new WebDriverWait(_webDriver, TimeSpan.FromSeconds(10));

                try
                {
                    var orgSearchField = wait.Until(driver =>
                    {
                        try
                        {
                            var element = driver.FindElement(By.Id("org_selector_filter"));
                            return element.Displayed ? element : null;
                        }
                        catch
                        {
                            return null;
                        }
                    });

                    if (orgSearchField != null && !string.IsNullOrEmpty(_schoolName))
                    {
                        orgSearchField.Clear();
                        await Task.Delay(300);
                        orgSearchField.SendKeys(_schoolName);
                        await Task.Delay(800);

                        IWebElement? schoolOption = null;
                        
                        var schoolNameLower = _schoolName.ToLower();
                        var fastSelector = $"//li[contains(@class, 'orglist_item') and @org_name='{schoolNameLower}']";
                        var primarySelector = $"//li[contains(@class, 'orglist_item') and .//span[@class='orglist_name' and text()='{_schoolName}']]";
                        
                        var selectors = new[]
                        {
                            fastSelector,
                            primarySelector,
                            $"//span[@class='orglist_name' and text()='{_schoolName}']/ancestor::li[contains(@class, 'orglist_item')]"
                        };

                        foreach (var selector in selectors)
                        {
                            try
                            {
                                schoolOption = wait.Until(driver =>
                                {
                                    try
                                    {
                                        var elems = driver.FindElements(By.XPath(selector));
                                        foreach (var elem in elems)
                                        {
                                            var style = elem.GetAttribute("style") ?? "";
                                            if (elem.Displayed && !style.Contains("display: none"))
                                            {
                                                return elem;
                                            }
                                        }
                                    }
                                    catch { }
                                    return null;
                                });

                                if (schoolOption != null)
                                    break;
                            }
                            catch { }
                        }

                        if (schoolOption == null) return false;

                        try
                        {
                            schoolOption.Click();
                        }
                        catch
                        {
                            var jsExecutor = (IJavaScriptExecutor)_webDriver;
                            jsExecutor.ExecuteScript("arguments[0].click();", schoolOption);
                        }
                        
                        await Task.Delay(400);

                        var continueButton = wait.Until(driver =>
                        {
                            try
                            {
                                var btn = driver.FindElement(By.Id("selectorg_button"));
                                var isDisabled = btn.GetAttribute("disabled");
                                if (btn.Displayed && isDisabled == null)
                                {
                                    return btn;
                                }
                            }
                            catch { }
                            return null;
                        });

                        if (continueButton == null) return false;

                        try
                        {
                            continueButton.Click();
                        }
                        catch
                        {
                            var jsExecutor = (IJavaScriptExecutor)_webDriver;
                            jsExecutor.ExecuteScript("arguments[0].click();", continueButton);
                        }
                        
                        await Task.Delay(1500);
                    }
                    else
                    {
                        return false;
                    }
                }
                catch
                {
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> HandleFastFeideLoginFormAsync()
        {
            try
            {
                if (_webDriver == null) return false;

                var wait = new WebDriverWait(_webDriver, TimeSpan.FromSeconds(10));

                var usernameField = wait.Until(driver =>
                {
                    try
                    {
                        var field = driver.FindElement(By.Id("username"));
                        return field.Displayed ? field : null;
                    }
                    catch
                    {
                        return null;
                    }
                });

                if (usernameField == null) return false;

                var passwordField = _webDriver.FindElement(By.Id("password"));
                if (passwordField == null) return false;

                usernameField.Clear();
                await Task.Delay(200);
                usernameField.SendKeys(_loginEmail);

                passwordField.Clear();
                await Task.Delay(200);
                var passwordPlain = SecureStringToString(_loginPasswordSecure);
                try
                {
                    passwordField.SendKeys(passwordPlain);
                }
                finally
                {
                    // Immediately clear plaintext password from memory
                    passwordPlain = null;
                }

                await Task.Delay(500);
                passwordField.SendKeys(Keys.Enter);
                
                Debug.WriteLine("[AUTH] ========== LOGIN FORM SUBMITTED ==========");
                Debug.WriteLine("[AUTH] Waiting for Feide response...");
                
                // Wait a bit for the page to process the login
                await Task.Delay(2000);

                for (int i = 0; i < 25; i++)
                {
                    await Task.Delay(1000);
                    var currentUrl = _webDriver.Url;
                    
                    Debug.WriteLine($"[AUTH] Checking login status... attempt {i + 1}/25");
                    Debug.WriteLine($"[AUTH] Current URL: {currentUrl}");
                    
                    // Check for success first
                    if (currentUrl.Contains("isFeideinnlogget=true") || 
                        currentUrl.Contains("ojr=timeplan"))
                    {
                        Debug.WriteLine("[AUTH] ✓ SUCCESS: Login successful - found success URL");
                        return true;
                    }
                    
                    // DEBUG: After 5 seconds, dump page content to see what's actually there
                    if (i == 4) // 5th attempt (5 seconds)
                    {
                        try
                        {
                            var pageSource = _webDriver.PageSource;
                            Debug.WriteLine("[AUTH] ========== PAGE SOURCE DUMP (after 5 seconds) ==========");
                            Debug.WriteLine($"[AUTH] Page title: {_webDriver.Title}");
                            Debug.WriteLine($"[AUTH] Current URL: {currentUrl}");
                            
                            if (pageSource.Contains("Innlogging feilet"))
                            {
                                Debug.WriteLine("[AUTH] ❌ FOUND 'Innlogging feilet' in page source!");
                                var startIndex = Math.Max(0, pageSource.IndexOf("Innlogging feilet") - 200);
                                var endIndex = Math.Min(pageSource.Length, pageSource.IndexOf("Innlogging feilet") + 200);
                                var context = pageSource.Substring(startIndex, endIndex - startIndex);
                                Debug.WriteLine($"[AUTH] Context around error: {context}");
                            }
                            else
                            {
                                Debug.WriteLine("[AUTH] No 'Innlogging feilet' found in page source");
                            }
                            
                            // Check for dialog elements
                            if (pageSource.Contains("dialog"))
                            {
                                Debug.WriteLine("[AUTH] Found 'dialog' in page source");
                            }
                            
                            if (pageSource.Contains("error"))
                            {
                                Debug.WriteLine("[AUTH] Found 'error' in page source");
                            }
                            
                            Debug.WriteLine("[AUTH] ========== END PAGE SOURCE DUMP ==========");
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[AUTH] Error dumping page source: {ex.Message}");
                        }
                    }
                    
                    // Check for Feide login error dialog using the exact structure provided by user
                    // <div class="dialog panel error" data-type="panel error">
                    try
                    {
                        // First, check if page source contains the error text (fastest check)
                        var pageSource = _webDriver.PageSource;
                        if (pageSource.Contains("Innlogging feilet"))
                        {
                            Debug.WriteLine("[AUTH] ❌ CONFIRMED: Found 'Innlogging feilet' in page source - INVALID CREDENTIALS!");
                            Debug.WriteLine("[AUTH] ❌ RETURNING FALSE FROM HandleFastFeideLoginFormAsync");
                            _credentialsWereRejected = true;
                            return false;
                        }
                        
                        // Try multiple CSS selectors to catch the error dialog
                        var selectors = new[]
                        {
                            "div.dialog.panel.error",           // Main selector
                            "div[data-type='panel error']",     // Data attribute selector
                            "div.dialog.error",                 // Simplified selector
                            ".dialog.panel.error",              // Class-based selector
                            ".error",                           // Generic error class
                            "[class*='error']"                  // Any element with 'error' in class
                        };
                        
                        foreach (var selector in selectors)
                        {
                            try
                            {
                                var errorElements = _webDriver.FindElements(By.CssSelector(selector));
                                if (errorElements.Count > 0)
                                {
                                    Debug.WriteLine($"[AUTH] Found {errorElements.Count} potential error element(s) using selector: {selector}");
                                    
                                    foreach (var errorElement in errorElements)
                                    {
                                        if (errorElement.Displayed)
                                        {
                                            var elementText = errorElement.Text;
                                            Debug.WriteLine($"[AUTH] Error element text: '{elementText}'");
                                            
                                            if (elementText.Contains("Innlogging feilet") || 
                                                elementText.Contains("feil brukernavn eller passord") ||
                                                elementText.Contains("Dette kan skyldes feil brukernavn"))
                                            {
                                                Debug.WriteLine("[AUTH] ❌ CONFIRMED: Error text detected - INVALID CREDENTIALS!");
                                                _credentialsWereRejected = true;
                                                return false;
                                            }
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[AUTH] Selector '{selector}' failed: {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[AUTH] Error checking for error dialog: {ex.Message}");
                    }
                    
                    // Also check for any element containing "Innlogging feilet" text (fallback)
                    try
                    {
                        var errorElements = _webDriver.FindElements(By.XPath("//*[contains(text(), 'Innlogging feilet')]"));
                        if (errorElements.Count > 0)
                        {
                            Debug.WriteLine($"[AUTH] Found {errorElements.Count} element(s) with 'Innlogging feilet' text");
                            
                            foreach (var element in errorElements)
                            {
                                if (element.Displayed)
                                {
                                    Debug.WriteLine($"[AUTH] ❌ CONFIRMED: Found visible 'Innlogging feilet' text - INVALID CREDENTIALS!");
                                    Debug.WriteLine($"[AUTH] Element tag: {element.TagName}, text: '{element.Text}'");
                                    _credentialsWereRejected = true;
                                    return false;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[AUTH] Error checking for 'Innlogging feilet' text: {ex.Message}");
                    }
                    
                    // Check for h2 with "Innlogging feilet" (specific to the error structure)
                    try
                    {
                        var h2Elements = _webDriver.FindElements(By.XPath("//h2[contains(text(), 'Innlogging feilet')]"));
                        if (h2Elements.Count > 0)
                        {
                            Debug.WriteLine($"[AUTH] Found {h2Elements.Count} h2 element(s) with 'Innlogging feilet'");
                            
                            foreach (var h2 in h2Elements)
                            {
                                if (h2.Displayed)
                                {
                                    Debug.WriteLine($"[AUTH] ❌ CONFIRMED: Found h2 with 'Innlogging feilet' - INVALID CREDENTIALS!");
                                    _credentialsWereRejected = true;
                                    return false;
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[AUTH] Error checking for h2 'Innlogging feilet': {ex.Message}");
                    }
                    
                    Debug.WriteLine($"[AUTH] No success or error detected yet, continuing to wait...");
                }

                Debug.WriteLine("[AUTH] ❌ LOGIN TIMEOUT - no success or error detected after 25 attempts");
                return false;
            }
            catch
            {
                return false;
            }
        }

        private async Task<bool> WaitForTargetUrlAsync()
        {
            var timeout = DateTime.Now.AddMinutes(10);

            while (DateTime.Now < timeout)
            {
                try
                {
                    if (_webDriver == null) return false;

                    var currentUrl = _webDriver.Url;
                    
                    if (currentUrl.Contains("isFeideinnlogget=true") && currentUrl.Contains("ojr=timeplan"))
                    {
                        return true;
                    }
                }
                catch
                {
                    return false;
                }

                await Task.Delay(2000);
            }

            return false;
        }

        private async Task<UserParameters?> QuickParameterCapture()
        {
            try
            {
                if (_webDriver == null) return null;

                return await Task.Run(() =>
                {
                    var jsExecutor = (IJavaScriptExecutor)_webDriver;
                    var result = jsExecutor.ExecuteScript(@"
                try {
                    var entries = performance.getEntries();
                    for (var i = 0; i < entries.length; i++) {
                        var entry = entries[i];
                        if (entry.name && entry.name.includes('fylkeid=')) {
                            var match = entry.name.match(/fylkeid=([^,&]+)[^,]*,planperi=([^,&]+)[^,]*,skoleid=([^,&]+)/);
                            if (match && match.length >= 4) {
                                return {
                                    fylkeid: match[1],
                                    planperi: match[2], 
                                    skoleid: match[3]
                                };
                            }
                        }
                    }
                    return null;
                } catch (e) {
                    return null;
                }
            ");

                    if (result is Dictionary<string, object> resultDict &&
                        resultDict.ContainsKey("fylkeid"))
                    {
                        return new UserParameters
                        {
                            FylkeId = resultDict["fylkeid"]?.ToString(),
                            PlanPeri = resultDict["planperi"]?.ToString(),
                            SkoleId = resultDict["skoleid"]?.ToString()
                        };
                    }

                    return null;
                });
            }
            catch
            {
                return null;
            }
        }

        private async Task SaveParametersAsync(UserParameters parameters)
        {
            try
            {
                var saveData = new SavedParameterData
                {
                    Parameters = parameters,
                    SavedAt = DateTime.Now,
                    SchoolYear = parameters.PlanPeri
                };

                var json = JsonSerializer.Serialize(saveData);
                var paramFile = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "AkademiTrack",
                    "user_parameters.json"
                );

                await System.IO.File.WriteAllTextAsync(paramFile, json);
            }
            catch { }
        }

        private bool IsCurrentSchoolYear(string? planPeri)
        {
            try
            {
                var now = DateTime.Now;
                var schoolYearStart = now.Month >= 8 ? now.Year : now.Year - 1;
                var currentSchoolYear = $"{schoolYearStart}-{(schoolYearStart + 1).ToString().Substring(2)}";
                return planPeri == currentSchoolYear;
            }
            catch
            {
                return false;
            }
        }

        private async Task CleanupWebDriverAsync()
        {
            try
            {
                if (_webDriver != null)
                {
                    try
                    {
                        _webDriver.Quit();
                    }
                    catch { }
                    
                    _webDriver.Dispose();
                    _webDriver = null;
                }
            }
            catch { }

            await Task.Delay(500);
        }

        private static SecureString StringToSecureString(string str)
        {
            if (string.IsNullOrEmpty(str))
                return new SecureString();

            var secure = new SecureString();
            foreach (char c in str)
            {
                secure.AppendChar(c);
            }
            secure.MakeReadOnly();
            return secure;
        }

        private static string SecureStringToString(SecureString? secure)
        {
            if (secure == null || secure.Length == 0)
                return string.Empty;

            IntPtr ptr = IntPtr.Zero;
            try
            {
                ptr = System.Runtime.InteropServices.Marshal.SecureStringToBSTR(secure);
                return System.Runtime.InteropServices.Marshal.PtrToStringBSTR(ptr) ?? string.Empty;
            }
            finally
            {
                if (ptr != IntPtr.Zero)
                {
                    System.Runtime.InteropServices.Marshal.ZeroFreeBSTR(ptr);
                }
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            
            try
            {
                _loginPasswordSecure?.Dispose();
                _loginPasswordSecure = null;
                
                if (_webDriver != null)
                {
                    try
                    {
                        _webDriver.Quit();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error quitting WebDriver: {ex.Message}");
                    }
                    
                    try
                    {
                        _webDriver.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Error disposing WebDriver: {ex.Message}");
                    }
                    
                    _webDriver = null;
                }
                
                _disposed = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in AuthenticationService.Dispose: {ex.Message}");
            }
        }
    }
}