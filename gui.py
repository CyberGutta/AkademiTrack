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
        QFrame, QProgressBar, QSizePolicy, QPlainTextEdit
    )
    from PyQt5.QtCore import QThread, pyqtSignal, Qt, QTimer
    from PyQt5.QtGui import QFont, QPalette, QColor, QIcon
except ImportError:
    print("PyQt5 is required. Please install it with: pip install PyQt5")
    sys.exit(1)

try:
    from backend import ImprovedISkoleBot
    from manual import ManualRegistrationWindow
except ImportError:
    print("backend.py or manual.py file is required in the same directory!")
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
    """Simple status indicator with working stylesheet"""
    def __init__(self):
        super().__init__("● Ready")
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
        """Initialize settings UI"""
        self.setWindowTitle("Settings - AkademiTrack")
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
        header_label = QLabel("Settings")
        header_label.setFont(QFont("SF Pro Display", 18, QFont.Bold))
        header_label.setStyleSheet("color: #212529; margin-bottom: 10px;")
        layout.addWidget(header_label)
        
        # Console section
        console_label = QLabel("Console Output")
        console_label.setFont(QFont("SF Pro Text", 14, QFont.Bold))
        console_label.setStyleSheet("color: #212529; margin-bottom: 8px;")
        layout.addWidget(console_label)
        
        # Create a new console widget for this window
        self.console_widget = QPlainTextEdit()
        self.console_widget.setReadOnly(True)
        self.console_widget.setLineWrapMode(QPlainTextEdit.NoWrap)
        self.console_widget.setStyleSheet("""
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
        
        layout.addWidget(self.console_widget, 1)
        
        # Button layout
        button_layout = QHBoxLayout()
        button_layout.addStretch()
        
        clear_button = SimpleButton("Clear Console")
        clear_button.clicked.connect(self.clear_console)
        button_layout.addWidget(clear_button)
        
        layout.addLayout(button_layout)
        
        # Copy existing console content if parent has it
        if self.parent_window and hasattr(self.parent_window, 'console'):
            self.console_widget.setPlainText(self.parent_window.console.toPlainText())
            
    def append_text(self, text):
        """Append text to console"""
        if text.strip():
            self.console_widget.appendPlainText(text.strip())
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
    """Simple, clean main window"""
    console_signal = pyqtSignal(str)
    
    def __init__(self):
        """Initialize the main window"""
        super().__init__()
        self.bot = None
        self.setup_thread = None
        self.scheduler_thread = None
        self.manual_window = None
        self.settings_window = None
        self.is_running = False
        self._orig_stdout = None
        self._orig_stderr = None
        
        # Create a hidden console for logging (not displayed in main UI)
        self.console = QPlainTextEdit()
        self.console.setReadOnly(True)
        self.console.setLineWrapMode(QPlainTextEdit.NoWrap)
        self.console.hide()  # Keep it hidden
        
        self.init_ui()
        self.init_bot()
        self.log_message("Click 'Setup & Login' first, then 'Start Automation' or 'Manual'")
        
        self.console_signal.connect(self.append_console)
        self._redirect_std_streams()
        
    def init_ui(self):
        """Initialize clean, simple UI"""
        self.setWindowTitle("AkademiTrack V1")
        self.setGeometry(200, 200, 600, 440)
        self.setMinimumSize(600, 440)
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
        
        subtitle = QLabel("Automatic attendance registration")
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
        """Create control buttons centered"""
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
        
        button_container = QHBoxLayout()
        button_container.addStretch()
        
        button_layout = QHBoxLayout()
        button_layout.setSpacing(12)
        
        self.setup_button = SimpleButton("Setup & Login")
        self.setup_button.clicked.connect(self.setup_and_login)
        
        self.start_button = SimpleButton("Start Automation", primary=True)
        self.start_button.clicked.connect(self.toggle_automation)
        
        self.manual_button = SimpleButton("Manual")
        self.manual_button.clicked.connect(self.open_manual_registration)
        
        self.settings_button = SimpleButton("Settings")
        self.settings_button.clicked.connect(self.open_settings)
        
        button_layout.addWidget(self.setup_button)
        button_layout.addWidget(self.start_button)
        button_layout.addWidget(self.manual_button)
        button_layout.addWidget(self.settings_button)
        
        button_container.addLayout(button_layout)
        button_container.addStretch()
        
        controls_layout.addLayout(button_container)
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
        """Append text to console and settings window if open"""
        if text.strip():
            # Update hidden main console
            self.console.appendPlainText(text.strip())
            scrollbar = self.console.verticalScrollBar()
            scrollbar.setValue(scrollbar.maximum())
            
            # Update settings window console if it's open
            if hasattr(self, 'settings_window') and self.settings_window and hasattr(self.settings_window, 'console_widget'):
                try:
                    self.settings_window.append_text(text.strip())
                except:
                    pass  # Settings window might be closed
            
            # Limit console to 1000 lines
            lines = self.console.toPlainText().split('\n')
            if len(lines) > 1000:
                new_text = '\n'.join(lines[-1000:])
                self.console.setPlainText(new_text)
    
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
        """Show only critical messages, update status indicator for others."""
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
        
        elif 'scheduler stopped' in message.lower():
            self.status_indicator.set_status("Ready", "#6c757d")
        elif 'started' in message.lower() or 'automation started' in message.lower():
            self.status_indicator.set_status("Running", "#28a745")
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
        
        self.progress_bar.setVisible(True)
        self.progress_bar.setValue(0)
        self.setup_button.setEnabled(False)
        self.setup_button.setText("Setting up...")
        self.status_indicator.set_status("Setting up...", "#0066cc")
        
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
            self.show_info("Setup completed successfully!")
        else:
            if getattr(self.bot, 'last_setup_cancelled', False):
                self.bot.last_setup_cancelled = False
                self.status_indicator.set_status("Ready", "#6c757d")
                return
            self.status_indicator.set_status("Setup failed", "#dc3545")
            self.show_error("Setup failed. Please try again.")
    
    def toggle_automation(self):
        """Toggle automation - COMPLETELY CRASH PROOF"""
        try:
            # Immediate return if already processing ANY action
            if hasattr(self, '_processing_any_action') and self._processing_any_action:
                print("Action already in progress, ignoring click...")
                return
                
            # Set global action lock
            self._processing_any_action = True
            
            # Disable button immediately to prevent further clicks
            self.start_button.setEnabled(False)
            
            if not self.is_running:
                print("Toggle: Starting automation...")
                self.start_button.setText("Starting...")
                self._safe_start_automation()
            else:
                print("Toggle: Stopping automation...")  
                self.start_button.setText("Stopping...")
                self._safe_stop_automation()
                
        except Exception as e:
            print(f"CRITICAL ERROR in toggle_automation: {e}")
            # Emergency reset
            self._emergency_reset()
        finally:
            # Always release the lock after a delay
            QTimer.singleShot(1000, self._release_action_lock)

    def start_automation(self):
        """Public start method - delegates to safe internal method"""
        print("Public start_automation called")
        self._safe_start_automation()

    def _safe_start_automation(self):
        """Internal safe start method"""
        try:
            # Check cookies
            cookies_ok = False
            if os.path.exists(self.bot.cookies_file):
                try:
                    if self.bot.load_cookies_from_file() and self.bot.test_cookies():
                        cookies_ok = True
                    else:
                        self.log_message("🔑 Session expired - running Setup & Login")
                except Exception as e:
                    self.log_message(f"Cookie validation error: {e}")
            else:
                self.log_message("🔑 No cookies found - running Setup & Login")

            if not cookies_ok:
                self.setup_and_login()
                self._reset_start_button()
                return

            # Start the automation
            self.bot.running = True
            
            # Kill any existing thread first
            self._force_kill_scheduler_thread()
            
            # Create new thread
            self.scheduler_thread = SchedulerThread(self.bot)
            self.scheduler_thread.message_signal.connect(self.log_message)
            self.scheduler_thread.status_signal.connect(self.update_status)
            self.scheduler_thread.start()

            # Update state
            self.is_running = True
            self._set_stop_button_style()
            self.setup_button.setEnabled(False)
            self.manual_button.setEnabled(False)
            self.status_indicator.set_status("Running", "#28a745")
            
            print("Safe start completed")
            
        except Exception as e:
            print(f"Error in _safe_start_automation: {e}")
            self._emergency_reset()

    
    def stop_automation(self):
        """Public stop method - delegates to safe internal method"""  
        print("Public stop_automation called")
        self._safe_stop_automation()

    def _safe_stop_automation(self):
        """Internal safe stop method - BULLETPROOF"""
        try:
            print("Safe stop starting...")
            
            # Set stopping state immediately
            self.is_running = False
            self.status_indicator.set_status("Stopping...", "#ffc107")
            
            # Stop bot
            if hasattr(self, 'bot') and self.bot:
                try:
                    self.bot.running = False
                    self.bot.stop_scheduler()
                    print("Bot stopped")
                except:
                    print("Error stopping bot, continuing...")
            
            # Force kill thread
            self._force_kill_scheduler_thread()
            
            # Reset UI
            self._reset_start_button()
            self.setup_button.setEnabled(True)
            self.manual_button.setEnabled(True)
            self.status_indicator.set_status("Stopped", "#6c757d")
            
            print("Safe stop completed")
            
        except Exception as e:
            print(f"Error in _safe_stop_automation: {e}")
            self._emergency_reset()

    def _force_kill_scheduler_thread(self):
        """Force kill any existing scheduler thread"""
        try:
            if hasattr(self, 'scheduler_thread') and self.scheduler_thread:
                print("Killing existing scheduler thread...")
                
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
                            print("Graceful stop failed, terminating...")
                            self.scheduler_thread.terminate()
                            self.scheduler_thread.wait(500)  # Wait 0.5 seconds for termination
                    except:
                        print("Graceful stop failed, forcing termination...")
                        try:
                            self.scheduler_thread.terminate()
                            self.scheduler_thread.wait(500)
                        except:
                            pass
                
                self.scheduler_thread = None
                print("Scheduler thread killed")
                
        except Exception as e:
            print(f"Error killing thread: {e}")
            # Set to None anyway
            self.scheduler_thread = None
    
    def _reset_start_button(self):
        """Reset start button to initial state"""
        self.start_button.setText("Start Automation")
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
        self.start_button.setText("Stop Automation")
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
        """Release the action lock"""
        try:
            self._processing_any_action = False
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
            self.manual_button.setEnabled(True)
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
        """Handle when scheduler stops itself - SAFE VERSION"""
        try:
            print("Scheduler stopped signal received")
            
            # Only update if we're actually running
            if hasattr(self, 'is_running') and self.is_running:
                # Don't call stop methods, just update UI
                self.is_running = False
                self._reset_start_button()
                self.setup_button.setEnabled(True)
                self.manual_button.setEnabled(True)
                self.status_indicator.set_status("Completed", "#6c757d")
                
        except Exception as e:
            print(f"Error in on_scheduler_stopped: {e}")
    
    def update_status(self, status):
        """Update status indicator"""
        if not self.is_running and status == "Running":
            return
            
        if status == "Running":
            self.status_indicator.set_status("Running", "#28a745")
        elif status == "Error":
            self.status_indicator.set_status("Error", "#dc3545")
            self.stop_automation()
        elif status == "Stopped":
            self.stop_automation()
    
    def open_manual_registration(self):
        """Open the manual registration window"""
        if self.is_running:
            self.show_warning("Please stop automation first")
            return
        if not self.bot.check_login_status():
            self.show_warning("Please complete Setup & Login first")
            return
        self.manual_window = ManualRegistrationWindow(self.bot, self)
        self.manual_window.show()
    
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
        print(f"Failed to start: {e}")
        sys.exit(1)


if __name__ == "__main__":
    main()