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
    from PyQt5.QtCore import QObject, pyqtSignal
except ImportError as e:
    print(f"Missing required package: {e}")
    print("Please run: pip install requests schedule browser-cookie3 psutil selenium webdriver-manager PyQt5")
    sys.exit(1)


class ImprovedISkoleBot(QObject):
    scheduler_stopped_signal = pyqtSignal()

    def __init__(self, gui_callback=None, parent=None):
        super().__init__(parent)
        self.base_url = "https://iskole.net"
        self.session = requests.Session()
        self._base_dir = Path(__file__).resolve().parent
        self.cookies_file = str(self._base_dir / "iskole_cookies.pkl")
        self.driver = None
        self.temp_profile = None
        self.gui_callback = gui_callback
        self.running = False
        self.cookies_ready = False
        self._should_stop = False
        self.last_setup_cancelled = False
        self.headers = self.get_os_specific_headers()
        logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')
        self.logger = logging.getLogger(__name__)
        self.timeplan_url = f"{self.base_url}/elev/?isFeideinnlogget=true&ojr=timeplan"
        self.fetched_periods = []
        self.last_fetch_date = None
        self.registered_timenrs = set()

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
                    try:
                        if "isfeideinnlogget=true" not in current_url or "ojr=timeplan" not in current_url:
                            self.log("🧭 Navigating to Timeplan page...")
                            self.driver.get(self.timeplan_url)
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
                if "ojr=timeplan" not in current_url:
                    self.log("🧭 Navigating to Timeplan page...")
                    self.driver.get(self.timeplan_url)
                    time.sleep(3)
                self.log("✅ Already logged in (isFeideinnlogget=true)!")
                if self.extract_cookies_from_selenium():
                    self._shutdown_browser()
                    return True
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
                    self._shutdown_browser()
                    return True
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
                self.log("⏳ Keeping the login window open for manual completion. It will close automatically when cookies are captured or if you close it.")
                extended_deadline = time.time() + 10 * 60
                last_log = 0
                while time.time() < extended_deadline:
                    try:
                        if self.driver is None or len(self.driver.window_handles) == 0:
                            self.last_setup_cancelled = True
                            return False
                    except WebDriverException:
                        self.last_setup_cancelled = True
                        return False

                    try:
                        current_url = self.driver.current_url.lower()
                    except Exception:
                        current_url = ""

                    if "isfeideinnlogget=true" in current_url:
                        if "ojr=timeplan" not in current_url:
                            try:
                                self.driver.get(self.timeplan_url)
                                time.sleep(2)
                            except Exception:
                                pass
                        if self.extract_cookies_from_selenium():
                            self._shutdown_browser()
                            return True

                    if time.time() - last_log > 30:
                        self.log("⏳ Waiting for you to finish login...")
                        last_log = time.time()
                    time.sleep(1.0)
                self.log("⌛ Extended wait elapsed without completing login. You can try Setup & Login again.")
                return False
            
        except WebDriverException:
            self.last_setup_cancelled = True
            return False
        except Exception as e:
            self.log(f"Error during automated login: {e}")
            return False
    
    def extract_cookies_from_selenium(self):
        """Enhanced cookie extraction with validation and console output"""
        try:
            self.log("🍪 Extracting cookies from browser session...")
            
            try:
                current_url = self.driver.current_url.lower()
            except Exception:
                current_url = ""
            if "isfeideinnlogget=true" not in current_url or "ojr=timeplan" not in current_url:
                self.log("⛔ Not on the required timeplan page yet. Navigate to the timeplan page and try again.")
                return False
            
            selenium_cookies = self.driver.get_cookies()
            
            extracted_cookies = {}
            cookies_found = False
            
            # Log all extracted cookies with details
            self.log("📋 Extracted Cookies Details:")
            self.log("=" * 50)
            
            for cookie in selenium_cookies:
                domain = cookie['domain']
                path = cookie.get('path', '/')
                self.session.cookies.set(
                    cookie['name'], 
                    cookie['value'], 
                    domain=domain,
                    path=path
                )
                extracted_cookies[cookie['name']] = cookie['value']
                
                # Detailed cookie logging
                self.log(f"Cookie: {cookie['name']}")
                self.log(f"  Value: {cookie['value'][:50]}{'...' if len(cookie['value']) > 50 else ''}")
                self.log(f"  Domain: {domain}")
                self.log(f"  Path: {path}")
                self.log(f"  Secure: {cookie.get('secure', False)}")
                if cookie.get('expires'):
                    expire_time = datetime.fromtimestamp(cookie['expires']).strftime('%Y-%m-%d %H:%M:%S')
                    self.log(f"  Expires: {expire_time}")
                self.log("-" * 30)
                
                cookies_found = True
            
            self.log(f"📊 Total cookies extracted: {len(extracted_cookies)}")
            self.log("=" * 50)
            
            if cookies_found:
                if self.test_cookies():
                    if self.save_cookies_to_file():
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
                self.log("❌ No cookies found in browser session")
                return False
                
        except Exception as e:
            self.log(f"Error extracting cookies: {e}")
            return False
    
    def save_cookies_to_file(self):
        """Save current session cookies to file with metadata, including domain and path"""
        try:
            cookies_list = []
            for cookie in self.session.cookies:
                cookies_list.append({
                    'name': cookie.name,
                    'value': cookie.value,
                    'domain': cookie.domain,
                    'path': cookie.path,
                    'secure': cookie.secure,
                    'expires': cookie.expires
                })
            
            cookie_data = {
                'cookies': cookies_list,
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
        """Load cookies from file with expiration check, including domain and path"""
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
                    # Backward compatibility for old format
                    cookies = [{'name': name, 'value': value, 'domain': '.iskole.net', 'path': '/'} for name, value in cookie_data.items()]
                
                now = time.time()
                loaded_count = 0
                for c in cookies:
                    if c.get('expires') and c['expires'] < now:
                        self.log(f"Skipping expired cookie: {c['name']}")
                        continue
                    self.session.cookies.set(
                        c['name'], 
                        c['value'], 
                        domain=c.get('domain', '.iskole.net'),
                        path=c.get('path', '/')
                    )
                    loaded_count += 1
                
                self.log(f"📂 Loaded {loaded_count} cookies from file")
                return loaded_count > 0
            else:
                self.log("No saved cookies file found")
                return False
        except Exception as e:
            self.log(f"Failed to load cookies: {e}")
            return False
    
    def test_cookies(self):
        """Enhanced cookie validation with detailed output"""
        try:
            self.log("🔍 Testing cookie validity...")
            
            test_urls = [
                f"{self.base_url}/elev/?isFeideinnlogget=true&ojr=fravar",
                f"{self.base_url}/elev/",
                f"{self.base_url}/"
            ]
            
            for i, url in enumerate(test_urls, 1):
                try:
                    self.log(f"📡 Test {i}/3: Testing {url}")
                    response = self.session.get(url, headers=self.headers, timeout=10)
                    
                    self.log(f"  Status: {response.status_code}")
                    self.log(f"  Content-Length: {len(response.text)} chars")
                    
                    if response.status_code == 200:
                        content = response.text.lower()
                        success_indicators = [
                            "fremmøte", "oppmøte", "fravær", "timeplan", 
                            "elev", "student", "logg ut", "log out"
                        ]
                        
                        found_indicators = [indicator for indicator in success_indicators if indicator in content]
                        
                        if found_indicators:
                            self.log(f"  ✅ Found success indicators: {', '.join(found_indicators)}")
                            self.log("✅ Cookie validation successful")
                            return True
                        else:
                            self.log(f"  ⚠️ No success indicators found")
                            
                except Exception as e:
                    self.log(f"  ❌ Test failed for {url}: {e}")
                    continue
            
            self.log("❌ Cookie validation failed - all tests unsuccessful")
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
        """Enhanced login status check with better logic"""
        try:
            self.log("🔍 Checking login status...")
            
            test_urls = [
                f"{self.base_url}/elev/?isFeideinnlogget=true&ojr=fravar",
                f"{self.base_url}/elev/"
            ]
            
            for i, url in enumerate(test_urls, 1):
                try:
                    self.log(f"📡 Login check {i}/2: {url}")
                    response = self.session.get(url, headers=self.headers, timeout=10)
                    
                    self.log(f"  Status: {response.status_code}")
                    self.log(f"  Content-Type: {response.headers.get('Content-Type', 'N/A')}")
                    self.log(f"  Content-Length: {len(response.text)} chars")
                    
                    if response.status_code == 200:
                        content = response.text.lower()
                        
                        # More specific success indicators
                        success_indicators = ["fremmøte", "oppmøte", "fravær", "timeplan"]
                        found_success = [ind for ind in success_indicators if ind in content]
                        
                        # More specific failure indicators - avoid generic "login"
                        failure_indicators = ["logg inn", "feide innlogging", "ikke logget inn", "you are not logged in"]
                        found_failure = [ind for ind in failure_indicators if ind in content]
                        
                        self.log(f"  Success indicators found: {found_success}")
                        self.log(f"  Failure indicators found: {found_failure}")
                        
                        # If we find specific success indicators and NO failure indicators, we're good
                        if found_success and not found_failure:
                            self.log("✅ Login status: VALID - Cookies are working")
                            return True
                        # If we only find "elev" but no specific attendance features, might be logged in but wrong page
                        elif "elev" in content and not found_failure and not found_success:
                            self.log("✅ Login status: VALID - On student page")
                            return True
                        elif found_failure:
                            self.log("🔑 Login status: EXPIRED - Need fresh login")
                            return False
                        else:
                            self.log("⚠️ Login status: UNCLEAR - Continuing checks...")
                            
                    else:
                        self.log(f"  ❌ HTTP {response.status_code} - Server error")
                        
                except Exception as e:
                    self.log(f"  ❌ Network error: {e}")
                    continue
            
            self.log("❌ Login status: FAILED - All checks unsuccessful")
            return False
                
        except Exception as e:
            self.log(f"Error checking login status: {e}")
            return False
    
    def fetch_schedule(self):
        """Fetch the current day's schedule with detailed console output - FIXED VERSION"""
        if not self.check_login_status():
            return False

        self.log("📅 Fetching today's schedule...")
        
        url = f"{self.base_url}/iskole_elev/rest/v0/VoTimeplan_elev_oppmote"
        params = {
            "finder": "RESTFilter;fylkeid=00,planperi=2025-26,skoleid=312",
            "onlyData": "true",
            "limit": "99",
            "offset": "0",
            "totalResults": "true"
        }

        current_date = datetime.now().strftime("%Y-%m-%d")
        if self.last_fetch_date != current_date:
            self.registered_timenrs = set()
            self.last_fetch_date = current_date

        try:
            self.log(f"📡 GET {url}")
            self.log(f"📋 Params: {params}")
            
            response = self.session.get(url, params=params, headers=self.get_api_headers(), timeout=10)
            
            self.log(f"📊 Response Status: {response.status_code}")
            self.log(f"📊 Response Headers: {dict(response.headers)}")
            
            if response.status_code != 200:
                self.log(f"❌ Schedule fetch failed: {response.status_code}")
                self.log(f"Response content: {response.text[:500]}...")
                return False

            data = response.json()
            
            # FIXED: Remove the [:1000] truncation and output complete JSON
            complete_json = json.dumps(data, indent=2, ensure_ascii=False)
            
            # Output in chunks to avoid GUI issues
            self.log("📋 Complete JSON response:")
            self.log("=" * 80)
            
            # Split into manageable chunks
            chunk_size = 2000
            for i in range(0, len(complete_json), chunk_size):
                chunk = complete_json[i:i + chunk_size]
                self.log(chunk)
            
            self.log("=" * 80)
            
            items = data.get('items', [])
            self.log(f"📊 Total items in response: {len(items)}")
            
            self.fetched_periods = []

            for i, item in enumerate(items, 1):
                self.log(f"📋 Processing item {i}: {item.get('Fag', 'Unknown')}")
                
                if not item['Fag'].endswith('STU'):
                    self.log(f"  ⏭️ Skipping non-STU subject: {item['Fag']}")
                    continue

                tidsrom = item.get('TidsromTilstedevaerelse')
                if not tidsrom:
                    self.log(f"  ⏭️ No registration window found for {item['Fag']}")
                    continue

                reg_start, reg_end = [t.strip() for t in tidsrom.split('-')]
                class_start = item['StartKl'][:2] + ':' + item['StartKl'][2:]
                class_end = item['SluttKl'][:2] + ':' + item['SluttKl'][2:]

                typefravaer = item.get('Typefravaer')

                period = {
                    'timenr': item['Timenr'],
                    'fag': item['Fag'],
                    'class_time': {'start': class_start, 'end': class_end},
                    'registration_window': {'start': reg_start, 'end': reg_end},
                    'typefravaer': "M",
                    'stkode': item['Stkode'],
                    'kl_trinn': item['KlTrinn'],
                    'kl_id': item['KlId'],
                    'k_navn': item['KNavn'],
                    'gruppe_nr': item['GruppeNr']
                }
                period['registered'] = typefravaer is not None
                
                self.log(f"  ✅ Added STU period:")
                self.log(f"    Timenr: {period['timenr']}")
                self.log(f"    Class: {class_start}-{class_end}")
                self.log(f"    Registration: {reg_start}-{reg_end}")
                self.log(f"    Already registered: {period['registered']}")
                
                if period['registered']:
                    self.registered_timenrs.add(period['timenr'])
                self.fetched_periods.append(period)

            self.log("=" * 50)
            self.log(f"📅 Schedule fetch complete: {len(self.fetched_periods)} STU periods found")
            for period in self.fetched_periods:
                status = "✅ Registered" if period['registered'] else "⏳ Pending"
                self.log(f"  {period['timenr']}: {period['class_time']['start']}-{period['class_time']['end']} - {status}")
            self.log("=" * 50)
            
            return True

        except Exception as e:
            self.log(f"❌ Error fetching schedule: {e}")
            return False
    
    def time_to_minutes(self, time_str):
        """Convert time string (HH:MM or HHMM) to minutes since midnight"""
        time_str = time_str.replace(' ', '')
        if len(time_str) == 4 and ':' not in time_str:
            time_str = time_str[:2] + ':' + time_str[2:]
        hours, minutes = map(int, time_str.split(':'))
        return hours * 60 + minutes
    
    def get_current_study_period(self):
        """Get the current study period dict if within class time"""
        now = datetime.now()
        current_time = now.strftime("%H:%M")
        current_minutes = self.time_to_minutes(current_time)
        
        for period in self.fetched_periods:
            start_minutes = self.time_to_minutes(period["class_time"]["start"])
            end_minutes = self.time_to_minutes(period["class_time"]["end"])
            if start_minutes <= current_minutes <= end_minutes:
                return period
        
        return None
    
    def get_current_registration_period(self):
        """Get the current registration period dict if within registration window"""
        now = datetime.now()
        current_time = now.strftime("%H:%M")
        current_minutes = self.time_to_minutes(current_time)
        
        for period in self.fetched_periods:
            start_minutes = self.time_to_minutes(period["registration_window"]["start"])
            end_minutes = self.time_to_minutes(period["registration_window"]["end"])
            if start_minutes <= current_minutes <= end_minutes:
                return period
        
        return None
    
    def is_study_period_time(self):
        """Check if current time is during a STU class"""
        return self.get_current_study_period() is not None
    
    def is_registration_time(self):
        """Check if current time is during a registration window"""
        return self.get_current_registration_period() is not None

    def are_all_periods_completed(self):
        """Check if all STU periods for the day have been registered or their registration windows have passed"""
        if not self.fetched_periods:
            return True

        now = datetime.now()
        current_time = now.strftime("%H:%M")
        current_minutes = self.time_to_minutes(current_time)
        current_date = now.strftime("%Y-%m-%d")

        if self.last_fetch_date != current_date:
            return False

        for period in self.fetched_periods:
            reg_end_minutes = self.time_to_minutes(period["registration_window"]["end"])
            if not period['registered'] and current_minutes <= reg_end_minutes:
                return False

        return True
    
    def log_registered_status(self):
        """Log the status of registered periods and the next one to register"""
        if not self.fetched_periods:
            self.log("No periods fetched yet")
            return

        registered = [p for p in self.fetched_periods if p['registered']]
        unregistered = [p for p in self.fetched_periods if not p['registered']]

        self.log(f"📋 Registered periods: {len(registered)} / {len(self.fetched_periods)}")
        for p in registered:
            self.log(f"✅ {p['timenr']}: {p['class_time']['start']}-{p['class_time']['end']}")

        if unregistered:
            now_minutes = self.time_to_minutes(datetime.now().strftime("%H:%M"))
            upcoming = [p for p in unregistered if self.time_to_minutes(p['registration_window']['start']) >= now_minutes]
            if upcoming:
                next_p = min(upcoming, key=lambda p: self.time_to_minutes(p['registration_window']['start']))
                self.log(f"⏭️ Next to register: {next_p['timenr']} at {next_p['registration_window']['start']}-{next_p['registration_window']['end']}")
            else:
                self.log("No upcoming registration windows; some past windows may be unregistered")
        else:
            self.log("🏁 All periods registered")
    
    def register_attendance_enhanced(self, period):
        """Enhanced attendance registration using fetched period data"""
        try:
            current_ip = self.get_current_ip()
            current_date = datetime.now().strftime("%Y%m%d")
            
            jsessionid = self.session.cookies.get('JSESSIONID')
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
                    {"stkode": period['stkode']},
                    {"kl_trinn": period['kl_trinn']},
                    {"kl_id": period['kl_id']},
                    {"k_navn": period['k_navn']},
                    {"gruppe_nr": period['gruppe_nr']},
                    {"timenr": period['timenr']},
                    {"fravaerstype": period['typefravaer']},
                    {"ip": current_ip}
                ]
            }
            
            class_time = f"{period['class_time']['start']}-{period['class_time']['end']}"
            reg_window = f"{period['registration_window']['start']}-{period['registration_window']['end']}"
            self.log(f"📅 Preparing to register for timenr {period['timenr']}: Class time {class_time}, Registration window {reg_window}")
            
            self.log(f"📡 Registering attendance for timenr: {period['timenr']}, IP: {current_ip}")
            
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
                self.registered_timenrs.add(period['timenr'])
                period['registered'] = True
                return True
            else:
                self.log(f"❌ Registration failed with status: {response.status_code}")
                return False
                
        except Exception as e:
            self.log(f"❌ Error during registration: {e}")
            return False
    
    def check_and_register(self):
        """Main check and register function using fetched schedule"""
        if self._should_stop:
            return
            
        current_time = datetime.now()
        self.log(f"🕒 Checking at {current_time.strftime('%H:%M:%S')}")
        
        if current_time.weekday() >= 5:
            self.log("📅 Weekend - skipping")
            return
        
        current_date = current_time.strftime("%Y-%m-%d")
        if self.last_fetch_date != current_date:
            if not self.fetch_schedule():
                self.log("❌ Failed to fetch schedule - skipping")
                return
        
        self.log_registered_status()
        
        if self.are_all_periods_completed():
            self.log("🏁 All STU classes for the day are completed or their registration windows have passed")
            self.stop_scheduler()
            return
        
        if not self.is_registration_time():
            period = self.get_current_study_period()
            if period:
                if not period['registered']:
                    self.log(f"📚 In STU class {period['class_time']['start']}-{period['class_time']['end']}, registration window {period['registration_window']['start']}-{period['registration_window']['end']}")
            else:
                self.log("⏰ Not during STU class time - skipping registration")
            return
        
        period = self.get_current_registration_period()
        if period['registered']:
            self.log(f"✅ Already registered for timenr {period['timenr']}")
            return
        
        if not self.get_browser_cookies_enhanced():
            self.log("🔑 Cookie authentication failed - please run 'Setup & Login' again")
            return
        
        if not self.check_login_status():
            self.log("🔑 Session expired - cookies need refreshing. Please run 'Setup & Login'")
            if os.path.exists(self.cookies_file):
                os.remove(self.cookies_file)
                self.log("🗑️ Removed expired cookies")
            return
        
        self.log(f"🚀 Attempting attendance registration for timenr {period['timenr']}")
        success = self.register_attendance_enhanced(period)
        if success:
            self.log("🎯 Attendance registered successfully!")
            self.log_registered_status()
            
            if self.are_all_periods_completed():
                self.log("🏁 All STU classes for the day are completed")
                self.stop_scheduler()
        else:
            self.log("❌ Registration attempt failed")
    
    def run_scheduler(self):
        """Run the automated scheduler with registration window awareness"""
        try:
            schedule.clear()
            schedule.every(1).minutes.do(self.check_and_register)
            
            self.running = True
            self._should_stop = False
            self.log("🤖 Scheduler started - checking every minute")
            self.log("📚 Will fetch STU periods from schedule and register during their windows")
            
            self.log("🚀 Running immediate check...")
            self.check_and_register()
            
            while self.running and not self._should_stop:
                schedule.run_pending()
                time.sleep(1)  # Short sleep for responsiveness
        except Exception as e:
            self.log(f"❌ Scheduler error: {e}")
            self.stop_scheduler()
    
    def stop_scheduler(self):
        """Stop the scheduler gracefully"""
        self.log("🛑 Stopping scheduler...")
        self._should_stop = True
        self.running = False
        schedule.clear()
        self.log("🛑 Scheduler stopped")
        self.scheduler_stopped_signal.emit()
    
    def get_status_info(self):
        """Get current status information for display"""
        now = datetime.now()
        current_time = now.strftime("%H:%M")
        current_date = now.strftime("%Y-%m-%d")
        
        current_study_period = self.get_current_study_period()
        current_registration_period = self.get_current_registration_period()
        is_study_time = self.is_study_period_time()
        is_reg_time = self.is_registration_time()
        
        next_reg_window = None
        current_minutes = self.time_to_minutes(current_time)
        for period in self.fetched_periods:
            reg_start_minutes = self.time_to_minutes(period["registration_window"]["start"])
            if reg_start_minutes > current_minutes:
                next_reg_window = {
                    'timenr': period["timenr"],
                    'start': period["registration_window"]["start"],
                    'end': period["registration_window"]["end"]
                }
                break
        
        status = {
            'current_time': current_time,
            'current_date': current_date,
            'fetched_periods_count': len(self.fetched_periods),
            'current_study_period': current_study_period['timenr'] if current_study_period else None,
            'current_registration_period': current_registration_period['timenr'] if current_registration_period else None,
            'is_study_time': is_study_time,
            'is_registration_time': is_reg_time,
            'next_registration_window': next_reg_window,
            'last_fetch_date': self.last_fetch_date,
            'cookies_ready': self.cookies_ready,
            'running': self.running
        }
        
        return status
