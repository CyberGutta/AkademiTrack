import requests
import json
import time
import schedule
from datetime import datetime, timedelta
import logging
import browser_cookie3
import os
import platform
import sqlite3
import shutil
from pathlib import Path
import pickle
import subprocess
import sys
import tempfile
import psutil
from selenium import webdriver
from selenium.webdriver.chrome.options import Options
from selenium.webdriver.common.by import By
from selenium.webdriver.support.ui import WebDriverWait
from selenium.webdriver.support import expected_conditions as EC
from selenium.common.exceptions import TimeoutException, WebDriverException
import uuid

# Set up logging
logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')
logger = logging.getLogger(__name__)

class ImprovedISkoleBot:
    def __init__(self):
        self.base_url = "https://iskole.net"
        self.session = requests.Session()
        self.cookies_file = "iskole_cookies.pkl"
        self.timenr_file = "timenr_counter.pkl"
        self.driver = None
        self.temp_profile = None
        
        # Detect OS and set appropriate headers
        self.headers = self.get_os_specific_headers()
        
        # Initialize timenr counter
        self.load_timenr_counter()
    
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
        """Close any existing Chrome processes that might interfere - Mac compatible"""
        try:
            if platform.system() == "Darwin":  # macOS
                # Use pkill on Mac for more reliable process termination
                try:
                    subprocess.run(['pkill', '-f', 'Chrome'], check=False, capture_output=True)
                    subprocess.run(['pkill', '-f', 'chrome'], check=False, capture_output=True)
                    subprocess.run(['pkill', '-f', 'Google Chrome'], check=False, capture_output=True)
                except:
                    pass
            else:
                # Original Windows/Linux method
                for proc in psutil.process_iter(['pid', 'name']):
                    if 'chrome' in proc.info['name'].lower():
                        try:
                            proc.terminate()
                            proc.wait(timeout=3)
                        except:
                            try:
                                proc.kill()
                            except:
                                pass
            time.sleep(2)
            logger.info("🔄 Closed existing Chrome processes")
        except Exception as e:
            logger.debug(f"Could not close Chrome processes: {e}")
    
    def create_temp_profile(self):
        """Create a temporary Chrome profile"""
        try:
            self.temp_profile = tempfile.mkdtemp(prefix="chrome_profile_")
            logger.info(f"📁 Created temp profile: {self.temp_profile}")
            return True
        except Exception as e:
            logger.error(f"Failed to create temp profile: {e}")
            return False
    
    def cleanup_temp_profile(self):
        """Clean up temporary profile"""
        if self.temp_profile and os.path.exists(self.temp_profile):
            try:
                shutil.rmtree(self.temp_profile)
                logger.info("🗑️  Cleaned up temp profile")
            except:
                pass
    
    def setup_selenium_driver(self):
        """Setup Selenium Chrome driver with Mac-specific options"""
        try:
            # Close existing Chrome processes
            self.close_existing_chrome_processes()
            
            # Create temp profile
            if not self.create_temp_profile():
                return False
            
            chrome_options = Options()
            
            # Use temporary profile to avoid conflicts
            chrome_options.add_argument(f"--user-data-dir={self.temp_profile}")
            chrome_options.add_argument("--profile-directory=Default")
            
            # Mac-specific Chrome options
            if platform.system() == "Darwin":
                # macOS specific arguments
                chrome_options.add_argument("--disable-dev-shm-usage")
                chrome_options.add_argument("--no-first-run")
                chrome_options.add_argument("--no-default-browser-check")
                chrome_options.add_argument("--disable-default-apps")
                # Remove sandbox disable for Mac (can cause issues)
                # chrome_options.add_argument("--no-sandbox")  # Comment out for Mac
            else:
                chrome_options.add_argument("--no-sandbox")
                chrome_options.add_argument("--disable-dev-shm-usage")
            
            # Enhanced stealth options (cross-platform)
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
            
            # Language settings for Norwegian
            chrome_options.add_argument("--lang=nb-NO")
            chrome_options.add_experimental_option("prefs", {
                "intl.accept_languages": "nb-NO,nb,no,nn,en-US,en"
            })
            
            chrome_options.add_experimental_option("excludeSwitches", ["enable-automation"])
            chrome_options.add_experimental_option('useAutomationExtension', False)
            
            # Try to create driver with webdriver-manager
            try:
                from webdriver_manager.chrome import ChromeDriverManager
                from selenium.webdriver.chrome.service import Service
                
                service = Service(ChromeDriverManager().install())
                self.driver = webdriver.Chrome(service=service, options=chrome_options)
                
                # Execute stealth script
                self.driver.execute_script("Object.defineProperty(navigator, 'webdriver', {get: () => undefined})")
                self.driver.execute_cdp_cmd('Network.setUserAgentOverride', {
                    "userAgent": self.headers['User-Agent']
                })
                
                logger.info("✅ Selenium Chrome driver initialized successfully")
                return True
                
            except ImportError:
                logger.info("📦 Installing webdriver-manager...")
                subprocess.check_call([sys.executable, "-m", "pip", "install", "webdriver-manager"])
                
                from webdriver_manager.chrome import ChromeDriverManager
                from selenium.webdriver.chrome.service import Service
                
                service = Service(ChromeDriverManager().install())
                self.driver = webdriver.Chrome(service=service, options=chrome_options)
                
                self.driver.execute_script("Object.defineProperty(navigator, 'webdriver', {get: () => undefined})")
                logger.info("✅ ChromeDriver installed and initialized")
                return True
                
        except Exception as e:
            logger.error(f"Failed to setup Selenium driver: {e}")
            self.cleanup_temp_profile()
            return False
    
    def wait_for_login_completion(self, max_wait_minutes=20):
        """Enhanced login detection with better monitoring"""
        logger.info("👤 Please complete the login process in the browser...")
        logger.info("⏳ The bot will detect when you reach the main page...")
        
        max_wait_time = max_wait_minutes * 60
        start_time = time.time()
        
        success_indicators = [
            'fravar', 'fravær', 'elev', 'student', 'timeplan', 'oppmøte', 'fremmøte'
        ]
        
        while time.time() - start_time < max_wait_time:
            try:
                current_url = self.driver.current_url.lower()
                page_title = self.driver.title.lower()
                
                # Get page source and check for success indicators
                try:
                    page_source = self.driver.page_source.lower()
                except:
                    page_source = ""
                
                # Check multiple success conditions
                url_check = any(indicator in current_url for indicator in success_indicators)
                title_check = any(indicator in page_title for indicator in success_indicators)
                content_check = any(indicator in page_source for indicator in success_indicators)
                
                if url_check or title_check or content_check:
                    logger.info("✅ Login detected! Proceeding with cookie extraction...")
                    
                    # Try to navigate to the fravær page specifically
                    try:
                        fravar_url = "https://iskole.net/elev/?isFeideinnlogget=true&ojr=fravar"
                        if current_url != fravar_url:
                            logger.info("🧭 Navigating to Fravær page...")
                            self.driver.get(fravar_url)
                            time.sleep(3)
                    except Exception as e:
                        logger.debug(f"Could not navigate to fravar page: {e}")
                    
                    return True
                
                # Check if we're on a login page or similar
                if 'feide' in current_url or 'login' in current_url:
                    time.sleep(3)  # Wait longer on login pages
                else:
                    time.sleep(2)
                
                # Progress indicator
                elapsed_minutes = int((time.time() - start_time) / 60)
                if elapsed_minutes > 0 and (time.time() - start_time) % 60 < 2:
                    logger.info(f"⏱️  Waiting... ({elapsed_minutes}/{max_wait_minutes} minutes)")
                    
            except Exception as e:
                logger.debug(f"Error during login monitoring: {e}")
                time.sleep(2)
        
        logger.warning(f"⏰ Timeout after {max_wait_minutes} minutes. Make sure you complete the login process.")
        return False
    
    def automated_login_and_cookie_extraction(self):
        """Improved automated login with better error handling"""
        if not self.setup_selenium_driver():
            logger.error("❌ Could not setup Selenium driver")
            return False
        
        try:
            logger.info("🚀 Opening iSkole in browser...")
            self.driver.get("https://iskole.net")
            time.sleep(10)
            
            # Check if already logged in
            current_url = self.driver.current_url.lower()
            if any(indicator in current_url for indicator in ['fravar', 'fravær', 'elev']):
                logger.info("✅ Already logged in!")
                return self.extract_cookies_from_selenium()
            
            # Look for login elements and click if found
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
                    logger.info(f"🔍 Found and clicking login element")
                    self.driver.execute_script("arguments[0].click();", login_element)
                    time.sleep(2)
                    break
                except TimeoutException:
                    continue
            
            # Wait for user to complete login
            if self.wait_for_login_completion():
                return self.extract_cookies_from_selenium()
            else:
                return False
            
        except Exception as e:
            logger.error(f"Error during automated login: {e}")
            return False
        finally:
            if self.driver:
                try:
                    self.driver.quit()
                except:
                    pass
                self.cleanup_temp_profile()
    
    def extract_cookies_from_selenium(self):
        """Enhanced cookie extraction with validation"""
        try:
            logger.info("🍪 Extracting cookies from browser session...")
            
            selenium_cookies = self.driver.get_cookies()
            important_cookies = ['JSESSIONID', 'PHPSESSID', 'sessionid', 'auth']
            
            cookies_found = False
            extracted_cookies = {}
            
            for cookie in selenium_cookies:
                if cookie['domain'] in ['.iskole.net', 'iskole.net'] or \
                   cookie['name'] in important_cookies:
                    self.session.cookies.set(
                        cookie['name'], 
                        cookie['value'], 
                        domain=cookie['domain'] if 'iskole.net' in cookie['domain'] else '.iskole.net'
                    )
                    extracted_cookies[cookie['name']] = cookie['value']
                    logger.info(f"✅ Extracted cookie: {cookie['name']}")
                    cookies_found = True
            
            if cookies_found:
                # Test the extracted cookies
                if self.test_cookies():
                    self.save_cookies_to_file()
                    logger.info("🎉 Cookies extracted and verified successfully!")
                    logger.info(f"📝 Extracted {len(extracted_cookies)} cookies")
                    return True
                else:
                    logger.warning("❌ Extracted cookies don't work properly")
                    return False
            else:
                logger.warning("❌ No relevant cookies found in browser session")
                return False
                
        except Exception as e:
            logger.error(f"Error extracting cookies: {e}")
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
            logger.info(f"💾 Cookies saved to {self.cookies_file}")
            return True
        except Exception as e:
            logger.error(f"Failed to save cookies: {e}")
            return False
    
    def load_cookies_from_file(self):
        """Load cookies from file with expiration check"""
        try:
            if os.path.exists(self.cookies_file):
                with open(self.cookies_file, 'rb') as f:
                    cookie_data = pickle.load(f)
                
                # Handle old format (just cookies) or new format (with metadata)
                if isinstance(cookie_data, dict) and 'cookies' in cookie_data:
                    cookies = cookie_data['cookies']
                    timestamp = datetime.fromisoformat(cookie_data.get('timestamp', '2025-01-01T00:00:00'))
                    
                    # Check if cookies are too old (more than 7 days)
                    if datetime.now() - timestamp > timedelta(days=7):
                        logger.info("⏰ Saved cookies are too old, will refresh")
                        os.remove(self.cookies_file)
                        return False
                else:
                    # Old format - just use the cookies directly
                    cookies = cookie_data
                
                for name, value in cookies.items():
                    self.session.cookies.set(name, value)
                
                logger.info(f"📂 Loaded {len(cookies)} cookies from file")
                return True
            else:
                logger.info("No saved cookies file found")
                return False
        except Exception as e:
            logger.error(f"Failed to load cookies: {e}")
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
                            logger.info("✅ Cookie validation successful")
                            return True
                            
                except Exception as e:
                    logger.debug(f"Test failed for {url}: {e}")
                    continue
            
            logger.info("❌ Cookie validation failed")
            return False
            
        except Exception as e:
            logger.debug(f"Cookie test error: {e}")
            return False
    
    def get_browser_cookies_enhanced(self):
        """Enhanced browser cookie extraction with Mac compatibility"""
        # Method 1: Try saved cookies first
        if self.load_cookies_from_file():
            if self.test_cookies():
                logger.info("✅ Using saved cookies (still valid)")
                return True
            else:
                logger.info("💾 Saved cookies invalid, refreshing...")
        
        # Method 2: Automated browser session (primary method)
        logger.info("🤖 Starting automated browser session...")
        if self.automated_login_and_cookie_extraction():
            return True
        
        # Method 3: Enhanced browser_cookie3 with Mac-specific handling
        logger.info("🔄 Trying browser_cookie3 with enhanced detection...")
        
        # Mac-specific browser detection
        if platform.system() == "Darwin":
            browsers_to_try = [
                ('Chrome', browser_cookie3.chrome),
                ('Safari', browser_cookie3.safari),
                ('Firefox', browser_cookie3.firefox)
            ]
        else:
            browsers_to_try = [
                ('Chrome', browser_cookie3.chrome),
                ('Edge', browser_cookie3.edge),
                ('Firefox', browser_cookie3.firefox)
            ]
        
        for browser_name, browser_func in browsers_to_try:
            try:
                logger.info(f"🔍 Trying {browser_name}...")
                
                # Try multiple domain variations
                domains_to_try = ['iskole.net', '.iskole.net', 'www.iskole.net']
                
                for domain in domains_to_try:
                    try:
                        cookies = browser_func(domain_name=domain)
                        if cookies:
                            cookie_count = 0
                            for cookie in cookies:
                                if cookie.name and cookie.value:
                                    self.session.cookies.set(cookie.name, cookie.value)
                                    cookie_count += 1
                            
                            if cookie_count > 0:
                                logger.info(f"🍪 Found {cookie_count} cookies from {browser_name}")
                                if self.test_cookies():
                                    self.save_cookies_to_file()
                                    logger.info(f"✅ Successfully extracted cookies from {browser_name}")
                                    return True
                    except Exception as e:
                        logger.debug(f"Failed to get cookies from {browser_name} for {domain}: {e}")
                        continue
                        
            except Exception as e:
                logger.debug(f"Browser {browser_name} failed: {e}")
                continue
        
        logger.error("❌ All cookie extraction methods failed")
        return False
    
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
        """Enhanced login status check"""
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
                            logger.info("✅ Login status confirmed")
                            return True
                except:
                    continue
            
            logger.warning("❌ Login status check failed")
            return False
                
        except Exception as e:
            logger.error(f"Error checking login status: {e}")
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
                    logger.info(f"📊 Loaded timenr counter: {self.current_timenr}")
            else:
                self.current_timenr = 21568187
                self.last_increment_date = ''
                self.daily_incremented_times = set()
                logger.info(f"🆕 Initialized timenr counter: {self.current_timenr}")
                self.save_timenr_counter()
        except Exception as e:
            logger.error(f"Failed to load timenr counter: {e}")
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
            logger.debug(f"💾 Saved timenr counter: {self.current_timenr}")
        except Exception as e:
            logger.error(f"Failed to save timenr counter: {e}")
    
    def should_increment_timenr(self):
        """Check if timenr should be incremented based on current time"""
        now = datetime.now()
        current_date = now.strftime("%Y-%m-%d")
        current_time = now.strftime("%H:%M")
        
        # Reset daily tracking if it's a new day (but keep timenr growing)
        if self.last_increment_date != current_date:
            self.daily_incremented_times = set()
            self.last_increment_date = current_date
            logger.info("🗓️ New day - reset daily increment tracking (timenr continues growing)")
            self.save_timenr_counter()
        
        # Define increment times
        increment_times = ["08:10", "08:55", "09:50", "10:40", "11:55", "12:40", "13:40", "14:25"]
        
        # Check if current time matches any increment time and hasn't been done today
        for increment_time in increment_times:
            if current_time == increment_time and increment_time not in self.daily_incremented_times:
                return increment_time
        
        return None
    
    def increment_timenr(self, increment_time=None):
        """Increment timenr by 1 and save"""
        self.current_timenr += 1
        if increment_time:
            self.daily_incremented_times.add(increment_time)
            logger.info(f"🕒 Time-based increment at {increment_time}: timenr = {self.current_timenr}")
        else:
            logger.info(f"🔢 Manual increment: timenr = {self.current_timenr}")
        self.save_timenr_counter()
    
    def check_timenr_increment(self):
        """Check and perform timenr increment if needed"""
        increment_time = self.should_increment_timenr()
        if increment_time:
            self.increment_timenr(increment_time)
            return True
        return False
    
    def register_attendance_enhanced(self):
        """Enhanced attendance registration - ALWAYS SENDS REQUEST"""
        try:
            current_ip = self.get_current_ip()
            current_date = datetime.now().strftime("%Y%m%d")
            
            # Get JSESSIONID
            jsessionid = None
            for cookie in self.session.cookies:
                if cookie.name == 'JSESSIONID':
                    jsessionid = cookie.value
                    break
            
            if not jsessionid:
                logger.error("❌ No JSESSIONID found - login may have expired")
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
            
            logger.info(f"📡 Attempting registration with IP: {current_ip}, timenr: {self.current_timenr}")
            
            response = self.session.post(
                url,
                headers=self.get_api_headers(),
                data=json.dumps(payload),
                timeout=15
            )
            
            logger.info(f"📊 Registration response: {response.status_code}")
            
            if response.status_code == 200:
                try:
                    response_data = response.json()
                    logger.info(f"📋 Response data: {response_data}")
                except:
                    logger.info("✅ Registration request sent successfully")
                
                logger.info("🎉 Attendance registration completed!")
                return True
            else:
                logger.error(f"❌ Registration failed with status: {response.status_code}")
                logger.debug(f"Response: {response.text[:200]}")
                return False
                
        except Exception as e:
            logger.error(f"❌ Error during registration: {e}")
            return False
    
    def check_and_register(self):
        """Modified main check and register function - ALWAYS ATTEMPTS REGISTRATION"""
        current_time = datetime.now()
        logger.info(f"🕒 Checking at {current_time.strftime('%H:%M:%S')}")
        
        # Skip weekends (optional - remove this if you want to register on weekends too)
        if current_time.weekday() >= 5:
            logger.info("📅 Weekend - skipping")
            return
        
        # Check if timenr should be incremented (independent of registration)
        timenr_incremented = self.check_timenr_increment()
        
        # Try to get cookies
        if not self.get_browser_cookies_enhanced():
            logger.warning("⚠️  Could not obtain valid cookies")
            return
        
        # Check login status
        if not self.check_login_status():
            logger.warning("⚠️  Login verification failed")
            # Remove expired cookies
            if os.path.exists(self.cookies_file):
                os.remove(self.cookies_file)
                logger.info("🔄 Removed expired cookies")
            return
        
        # ALWAYS ATTEMPT REGISTRATION (removed time window check)
        logger.info("🚀 Attempting attendance registration (no time restrictions)")
        success = self.register_attendance_enhanced()
        if success:
            logger.info("🎯 Attendance registered successfully!")
        else:
            logger.warning("❌ Registration attempt failed")
    
    def get_next_increment_time(self):
        """Get the next timenr increment time"""
        now = datetime.now()
        current_time = now.hour * 60 + now.minute
        current_date = now.strftime("%Y-%m-%d")
        
        # Reset daily tracking if it's a new day (but keep timenr growing)
        if self.last_increment_date != current_date:
            self.daily_incremented_times = set()
        
        increment_times = [
            (8*60 + 10, "08:10"),   # 08:10
            (8*60 + 55, "08:55"),   # 08:55
            (9*60 + 50, "09:50"),   # 09:50
            (10*60 + 40, "10:40"),  # 10:40
            (11*60 + 55, "11:55"),  # 11:55
            (12*60 + 40, "12:40"),  # 12:40
            (13*60 + 40, "13:40"),  # 13:40
            (14*60 + 25, "14:25")   # 14:25
        ]
        
        for time_minutes, time_str in increment_times:
            if current_time < time_minutes and time_str not in self.daily_incremented_times:
                return time_str
        
        return "08:10 (tomorrow)"
    
    def run_scheduler(self):
        """Run the enhanced automated scheduler with always-register mode"""
        # Check every minute for more precise timenr increment timing
        schedule.every(1).minutes.do(self.check_and_register)
        
        print("\n🤖 Enhanced iSkole Bot Started (ALWAYS REGISTER MODE)!")
        print("⚠️  WARNING: This will attempt registration EVERY MINUTE!")
        print("📋 No time restrictions - registers at ANY time")
        print("🔢 Time-based timenr increments:")
        print("   • 08:10, 08:55, 09:50, 10:40")
        print("   • 11:55, 12:40, 13:40, 14:25")
        print("   • timenr keeps growing continuously (no daily reset)")
        print("⏰ Checking every minute...")
        print("🔄 Enhanced cookie management")
        print("✨ Improved login detection")
        print("🔢 Continuous timenr growth")
        print(f"📊 Current timenr: {self.current_timenr}")
        print("🚨 CAUTION: This may cause many registration attempts!")
        print("-" * 50)
        
        # Initial check
        self.check_and_register()
        
        while True:
            try:
                schedule.run_pending()
                time.sleep(30)
            except KeyboardInterrupt:
                logger.info("👋 Stopping automation...")
                break
            except Exception as e:
                logger.error(f"Scheduler error: {e}")
                time.sleep(60)

