import sys
import os
import time
import platform
import subprocess
import json
from datetime import datetime
from pathlib import Path

try:
    from PyQt5.QtWidgets import (
    QApplication, QMainWindow, QVBoxLayout, QHBoxLayout, 
    QWidget, QPushButton, QTextEdit, QLabel, QMessageBox,
    QFrame, QProgressBar, QSizePolicy, QPlainTextEdit, QDialog, QLineEdit
    )
    from PyQt5.QtCore import QThread, pyqtSignal, Qt, QTimer, QPropertyAnimation, QEasingCurve, QRect
    from PyQt5.QtGui import QFont, QPalette, QColor, QIcon
except ImportError:
    print("PyQt5 is required. Please install it with: pip install PyQt5")
    sys.exit(1)

try:
    from backend import ImprovedISkoleBot
except ImportError:
    print("backend.py or manual.py file is required in the same directory!")
    sys.exit(1)

class NotificationWidget(QWidget):
    """Professional notification widget with modern design and smooth animations"""
    
    def __init__(self, message, notification_type="info", duration=5000, parent=None):
        super().__init__(parent)
        self.parent_widget = parent
        self.duration = duration
        self.notification_type = notification_type
        
        # Modern window flags for professional appearance
        self.setWindowFlags(Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint | Qt.Tool)
        self.setAttribute(Qt.WA_DeleteOnClose)
        
        self.init_ui(message)
        self.setup_animations()
        
    def init_ui(self, message):
        """Initialize professional notification UI with modern design"""
        self.setFixedSize(380, 90)
        
        # Professional color schemes and icons
        notification_configs = {
            "success": {
                "bg_color": "#10B981",  # Modern green
                "text_color": "white",
                "icon": "✓",
                "border": "#059669",
                "shadow": "rgba(16, 185, 129, 0.4)"
            },
            "error": {
                "bg_color": "#EF4444",  # Modern red
                "text_color": "white", 
                "icon": "✕",
                "border": "#DC2626",
                "shadow": "rgba(239, 68, 68, 0.4)"
            },
            "warning": {
                "bg_color": "#F59E0B",  # Modern amber
                "text_color": "white",
                "icon": "⚠",
                "border": "#D97706", 
                "shadow": "rgba(245, 158, 11, 0.4)"
            },
            "completed": {
                "bg_color": "#8B5CF6",  # Modern purple
                "text_color": "white",
                "icon": "🎉",
                "border": "#7C3AED",
                "shadow": "rgba(139, 92, 246, 0.4)"
            },
            "info": {
                "bg_color": "#3B82F6",  # Modern blue
                "text_color": "white",
                "icon": "ℹ",
                "border": "#2563EB",
                "shadow": "rgba(59, 130, 246, 0.4)"
            }
        }
        
        config = notification_configs.get(self.notification_type, notification_configs["info"])
        
        # Modern stylesheet with gradient and shadow effects
        self.setStyleSheet(f"""
            NotificationWidget {{
                background: qlineargradient(x1:0, y1:0, x2:0, y2:1,
                    stop:0 {config["bg_color"]}, 
                    stop:1 {self.darken_color(config["bg_color"], 0.1)});
                border: 1px solid {config["border"]};
                border-radius: 12px;
                font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', 'SF Pro Display', system-ui, sans-serif;
            }}
        """)
        
        layout = QHBoxLayout(self)
        layout.setContentsMargins(20, 16, 20, 16)
        layout.setSpacing(16)
        
        # Modern icon with better styling
        icon_label = QLabel(config["icon"])
        icon_label.setFont(QFont("SF Pro Display", 20, QFont.Bold))
        icon_label.setFixedSize(32, 32)
        icon_label.setAlignment(Qt.AlignCenter)
        icon_label.setStyleSheet(f"""
            QLabel {{
                color: {config["text_color"]};
                background: rgba(255, 255, 255, 0.2);
                border-radius: 16px;
                border: none;
            }}
        """)
        
        # Content container for better text layout
        content_widget = QWidget()
        content_layout = QVBoxLayout(content_widget)
        content_layout.setContentsMargins(0, 0, 0, 0)
        content_layout.setSpacing(2)
        
        # Main message with better typography
        message_label = QLabel(message)
        message_label.setStyleSheet(f"""
            QLabel {{
                color: {config["text_color"]};
                font-size: 14px;
                font-weight: 600;
                background: transparent;
                border: none;
                letter-spacing: 0.3px;
            }}
        """)
        message_label.setWordWrap(True)
        
        # Subtle timestamp
        timestamp_label = QLabel(datetime.now().strftime("%H:%M"))
        timestamp_label.setStyleSheet(f"""
            QLabel {{
                color: rgba(255, 255, 255, 0.8);
                font-size: 11px;
                font-weight: 400;
                background: transparent;
                border: none;
            }}
        """)
        
        content_layout.addWidget(message_label)
        content_layout.addWidget(timestamp_label)
        
        # Close button (optional, appears on hover)
        close_button = QPushButton("×")
        close_button.setFixedSize(24, 24)
        close_button.setStyleSheet(f"""
            QPushButton {{
                color: rgba(255, 255, 255, 0.7);
                background: transparent;
                border: none;
                border-radius: 12px;
                font-size: 16px;
                font-weight: bold;
            }}
            QPushButton:hover {{
                background: rgba(255, 255, 255, 0.2);
                color: white;
            }}
        """)
        close_button.clicked.connect(self.hide_notification)
        
        layout.addWidget(icon_label)
        layout.addWidget(content_widget, 1)
        layout.addWidget(close_button)
        
    def darken_color(self, hex_color, factor):
        """Darken a hex color by a factor (0-1)"""
        try:
            # Remove # if present
            hex_color = hex_color.lstrip('#')
            # Convert to RGB
            rgb = tuple(int(hex_color[i:i+2], 16) for i in (0, 2, 4))
            # Darken each component
            darkened = tuple(int(c * (1 - factor)) for c in rgb)
            # Convert back to hex
            return f"#{darkened[0]:02x}{darkened[1]:02x}{darkened[2]:02x}"
        except:
            return hex_color
        
    def setup_animations(self):
        """Setup smooth professional animations"""
        # Slide-in animation with easing
        self.slide_in_animation = QPropertyAnimation(self, b"geometry")
        self.slide_in_animation.setDuration(400)  # Slightly longer for smoothness
        self.slide_in_animation.setEasingCurve(QEasingCurve.OutCubic)
        
        # Slide-out animation
        self.slide_out_animation = QPropertyAnimation(self, b"geometry")
        self.slide_out_animation.setDuration(300)
        self.slide_out_animation.setEasingCurve(QEasingCurve.InCubic)
        self.slide_out_animation.finished.connect(self.close)
        
        # Fade-out animation for smooth disappearing
        self.fade_animation = QPropertyAnimation(self, b"windowOpacity")
        self.fade_animation.setDuration(250)
        self.fade_animation.setStartValue(1.0)
        self.fade_animation.setEndValue(0.0)
        self.fade_animation.finished.connect(self.close)
        
    def show_notification(self):
        """Show notification with enhanced positioning and stacking"""
        try:
            app = QApplication.instance()
            screen = app.primaryScreen().availableGeometry()
            
            # Better positioning - account for taskbar and multiple notifications
            margin = 20
            notification_spacing = 100  # Space between stacked notifications
            
            # Calculate position considering existing notifications
            if hasattr(app, '_active_notifications'):
                existing_count = len([n for n in app._active_notifications if n.isVisible()])
            else:
                app._active_notifications = []
                existing_count = 0
            
            start_x = screen.right()
            end_x = screen.right() - self.width() - margin
            y = screen.top() + margin + (existing_count * notification_spacing)
            
            # Ensure notification doesn't go off-screen
            max_y = screen.bottom() - self.height() - margin
            if y > max_y:
                y = screen.top() + margin  # Reset to top if too many notifications
            
            # Set initial position and show
            self.setGeometry(start_x, y, self.width(), self.height())
            self.show()
            self.raise_()
            self.activateWindow()
            
            # Add to active notifications list
            app._active_notifications.append(self)
            
            # Animate slide-in
            self.slide_in_animation.setStartValue(QRect(start_x, y, self.width(), self.height()))
            self.slide_in_animation.setEndValue(QRect(end_x, y, self.width(), self.height()))
            self.slide_in_animation.start()
            
            # Auto-hide after duration
            QTimer.singleShot(self.duration, self.hide_notification)
            
        except Exception as e:
            print(f"Error showing notification: {e}")
    
    def hide_notification(self):
        """Hide notification with smooth fade animation"""
        if not self.isVisible():
            return
        
        # Use fade animation instead of slide for smoother exit
        self.fade_animation.start()
    
    def closeEvent(self, event):
        """Clean up when notification closes"""
        try:
            # Remove from active notifications list
            app = QApplication.instance()
            if hasattr(app, '_active_notifications') and self in app._active_notifications:
                app._active_notifications.remove(self)
        except:
            pass
        event.accept()
    
    def mousePressEvent(self, event):
        """Close notification when clicked"""
        try:
            self.hide_notification()
            event.accept()
        except Exception as e:
            print(f"Error in notification click: {e}")
            try:
                self.close()
            except:
                pass

