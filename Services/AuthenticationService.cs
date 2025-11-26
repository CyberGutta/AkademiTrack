using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using OpenQA.Selenium.Support.UI;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security;

namespace AkademiTrack.Services
{
    public class AuthenticationService
    {
        private IWebDriver? _webDriver;
        private string _loginEmail = "";
        private SecureString? _loginPasswordSecure;
        private string _schoolName = "";
        private Dictionary<string, string>? _cachedCookies;

        public async Task<AuthenticationResult> AuthenticateAsync()
        {
            try
            {
                // Load credentials
                _loginEmail = await SecureCredentialStorage.GetCredentialAsync("LoginEmail") ?? "";
                var passwordPlain = await SecureCredentialStorage.GetCredentialAsync("LoginPassword") ?? "";
                _loginPasswordSecure = StringToSecureString(passwordPlain);
                passwordPlain = null;
                _schoolName = await SecureCredentialStorage.GetCredentialAsync("SchoolName") ?? "";

                // Try loading existing cookies ONCE and cache them
                if (_cachedCookies == null)
                {
                    _cachedCookies = await SecureCredentialStorage.LoadCookiesAsync();
                }
                
                if (_cachedCookies != null)
                {
                    if (await TestCookiesAsync(_cachedCookies))
                    {
                        var parameters = await ExtractParametersFromCookies(_cachedCookies);
                        
                        if (parameters != null && parameters.IsComplete)
                        {
                            return new AuthenticationResult
                            {
                                Success = true,
                                Cookies = _cachedCookies,
                                Parameters = parameters
                            };
                        }
                    }
                    else
                    {
                        _cachedCookies = null;
                    }
                }

                // Need fresh login
                bool hasCredentials = !string.IsNullOrEmpty(_loginEmail) &&
                                    _loginPasswordSecure != null && _loginPasswordSecure.Length > 0 &&
                                    !string.IsNullOrEmpty(_schoolName);

                var loginResult = await PerformBrowserLoginAsync(hasCredentials);
                
                if (loginResult.Success)
                {
                    _cachedCookies = loginResult.Cookies;
                    return loginResult;
                }
                
                return new AuthenticationResult { Success = false };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Authentication error: {ex.Message}");
                return new AuthenticationResult { Success = false };
            }
            finally
            {
                await CleanupWebDriverAsync();
            }
        }

        private async Task<bool> TestCookiesAsync(Dictionary<string, string> cookies)
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
            try
            {
                var options = new ChromeOptions();
                options.AddArgument("--no-sandbox");
                options.AddArgument("--disable-dev-shm-usage");
                options.AddArgument("--disable-blink-features=AutomationControlled");
                options.AddExcludedArgument("enable-automation");
                options.AddArgument("--disable-gpu");

                if (hasCredentials)
                {
                    
                    options.AddArgument("--window-size=1920,1080");
                }
                else
                {
                    options.AddArgument("--start-maximized");
                }

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
                        loginSuccess = await PerformFastAutomaticLoginAsync();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Auto-login failed: {ex.Message}");
                        loginSuccess = false;
                    }
                }

                if (!loginSuccess)
                {
                    if (!await WaitForTargetUrlAsync())
                    {
                        return new AuthenticationResult { Success = false };
                    }
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
                Debug.WriteLine($"Login error: {ex.Message}");
                return new AuthenticationResult { Success = false };
            }
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
                var loginResult = await HandleFastFeideLoginFormAsync();
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
                passwordField.SendKeys(passwordPlain);
                passwordPlain = null;

                await Task.Delay(500);
                passwordField.SendKeys(Keys.Enter);

                for (int i = 0; i < 15; i++)
                {
                    await Task.Delay(1000);
                    var currentUrl = _webDriver.Url;
                    
                    if (currentUrl.Contains("isFeideinnlogget=true") || 
                        currentUrl.Contains("ojr=timeplan"))
                    {
                        return true;
                    }
                    
                    try
                    {
                        var errorElement = _webDriver.FindElement(By.ClassName("error"));
                        if (errorElement != null && errorElement.Displayed)
                        {
                            return false;
                        }
                    }
                    catch { }
                }

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
            _loginPasswordSecure?.Dispose();
            _webDriver?.Dispose();
        }
    }
}