import sys
import os
from datetime import datetime, timedelta
import time
import json
import logging
import pickle
import subprocess
import tempfile
import shutil
from pathlib import Path
import uuid
import platform

# Third-party imports with error handling
try:
    import requests
    import schedule
    import browser_cookie3
    import sqlite3
    import psutil
    from selenium import webdriver
    from selenium.webdriver.chrome.options import Options
    from selenium.webdriver.common.by import By
    from selenium.webdriver.support.ui import WebDriverWait
    from selenium.webdriver.support import expected_conditions as EC
    from selenium.common.exceptions import TimeoutException, WebDriverException
except ImportError as e:
    print(f"Missing required package: {e}")
    print("Please run: pip install requests schedule browser-cookie3 psutil selenium webdriver-manager")
    sys.exit(1)


class ImprovedISkoleBot:
    def __init__(self, gui_callback=None):
        self.base_url = "https://iskole.net"
        self.session = requests.Session()
        # Ensure files are saved alongside this script (avoid CWD issues)
        self._base_dir = Path(__file__).resolve().parent
        self.cookies_file = str(self._base_dir / "iskole_cookies.pkl")
        self.timenr_file = str(self._base_dir / "timenr_counter.pkl")
        self.driver = None
        self.temp_profile = None
        self.gui_callback = gui_callback
        self.running = False
        self.cookies_ready = False
        self._should_stop = False
        # Track if user cancelled setup by closing the login window
        self.last_setup_cancelled = False
        
        # Detect OS and set appropriate headers
        self.headers = self.get_os_specific_headers()
        
        # Initialize timenr counter
        self.load_timenr_counter()
        
        # Set up logging
        logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')
        self.logger = logging.getLogger(__name__)
        
        # Target URL that guarantees user is fully authenticated (timeplan view)
        self.timeplan_url = f"{self.base_url}/elev/?isFeideinnlogget=true&ojr=timeplan"
    
    def log(self, message):
        """Log message and send to GUI if available"""
        timestamp = datetime.now().strftime('%H:%M:%S')
        formatted_message = f"[{timestamp}] {message}"
        print(formatted_message)
        if self.gui_callback:
            self.gui_callback(formatted_message)
    
    def get_os_specific_headers(self):
        """Get OS-specific headers for better compatibility"""
        if platform.system() == "Darwin":  # macOS
            user_agent = 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0.0.0 Safari/537.36'
            platform_header = '"macOS"'
        else:  # Windows/Linux fallback
            user_agent = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/136.0.0.0 Safari/537.36'
            platform_header = '"Windows"'
            
        return {
            'User-Agent': user_agent,
            'Accept': 'text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8',
            'Accept-Language': 'nb-NO,nb;q=0.9,no;q=0.8,nn;q=0.7,en-US;q=0.6,en;q=0.5',
            'Accept-Encoding': 'gzip, deflate, br',
            'Connection': 'keep-alive',
            'Upgrade-Insecure-Requests': '1',
            'Sec-Ch-Ua': '"Not.A/Brand";v="99", "Chromium";v="136"',
            'Sec-Ch-Ua-Mobile': '?0',
            'Sec-Ch-Ua-Platform': platform_header,
            'Sec-Fetch-Site': 'same-origin',
            'Sec-Fetch-Mode': 'navigate',
            'Sec-Fetch-User': '?1',
            'Sec-Fetch-Dest': 'document'
        }
    
    def close_existing_chrome_processes(self):
        """Close any existing Chrome processes that might interfere"""
        try:
            if platform.system() == "Darwin":  # macOS
                try:
                    subprocess.run(['pkill', '-f', 'Chrome'], check=False, capture_output=True)
                    subprocess.run(['pkill', '-f', 'chrome'], check=False, capture_output=True)
                    subprocess.run(['pkill', '-f', 'Google Chrome'], check=False, capture_output=True)
                except:
                    pass
            else:
                for proc in psutil.process_iter(['pid', 'name']):
                    try:
                        if 'chrome' in proc.info['name'].lower():
                            proc.terminate()
                            proc.wait(timeout=3)
                    except:
                        try:
                            if proc.is_running():
                                proc.kill()
                        except:
                            pass
            time.sleep(2)
            self.log("🔄 Closed existing Chrome processes")
        except Exception as e:
            self.log(f"Could not close Chrome processes: {e}")
    
    def create_temp_profile(self):
        """Create a temporary Chrome profile"""
        try:
            self.temp_profile = tempfile.mkdtemp(prefix="chrome_profile_")
            self.log(f"📁 Created temp profile: {self.temp_profile}")
            return True
        except Exception as e:
            self.log(f"Failed to create temp profile: {e}")
            return False
    
    def cleanup_temp_profile(self):
        """Clean up temporary profile"""
        if self.temp_profile and os.path.exists(self.temp_profile):
            try:
                shutil.rmtree(self.temp_profile)
                self.log("🗑️ Cleaned up temp profile")
            except Exception as e:
                self.log(f"Failed to cleanup temp profile: {e}")
    
    def setup_selenium_driver(self):
        """Setup Selenium Chrome driver with platform-specific options"""
        try:
            # If a driver already exists (window open), reuse it
            if self.driver is not None:
                try:
                    _ = self.driver.current_url  # probe
                    self.log("♻️ Reusing existing Selenium Chrome driver")
                    return True
                except Exception:
                    # Driver is stale; continue to recreate
                    try:
                        self.driver.quit()
                    except Exception:
                        pass
                    self.driver = None
            
            self.close_existing_chrome_processes()
            
            if not self.create_temp_profile():
                return False
            
            # Suppress noisy logs from dependencies
            try:
                os.environ.setdefault("TF_CPP_MIN_LOG_LEVEL", "3")  # 0=all, 3=error
                os.environ.setdefault("ABSL_LOG_SEVERITY", "fatal")
                os.environ.setdefault("GLOG_minloglevel", "3")
                os.environ.setdefault("WDM_LOG", "0")  # webdriver_manager
            except Exception:
                pass

            chrome_options = Options()
            chrome_options.add_argument(f"--user-data-dir={self.temp_profile}")
            chrome_options.add_argument("--profile-directory=Default")
            
            # Platform-specific options
            if platform.system() == "Darwin":
                chrome_options.add_argument("--disable-dev-shm-usage")
                chrome_options.add_argument("--no-first-run")
                chrome_options.add_argument("--no-default-browser-check")
                chrome_options.add_argument("--disable-default-apps")
            else:
                chrome_options.add_argument("--no-sandbox")
                chrome_options.add_argument("--disable-dev-shm-usage")
            
            # General options
            chrome_options.add_argument("--disable-blink-features=AutomationControlled")
            chrome_options.add_argument("--disable-extensions")
            chrome_options.add_argument("--disable-plugins-discovery")
            chrome_options.add_argument("--disable-web-security")
            chrome_options.add_argument("--allow-running-insecure-content")
            chrome_options.add_argument("--disable-features=VizDisplayCompositor")
            chrome_options.add_argument("--disable-ipc-flooding-protection")
            chrome_options.add_argument("--disable-background-timer-throttling")
            chrome_options.add_argument("--disable-renderer-backgrounding")
            chrome_options.add_argument("--disable-backgrounding-occluded-windows")
            
            # Language settings
            chrome_options.add_argument("--lang=nb-NO")
            chrome_options.add_experimental_option("prefs", {
                "intl.accept_languages": "nb-NO,nb,no,nn,en-US,en"
            })
            
            # Logging/telemetry suppression
            chrome_options.add_experimental_option("excludeSwitches", ["enable-automation", "enable-logging"])  # remove USB and GCM noise on Windows
            chrome_options.add_argument("--log-level=3")  # INFO=0, WARNING=1, LOG_ERROR=2, LOG_FATAL=3
            chrome_options.add_argument("--silent")
            chrome_options.add_argument("--disable-logging")
            chrome_options.add_argument("--v=0")
            
            # Keep automation extension disabled
            chrome_options.add_experimental_option('useAutomationExtension', False)
            
            try:
                from webdriver_manager.chrome import ChromeDriverManager
                from selenium.webdriver.chrome.service import Service
                
                # Route ChromeDriver logs to DEVNULL where supported
                try:
                    service = Service(ChromeDriverManager().install(), log_output=subprocess.DEVNULL)
                except TypeError:
                    service = Service(ChromeDriverManager().install())
                self.driver = webdriver.Chrome(service=service, options=chrome_options)
                
                # Remove webdriver property
                self.driver.execute_script("Object.defineProperty(navigator, 'webdriver', {get: () => undefined})")
                self.driver.execute_cdp_cmd('Network.setUserAgentOverride', {
                    "userAgent": self.headers['User-Agent']
                })
                
                self.log("✅ Selenium Chrome driver initialized successfully")
                return True
                
            except ImportError:
                self.log("📦 Installing webdriver-manager...")
                subprocess.check_call([sys.executable, "-m", "pip", "install", "webdriver-manager"])
                
                from webdriver_manager.chrome import ChromeDriverManager
                from selenium.webdriver.chrome.service import Service
                
                try:
                    service = Service(ChromeDriverManager().install(), log_output=subprocess.DEVNULL)
                except TypeError:
                    service = Service(ChromeDriverManager().install())
                self.driver = webdriver.Chrome(service=service, options=chrome_options)
                
                self.driver.execute_script("Object.defineProperty(navigator, 'webdriver', {get: () => undefined})")
                self.log("✅ ChromeDriver installed and initialized")
                return True
                
        except Exception as e:
            self.log(f"Failed to setup Selenium driver: {e}")
            self.cleanup_temp_profile()
            return False

    def _shutdown_browser(self):
        """Safely close the Selenium browser and cleanup temp profile"""
        try:
            if self.driver:
                try:
                    self.driver.quit()
                except Exception:
                    pass
                self.driver = None
        finally:
            self.cleanup_temp_profile()
    
    def wait_for_login_completion(self, max_wait_minutes=20):
        """Enhanced login detection with better monitoring"""
        self.log("👤 Please complete the login process in the browser...")
        self.log("⏳ The bot will detect when you reach the main page...")
        
        max_wait_time = max_wait_minutes * 60
        start_time = time.time()
        
        success_indicators = [
            'isfeideinnlogget=true', 'timeplan'
        ]
        
        while time.time() - start_time < max_wait_time and not self._should_stop:
            try:
                current_url = self.driver.current_url.lower()
                page_title = self.driver.title.lower()
                
                try:
                    page_source = self.driver.page_source.lower()
                except:
                    page_source = ""
                
                url_check = any(indicator in current_url for indicator in success_indicators)
                title_check = any(indicator in page_title for indicator in success_indicators)
                content_check = any(indicator in page_source for indicator in success_indicators)
                
                if url_check or title_check or content_check:
                    # Ensure we are on the exact timeplan page with isFeideinnlogget=true
                    try:
                        if "isfeideinnlogget=true" not in current_url or "ojr=timeplan" not in current_url:
                            self.log("🧭 Navigating to Timeplan page...")
                            self.driver.get(self.timeplan_url)
                            # wait a few seconds to let it load
                            time.sleep(3)
                            current_url = self.driver.current_url.lower()
                    except Exception as e:
                        self.log(f"Navigation to timeplan failed: {e}")
                    
                    if "isfeideinnlogget=true" in current_url and "ojr=timeplan" in current_url:
                        self.log("✅ On timeplan page with isFeideinnlogget=true. Ready for cookie extraction.")
                        return True
                
                if 'feide' in current_url or 'login' in current_url:
                    time.sleep(3)
                else:
                    time.sleep(2)
                
                elapsed_minutes = int((time.time() - start_time) / 60)
                if elapsed_minutes > 0 and (time.time() - start_time) % 60 < 2:
                    self.log(f"⏱️ Waiting... ({elapsed_minutes}/{max_wait_minutes} minutes)")
                    
            except Exception as e:
                self.log(f"Error during login monitoring: {e}")
                time.sleep(2)
        
        if self._should_stop:
            self.log("🛑 Setup cancelled by user")
        else:
            self.log(f"⏰ Timeout after {max_wait_minutes} minutes. Make sure you complete the login process.")
        return False
    
    def automated_login_and_cookie_extraction(self):
        """Improved automated login with better error handling"""
        if not self.setup_selenium_driver():
            self.log("❌ Could not setup Selenium driver")
            return False
        
        try:
            self.log("🚀 Opening iSkole in browser...")
            self.driver.get("https://iskole.net")
            time.sleep(3)
            
            current_url = self.driver.current_url.lower()
            if "isfeideinnlogget=true" in current_url:
                # Ensure we are at timeplan specifically
                if "ojr=timeplan" not in current_url:
                    self.log("🧭 Navigating to Timeplan page...")
                    self.driver.get(self.timeplan_url)
                    time.sleep(3)
                self.log("✅ Already logged in (isFeideinnlogget=true)!")
                if self.extract_cookies_from_selenium():
                    # Close only after successful cookie capture
                    self._shutdown_browser()
                    return True
                # If extraction failed despite being logged in, keep monitoring for a bit
                self.log("⏳ Logged in but cookies not ready yet. Waiting...")
                grace_deadline = time.time() + 5 * 60
                while time.time() < grace_deadline:
                    try:
                        if self.driver is None or len(self.driver.window_handles) == 0:
                            self.last_setup_cancelled = True
                            return False
                    except WebDriverException:
                        self.last_setup_cancelled = True
                        return False
                    if self.extract_cookies_from_selenium():
                        self._shutdown_browser()
                        return True
                    time.sleep(1.0)
                return False
            
            # Try to find and click login button
            login_selectors = [
                "//a[contains(text(), 'Logg inn')]",
                "//button[contains(text(), 'Logg inn')]",
                "//a[contains(text(), 'Login')]",
                "//button[contains(text(), 'Login')]",
                "//a[contains(@href, 'login')]",
                "//a[contains(@href, 'feide')]",
                "//a[contains(text(), 'Feide')]",
                "//button[contains(text(), 'Feide')]",
                "//input[@type='submit'][@value*='Logg']"
            ]
            
            for selector in login_selectors:
                try:
                    login_element = WebDriverWait(self.driver, 3).until(
                        EC.element_to_be_clickable((By.XPATH, selector))
                    )
                    self.log("🔍 Found and clicking login element")
                    self.driver.execute_script("arguments[0].click();", login_element)
                    time.sleep(2)
                    break
                except TimeoutException:
                    continue
            
            if self.wait_for_login_completion():
                if self.extract_cookies_from_selenium():
                    # Close only after successful cookie capture
                    self._shutdown_browser()
                    return True
                # If extraction failed post-login, keep monitoring similarly
                self.log("⏳ Logged in but cookies not ready yet. Waiting...")
                grace_deadline = time.time() + 5 * 60
                while time.time() < grace_deadline:
                    try:
                        if self.driver is None or len(self.driver.window_handles) == 0:
                            self.last_setup_cancelled = True
                            return False
                    except WebDriverException:
                        self.last_setup_cancelled = True
                        return False
                    if self.extract_cookies_from_selenium():
                        self._shutdown_browser()
                        return True
                    time.sleep(1.0)
                return False
            else:
                # Do NOT close the window on timeout; allow user to continue manually
                self.log("⏳ Keeping the login window open for manual completion. It will close automatically when cookies are captured or if you close it.")
                # Keep monitoring for a while longer without failing immediately
                extended_deadline = time.time() + 10 * 60  # extra 10 minutes
                last_log = 0
                while time.time() < extended_deadline:
                    # If user closed the window, treat as cancellation
                    try:
                        if self.driver is None or len(self.driver.window_handles) == 0:
                            self.last_setup_cancelled = True
                            return False
                    except WebDriverException:
                        self.last_setup_cancelled = True
                        return False

                    # Check URL for login completion
                    try:
                        current_url = self.driver.current_url.lower()
                    except Exception:
                        current_url = ""

                    if "isfeideinnlogget=true" in current_url:
                        # Ensure we are on timeplan specifically
                        if "ojr=timeplan" not in current_url:
                            try:
                                self.driver.get(self.timeplan_url)
                                time.sleep(2)
                            except Exception:
                                pass
                        # Try extracting cookies now
                        if self.extract_cookies_from_selenium():
                            self._shutdown_browser()
                            return True

                    # Log a heartbeat every 30s to show we are waiting
                    if time.time() - last_log > 30:
                        self.log("⏳ Waiting for you to finish login...")
                        last_log = time.time()
                    time.sleep(1.0)
                # If we reach here, extended wait elapsed
                self.log("⌛ Extended wait elapsed without completing login. You can try Setup & Login again.")
                return False
            
        except WebDriverException:
            # Treat browser window close or driver disconnect as user cancellation
            self.last_setup_cancelled = True
            # Do not log an error; user simply closed the window
            return False
        except Exception as e:
            # Unexpected errors still logged
            self.log(f"Error during automated login: {e}")
            return False
    
    def extract_cookies_from_selenium(self):
        """Enhanced cookie extraction with validation"""
        try:
            self.log("🍪 Extracting cookies from browser session...")
            
            # Enforce that we are on timeplan URL with isFeideinnlogget=true before extracting
            try:
                current_url = self.driver.current_url.lower()
            except Exception:
                current_url = ""
            if "isfeideinnlogget=true" not in current_url or "ojr=timeplan" not in current_url:
                self.log("⛔ Not on the required timeplan page yet. Navigate to the timeplan page and try again.")
                return False
            
            selenium_cookies = self.driver.get_cookies()
            important_cookies = ['JSESSIONID', 'PHPSESSID', 'sessionid', 'auth']
            
            cookies_found = False
            extracted_cookies = {}
            
            iskole_cookie_count = 0
            for cookie in selenium_cookies:
                if cookie['domain'] in ['.iskole.net', 'iskole.net'] or \
                   cookie['name'] in important_cookies:
                    self.session.cookies.set(
                        cookie['name'], 
                        cookie['value'], 
                        domain=cookie['domain'] if 'iskole.net' in cookie['domain'] else '.iskole.net'
                    )
                    extracted_cookies[cookie['name']] = cookie['value']
                    self.log(f"✅ Extracted cookie: {cookie['name']}")
                    cookies_found = True
                    if 'iskole.net' in cookie.get('domain', ''):
                        iskole_cookie_count += 1
            
            # If we have some cookies and validation passes, save regardless of count
            if cookies_found:
                if iskole_cookie_count < 3:
                    self.log(f"ℹ️ Only {iskole_cookie_count} cookies from iskole.net so far; will still verify.")
                if self.test_cookies():
                    if self.save_cookies_to_file():
                        # Verify file exists and report absolute path
                        if os.path.exists(self.cookies_file):
                            self.log(f"✅ Cookies file written at: {self.cookies_file}")
                        self.cookies_ready = True
                        self.log("🎉 Cookies extracted and verified successfully!")
                        self.log(f"📝 Extracted {len(extracted_cookies)} cookies")
                        return True
                    else:
                        self.log("❌ Failed to write cookies file to disk")
                        return False
                else:
                    self.log("❌ Extracted cookies don't work properly")
                    return False
            else:
                if not cookies_found:
                    self.log("❌ No relevant cookies found in browser session")
                else:
                    self.log("❌ Not enough iSkole cookies yet. Please complete login. The window will remain open.")
                return False
                
        except Exception as e:
            self.log(f"Error extracting cookies: {e}")
            return False
    
    def save_cookies_to_file(self):
        """Save current session cookies to file with metadata"""
        try:
            cookie_data = {
                'cookies': dict(self.session.cookies),
                'timestamp': datetime.now().isoformat(),
                'user_agent': self.headers['User-Agent']
            }
            
            with open(self.cookies_file, 'wb') as f:
                pickle.dump(cookie_data, f)
            self.log(f"💾 Cookies saved to {self.cookies_file}")
            return True
        except Exception as e:
            self.log(f"Failed to save cookies: {e}")
            return False
    
    def load_cookies_from_file(self):
        """Load cookies from file with expiration check"""
        try:
            if os.path.exists(self.cookies_file):
                with open(self.cookies_file, 'rb') as f:
                    cookie_data = pickle.load(f)
                
                if isinstance(cookie_data, dict) and 'cookies' in cookie_data:
                    cookies = cookie_data['cookies']
                    timestamp = datetime.fromisoformat(cookie_data.get('timestamp', '2025-01-01T00:00:00'))
                    
                    if datetime.now() - timestamp > timedelta(days=7):
                        self.log("⏰ Saved cookies are too old, will refresh")
                        os.remove(self.cookies_file)
                        return False
                else:
                    cookies = cookie_data
                
                for name, value in cookies.items():
                    self.session.cookies.set(name, value)
                
                self.log(f"📂 Loaded {len(cookies)} cookies from file")
                return True
            else:
                self.log("No saved cookies file found")
                return False
        except Exception as e:
            self.log(f"Failed to load cookies: {e}")
            return False
    
    def test_cookies(self):
        """Enhanced cookie validation"""
        try:
            test_urls = [
                f"{self.base_url}/elev/?isFeideinnlogget=true&ojr=fravar",
                f"{self.base_url}/elev/",
                f"{self.base_url}/"
            ]
            
            for url in test_urls:
                try:
                    response = self.session.get(url, headers=self.headers, timeout=10)
                    
                    if response.status_code == 200:
                        content = response.text.lower()
                        success_indicators = [
                            "fremmøte", "oppmøte", "fravær", "timeplan", 
                            "elev", "student", "logg ut", "log out"
                        ]
                        
                        if any(indicator in content for indicator in success_indicators):
                            self.log("✅ Cookie validation successful")
                            return True
                            
                except Exception as e:
                    self.log(f"Test failed for {url}: {e}")
                    continue
            
            self.log("❌ Cookie validation failed")
            return False
            
        except Exception as e:
            self.log(f"Cookie test error: {e}")
            return False
    
    def get_browser_cookies_enhanced(self):
        """Enhanced browser cookie extraction"""
        if self.load_cookies_from_file():
            if self.test_cookies():
                self.log("✅ Using saved cookies (still valid)")
                return True
            else:
                self.log("💾 Saved cookies invalid, refreshing...")
        
        self.log("🤖 Starting automated browser session...")
        return self.automated_login_and_cookie_extraction()
    
    def get_api_headers(self):
        """Get headers specifically for API requests"""
        base_headers = self.headers.copy()
        base_headers.update({
            'Accept': 'application/json, text/javascript, */*; q=0.01',
            'Content-Type': 'application/vnd.oracle.adf.action+json',
            'X-Requested-With': 'XMLHttpRequest',
            'Origin': 'https://iskole.net',
            'Referer': 'https://iskole.net/elev/?isFeideinnlogget=true&ojr=fravar',
            'Sec-Fetch-Mode': 'cors',
            'Sec-Fetch-Dest': 'empty'
        })
        return base_headers
    
    def get_current_ip(self):
        """Get current public IP address with fallback"""
        try:
            response = requests.get('https://api.ipify.org/?format=json', timeout=5)
            return response.json().get('ip', '109.247.238.162')
        except:
            try:
                response = requests.get('https://httpbin.org/ip', timeout=5)
                return response.json().get('origin', '109.247.238.162')
            except:
                return '109.247.238.162'
    
    def check_login_status(self):
        """Enhanced login status check with clear cookie status reporting"""
        try:
            test_urls = [
                f"{self.base_url}/elev/?isFeideinnlogget=true&ojr=fravar",
                f"{self.base_url}/elev/"
            ]
            
            for url in test_urls:
                try:
                    response = self.session.get(url, headers=self.headers, timeout=10)
                    
                    if response.status_code == 200:
                        content = response.text.lower()
                        if any(indicator in content for indicator in 
                            ["fremmøte", "oppmøte", "fravær", "elev", "timeplan"]):
                            self.log("✅ Cookies are valid and working")
                            return True
                        elif "login" in content or "logg inn" in content:
                            self.log("🔑 Cookies expired - need fresh login")
                            return False
                except:
                    continue
            
            self.log("❌ Cookies invalid or connection failed - please run setup again")
            return False
                
        except Exception as e:
            self.log(f"Error checking login status: {e}")
            return False
    
    def load_timenr_counter(self):
        """Load the timenr counter from file"""
        try:
            if os.path.exists(self.timenr_file):
                with open(self.timenr_file, 'rb') as f:
                    data = pickle.load(f)
                    self.current_timenr = data.get('timenr', 21568187)
                    self.last_increment_date = data.get('last_increment_date', '')
                    self.daily_incremented_times = set(data.get('daily_incremented_times', []))
                    self.log(f"📊 Loaded timenr counter: {self.current_timenr}")
            else:
                self.current_timenr = 21568187
                self.last_increment_date = ''
                self.daily_incremented_times = set()
                self.log(f"🆕 Initialized timenr counter: {self.current_timenr}")
                self.save_timenr_counter()
        except Exception as e:
            self.log(f"Failed to load timenr counter: {e}")
            self.current_timenr = 21568187
            self.last_increment_date = ''
            self.daily_incremented_times = set()
    
    def save_timenr_counter(self):
        """Save the timenr counter to file"""
        try:
            data = {
                'timenr': self.current_timenr,
                'last_updated': datetime.now().isoformat(),
                'last_increment_date': self.last_increment_date,
                'daily_incremented_times': list(self.daily_incremented_times)
            }
            with open(self.timenr_file, 'wb') as f:
                pickle.dump(data, f)
        except Exception as e:
            self.log(f"Failed to save timenr counter: {e}")
    
    def should_increment_timenr(self):
        """Check if timenr should be incremented based on current time"""
        now = datetime.now()
        current_date = now.strftime("%Y-%m-%d")
        current_time = now.strftime("%H:%M")
        
        if self.last_increment_date != current_date:
            self.daily_incremented_times = set()
            self.last_increment_date = current_date
            self.log("🗓️ New day - reset daily increment tracking")
            self.save_timenr_counter()
        
        increment_times = ["08:10", "08:55", "09:50", "10:40", "11:55", "12:40", "13:40", "14:25"]
        
        for increment_time in increment_times:
            if current_time == increment_time and increment_time not in self.daily_incremented_times:
                return increment_time
        
        return None
    
    def increment_timenr(self, increment_time=None):
        """Increment timenr by 1 and save"""
        self.current_timenr += 1
        if increment_time:
            self.daily_incremented_times.add(increment_time)
            self.log(f"🕒 Time-based increment at {increment_time}: timenr = {self.current_timenr}")
        else:
            self.log(f"🔢 Manual increment: timenr = {self.current_timenr}")
        self.save_timenr_counter()
    
    def check_timenr_increment(self):
        """Check and perform timenr increment if needed"""
        increment_time = self.should_increment_timenr()
        if increment_time:
            self.increment_timenr(increment_time)
            return True
        return False
    
    def register_attendance_enhanced(self):
        """Enhanced attendance registration"""
        try:
            current_ip = self.get_current_ip()
            current_date = datetime.now().strftime("%Y%m%d")
            
            jsessionid = None
            for cookie in self.session.cookies:
                if cookie.name == 'JSESSIONID':
                    jsessionid = cookie.value
                    break
            
            if not jsessionid:
                self.log("❌ No JSESSIONID found - login may have expired")
                return False
            
            url = f"{self.base_url}/iskole_elev/rest/v0/VoTimeplan_elev_oppmote;jsessionid={jsessionid}"
            
            payload = {
                "name": "lagre_oppmote",
                "parameters": [
                    {"fylkeid": "00"},
                    {"skoleid": "312"},
                    {"planperi": "2025-26"},
                    {"ansidato": current_date},
                    {"stkode": "PB"},
                    {"kl_trinn": "3"},
                    {"kl_id": "A"},
                    {"k_navn": "STU"},
                    {"gruppe_nr": "$"},
                    {"timenr": self.current_timenr},
                    {"fravaerstype": "M"},
                    {"ip": current_ip}
                ]
            }
            
            self.log(f"📡 Attempting registration with IP: {current_ip}, timenr: {self.current_timenr}")
            
            response = self.session.post(
                url,
                headers=self.get_api_headers(),
                data=json.dumps(payload),
                timeout=15
            )
            
            self.log(f"📊 Registration response: {response.status_code}")
            
            if response.status_code == 200:
                try:
                    response_data = response.json()
                    self.log(f"📋 Response data: {response_data}")
                except:
                    self.log("✅ Registration request sent successfully")
                
                self.log("🎉 Attendance registration completed!")
                return True
            else:
                self.log(f"❌ Registration failed with status: {response.status_code}")
                return False
                
        except Exception as e:
            self.log(f"❌ Error during registration: {e}")
            return False
    
    def check_and_register(self):
        """Main check and register function with better cookie status feedback"""
        if self._should_stop:
            return
            
        current_time = datetime.now()
        self.log(f"🕒 Checking at {current_time.strftime('%H:%M:%S')}")
        
        if current_time.weekday() >= 5:
            self.log("📅 Weekend - skipping")
            return
        
        # Define study hours (8:00-15:00 on weekdays)
        study_start = 8
        study_end = 15
        is_study_time = study_start <= current_time.hour < study_end
        
        if is_study_time:
            self.log("📚 During study hours - processing request")
            
            # Check for timenr increment
            self.check_timenr_increment()
            
            if not self.get_browser_cookies_enhanced():
                self.log("🔑 Cookie authentication failed - please run 'Setup & Login' again")
                return
            
            if not self.check_login_status():
                self.log("🔑 Session expired - cookies need refreshing. Please run 'Setup & Login'")
                if os.path.exists(self.cookies_file):
                    os.remove(self.cookies_file)
                    self.log("🗑️ Removed expired cookies")
                return
            
            self.log("🚀 Attempting attendance registration")
            success = self.register_attendance_enhanced()
            if success:
                self.log("🎯 Attendance registered successfully!")
            else:
                self.log("❌ Registration attempt failed")
        else:
            self.log("⏰ Outside study hours - skipping registration")
    
    def run_scheduler(self):
        """Run the automated scheduler with study-time aware intervals"""
        schedule.clear()  # Clear any existing jobs
        
        # Schedule more frequent checks during study hours
        schedule.every(2).minutes.do(self.check_and_register)  # Check every 2 minutes instead of 1
        
        self.running = True
        self._should_stop = False
        self.log("🤖 Scheduler started - checking every 2 minutes")
        
        # IMMEDIATE CHECK when starting
        self.log("🚀 Running immediate check...")
        self.check_and_register()
        
        while self.running and not self._should_stop:
            try:
                current_time = datetime.now()
                is_study_time = 8 <= current_time.hour < 15 and current_time.weekday() < 5
                
                schedule.run_pending()
                
                # Adjust sleep based on study time
                if is_study_time:
                    # During study hours: check more frequently (every 60 seconds)
                    sleep_interval = 60
                else:
                    # Outside study hours: check less frequently (every 5 minutes)
                    sleep_interval = 300
                
                # Sleep in small chunks for better responsiveness
                for _ in range(sleep_interval):
                    if self._should_stop:
                        break
                    time.sleep(1)
                    
            except Exception as e:
                self.log(f"Scheduler error: {e}")
                time.sleep(10)
        
        self.log("🛑 Scheduler loop ended")
    
    def stop_scheduler(self):
        """Stop the scheduler gracefully"""
        self.log("🛑 Stopping scheduler...")
        self._should_stop = True
        self.running = False
        schedule.clear()
        self.log("🛑 Scheduler stopped")