class PostRequestWindow(QDialog):
    def __init__(self, bot, parent=None):
        super().__init__(parent)
        self.bot = bot
        self.base_url = "https://iskole.net"
        self.init_ui()

    def init_ui(self):
        """Initialize the POST request window UI"""
        self.setWindowTitle("Manual POST Request")
        self.setGeometry(300, 300, 900, 700)
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
            QLineEdit, QTextEdit {
                background-color: #f8f9fa;
                border: 1px solid #dee2e6;
                border-radius: 8px;
                padding: 8px;
                font-size: 12px;
                color: #495057;
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
            QTextEdit#console {
                background-color: #0b0c10;
                color: #e6edf3;
                font-family: 'SF Mono', 'Consolas', 'Fira Code', monospace;
                font-size: 12px;
                border: 1px solid #1f2833;
            }
        """)

        layout = QVBoxLayout()
        layout.setSpacing(12)
        layout.setContentsMargins(16, 16, 16, 16)

        # Title
        title = QLabel("Manual POST Request")
        title.setFont(QFont("SF Pro Display", 18, QFont.Bold))
        layout.addWidget(title)

        # Form fields
        form_layout = QVBoxLayout()
        
        # Fylkeid
        form_layout.addWidget(QLabel("Fylke ID:"))
        self.fylkeid_input = QLineEdit("00")
        form_layout.addWidget(self.fylkeid_input)
        
        # Skoleid  
        form_layout.addWidget(QLabel("Skole ID:"))
        self.skoleid_input = QLineEdit("312")
        form_layout.addWidget(self.skoleid_input)
        
        # Planperi
        form_layout.addWidget(QLabel("Plan Periode:"))
        self.planperi_input = QLineEdit("2025-26")
        form_layout.addWidget(self.planperi_input)
        
        # Stkode
        form_layout.addWidget(QLabel("ST Kode:"))
        self.stkode_input = QLineEdit("PB")
        form_layout.addWidget(self.stkode_input)
        
        # Kl_trinn
        form_layout.addWidget(QLabel("Klasse Trinn:"))
        self.kl_trinn_input = QLineEdit("3")
        form_layout.addWidget(self.kl_trinn_input)
        
        # Kl_id
        form_layout.addWidget(QLabel("Klasse ID:"))
        self.kl_id_input = QLineEdit("A")
        form_layout.addWidget(self.kl_id_input)
        
        # K_navn
        form_layout.addWidget(QLabel("K Navn:"))
        self.k_navn_input = QLineEdit("STU")
        form_layout.addWidget(self.k_navn_input)
        
        # Gruppe_nr
        form_layout.addWidget(QLabel("Gruppe Nr:"))
        self.gruppe_nr_input = QLineEdit("$")
        form_layout.addWidget(self.gruppe_nr_input)
        
        # Timenr
        form_layout.addWidget(QLabel("Time Nr:"))
        self.timenr_input = QLineEdit("1")
        form_layout.addWidget(self.timenr_input)
        
        layout.addLayout(form_layout)

        # Buttons
        button_layout = QHBoxLayout()
        self.send_button = QPushButton("Send POST Request")
        self.send_button.clicked.connect(self.send_post_request)
        button_layout.addWidget(self.send_button)

        self.close_button = QPushButton("Close")
        self.close_button.clicked.connect(self.close)
        button_layout.addWidget(self.close_button)

        layout.addLayout(button_layout)
        
        # Console output
        layout.addWidget(QLabel("Response:"))
        self.console = QTextEdit()
        self.console.setObjectName("console")
        self.console.setReadOnly(True)
        self.console.setMinimumHeight(200)
        layout.addWidget(self.console)

        self.setLayout(layout)

    def log(self, message):
        """Log message to console"""
        timestamp = datetime.now().strftime('%H:%M:%S')
        formatted_message = f"[{timestamp}] {message}"
        self.console.append(formatted_message)
        if self.bot.gui_callback:
            self.bot.gui_callback(formatted_message)

    def send_post_request(self):
        """Send the POST request with current form values - FIXED OUTPUT"""
        try:
            current_ip = self.bot.get_current_ip()
            current_date = datetime.now().strftime("%Y%m%d")
            
            # Load cookies if they exist
            if os.path.exists(self.bot.cookies_file):
                self.bot.load_cookies_from_file()
                self.log("📂 Loaded cookies from file")
            else:
                self.log("❌ No cookies file found - please run Setup & Login first")
                return
            
            # Get JSESSIONID from the bot's session cookies
            jsessionid = None
            for cookie in self.bot.session.cookies:
                if cookie.name == 'JSESSIONID':
                    jsessionid = cookie.value
                    break
            
            if not jsessionid:
                self.log("❌ No JSESSIONID found in cookies")
                self.log("Available cookies:")
                for cookie in self.bot.session.cookies:
                    self.log(f"  - {cookie.name}: {cookie.value[:20]}...")
                return
            
            url = f"{self.base_url}/iskole_elev/rest/v0/VoTimeplan_elev_oppmote;jsessionid={jsessionid}"
            
            payload = {
                "name": "lagre_oppmote",
                "parameters": [
                    {"fylkeid": self.fylkeid_input.text()},
                    {"skoleid": self.skoleid_input.text()},
                    {"planperi": self.planperi_input.text()},
                    {"ansidato": current_date},
                    {"stkode": self.stkode_input.text()},
                    {"kl_trinn": self.kl_trinn_input.text()},
                    {"kl_id": self.kl_id_input.text()},
                    {"k_navn": self.k_navn_input.text()},
                    {"gruppe_nr": self.gruppe_nr_input.text()},
                    {"timenr": self.timenr_input.text()},
                    {"fravaerstype": "M"},
                    {"ip": current_ip}
                ]
            }
            
            self.log(f"📤 Sending POST to: {url}")
            self.log(f"📋 Payload: {json.dumps(payload, indent=2)}")
            self.log(f"🌐 Using IP: {current_ip}")
            self.log(f"📅 Date: {current_date}")
            
            # Use the bot's session and headers
            response = self.bot.session.post(
                url,
                headers=self.bot.get_api_headers(),
                data=json.dumps(payload),
                timeout=15
            )
            
            self.log(f"📡 Response Status: {response.status_code}")
            self.log(f"📋 Response Headers: {dict(response.headers)}")
            
            try:
                response_data = response.json()
                self.log("📋 Complete Response JSON:")
                self.log("=" * 80)
                
                # FIXED: Output complete JSON without any limits
                complete_json = json.dumps(response_data, indent=2, ensure_ascii=False)
                
                # Split into smaller chunks to avoid GUI truncation
                chunk_size = 1000
                for i in range(0, len(complete_json), chunk_size):
                    chunk = complete_json[i:i + chunk_size]
                    self.console.append(chunk)
                    # Force GUI update for each chunk
                    self.console.repaint()
                    
                self.log("=" * 80)
                
            except Exception as e:
                self.log(f"📋 Raw Response Text: {response.text}")
                self.log(f"JSON Parse Error: {e}")

            if response.status_code == 200:
                self.log("✅ POST request completed successfully!")
            else:
                self.log(f"⚠️ POST request completed with status {response.status_code}")
                
        except Exception as e:
            self.log(f"❌ Error sending POST request: {e}")
            import traceback
            self.log(f"📋 Full error: {traceback.format_exc()}")

class SimpleButton(QPushButton):
    """Clean, simple button"""
    def __init__(self, text, primary=False):
        super().__init__(text)
        if primary:
            self.setStyleSheet("""
                QPushButton {
                    background-color: #0066cc;
                    color: white;
                    border: none;
                    border-radius: 8px;
                    padding: 12px 24px;
                    font-size: 14px;
                    font-weight: 600;
                    min-height: 20px;
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
        else:
            self.setStyleSheet("""
                QPushButton {
                    background-color: #f8f9fa;
                    color: #495057;
                    border: 1px solid #dee2e6;
                    border-radius: 8px;
                    padding: 12px 24px;
                    font-size: 14px;
                    min-height: 20px;
                }
                QPushButton:hover {
                    background-color: #e9ecef;
                    border-color: #adb5bd;
                }
                QPushButton:pressed {
                    background-color: #dee2e6;
                }
                QPushButton:disabled {
                    background-color: #f8f9fa;
                    color: #6c757d;
                    border-color: #dee2e6;
                }
            """)


class StatusIndicator(QLabel):
    """Simple status indicator with working stylesheet"""
    def __init__(self):
        super().__init__("● Klar") 
        # Set initial style without dynamic color
        self.setStyleSheet("""
            QLabel {
                color: #6c757d;
                font-size: 14px;
                font-weight: bold;
                padding: 8px 12px;
                background-color: #f8f9fa;
                border-radius: 20px;
                border: 1px solid #dee2e6;
            }
        """)
        
    def set_status(self, status, color="#6c757d"):
        """Set status text and color - FIXED METHOD"""
        self.setText(f"● {status}")
        # Use hardcoded colors instead of dynamic CSS
        if color == "#28a745":  # Green/Running
            style = """
                QLabel {
                    color: #28a745;
                    font-size: 14px;
                    font-weight: bold;
                    padding: 8px 12px;
                    background-color: #d4edda;
                    border-radius: 20px;
                    border: 1px solid #c3e6cb;
                }
            """
        elif color == "#dc3545":  # Red/Error
            style = """
                QLabel {
                    color: #dc3545;
                    font-size: 14px;
                    font-weight: bold;
                    padding: 8px 12px;
                    background-color: #f8d7da;
                    border-radius: 20px;
                    border: 1px solid #f5c6cb;
                }
            """
        elif color == "#ffc107":  # Yellow/Warning
            style = """
                QLabel {
                    color: #856404;
                    font-size: 14px;
                    font-weight: bold;
                    padding: 8px 12px;
                    background-color: #fff3cd;
                    border-radius: 20px;
                    border: 1px solid #ffeaa7;
                }
            """
        else:  # Default gray
            style = """
                QLabel {
                    color: #6c757d;
                    font-size: 14px;
                    font-weight: bold;
                    padding: 8px 12px;
                    background-color: #f8f9fa;
                    border-radius: 20px;
                    border: 1px solid #dee2e6;
                }
            """
        self.setStyleSheet(style)

class SettingsWindow(QWidget):
    """Settings window as a separate independent window"""
    def __init__(self, parent=None):
        super().__init__(parent, Qt.Window)  # Make it a proper window
        self.parent_window = parent
        self.console_widget = None
        self.init_ui()
        
    def init_ui(self):
        """Initialize settings UI - FIXED CONSOLE VERSION"""
        self.setWindowTitle("Innstillinger - AkademiTrack")
        self.setGeometry(300, 300, 800, 600)
        self.setMinimumSize(600, 400)
        
        # Set window flags for proper behavior
        self.setWindowFlags(Qt.Window | Qt.WindowCloseButtonHint | Qt.WindowMinMaxButtonsHint)
        
        self.setStyleSheet("""
            QWidget {
                background-color: white;
                color: #212529;
                font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', system-ui, sans-serif;
            }
            QPlainTextEdit {
                background-color: #0b0c10;
                color: #e6edf3;
                font-family: 'SF Mono', 'Consolas', 'Fira Code', monospace;
                font-size: 12px;
                border: 1px solid #1f2833;
                border-radius: 8px;
                padding: 10px;
            }
        """)
        
        layout = QVBoxLayout(self)
        layout.setSpacing(20)
        layout.setContentsMargins(20, 20, 20, 20)
        
        # Header
        header_label = QLabel("Innstillinger")
        header_label.setFont(QFont("SF Pro Display", 18, QFont.Bold))
        header_label.setStyleSheet("color: #212529; margin-bottom: 10px;")
        layout.addWidget(header_label)
        
        # Console section
        console_label = QLabel("Konsoll")
        console_label.setFont(QFont("SF Pro Text", 14, QFont.Bold))
        console_label.setStyleSheet("color: #212529; margin-bottom: 8px;")
        layout.addWidget(console_label)
        
        # FIXED: Create console widget without any limits
        self.console_widget = QPlainTextEdit()
        self.console_widget.setReadOnly(True)
        self.console_widget.setLineWrapMode(QPlainTextEdit.WidgetWidth)
        # FIXED: Remove ALL block count limits
        self.console_widget.setMaximumBlockCount(0)  
        self.console_widget.document().setMaximumBlockCount(0) 
        
        layout.addWidget(self.console_widget, 1)
        
        # Button layout
        button_layout = QHBoxLayout()
        button_layout.addStretch()

        self.post_button = SimpleButton("POST Request")
        self.post_button.clicked.connect(self.open_post_request)
        button_layout.addWidget(self.post_button)

        clear_button = SimpleButton("Tøm konsoll")
        clear_button.clicked.connect(self.clear_console)
        button_layout.addWidget(clear_button)

        layout.addLayout(button_layout)
        
        # Copy existing console content if parent has it
        if self.parent_window and hasattr(self.parent_window, 'console'):
            self.console_widget.setPlainText(self.parent_window.console.toPlainText())
            
    def open_post_request(self):
        """Open the POST request window from settings"""
        if not self.parent_window or not self.parent_window.bot:
            self.append_text("❌ No bot instance available")
            return
            
        if self.parent_window.is_running:
            self.append_text("⚠️ Stop automation first before using POST Request")
            return
        
        # Don't check login status - just check if cookies exist
        if not os.path.exists(self.parent_window.bot.cookies_file):
            self.append_text("❌ No cookies found - please run Setup & Login first")
            return
            
        self.append_text("🚀 Opening POST Request window...")
        self.post_window = PostRequestWindow(self.parent_window.bot, self)
        self.post_window.show()
    
    def append_text(self, text):
        """Append text to console without limits - FIXED VERSION"""
        if text.strip():
            # FIXED: Remove all block limits and use insertPlainText for large content
            cursor = self.console_widget.textCursor()
            cursor.movePosition(cursor.End)
            self.console_widget.setTextCursor(cursor)
            self.console_widget.insertPlainText(text.strip() + "\n")
            
            # Scroll to bottom
            scrollbar = self.console_widget.verticalScrollBar()
            scrollbar.setValue(scrollbar.maximum())
        
    def clear_console(self):
        """Clear the console output"""
        self.console_widget.clear()
        # Also clear parent console if it exists
        if self.parent_window and hasattr(self.parent_window, 'console'):
            self.parent_window.console.clear()
        
    def closeEvent(self, event):
        """Handle window close"""
        event.accept()

class SetupThread(QThread):
    """Thread for handling setup operations"""
    message_signal = pyqtSignal(str)
    finished_signal = pyqtSignal(bool)
    progress_signal = pyqtSignal(int)
    
    def __init__(self, bot):
        super().__init__()
        self.bot = bot
    
    def run(self):
        try:
            self.progress_signal.emit(10)
            self.message_signal.emit("Starting setup...")
            
            self.progress_signal.emit(30)
            self.message_signal.emit("Opening browser - please complete login")
            
            self.progress_signal.emit(50)
            success = self.bot.get_browser_cookies_enhanced()
            
            if success:
                self.progress_signal.emit(90)
                self.message_signal.emit("Setup completed successfully!")
                self.progress_signal.emit(100)
                self.finished_signal.emit(True)
            else:
                self.progress_signal.emit(100)
                self.message_signal.emit("Setup failed - please try again")
                self.finished_signal.emit(False)
                
        except Exception as e:
            self.progress_signal.emit(100)
            self.message_signal.emit(f"Setup error: {e}")
            self.finished_signal.emit(False)


class SchedulerThread(QThread):
    """Thread for running the bot scheduler"""
    message_signal = pyqtSignal(str)
    status_signal = pyqtSignal(str)
    
    def __init__(self, bot):
        super().__init__()
        self.bot = bot
        self.should_stop = False
    
    def run(self):
        try:
            self.status_signal.emit("Running")
            if self.bot.running:
                self.bot.run_scheduler()
            self.status_signal.emit("Stopped")
        except Exception as e:
            self.message_signal.emit(f"Scheduler error: {e}")
            self.status_signal.emit("Error")
    
    def stop(self):
        """Stop the thread gracefully"""
        self.should_stop = True
        if self.bot:
            self.bot.stop_scheduler()


class AkademiTrackWindow(QMainWindow):
    """Simple, clean main window with notification system"""
    console_signal = pyqtSignal(str)
    
    def __init__(self):
        """Initialize the main window"""
        super().__init__()
        self.bot = None
        self.setup_thread = None
        self.scheduler_thread = None
        self.post_window = None  # CHANGED from manual_window
        self.settings_window = None
        self.is_running = False
        self._orig_stdout = None
        self._orig_stderr = None
        self.active_notifications = []  # Track active notifications
        
        # Create a hidden console for logging (not displayed in main UI)
        self.console = QPlainTextEdit()
        self.console.setReadOnly(True)
        self.console.setLineWrapMode(QPlainTextEdit.NoWrap)
        self.console.setMaximumBlockCount(0)  # Remove limits
        self.console.hide()
        
        self.init_ui()
        self.init_bot()
        self.log_message("Klikk 'Oppsett og innlogging' først, deretter 'Start automatisering' eller 'POST Request'")
        
        self.console_signal.connect(self.append_console)
        self._redirect_std_streams()
    
    

    def show_notification(self, message, notification_type="info", duration=5000):
        """Show enhanced professional notification with better stacking"""
        try:
            # Prevent spam notifications
            current_time = time.time()
            message_key = f"{message}_{notification_type}"
            
            if not hasattr(self, '_last_notifications'):
                self._last_notifications = {}
            
            # Check for duplicate within 3 seconds
            if message_key in self._last_notifications:
                if current_time - self._last_notifications[message_key] < 3.0:
                    return
            
            self._last_notifications[message_key] = current_time
            
            # Create and show notification
            notification = NotificationWidget(message, notification_type, duration, None)
            notification.show_notification()
            
            # Clean up old notification references periodically
            if len(self._last_notifications) > 50:  # Keep only recent ones
                cutoff_time = current_time - 300  # 5 minutes
                self._last_notifications = {
                    k: v for k, v in self._last_notifications.items() 
                    if v > cutoff_time
                }
            
        except Exception as e:
            print(f"Error showing notification: {e}")
        
    def init_ui(self):
        """Initialize clean, simple UI"""
        self.setWindowTitle("AkademiTrack V1")
        self.setGeometry(200, 200, 600, 440)
        self.setMinimumSize(750, 440)
        self.setMaximumSize(16777215, 16777215)
        
        self.setStyleSheet("""
            QMainWindow {
                background-color: white;
            }
            QWidget {
                background-color: white;
                color: #212529;
                font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', system-ui, sans-serif;
            }
            QTextEdit, QPlainTextEdit {
                background-color: #f8f9fa;
                color: #495057;
                font-family: 'SF Mono', 'Monaco', 'Inconsolata', 'Fira Code', monospace;
                font-size: 12px;
                border: 1px solid #dee2e6;
                border-radius: 8px;
                padding: 12px;
                line-height: 1.4;
            }
            QProgressBar {
                border: 1px solid #dee2e6;
                border-radius: 8px;
                background-color: #f8f9fa;
                text-align: center;
                color: #495057;
                font-weight: 500;
            }
            QProgressBar::chunk {
                background-color: #0066cc;
                border-radius: 7px;
            }
        """)
        
        central_widget = QWidget()
        self.setCentralWidget(central_widget)
        
        main_layout = QVBoxLayout(central_widget)
        main_layout.setSpacing(24)
        main_layout.setContentsMargins(40, 40, 40, 40)
        
        self.create_header(main_layout)
        self.create_controls(main_layout)
        self.create_status_area(main_layout)
        
        main_layout.addStretch()
    
    def create_header(self, parent_layout):
        """Create simple header with centered status"""
        header_layout = QVBoxLayout()
        
        title = QLabel("AkademiTrack")
        title.setFont(QFont("SF Pro Display", 24, QFont.Bold))
        title.setStyleSheet("color: #212529; margin-bottom: 4px;")
        title.setAlignment(Qt.AlignCenter)
        
        subtitle = QLabel("Automatisk frammøteregistrering")
        subtitle.setFont(QFont("SF Pro Text", 14))
        subtitle.setStyleSheet("color: #495057; font-weight: 500;")
        subtitle.setAlignment(Qt.AlignCenter)
        
        status_layout = QHBoxLayout()
        status_layout.addStretch()
        self.status_indicator = StatusIndicator()
        status_layout.addWidget(self.status_indicator)
        status_layout.addStretch()
        
        header_layout.addWidget(title)
        header_layout.addWidget(subtitle)
        header_layout.addSpacing(20)
        header_layout.addLayout(status_layout)
        
        parent_layout.addLayout(header_layout)
    
    def create_controls(self, parent_layout):
        """Create control buttons in two rows"""
        controls_layout = QVBoxLayout()
        
        progress_layout = QHBoxLayout()
        progress_layout.addStretch()
        
        self.progress_bar = QProgressBar()
        self.progress_bar.setVisible(False)
        self.progress_bar.setTextVisible(True)
        self.progress_bar.setFixedHeight(25)
        self.progress_bar.setFixedWidth(400)
        self.progress_bar.setStyleSheet("""
            QProgressBar {
                border: 1px solid #dee2e6;
                border-radius: 8px;
                background-color: #f8f9fa;
                text-align: center;
                color: #495057;
                font-weight: 500;
                font-size: 12px;
            }
            QProgressBar::chunk {
                background-color: #0066cc;
                border-radius: 7px;
            }
        """)
        
        progress_layout.addWidget(self.progress_bar)
        progress_layout.addStretch()
        controls_layout.addLayout(progress_layout)
        
        # First row - Main buttons (Setup and Start)
        main_button_container = QHBoxLayout()
        main_button_container.addStretch()
        
        main_button_layout = QHBoxLayout()
        main_button_layout.setSpacing(12)
        
        self.setup_button = SimpleButton("Oppsett og innlogging")
        self.setup_button.clicked.connect(self.setup_and_login)
        
        self.start_button = SimpleButton("Start automatisering", primary=True)
        self.start_button.clicked.connect(self.toggle_automation)
        
        main_button_layout.addWidget(self.setup_button)
        main_button_layout.addWidget(self.start_button)
        
        main_button_container.addLayout(main_button_layout)
        main_button_container.addStretch()
        
        # Second row - Secondary buttons (Manual and Settings)
        secondary_button_container = QHBoxLayout()
        secondary_button_container.addStretch()
        
        secondary_button_layout = QHBoxLayout()
        secondary_button_layout.setSpacing(12)
        
        self.settings_button = SimpleButton("Innstillinger")
        self.settings_button.clicked.connect(self.open_settings)
        
        secondary_button_layout.addWidget(self.settings_button)
        
        secondary_button_container.addLayout(secondary_button_layout)
        secondary_button_container.addStretch()
        
        # Add both button rows to controls layout with spacing
        controls_layout.addLayout(main_button_container)
        controls_layout.addSpacing(12)  # Space between button rows
        controls_layout.addLayout(secondary_button_container)
        
        parent_layout.addLayout(controls_layout)

    def open_settings(self):
        """Open settings window as independent window"""
        # Always create a new window (don't reuse)
        self.settings_window = SettingsWindow(self)
        self.settings_window.show()
        
        # Position it relative to main window
        main_pos = self.pos()
        self.settings_window.move(main_pos.x() + 50, main_pos.y() + 50)
    
    def create_status_area(self, parent_layout):
        """Create minimal status area - only for critical errors"""
        status_layout = QVBoxLayout()
        
        self.status_message = QLabel("")
        self.status_message.setFont(QFont("SF Pro Text", 12))
        self.status_message.setStyleSheet("""
            color: #721c24;
            background-color: #f8d7da;
            border: 1px solid #f5c6cb;
            border-radius: 6px;
            padding: 8px 12px;
            margin: 4px 0;
        """)
        self.status_message.setVisible(False)
        self.status_message.setWordWrap(True)
        self.status_message.setMaximumHeight(80)
        
        status_layout.addWidget(self.status_message)
        parent_layout.addLayout(status_layout)
    
    def init_bot(self):
        """Initialize the bot"""
        self.install_requirements()
        self.bot = ImprovedISkoleBot(gui_callback=self.log_message)
        # Connect the scheduler stopped signal to our handler
        self.bot.scheduler_stopped_signal.connect(self.on_scheduler_stopped)
    
    def _redirect_std_streams(self):
        class _StdRedirector:
            def __init__(self, write_cb):
                self._write_cb = write_cb
            def write(self, text):
                if text.strip():
                    self._write_cb(text)
            def flush(self):
                pass
        
        self._orig_stdout = sys.stdout
        self._orig_stderr = sys.stderr
        sys.stdout = _StdRedirector(lambda t: self.console_signal.emit(t))
        sys.stderr = _StdRedirector(lambda t: self.console_signal.emit(t))
    
    def append_console(self, text):
        """Append text to console and settings window - handles large JSON properly"""
        if text.strip():
            # Update hidden main console - NO LIMITS for full JSON
            cursor = self.console.textCursor()
            cursor.movePosition(cursor.End)
            self.console.setTextCursor(cursor)
            self.console.insertPlainText(text.strip() + "\n")
            
            scrollbar = self.console.verticalScrollBar()
            scrollbar.setValue(scrollbar.maximum())
            
            # Update settings window console if it's open - NO LIMITS
            if hasattr(self, 'settings_window') and self.settings_window and hasattr(self.settings_window, 'console_widget'):
                try:
                    # Use insertPlainText for large content instead of append
                    cursor = self.settings_window.console_widget.textCursor()
                    cursor.movePosition(cursor.End)
                    self.settings_window.console_widget.setTextCursor(cursor)
                    self.settings_window.console_widget.insertPlainText(text.strip() + "\n")
                    
                    scrollbar = self.settings_window.console_widget.verticalScrollBar()
                    scrollbar.setValue(scrollbar.maximum())
                except Exception as e:
                    print(f"Error updating settings console: {e}")
    
    def install_requirements(self):
        """Install required packages quietly"""
        required_packages = [
            'selenium', 'webdriver-manager', 'psutil', 
            'requests', 'schedule', 'browser-cookie3'
        ]
        
        missing_packages = []
        for package in required_packages:
            try:
                __import__(package.replace('-', '_'))
            except ImportError:
                missing_packages.append(package)
        
        if missing_packages:
            try:
                for package in missing_packages:
                    subprocess.check_call([sys.executable, "-m", "pip", "install", package], 
                                        capture_output=True)
            except Exception as e:
                self.show_error(f"Failed to install packages: {e}")
    
    def log_message(self, message):
        """Enhanced log message handler with professional notifications"""
        try:
            should_notify = False
            notification_type = "info"
            notification_message = ""
            
            message_lower = message.lower()
            
            # SUCCESS NOTIFICATIONS
            if ('🎯 registrert studietid' in message_lower or 
                'attendance registered successfully' in message_lower):
                should_notify = True
                notification_type = "success"
                
                # Extract time number for more specific message
                if 'timenr' in message_lower:
                    import re
                    timenr_match = re.search(r'timenr (\w+)', message_lower)
                    if timenr_match:
                        notification_message = f"Studietid registrert - Time {timenr_match.group(1)}"
                    else:
                        notification_message = "Studietid registrert"
                else:
                    notification_message = "Studietid registrert"
            
            # COMPLETION NOTIFICATIONS
            elif ('🏁 all stu classes for the day are completed' in message_lower or
                '🎊 all stu classes for the day are completed' in message_lower):
                should_notify = True
                notification_type = "completed"
                notification_message = "Alle studietimer fullført i dag!"
            
            # SETUP SUCCESS
            elif ('🎉 setup completed successfully' in message_lower or
                'setup completed successfully' in message_lower):
                should_notify = True
                notification_type = "success"
                notification_message = "Oppsett fullført!"
            
            # ERROR NOTIFICATIONS  
            elif ('registration failed' in message_lower and 'status' in message_lower):
                should_notify = True
                notification_type = "error"
                notification_message = "Registrering mislyktes"
            
            elif 'setup failed' in message_lower:
                should_notify = True
                notification_type = "error"
                notification_message = "Oppsett mislyktes"
            
            # WARNING NOTIFICATIONS
            elif ('cookie authentication failed' in message_lower or 
                'session expired' in message_lower):
                should_notify = True
                notification_type = "warning"
                notification_message = "Økt utløpt - Kjør oppsett på nytt"
            
            # AUTOMATION STATUS
            elif '🚀 automatisering startet' in message_lower:
                should_notify = True
                notification_type = "info"
                notification_message = "Automatisering startet"
            
            elif 'scheduler stopped' in message_lower:
                should_notify = True
                notification_type = "info"
                notification_message = "Automatisering stoppet"
            
            # Show professional notification
            if should_notify and notification_message:
                try:
                    self.show_notification(notification_message, notification_type)
                except Exception as e:
                    print(f"Notification error: {e}")
            
            # Handle status messages and UI updates
            if any(keyword in message_lower for keyword in ['cookie', 'expired', 'login', 'setup']):
                if 'expired' in message_lower or 'invalid' in message_lower:
                    self.status_message.setText("⚠️ Økt utløpt - Kjør 'Oppsett og innlogging'")
                    self.status_message.setVisible(True)
                    self.status_message.setStyleSheet("""
                        color: #856404;
                        background-color: #fff3cd;
                        border: 1px solid #ffeaa7;
                        border-radius: 8px;
                        padding: 12px 16px;
                        margin: 8px 0;
                    """)
                    QTimer.singleShot(5000, lambda: self.status_message.setVisible(False))
            
            elif 'error' in message_lower and 'critical' in message_lower:
                self.status_message.setText(message)
                self.status_message.setVisible(True)
                self.status_message.setStyleSheet("""
                    color: #721c24;
                    background-color: #f8d7da;
                    border: 1px solid #f5c6cb;
                    border-radius: 8px;
                    padding: 12px 16px;
                    margin: 8px 0;
                """)
                QTimer.singleShot(3000, lambda: self.status_message.setVisible(False))
            
            elif 'scheduler stopped' in message_lower:
                self.status_indicator.set_status("Klar", "#6c757d")
            elif 'started' in message_lower or 'automatisering starta' in message_lower:
                self.status_indicator.set_status("Running", "#28a745")
            elif 'setup completed' in message_lower:
                self.status_indicator.set_status("Klar", "#28a745")
            elif 'failed' in message_lower:
                self.status_indicator.set_status("Feil", "#dc3545")
            
        except Exception as e:
            print(f"Error in log_message: {e}")
    
    def setup_and_login(self):
        """Handle setup and login"""
        if self.is_running:
            self.show_warning("Stopp automatisering først")
            return
            
        if self.setup_thread and self.setup_thread.isRunning():
            return
        
        self.progress_bar.setVisible(True)
        self.progress_bar.setValue(0)
        self.setup_button.setEnabled(False)
        self.setup_button.setText("Setter opp...")
        self.status_indicator.set_status("Setter opp...", "#0066cc")
        
        self.bot.last_setup_cancelled = False
        self.setup_thread = SetupThread(self.bot)
        self.setup_thread.message_signal.connect(self.log_message)
        self.setup_thread.finished_signal.connect(self.on_setup_finished)
        self.setup_thread.progress_signal.connect(self.progress_bar.setValue)
        self.setup_thread.start()
    
    def on_setup_finished(self, success):
        """Handle setup completion"""
        self.progress_bar.setVisible(False)
        self.setup_button.setEnabled(True)
        self.setup_button.setText("Oppsett og innlogging")
        
        if success:
            self.status_indicator.set_status("Klar", "#28a745")
        else:
            if getattr(self.bot, 'last_setup_cancelled', False):
                self.bot.last_setup_cancelled = False
                self.status_indicator.set_status("Ready", "#6c757d")
                return
            self.status_indicator.set_status("Oppsett mislyktes", "#dc3545")
            self.show_error("Oppsett mislyktes. Prøv igjen.")
    
    def toggle_automation(self):
        """Toggle automation - COMPLETELY CRASH PROOF"""
        try:
            # Prevent multiple rapid clicks
            if hasattr(self, '_processing_any_action') and self._processing_any_action:
                print("Action already in progress, ignoring click...")
                return
                    
            self._processing_any_action = True
            self.start_button.setEnabled(False)
            
            if not self.is_running:
                print("Toggle: Starting automation...")
                self.start_button.setText("Starter...")
                self._safe_start_automation()
            else:
                print("Toggle: Stopping automation...")  
                self.start_button.setText("Stopper...")
                self._stopping_in_progress = True
                self._safe_stop_automation()
                    
        except Exception as e:
            print(f"CRITICAL ERROR in toggle_automation: {e}")
            self._emergency_reset()
        finally:
            QTimer.singleShot(1000, self._release_action_lock)

    def start_automation(self):
        """Public start method - delegates to safe internal method"""
        print("Public start_automation called")
        self._safe_start_automation()

    def _safe_start_automation(self):
        """Internal safe start method with better cookie handling"""
        try:
            # Check cookies with better error handling
            cookies_ok = False
            cookies_exist = os.path.exists(self.bot.cookies_file)
            
            if cookies_exist:
                try:
                    if self.bot.load_cookies_from_file() and self.bot.test_cookies():
                        cookies_ok = True
                        self.log_message("✅ Using existing valid cookies")  # FIXED: log -> log_message
                    else:
                        self.log_message("🔑 Existing cookies are invalid")  # FIXED: log -> log_message
                except Exception as e:
                    self.log_message(f"Cookie validation error: {e}")  # FIXED: log -> log_message
            else:
                self.log_message("🔑 No cookies file found")  # FIXED: log -> log_message

            if not cookies_ok:
                # Show specific notification for first-time setup needed
                self.show_notification("Trenger oppsett først", "warning", 3000)
                self.log_message("❌ Please run 'Oppsett og innlogging' first before starting automation")  # FIXED: log -> log_message
                self._reset_start_button()
                return

            # Start the automation
            self.bot.running = True
            
            # Kill any existing thread first
            self._force_kill_scheduler_thread()
            
            # Create new thread
            self.scheduler_thread = SchedulerThread(self.bot)
            self.scheduler_thread.message_signal.connect(self.log_message)  # This is correct
            self.scheduler_thread.status_signal.connect(self.update_status)
            self.scheduler_thread.start()

            # Update state
            self.is_running = True
            self._set_stop_button_style()
            self.setup_button.setEnabled(False)
            self.status_indicator.set_status("Kjører", "#28a745")
            
            # Show start notification only once
            self.show_notification("Automatisering startet", "info", 2000)
            
            print("Safe start completed")  # Use print for debug, not self.log
            
        except Exception as e:
            print(f"Error in _safe_start_automation: {e}")  # Use print for debug
            self.show_notification("Feil ved oppstart", "error", 3000)
            self._emergency_reset()

    
    def stop_automation(self):
        """Public stop method - delegates to safe internal method"""  
        print("Kode: stopp_automatisering kalt")
        self._safe_stop_automation()

    def _safe_stop_automation(self):
        """Internal safe stop method - BULLETPROOF"""
        try:
            print("Safe stop starting...")  # Use print for debug
            
            # Set stopping state immediately
            self.is_running = False
            self.status_indicator.set_status("Stopper...", "#ffc107")
            
            # Stop bot
            if hasattr(self, 'bot') and self.bot:
                try:
                    self.bot.running = False
                    self.bot.stop_scheduler()
                    print("Bot stopped")  # Use print for debug
                except:
                    print("Error stopping bot, continuing...")  # Use print for debug
            
            # Force kill thread
            self._force_kill_scheduler_thread()
            
            # Reset UI
            self._reset_start_button()
            self.setup_button.setEnabled(True)
            self.status_indicator.set_status("Stoppet", "#6c757d")
            
            # Show stop notification
            self.show_notification("Automatisering stoppet", "info", 2000)
            
            print("Safe stop completed")  # Use print for debug
            
        except Exception as e:
            print(f"Error in _safe_stop_automation: {e}")  # Use print for debug
            self._emergency_reset()

    def _force_kill_scheduler_thread(self):
        """Force kill any existing scheduler thread"""
        try:
            if hasattr(self, 'scheduler_thread') and self.scheduler_thread:
                print("stopper eksisterende planleggertråd...")
                
                # Disconnect all signals first
                try:
                    self.scheduler_thread.message_signal.disconnect()
                    self.scheduler_thread.status_signal.disconnect()
                except:
                    pass
                
                # Try graceful stop first
                if self.scheduler_thread.isRunning():
                    try:
                        self.scheduler_thread.stop()
                        if not self.scheduler_thread.wait(1000):  # Wait 1 second max
                            print("Grasiøs stopp mislyktes, avsluttes...")
                            self.scheduler_thread.terminate()
                            self.scheduler_thread.wait(500)  # Wait 0.5 seconds for termination
                    except:
                        print("Grasiøs stopp mislyktes, og fremtvinger avslutning...")
                        try:
                            self.scheduler_thread.terminate()
                            self.scheduler_thread.wait(500)
                        except:
                            pass
                
                self.scheduler_thread = None
                print("Planleggertråden avsluttet")
                
        except Exception as e:
            print(f"Feil med avslutting av planleggertråd: {e}")
            # Set to None anyway
            self.scheduler_thread = None
    
    def _reset_start_button(self):
        """Reset start button to initial state"""
        self.start_button.setText("Start automatisering")
        self.start_button.setEnabled(True)
        self.start_button.setStyleSheet("""
            QPushButton {
                background-color: #0066cc !important;
                color: white !important;
                border: none;
                border-radius: 8px;
                padding: 12px 24px;
                font-size: 14px;
                font-weight: 600;
                min-height: 20px;
            }
            QPushButton:hover {
                background-color: #0052a3 !important;
            }
        """)

    def _set_stop_button_style(self):
        """Set button to stop style"""
        self.start_button.setText("Stopp automatisering")
        self.start_button.setEnabled(True)
        self.start_button.setStyleSheet("""
            QPushButton {
                background-color: #dc3545 !important;
                color: white !important;
                border: none;
                border-radius: 8px;
                padding: 12px 24px;
                font-size: 14px;
                font-weight: 600;
                min-height: 20px;
            }
            QPushButton:hover {
                background-color: #c82333 !important;
            }
        """)

    def _release_action_lock(self):
        """Release the action lock and stop flag"""
        try:
            self._processing_any_action = False
            if hasattr(self, '_stopping_in_progress'):
                self._stopping_in_progress = False
            print("Action lock released")
        except:
            pass

    def _emergency_reset(self):
        """Emergency reset all states"""
        try:
            print("EMERGENCY RESET!")
            self.is_running = False
            self._force_kill_scheduler_thread()
            self._reset_start_button()
            self.setup_button.setEnabled(True)
            self.status_indicator.set_status("Reset", "#dc3545")
            
            # Clear all locks
            self._processing_any_action = False
            if hasattr(self, '_starting'):
                self._starting = False
            if hasattr(self, '_stopping'):
                self._stopping = False
                
        except Exception as e:
            print(f"Error in emergency reset: {e}")


    def on_scheduler_stopped(self):
        """Handle when scheduler stops itself - prevent duplicate notifications"""
        try:
            print("Scheduler stopped signal received")
            
            # FIXED: Only update if we're actually running and not already stopping
            if (hasattr(self, 'is_running') and self.is_running and 
                not getattr(self, '_stopping_in_progress', False)):
                
                self.is_running = False
                self._reset_start_button()
                self.setup_button.setEnabled(True)
                self.status_indicator.set_status("Fullført", "#6c757d")
                
        except Exception as e:
            print(f"Error in on_scheduler_stopped: {e}")
    
    def update_status(self, status):
        """Update status indicator"""
        if not self.is_running and status == "Running":
            return
            
        if status == "Kjører":
            self.status_indicator.set_status("Running", "#28a745")
        elif status == "Feil":
            self.status_indicator.set_status("Error", "#dc3545")
            self.stop_automation()
        elif status == "Stoppet":
            self.stop_automation()
    
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
        msg.setWindowTitle("Advarsel")
        msg.setText(message)
        msg.exec_()
    
    def show_error(self, message):
        """Show error dialog"""
        msg = QMessageBox(self)
        msg.setIcon(QMessageBox.Critical)
        msg.setWindowTitle("Feil")
        msg.setText(message)
        msg.exec_()
    
    def closeEvent(self, event):
        """Handle window close"""
        if self.is_running:
            reply = QMessageBox.question(
                self, 
                'Exit', 
                'Automatisering kjører. Stopp og avslutt?',
                QMessageBox.Yes | QMessageBox.No
            )
            if reply == QMessageBox.Yes:
                self.stop_automation()
                time.sleep(0.5)
                try:
                    if self._orig_stdout:
                        sys.stdout = self._orig_stdout
                    if self._orig_stderr:
                        sys.stderr = self._orig_stderr
                except Exception:
                    pass
                event.accept()
            else:
                event.ignore()
        else:
            try:
                if self._orig_stdout:
                    sys.stdout = self._orig_stdout
                if self._orig_stderr:
                    sys.stderr = self._orig_stderr
            except Exception:
                pass
            event.accept()


def main():
    """Main function"""
    app = QApplication(sys.argv)
    app.setStyle('Fusion')
    
    try:
        window = AkademiTrackWindow()
        window.show()
        
        screen = app.primaryScreen().geometry()
        window.move(
            (screen.width() - window.width()) // 2,
            (screen.height() - window.height()) // 2
        )
        
        sys.exit(app.exec_())
        
    except Exception as e:
        print(f"Kunne ikke starte: {e}")
        sys.exit(1)


if __name__ == "__main__":
    main()