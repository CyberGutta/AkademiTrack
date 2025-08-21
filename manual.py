import sys
import json
import requests
import pickle
import os
from datetime import datetime
from PyQt5.QtWidgets import (
    QDialog, QVBoxLayout, QHBoxLayout, QPushButton, QTableWidget, 
    QTableWidgetItem, QLabel, QMessageBox
)
from PyQt5.QtCore import Qt
from PyQt5.QtGui import QFont
from pathlib import Path

class ManualRegistrationWindow(QDialog):
    def __init__(self, bot, parent=None):
        super().__init__(parent)
        self.bot = bot
        self.base_url = "https://iskole.net"
        self.session = requests.Session()
        self.headers = bot.get_api_headers()  # Use bot's API headers
        self.cookies_file = str(Path(__file__).resolve().parent / "iskole_cookies.pkl")
        self.log(f"📂 Cookies file path: {self.cookies_file}")
        self.load_cookies()
        self.periods = []
        self.init_ui()

    def load_cookies(self):
        """Load cookies from iskole_cookies.pkl if available"""
        try:
            if not os.path.exists(self.cookies_file):
                self.log(f"⚠️ Cookies file not found at {self.cookies_file}")
                self.session.cookies.update(self.bot.session.cookies)
                self.log(f"📂 Using bot session cookies: {dict(self.session.cookies)}")
                return

            with open(self.cookies_file, 'rb') as f:
                cookies = pickle.load(f)
            
            # Handle different cookie formats
            if isinstance(cookies, requests.cookies.RequestsCookieJar):
                self.session.cookies.update(cookies)
            elif isinstance(cookies, dict):
                if 'cookies' in cookies:  # Handle case where cookies are nested
                    self.session.cookies.update(cookies['cookies'])
                else:
                    self.session.cookies.update(cookies)
            else:
                self.log(f"❌ Invalid cookie format in {self.cookies_file}: {type(cookies)}")
                self.session.cookies.update(self.bot.session.cookies)
                return

            self.log(f"📂 Successfully loaded cookies from {self.cookies_file}: {dict(self.session.cookies)}")
        except Exception as e:
            self.log(f"❌ Failed to load cookies from {self.cookies_file}: {e}")
            self.session.cookies.update(self.bot.session.cookies)
            self.log(f"📂 Fallback to bot session cookies: {dict(self.session.cookies)}")

    def init_ui(self):
        """Initialize the manual registration window UI"""
        self.setWindowTitle("Manual Registration")
        self.setGeometry(300, 300, 800, 500)
        self.setStyleSheet("""
            QDialog {
                background-color: white;
                font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', system-ui, sans-serif;
            }
            QLabel {
                color: #212529;
                font-size: 14px;
                font-weight: 500;
            }
            QTableWidget {
                background-color: #f8f9fa;
                border: 1px solid #dee2e6;
                border-radius: 8px;
                font-size: 12px;
                color: #495057;
            }
            QTableWidget::item {
                padding: 8px;
            }
            QPushButton {
                background-color: #0066cc;
                color: white;
                border: none;
                border-radius: 6px;
                padding: 8px 16px;
                font-size: 12px;
                font-weight: 600;
            }
            QPushButton:hover {
                background-color: #0052a3;
            }
            QPushButton:pressed {
                background-color: #003d7a;
            }
            QPushButton:disabled {
                background-color: #cccccc;
                color: #666666;
            }
        """)

        layout = QVBoxLayout()
        layout.setSpacing(12)
        layout.setContentsMargins(16, 16, 16, 16)

        title = QLabel("Manual Registration")
        title.setFont(QFont("SF Pro Display", 18, QFont.Bold))
        layout.addWidget(title)

        self.table = QTableWidget()
        self.table.setColumnCount(5)
        self.table.setHorizontalHeaderLabels(["Subject", "Time", "Reg Window", "Status", "Action"])
        self.table.setSelectionMode(QTableWidget.NoSelection)
        self.table.setEditTriggers(QTableWidget.NoEditTriggers)
        self.table.setAlternatingRowColors(True)
        self.table.setStyleSheet("""
            QTableWidget::item {
                padding: 8px;
            }
            QHeaderView::section {
                background-color: #e9ecef;
                color: #212529;
                padding: 8px;
                font-weight: 600;
                border: 1px solid #dee2e6;
            }
        """)
        self.table.horizontalHeader().setStretchLastSection(True)
        self.table.setColumnWidth(0, 150)
        self.table.setColumnWidth(1, 100)
        self.table.setColumnWidth(2, 100)
        self.table.setColumnWidth(3, 100)
        layout.addWidget(self.table)

        button_layout = QHBoxLayout()
        self.refresh_button = QPushButton("Refresh")
        self.refresh_button.clicked.connect(self.refresh_schedule)
        button_layout.addWidget(self.refresh_button)

        self.close_button = QPushButton("Close")
        self.close_button.clicked.connect(self.close)
        button_layout.addWidget(self.close_button)

        layout.addLayout(button_layout)
        self.setLayout(layout)

        self.refresh_schedule()

    def log(self, message):
        """Log message to the parent window's console"""
        timestamp = datetime.now().strftime('%H:%M:%S')
        formatted_message = f"[{timestamp}] Manual: {message}"
        if self.bot.gui_callback:
            self.bot.gui_callback(formatted_message)

    def fetch_all_classes(self):
        """Fetch all classes (NAT, NOR, STU, 2PY 1, HIS, KRO, MOM) for the current day"""
        self.log("🚀 Starting fetch_all_classes")
        if not self.bot.check_login_status():
            self.log("❌ Cookies invalid or login expired - please run Setup & Login")
            self.show_warning("Cookies invalid or login expired. Please run Setup & Login and try again.")
            return []

        jsessionid = self.session.cookies.get('JSESSIONID')
        if not jsessionid:
            self.log("❌ No JSESSIONID found in session")
            self.show_warning("No valid session found. Please run Setup & Login.")
            return []

        self.log(f"📂 Session cookies: {dict(self.session.cookies)}")
        url = f"{self.base_url}/iskole_elev/rest/v0/VoTimeplan_elev_oppmote;jsessionid={jsessionid}"
        # Broaden the request to avoid potential filtering
        params = {
            "finder": "RESTFilter;fylkeid=00,planperi=2025-26,skoleid=312",
            "limit": "99",
            "offset": "0",
            "totalResults": "true"
        }

        self.log(f"📤 Sending GET request to {url} with params: {params}")
        try:
            response = self.session.get(url, params=params, headers=self.headers, timeout=10)
            self.log(f"📡 GET response status: {response.status_code}")
            self.log(f"📋 Response content: {response.text[:2000]}...")  # Increased to 2000 chars for more data

            if response.status_code != 200:
                self.log(f"❌ Failed to fetch all classes: Status {response.status_code}")
                self.show_warning(f"Failed to fetch classes (Status {response.status_code}). Please try again or check your login.")
                return []

            try:
                data = response.json()
                self.log(f"📋 Response JSON parsed successfully: {json.dumps(data, indent=2)[:2000]}...")
            except json.JSONDecodeError as e:
                self.log(f"❌ Failed to parse JSON response: {e}")
                self.show_warning("Invalid response from server. Please try again later.")
                return []

            items = data.get('items', [])
            if not items:
                self.log("⚠️ No classes found in response")
                self.show_warning("No classes found for today. Please verify your schedule or try again.")
                return []

            periods = []
            for item in items:
                try:
                    tidsrom = item.get('TidsromTilstedevaerelse', 'N/A')
                    reg_start, reg_end = ('N/A', 'N/A') if tidsrom == 'N/A' else [t.strip() for t in tidsrom.split('-')]
                    class_start = item.get('StartKl', 'N/A')
                    class_end = item.get('SluttKl', 'N/A')
                    
                    if class_start != 'N/A' and len(class_start) >= 4:
                        class_start = f"{class_start[:2]}:{class_start[2:]}"
                    if class_end != 'N/A' and len(class_end) >= 4:
                        class_end = f"{class_end[:2]}:{class_end[2:]}"

                    period = {
                        'timenr': item.get('Timenr', 'N/A'),
                        'fag': item.get('Fag', 'Unknown'),
                        'class_time': {'start': class_start, 'end': class_end},
                        'registration_window': {'start': reg_start, 'end': reg_end},
                        'typefravaer': item.get('Typefravaer', 'M'),
                        'registered': False,
                        'stkode': item.get('Stkode', 'Unknown'),
                        'kl_trinn': item.get('KlTrinn', 'Unknown'),
                        'kl_id': item.get('KlId', 'Unknown'),
                        'k_navn': item.get('KNavn', 'Unknown'),
                        'gruppe_nr': item.get('GruppeNr', 'Unknown')
                    }
                    periods.append(period)
                    self.log(f"📋 Processed period: timenr={period['timenr']}, fag={period['fag']}")
                except Exception as e:
                    self.log(f"❌ Error processing item {item.get('Timenr', 'unknown')}: {e}")
                    continue

            self.log(f"📅 Fetched {len(periods)} classes for today")
            return periods

        except requests.exceptions.RequestException as e:
            self.log(f"❌ Network error fetching classes: {e}")
            self.show_warning("Network error while fetching classes. Please check your connection and try again.")
            return []
        except Exception as e:
            self.log(f"❌ Unexpected error fetching classes: {e}")
            self.show_warning("An unexpected error occurred while fetching classes. Please try again.")
            return []

    def register_attendance(self, period):
        """Register attendance for the selected period"""
        try:
            current_ip = self.bot.get_current_ip()
            current_date = datetime.now().strftime("%Y%m%d")
            
            jsessionid = self.session.cookies.get('JSESSIONID')
            if not jsessionid:
                self.log("❌ No JSESSIONID found - login may have expired")
                self.show_warning("No valid session found. Please run Setup & Login.")
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
            self.log(f"📅 Registering for timenr {period['timenr']}: {period['fag']} at {class_time}")
            self.log(f"📤 Sending POST request with payload: {json.dumps(payload, indent=2)}")
            
            response = self.session.post(
                url,
                headers=self.headers,
                data=json.dumps(payload),
                timeout=15
            )
            
            self.log(f"📡 Registration response: {response.status_code}")
            
            if response.status_code == 200:
                try:
                    response_data = response.json()
                    self.log(f"📋 Response data: {json.dumps(response_data, indent=2)}")
                except json.JSONDecodeError:
                    self.log("✅ Registration request sent successfully (no JSON response)")
                
                period['registered'] = True
                self.log("🎉 Attendance registered successfully!")
                return True
            else:
                self.log(f"❌ Registration failed: {response.status_code}")
                self.log(f"Response content: {response.text[:1000]}...")
                self.show_warning(f"Registration failed (Status {response.status_code}). Please try again.")
                return False
                
        except requests.exceptions.RequestException as e:
            self.log(f"❌ Network error during registration: {e}")
            self.show_warning("Network error during registration. Please check your connection and try again.")
            return False
        except Exception as e:
            self.log(f"❌ Unexpected error during registration: {e}")
            self.show_warning("An unexpected error occurred during registration. Please try again.")
            return False

    def refresh_schedule(self):
        """Refresh the schedule and update the table"""
        self.periods = self.fetch_all_classes()
        self.table.setRowCount(len(self.periods))

        for row, period in enumerate(self.periods):
            self.table.setItem(row, 0, QTableWidgetItem(period['fag']))
            time_str = f"{period['class_time']['start']}-{period['class_time']['end']}"
            self.table.setItem(row, 1, QTableWidgetItem(time_str))
            
            reg_window = period['registration_window']
            reg_str = f"{reg_window['start']}-{reg_window['end']}" if reg_window['start'] != 'N/A' else 'N/A'
            self.table.setItem(row, 2, QTableWidgetItem(reg_str))
            
            status = "Registered" if period['registered'] else "Not Registered"
            self.table.setItem(row, 3, QTableWidgetItem(status))
            
            button = QPushButton("Register" if not period['registered'] else "Registered")
            button.setEnabled(not period['registered'])
            if period['registered']:
                button.setStyleSheet("""
                    QPushButton {
                        background-color: #28a745;
                        color: white;
                        border: none;
                        border-radius: 6px;
                        padding: 8px 16px;
                        font-size: 12px;
                        font-weight: 600;
                    }
                """)
            button.clicked.connect(lambda _, p=period: self.register_button_clicked(p))
            self.table.setCellWidget(row, 4, button)

        if not self.periods:
            self.show_warning("Failed to fetch classes. Please ensure you are logged in and try again.")

    def register_button_clicked(self, period):
        """Handle register button click for a specific period"""
        if period['registered']:
            self.show_info("This period is already registered.")
            return

        success = self.register_attendance(period)
        if success:
            self.refresh_schedule()
            self.show_info("Attendance registered successfully!")
        else:
            self.show_warning("Failed to register attendance. Please try again.")

    def show_info(self, message):
        """Show info dialog"""
        msg = QMessageBox(self)
        msg.setIcon(QMessageBox.Information)
        msg.setWindowTitle("Info")
        msg.setText(message)
        msg.exec_()

    def show_warning(self, message):
        """Show warning dialog"""
        msg = QMessageBox(self)
        msg.setIcon(QMessageBox.Warning)
        msg.setWindowTitle("Warning")
        msg.setText(message)
        msg.exec_()