import requests
import json
import time
import schedule
from datetime import datetime
import logging
import browser_cookie3
import os

# Set up logging
logging.basicConfig(level=logging.INFO, format='%(asctime)s - %(levelname)s - %(message)s')
logger = logging.getLogger(__name__)

class SimpleISkoleAutomation:
    def __init__(self):
        self.base_url = "https://iskole.net"
        self.session = requests.Session()
        
        # Headers based on your request
        self.headers = {
            'User-Agent': 'Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/139.0.0.0 Safari/537.36',
            'Accept': 'application/json, text/javascript, */*; q=0.01',
            'Accept-Language': 'nb-NO,nb;q=0.9',
            'Accept-Encoding': 'gzip, deflate, br',
            'Content-Type': 'application/vnd.oracle.adf.action+json',
            'X-Requested-With': 'XMLHttpRequest',
            'Origin': 'https://iskole.net',
            'Referer': 'https://iskole.net/elev/?isFeideinnlogget=true&ojr=fravar',
            'Sec-Fetch-Site': 'same-origin',
            'Sec-Fetch-Mode': 'cors',
            'Sec-Fetch-Dest': 'empty',
            'Connection': 'keep-alive'
        }
    
    def get_browser_cookies(self):
        """Get cookies from your browser automatically"""
        try:
            # Try different browsers
            browsers = [
                ('Chrome', browser_cookie3.chrome),
                ('Firefox', browser_cookie3.firefox),
                ('Safari', browser_cookie3.safari),
                ('Edge', browser_cookie3.edge)
            ]
            
            for browser_name, browser_func in browsers:
                try:
                    cookies = browser_func(domain_name='iskole.net')
                    if cookies:
                        logger.info(f"Found cookies from {browser_name}")
                        # Add cookies to session
                        for cookie in cookies:
                            self.session.cookies.set(cookie.name, cookie.value)
                        return True
                except Exception as e:
                    logger.debug(f"Could not get cookies from {browser_name}: {e}")
                    continue
            
            logger.error("Could not find cookies from any browser")
            return False
            
        except Exception as e:
            logger.error(f"Error getting browser cookies: {e}")
            return False
    
    def get_current_ip(self):
        """Get current public IP address"""
        try:
            response = requests.get('https://api.ipify.org/?format=json', timeout=10)
            return response.json().get('ip', '109.247.238.162')
        except:
            # Fallback to your IP from the request
            return '109.247.238.162'
    
    def check_login_status(self):
        """Check if we can access the fravar page"""
        try:
            response = self.session.get(
                f"{self.base_url}/elev/?isFeideinnlogget=true&ojr=fravar",
                headers=self.headers,
                timeout=10
            )
            
            if response.status_code == 200 and "fremmøte" in response.text.lower():
                logger.info("✅ Successfully connected to iSkole fravar page")
                return True
            else:
                logger.warning("❌ Could not access fravar page - please make sure you're logged in to iSkole in your browser")
                return False
                
        except Exception as e:
            logger.error(f"Error checking login status: {e}")
            return False
    
    def get_current_lessons(self):
        """Fetch current lesson data to get the correct timenr"""
        try:
            # Find the JSESSIONID from cookies
            jsessionid = None
            for cookie in self.session.cookies:
                if cookie.name == 'JSESSIONID':
                    jsessionid = cookie.value
                    break
            
            if not jsessionid:
                logger.error("No JSESSIONID found in cookies")
                return None
            
            # Build URL to get lesson data
            url = f"{self.base_url}/iskole_elev/rest/v0/VoTimeplan_elev_oppmote;jsessionid={jsessionid}"
            params = {
                'finder': 'RESTFilter;fylkeid=00,planperi=2025-26,skoleid=312',
                'onlyData': 'true',
                'limit': '99',
                'offset': '0',
                'totalResults': 'true'
            }
            
            response = self.session.get(url, params=params, headers=self.headers, timeout=10)
            
            if response.status_code == 200:
                data = response.json()
                logger.info("📚 Successfully fetched lesson data")
                return data
            else:
                logger.error(f"Failed to fetch lesson data: {response.status_code}")
                return None
                
        except Exception as e:
            logger.error(f"Error fetching lesson data: {e}")
            return None

    def calculate_timenr(self, base_timenr=21568187, reference_time=None):
        """Calculate the correct timenr based on time passed"""
        if reference_time is None:
            # Use your first capture as reference: 2025-08-19 (assuming around 8:15 AM)
            reference_time = datetime(2025, 8, 19, 8, 15)
        
        current_time = datetime.now()
        time_diff = current_time - reference_time
        
        # Calculate different possible increments
        hours_passed = int(time_diff.total_seconds() / 3600)
        days_passed = time_diff.days
        
        # Method 1: Hourly increment
        timenr_hourly = base_timenr + hours_passed
        
        # Method 2: School period increment (assuming 8 periods per day, 5 days per week)
        # School day schedule: 8:15, 9:00, 10:00, 10:45, 12:00, 12:45, 13:45, 14:30
        school_periods = [
            (8, 15), (9, 0), (10, 0), (10, 45),
            (12, 0), (12, 45), (13, 45), (14, 30)
        ]
        
        # Calculate which school period we're in
        current_period = self.get_current_school_period(current_time, school_periods)
        periods_since_reference = self.calculate_periods_since_reference(
            reference_time, current_time, school_periods
        )
        timenr_period = base_timenr + periods_since_reference
        
        logger.info(f"📊 Timenr calculations:")
        logger.info(f"   Hourly method: {timenr_hourly}")
        logger.info(f"   Period method: {timenr_period}")
        logger.info(f"   Hours passed: {hours_passed}")
        logger.info(f"   Days passed: {days_passed}")
        
        return timenr_hourly, timenr_period
    
    def get_current_school_period(self, current_time, school_periods):
        """Determine which school period we're currently in"""
        current_minutes = current_time.hour * 60 + current_time.minute
        
        for i, (hour, minute) in enumerate(school_periods):
            period_start = hour * 60 + minute
            period_end = period_start + 45  # 45 minute periods
            
            if period_start <= current_minutes < period_end:
                return i
        return -1  # Not in any school period
    
    def calculate_periods_since_reference(self, ref_time, current_time, school_periods):
        """Calculate how many school periods have passed since reference"""
        periods = 0
        
        # Simple calculation: assume 8 periods per school day
        days_diff = (current_time.date() - ref_time.date()).days
        weekdays_diff = self.count_weekdays(ref_time.date(), current_time.date())
        
        # Add periods for complete weekdays
        periods += weekdays_diff * 8
        
        # Add periods for current day
        ref_period = self.get_current_school_period(ref_time, school_periods)
        current_period = self.get_current_school_period(current_time, school_periods)
        
        if current_time.date() == ref_time.date():
            # Same day
            periods += max(0, current_period - ref_period)
        else:
            # Different day
            periods += current_period + 1 if current_period >= 0 else 0
        
        return periods
    
    def count_weekdays(self, start_date, end_date):
        """Count weekdays between two dates"""
        weekdays = 0
        current_date = start_date
        while current_date < end_date:
            if current_date.weekday() < 5:  # Monday = 0, Friday = 4
                weekdays += 1
            current_date += timedelta(days=1)
        return weekdays

    def find_current_lesson_smart(self, lessons_data):
        """Find the lesson using smart timenr calculation and API data"""
        if not lessons_data or 'items' not in lessons_data:
            logger.warning("No lesson data available")
            return None
        
        # Calculate expected timenr values
        timenr_hourly, timenr_period = self.calculate_timenr()
        
        # Try to find lesson in the API data first (most reliable)
        current_time = datetime.now()
        current_minutes = current_time.hour * 60 + current_time.minute
        
        # Registration windows
        windows = [
            (8*60 + 15, 8*60 + 30, "morning_1"),
            (9*60, 9*60 + 15, "morning_2"),  
            (14*60 + 15, 14*60 + 30, "afternoon_1"),
            (15*60, 15*60 + 15, "afternoon_2")
        ]
        
        current_window = None
        for start, end, name in windows:
            if start <= current_minutes <= end:
                current_window = name
                break
        
        if not current_window:
            logger.warning("Not in any registration window")
            return None
        
        # Look for lessons in the API data
        logger.info(f"📚 Searching through {len(lessons_data['items'])} lessons...")
        
        best_match = None
        for lesson in lessons_data['items']:
            if 'timenr' in lesson:
                lesson_timenr = lesson['timenr']
                logger.debug(f"Found lesson with timenr: {lesson_timenr}")
                
                # Check if this timenr is close to our calculated values
                if (abs(lesson_timenr - timenr_hourly) <= 2 or 
                    abs(lesson_timenr - timenr_period) <= 2):
                    logger.info(f"🎯 Found matching lesson: timenr={lesson_timenr}")
                    best_match = lesson
                    break
        
        if best_match:
            return best_match
        
        # Fallback: create lesson data with calculated timenr
        logger.info("🔄 Using calculated timenr (fallback method)")
        fallback_lesson = {
            'timenr': timenr_hourly  # Use hourly as primary guess
        }
        return fallback_lesson

    def try_multiple_timenr_values(self, base_payload):
        """Try registration with multiple timenr values"""
        timenr_hourly, timenr_period = self.calculate_timenr()
        
        # Try different timenr values in order of likelihood
        timenr_candidates = [
            timenr_hourly,
            timenr_period,
            timenr_hourly + 1,
            timenr_hourly - 1,
            timenr_period + 1,
            timenr_period - 1
        ]
        
        for timenr in timenr_candidates:
            logger.info(f"🎲 Trying timenr: {timenr}")
            
            # Update payload with this timenr
            payload = base_payload.copy()
            for param in payload['parameters']:
                if 'timenr' in param:
                    param['timenr'] = timenr
                    break
            
            success = self.send_registration_request(payload)
            if success:
                logger.info(f"✅ Success with timenr: {timenr}")
                return True
            
            # Small delay between attempts
            time.sleep(1)
        
        logger.error("❌ Failed with all timenr values")
        return False

    def send_registration_request(self, payload):
        """Send the actual registration request"""
        try:
            # Find the JSESSIONID from cookies
            jsessionid = None
            for cookie in self.session.cookies:
                if cookie.name == 'JSESSIONID':
                    jsessionid = cookie.value
                    break
            
            if not jsessionid:
                logger.error("No JSESSIONID found in cookies")
                return False
            
            # Build URL with session ID
            url = f"{self.base_url}/iskole_elev/rest/v0/VoTimeplan_elev_oppmote;jsessionid={jsessionid}"
            
            response = self.session.post(
                url,
                headers=self.headers,
                data=json.dumps(payload),
                timeout=10
            )
            
            if response.status_code == 200:
                return True
            else:
                logger.debug(f"Registration failed: {response.status_code} - {response.text}")
                return False
                
        except Exception as e:
            logger.error(f"Request failed: {e}")
            return False

    def register_attendance_now(self):
        """Try to register attendance right now with smart timenr calculation"""
        try:
            # First, get current lesson data from API
            lessons_data = self.get_current_lessons()
            
            # Get current IP and date
            current_ip = self.get_current_ip()
            current_date = datetime.now().strftime("%Y%m%d")
            
            # Build base payload
            base_payload = {
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
                    {"timenr": 0},  # Will be updated
                    {"fravaerstype": "M"},
                    {"ip": current_ip}
                ]
            }
            
            # Method 1: Try with API lesson data
            if lessons_data:
                current_lesson = self.find_current_lesson_smart(lessons_data)
                if current_lesson and 'timenr' in current_lesson:
                    # Update payload with correct timenr
                    for param in base_payload['parameters']:
                        if 'timenr' in param:
                            param['timenr'] = current_lesson['timenr']
                            break
                    
                    logger.info(f"🎯 Using API lesson data: timenr={current_lesson['timenr']}")
                    success = self.send_registration_request(base_payload)
                    if success:
                        logger.info("🎉 Successfully registered attendance!")
                        return True
            
            # Method 2: Try with calculated timenr values
            logger.info("🔄 Trying calculated timenr values...")
            success = self.try_multiple_timenr_values(base_payload)
            if success:
                return True
            
            logger.error("❌ All registration attempts failed")
            return False
                
        except Exception as e:
            logger.error(f"Error registering attendance: {e}")
            return False
    
    def is_registration_time(self):
        """Check if current time is within a registration window"""
        now = datetime.now()
        current_time = now.hour * 60 + now.minute  # Convert to minutes since midnight
        
        # Registration windows based on your schedule
        # Format: (start_minutes, end_minutes, description)
        windows = [
            (8*60 + 15, 8*60 + 30, "08:15-08:30 (første time)"),
            (9*60, 9*60 + 15, "09:00-09:15 (andre time)"),  
            (14*60 + 15, 14*60 + 30, "14:15-14:30 (sjette time)"),
            (15*60, 15*60 + 15, "15:00-15:15 (syvende time)")
        ]
        
        for start, end, desc in windows:
            if start <= current_time <= end:
                logger.info(f"⏰ Within registration window: {desc}")
                return True
        
        return False
    
    def check_and_register(self):
        """Main function to check if we should register attendance"""
        current_time = datetime.now()
        logger.info(f"🕒 Checking at {current_time.strftime('%H:%M:%S')}")
        
        # Skip weekends
        if current_time.weekday() >= 5:  # 5 = Saturday, 6 = Sunday
            logger.info("📅 Weekend - skipping check")
            return
        
        # Get browser cookies
        if not self.get_browser_cookies():
            logger.warning("⚠️  Could not get browser cookies - make sure iSkole is open in your browser")
            return
        
        # Check if we can access the system
        if not self.check_login_status():
            logger.warning("⚠️  Not logged in - please open iSkole in your browser and log in")
            return
        
        # Check if it's time to register
        if self.is_registration_time():
            logger.info("✅ Time to register attendance!")
            success = self.register_attendance_now()
            if success:
                logger.info("🎯 Attendance registered successfully!")
            else:
                logger.warning("❌ Failed to register attendance")
        else:
            next_window = self.get_next_registration_window()
            logger.info(f"⏳ Not registration time. Next window: {next_window}")
    
    def get_next_registration_window(self):
        """Get description of next registration window"""
        now = datetime.now()
        current_time = now.hour * 60 + now.minute
        
        windows = [
            (8*60 + 15, "08:15"),
            (9*60, "09:00"),
            (14*60 + 15, "14:15"), 
            (15*60, "15:00")
        ]
        
        for start, time_str in windows:
            if current_time < start:
                return f"{time_str} today"
        
        return "08:15 tomorrow"
    
    def run_scheduler(self):
        """Set up and run the scheduler"""
        # Check every 5 minutes during school hours
        schedule.every(5).minutes.do(self.check_and_register)
        
        logger.info("🚀 iSkole attendance automation started!")
        logger.info("📋 Registration windows:")
        logger.info("   • 08:15-08:30 (første time)")
        logger.info("   • 09:00-09:15 (andre time)")  
        logger.info("   • 14:15-14:30 (sjette time)")
        logger.info("   • 15:00-15:15 (syvende time)")
        logger.info("⏰ Checking every 5 minutes...")
        logger.info("💡 Make sure iSkole is open and logged in in your browser!")
        print("\n" + "="*50)
        
        # Do an initial check
        self.check_and_register()
        
        # Run the scheduler
        while True:
            try:
                schedule.run_pending()
                time.sleep(30)  # Check every 30 seconds
            except KeyboardInterrupt:
                logger.info("👋 Stopping automation...")
                break
            except Exception as e:
                logger.error(f"Scheduler error: {e}")
                time.sleep(60)  # Wait a bit before retrying

def main():
    print("🎓 iSkole Attendance Automation")
    print("="*40)
    print("Instructions:")
    print("1. Open iSkole in your browser and log in")
    print("2. Navigate to the 'Fravær' page") 
    print("3. Keep the browser open (you can minimize it)")
    print("4. This program will automatically register attendance during class times")
    print("="*40)
    
    input("Press Enter when you're logged in to iSkole and on the Fravær page...")
    
    automation = SimpleISkoleAutomation()
    automation.run_scheduler()

if __name__ == "__main__":
    main()