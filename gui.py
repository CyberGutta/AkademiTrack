import sys
import os
import time
import platform
import subprocess
from datetime import datetime
from pathlib import Path

try:
    from PyQt5.QtWidgets import (
        QApplication, QMainWindow, QVBoxLayout, QHBoxLayout, 
        QWidget, QPushButton, QTextEdit, QLabel, QMessageBox,
        QFrame, QProgressBar, QSizePolicy
    )
    from PyQt5.QtCore import QThread, pyqtSignal, Qt, QTimer
    from PyQt5.QtGui import QFont, QPalette, QColor, QIcon
except ImportError:
    print("PyQt5 is required. Please install it with: pip install PyQt5")
    sys.exit(1)

# Import our backend
try:
    from backend import ImprovedISkoleBot
except ImportError:
    print("backend.py file is required in the same directory!")
    sys.exit(1)


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
    """Simple status indicator"""
    def __init__(self):
        super().__init__("● Ready")
        self.setStyleSheet("""
            QLabel {
                color: #6c757d;
                font-size: 14px;
                font-weight: 500;
                padding: 8px 12px;
                background-color: #f8f9fa;
                border-radius: 20px;
                border: 1px solid #dee2e6;
            }
        """)
        
    def set_status(self, status, color="#6c757d"):
        self.setText(f"● {status}")
        self.setStyleSheet(f"""
            QLabel {{
                color: {color};
                font-size: 14px;
                font-weight: 500;
                padding: 8px 12px;
                background-color: #f8f9fa;
                border-radius: 20px;
                border: 1px solid #dee2e6;
            }}
        """)


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
            # Check if we should stop before starting
            while not self.should_stop:
                if self.bot.running:  # Check bot's running flag
                    self.bot.run_scheduler()
                    break
                self.msleep(100)  # Small delay
        except Exception as e:
            self.message_signal.emit(f"Scheduler error: {e}")
            self.status_signal.emit("Error")
    
    def stop(self):
        """Stop the thread gracefully"""
        self.should_stop = True
        if self.bot:
            self.bot.running = False