def install_requirements():
    """Install required packages with Mac compatibility"""
    required_packages = [
        'selenium', 'webdriver-manager', 'psutil', 
        'requests', 'schedule', 'browser-cookie3'
    ]
    
    for package in required_packages:
        try:
            __import__(package.replace('-', '_'))
        except ImportError:
            logger.info(f"📦 Installing {package}...")
            subprocess.check_call([sys.executable, "-m", "pip", "install", package])

def main():
    print("🚀 ENHANCED iSkole Bot v4.1 (ALWAYS REGISTER MODE)")
    print("=" * 55)
    print(f"💻 OS: {platform.system()} {platform.release()}")
    
    if platform.system() == "Darwin":
        print("🍎 macOS optimizations enabled")
    
    print("\n⚠️  WARNING: ALWAYS REGISTER MODE ENABLED!")
    print("This version will attempt registration EVERY MINUTE")
    print("regardless of time windows!")
    print("\n✨ FEATURES:")
    print("✅ macOS compatibility")
    print("✅ Time-based timenr increments")
    print("✅ Enhanced cookie extraction")
    print("✅ Better Chrome process management") 
    print("✅ Improved login detection")
    print("✅ Multiple fallback methods")
    print("✅ Automatic Chrome process cleanup")
    print("✅ Temporary profile management")
    print("✅ Enhanced error handling")
    print("🚨 NO TIME RESTRICTIONS - ALWAYS REGISTERS!")
    print("\n📋 How it works:")
    print("1. First run: Opens browser automatically")
    print("2. You login once manually")
    print("3. Bot extracts and saves cookies")
    print("4. Future runs: Fully automatic")
    print("5. Registers EVERY MINUTE (no time restrictions)")
    print("6. timenr increments at specific times daily:")
    print("   08:10, 08:55, 09:50, 10:40, 11:55, 12:40, 13:40, 14:25")
    print("7. timenr keeps growing continuously (no daily reset)")
    print("=" * 55)
    
    # Mac-specific installation notes
    if platform.system() == "Darwin":
        print("\n🍎 macOS Notes:")
        print("• Chrome must be installed in Applications")
        print("• Allow permissions if prompted")
        print("• Close existing Chrome windows before starting")
    
    # Warning about always register mode
    print("\n🚨 IMPORTANT WARNING:")
    print("This version will send registration requests EVERY MINUTE!")
    print("This may result in many requests to the server.")
    print("Use responsibly and consider the impact on the system.")
    
    confirm = input("\nType 'YES' to confirm you want to run in always-register mode: ").strip()
    if confirm.upper() != 'YES':
        print("❌ Operation cancelled. Exiting...")
        return
    
    # Install requirements
    try:
        install_requirements()
        print("📦 All requirements installed")
    except Exception as e:
        print(f"⚠️  Could not install requirements: {e}")
        print("Please run: pip install selenium webdriver-manager psutil requests schedule browser-cookie3")
        input("Press Enter after installing requirements...")
    
    automation = ImprovedISkoleBot()
    
    # Show current timenr
    print(f"\n🔢 Current timenr: {automation.current_timenr}")
    
    # timenr management
    print(f"\n🔢 Current timenr: {automation.current_timenr}")
    print("\ntimenr Options:")
    print("  [Enter] - Continue with current timenr")
    print("  'set' - Set custom timenr value")
    print("  'reset' - Reset timenr to default (21568187)")
    
    timenr_choice = input("\ntimenr choice: ").strip().lower()
    
    if timenr_choice == 'set':
        while True:
            try:
                custom_timenr = input("\nEnter custom timenr value: ").strip()
                new_timenr = int(custom_timenr)
                if new_timenr > 0:
                    automation.current_timenr = new_timenr
                    automation.save_timenr_counter()
                    print(f"✅ timenr set to: {automation.current_timenr}")
                    break
                else:
                    print("❌ Please enter a positive number")
            except ValueError:
                print("❌ Please enter a valid number")
            except KeyboardInterrupt:
                print("\n🚫 Cancelled - keeping current timenr")
                break
    elif timenr_choice == 'reset':
        automation.current_timenr = 21568187
        automation.save_timenr_counter()
        print(f"🔄 timenr reset to: {automation.current_timenr}")
    
    # Check for existing cookies
    if os.path.exists(automation.cookies_file):
        print("\n💾 Found saved cookies!")
        print("Cookie Options:")
        print("  [Enter] - Start with saved cookies")
        print("  'reset' - Delete saved cookies and start fresh")
        print("  'test' - Test current cookies")
        
        choice = input("\nCookie choice: ").strip().lower()
        
        if choice == 'reset':
            os.remove(automation.cookies_file)
            print("🗑️  Cookies deleted - will need fresh login")
        elif choice == 'test':
            print("🧪 Testing saved cookies...")
            if automation.load_cookies_from_file() and automation.test_cookies():
                print("✅ Saved cookies are working!")
            else:
                print("❌ Saved cookies are invalid")
                os.remove(automation.cookies_file)
                print("🗑️  Invalid cookies deleted")
    else:
        print("\n🆕 First run detected")
        print("The bot will open a browser for you to login")
    
    print("\n🚨 FINAL WARNING:")
    print("Starting ALWAYS REGISTER mode - will attempt registration every minute!")
    print("This may generate many requests. Use responsibly!")
    
    final_confirm = input("Type 'START' to begin: ").strip()
    if final_confirm.upper() != 'START':
        print("❌ Operation cancelled. Exiting...")
        return
    
    print("\n🚀 Starting enhanced automation in ALWAYS REGISTER mode...")
    print("💡 TIP: Keep this window open to see status updates")
    print("🛑 Press Ctrl+C to stop the bot")
    
    try:
        automation.run_scheduler()
    except KeyboardInterrupt:
        print("\n👋 Bot stopped by user")
    except Exception as e:
        print(f"\n❌ Unexpected error: {e}")
        print("💡 Try restarting the bot")

if __name__ == "__main__":
    main()