class AkademiTrackWindow(QMainWindow):
    """Simple, clean main window"""
    # Signal to append text to the console from any thread
    console_signal = pyqtSignal(str)
    
    def __init__(self):
        super().__init__()
        self.bot = None
        self.setup_thread = None
        self.scheduler_thread = None
        self.is_running = False
        # Console redirection holders
        self._orig_stdout = None
        self._orig_stderr = None
        
        self.init_ui()
        self.init_bot()
        self.log_message("Click 'Setup & Login' first, then 'Start Automation'")
        
        # Connect console signal after UI is ready
        self.console_signal.connect(self.append_console)
        
        # Redirect stdout/stderr to in-app console
        self._redirect_std_streams()
        
    def init_ui(self):
        """Initialize clean, simple UI"""
        self.setWindowTitle("AkademiTrack V1")
        # Default window size as requested
        self.setGeometry(200, 200, 850, 440)
        self.setMinimumSize(850, 440)
        # Allow resizing; do not cap the maximum size too low
        self.setMaximumSize(16777215, 16777215)
        
        # Apply clean theme
        self.setStyleSheet("""
            QMainWindow {
                background-color: white;
            }
            QWidget {
                background-color: white;
                color: #212529;
                font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', system-ui, sans-serif;
            }
            QTextEdit {
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
        
        # Central widget
        central_widget = QWidget()
        self.setCentralWidget(central_widget)
        
        # Main horizontal layout: left (controls) | right (console)
        main_hbox = QHBoxLayout(central_widget)
        main_hbox.setSpacing(24)
        main_hbox.setContentsMargins(20, 20, 20, 20)
        
        # Left vertical layout (existing UI)
        left_layout = QVBoxLayout()
        left_layout.setSpacing(20)
        left_layout.setContentsMargins(10, 10, 10, 10)
        
        # Header
        self.create_header(left_layout)
        
        # Controls
        self.create_controls(left_layout)
        
        # Status messages area
        self.create_status_area(left_layout)
        
        # Right console panel
        right_layout = QVBoxLayout()
        right_layout.setSpacing(12)
        right_layout.setContentsMargins(0, 6, 0, 6)
        
        console_label = QLabel("Console")
        console_label.setFont(QFont("SF Pro Text", 12, QFont.Bold))
        console_label.setStyleSheet("color: #212529;")
        right_layout.addWidget(console_label)
        
        from PyQt5.QtWidgets import QPlainTextEdit
        self.console = QPlainTextEdit()
        self.console.setReadOnly(True)
        self.console.setLineWrapMode(QPlainTextEdit.NoWrap)
        self.console.setStyleSheet("""
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
        self.console.setMinimumWidth(360)
        right_layout.addWidget(self.console, 1)
        
        # Assemble
        main_hbox.addLayout(left_layout, 2)
        main_hbox.addLayout(right_layout, 3)
    
    def create_header(self, parent_layout):
        """Create simple header"""
        header_layout = QVBoxLayout()
        
        # Title
        title = QLabel("AkademiTrack")
        title.setFont(QFont("SF Pro Display", 24, QFont.Bold))
        title.setStyleSheet("color: #212529; margin-bottom: 4px;")
        
        # Subtitle
        subtitle = QLabel("Automatic attendance registration")
        subtitle.setFont(QFont("SF Pro Text", 14))
        subtitle.setStyleSheet("color: #495057; font-weight: 500;")
        
        # Status
        status_layout = QHBoxLayout()
        self.status_indicator = StatusIndicator()
        status_layout.addStretch()
        status_layout.addWidget(self.status_indicator)
        
        header_layout.addWidget(title)
        header_layout.addWidget(subtitle)
        header_layout.addSpacing(12)
        header_layout.addLayout(status_layout)
        
        parent_layout.addLayout(header_layout)
    
    def create_controls(self, parent_layout):
        """Create control buttons"""
        controls_layout = QVBoxLayout()
        
        # Progress bar (hidden by default)
        self.progress_bar = QProgressBar()
        self.progress_bar.setVisible(False)
        self.progress_bar.setTextVisible(True)
        self.progress_bar.setFixedHeight(25)
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
        controls_layout.addWidget(self.progress_bar)
        
        # Buttons
        button_layout = QHBoxLayout()
        button_layout.setSpacing(12)
        
        self.setup_button = SimpleButton("Setup & Login")
        self.setup_button.clicked.connect(self.setup_and_login)
        
        self.start_button = SimpleButton("Start Automation", primary=True)
        self.start_button.clicked.connect(self.toggle_automation)
        
        button_layout.addWidget(self.setup_button)
        button_layout.addWidget(self.start_button)
        
        controls_layout.addLayout(button_layout)
        parent_layout.addLayout(controls_layout)
    
    def create_status_area(self, parent_layout):
        """Create minimal status area - only for critical errors"""
        status_layout = QVBoxLayout()
        
        # Only show for critical errors
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
    
    # ---------- Console plumbing ----------
    def _redirect_std_streams(self):
        class _StdRedirector:
            def __init__(self, write_cb):
                self._write_cb = write_cb
            def write(self, text):
                if text:
                    self._write_cb(text)
            def flush(self):
                pass
        
        # Store originals so we can restore on exit
        self._orig_stdout = sys.stdout
        self._orig_stderr = sys.stderr
        sys.stdout = _StdRedirector(lambda t: self.console_signal.emit(t))
        sys.stderr = _StdRedirector(lambda t: self.console_signal.emit(t))
    
    def append_console(self, text: str):
        """Append text to console safely (auto-scroll)."""
        # Normalize newlines
        self.console.moveCursor(self.console.textCursor().End)
        self.console.insertPlainText(text)
        self.console.moveCursor(self.console.textCursor().End)
    
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
        """Show only critical messages, update status indicator for others.
        Do NOT print here to avoid duplicate lines because stdout is already
        redirected to the in-app console and backend.log() prints.
        """
        # Show cookie-related messages as warnings
        if any(keyword in message.lower() for keyword in ['cookie', 'expired', 'login', 'setup']):
            if 'expired' in message.lower() or 'invalid' in message.lower():
                self.status_message.setText("⚠️ Session expired - Please run 'Setup & Login'")
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
        
        # Only show critical errors in message area
        elif 'error' in message.lower() and 'critical' in message.lower():
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
        
        # Update status indicator based on message content
        elif 'started' in message.lower() or 'automation started' in message.lower():
            self.status_indicator.set_status("Running", "#28a745")
        elif 'stopped' in message.lower() or 'automation stopped' in message.lower():
            self.status_indicator.set_status("Ready", "#6c757d")
        elif 'setup completed' in message.lower():
            self.status_indicator.set_status("Ready", "#28a745")
        elif 'failed' in message.lower():
            self.status_indicator.set_status("Error", "#dc3545")
    
    def setup_and_login(self):
        """Handle setup and login"""
        if self.is_running:
            self.show_warning("Please stop automation first")
            return
            
        if self.setup_thread and self.setup_thread.isRunning():
            return
        
        # Show progress
        self.progress_bar.setVisible(True)
        self.progress_bar.setValue(0)
        self.setup_button.setEnabled(False)
        self.setup_button.setText("Setting up...")
        self.status_indicator.set_status("Setting up...", "#0066cc")
        
        # Clear prior cancellation flag and start setup
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
        self.setup_button.setText("Setup & Login")
        
        if success:
            self.status_indicator.set_status("Ready", "#28a745")
            # Use a clean popup instead of ugly text box
            self.show_info("Setup completed successfully!")
        else:
            # If user cancelled by closing the login window, do not show errors
            if getattr(self.bot, 'last_setup_cancelled', False):
                # Reset flag and keep UI in Ready state without alerts
                self.bot.last_setup_cancelled = False
                self.status_indicator.set_status("Ready", "#6c757d")
                return
            # Otherwise, show a normal failure message
            self.status_indicator.set_status("Setup failed", "#dc3545")
            self.show_error("Setup failed. Please try again.")
    
    def toggle_automation(self):
        """Toggle automation on/off"""
        if not self.is_running:
            self.start_automation()
        else:
            self.stop_automation()

    def start_automation(self):
        """Start automation with immediate execution after cookie validation"""
        # Always validate cookies before starting
        cookies_ok = False
        if os.path.exists(self.bot.cookies_file):
            try:
                if self.bot.load_cookies_from_file() and self.bot.test_cookies():
                    cookies_ok = True
                else:
                    self.log_message("🔑 Session expired or invalid - running Setup & Login")
            except Exception as e:
                self.log_message(f"Cookie validation error: {e}")
        else:
            self.log_message("🔑 No cookies found - running Setup & Login")

        if not cookies_ok:
            # Trigger setup flow automatically
            self.setup_and_login()
            return

        # Set the bot running flag FIRST
        self.bot.running = True

        # Start scheduler thread
        self.scheduler_thread = SchedulerThread(self.bot)
        self.scheduler_thread.message_signal.connect(self.log_message)
        self.scheduler_thread.status_signal.connect(self.update_status)
        self.scheduler_thread.start()

        # Update UI
        self.is_running = True
        self.start_button.setText("Stop Automation")
        self.start_button.setStyleSheet("""
            QPushButton {
                background-color: #dc3545;
                color: white;
                border: none;
                border-radius: 8px;
                padding: 12px 24px;
                font-size: 14px;
                font-weight: 600;
                min-height: 20px;
            }
            QPushButton:hover {
                background-color: #c82333;
            }
        """)

        self.setup_button.setEnabled(False)
        self.status_indicator.set_status("Running", "#28a745")
        # self.log_message("Automation started with immediate check")
    
    def stop_automation(self):
        """Stop automation"""
        # Update UI immediately to prevent visual glitches
        self.start_button.setText("Stopping...")
        self.start_button.setEnabled(False)
        self.status_indicator.set_status("Stopping...", "#ffc107")
        
        # Set running flag to false first
        self.is_running = False
        
        # Stop the bot scheduler gracefully
        if self.bot:
            self.bot.stop_scheduler()
        
        # Stop the thread gracefully
        if self.scheduler_thread and self.scheduler_thread.isRunning():
            self.scheduler_thread.stop()
            
            if self.scheduler_thread.wait(1000):
                self.scheduler_thread = None
            else:
                self.scheduler_thread.terminate()
                self.scheduler_thread.wait(500)
                self.scheduler_thread = None
        
        # Update UI after thread is properly stopped
        self.start_button.setText("Start Automation")
        self.start_button.setEnabled(True)
        self.start_button.setStyleSheet("""
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
        """)
        
        self.setup_button.setEnabled(True)
        self.status_indicator.set_status("Ready", "#6c757d")
        # Remove this ugly message
        # self.log_message("Automation stopped")
    
    def update_status(self, status):
        """Update status indicator"""
        # Prevent status updates when we're stopping
        if not self.is_running and status == "Running":
            return
            
        if status == "Running":
            self.status_indicator.set_status("Running", "#28a745")
        elif status == "Error":
            self.status_indicator.set_status("Error", "#dc3545")
        else:
            self.status_indicator.set_status("Ready", "#6c757d")
    
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
    
    def show_error(self, message):
        """Show error dialog"""
        msg = QMessageBox(self)
        msg.setIcon(QMessageBox.Critical)
        msg.setWindowTitle("Error")
        msg.setText(message)
        msg.exec_()
    
    def closeEvent(self, event):
        """Handle window close"""
        if self.is_running:
            reply = QMessageBox.question(
                self, 
                'Exit', 
                'Automation is running. Stop and exit?',
                QMessageBox.Yes | QMessageBox.No
            )
            if reply == QMessageBox.Yes:
                self.stop_automation()
                time.sleep(0.5)
                # Restore stdout/stderr before exit
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
            # Restore stdout/stderr before exit
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
        
        # Center window
        screen = app.primaryScreen().geometry()
        window.move(
            (screen.width() - window.width()) // 2,
            (screen.height() - window.height()) // 2
        )
        
        sys.exit(app.exec_())
        
    except Exception as e:
        print(f"Failed to start: {e}")
        sys.exit(1)


if __name__ == "__main__":
    main()