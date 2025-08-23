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
    from PyQt5.QtGui import QFont, QPalette, QColor, QIcon, QPixmap
    from PyQt5.QtWidgets import QDialog, QVBoxLayout, QHBoxLayout, QLabel, QPushButton, QTextEdit, QCheckBox

except ImportError:
    print("PyQt5 is required. Please install it with: pip install PyQt5")
    sys.exit(1)

try:
    from backend import ImprovedISkoleBot
except ImportError:
    print("backend.py or manual.py file is required in the same directory!")
    sys.exit(1)

class WelcomeDialog(QDialog):
    """Welcome dialog for first-time users - IMPROVED: Responsive sizing and proper centering"""
    
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("Velkommen til AkademiTrack")
        self.setModal(True)
        
        # FIXED: Responsive sizing based on screen dimensions
        self.setup_responsive_size()
        self.init_ui()
        
    def setup_responsive_size(self):
        """Setup responsive dialog size based on screen dimensions"""
        try:
            # Get screen geometry
            from PyQt5.QtWidgets import QApplication
            app = QApplication.instance()
            if app is None:
                # Fallback if no app instance
                self.setFixedSize(900, 700)
                return
                
            screen = app.primaryScreen().availableGeometry()
            screen_width = screen.width()
            screen_height = screen.height()
            
            # Calculate responsive size (60-80% of screen, with reasonable limits)
            min_width, max_width = 800, 1200
            min_height, max_height = 600, 900
            
            # Use 65% of screen width and 75% of screen height
            target_width = int(screen_width * 0.65)
            target_height = int(screen_height * 0.75)
            
            # Clamp to reasonable limits
            final_width = max(min_width, min(max_width, target_width))
            final_height = max(min_height, min(max_height, target_height))
            
            self.setFixedSize(final_width, final_height)
            
            # Center on screen immediately without blinking
            x = (screen_width - final_width) // 2
            y = (screen_height - final_height) // 2
            self.move(x, y)
            
        except Exception as e:
            # Fallback to reasonable default size
            self.setFixedSize(900, 700)
            # Try to center with fallback positioning
            try:
                from PyQt5.QtWidgets import QDesktopWidget
                desktop = QDesktopWidget()
                screen_rect = desktop.screenGeometry()
                x = (screen_rect.width() - 900) // 2
                y = (screen_rect.height() - 700) // 2
                self.move(max(0, x), max(0, y))
            except:
                pass
        
    def init_ui(self):
        """Initialize the welcome dialog UI with responsive text sizing"""
        # Get current dialog size for responsive text scaling
        dialog_width = self.width()
        dialog_height = self.height()
        
        # Calculate scaling factors
        width_scale = dialog_width / 1000.0  # Base width of 1000px
        height_scale = dialog_height / 800.0  # Base height of 800px
        scale_factor = min(width_scale, height_scale)  # Use smaller factor to prevent overflow
        
        # Responsive font sizes
        title_size = max(18, int(28 * scale_factor))
        subtitle_size = max(14, int(18 * scale_factor))
        content_size = max(12, int(16 * scale_factor))
        button_size = max(12, int(16 * scale_factor))
        
        self.setStyleSheet(f"""
            QDialog {{
                background-color: white;
                font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', system-ui, sans-serif;
            }}
            QLabel {{
                color: #212529;
            }}
            QTextEdit {{
                background-color: #f8f9fa;
                color: #495057;
                border: 1px solid #dee2e6;
                border-radius: 8px;
                padding: {max(12, int(20 * scale_factor))}px;
                font-size: {content_size}px;
                line-height: 1.6;
            }}
            QPushButton {{
                background-color: #0066cc;
                color: white;
                border: none;
                border-radius: 6px;
                padding: {max(10, int(14 * scale_factor))}px {max(18, int(28 * scale_factor))}px;
                font-size: {button_size}px;
                font-weight: 600;
                min-height: {max(16, int(24 * scale_factor))}px;
            }}
            QPushButton:hover {{
                background-color: #0052a3;
            }}
            QPushButton#secondaryButton {{
                background-color: #6c757d;
            }}
            QPushButton#secondaryButton:hover {{
                background-color: #545b62;
            }}
            QCheckBox {{
                font-size: {max(11, int(14 * scale_factor))}px;
                color: #495057;
            }}
        """)
        
        layout = QVBoxLayout(self)
        # Responsive spacing and margins
        spacing = max(20, int(30 * scale_factor))
        margin = max(30, int(50 * scale_factor))
        layout.setSpacing(spacing)
        layout.setContentsMargins(margin, margin, margin, margin)
        
        # Header with responsive fonts
        title = QLabel("🎓 Velkommen til AkademiTrack")
        title.setFont(QFont("SF Pro Display", title_size, QFont.Bold))
        title.setStyleSheet("color: #0066cc; margin-bottom: 20px;")
        title.setAlignment(Qt.AlignCenter)
        layout.addWidget(title)
        
        subtitle = QLabel("Automatisk oppmøteregistrering for iSkole")
        subtitle.setFont(QFont("SF Pro Text", subtitle_size))
        subtitle.setStyleSheet("color: #6c757d; margin-bottom: 30px;")
        subtitle.setAlignment(Qt.AlignCenter)
        layout.addWidget(subtitle)
        
        # Instructions text with responsive content
        instructions_text = QTextEdit()
        instructions_text.setReadOnly(True)
        # Responsive height (40-60% of dialog height)
        text_height = max(300, int(dialog_height * 0.5))
        instructions_text.setMaximumHeight(text_height)
        
        # Responsive font sizes in HTML content
        html_title_size = max(16, int(22 * scale_factor))
        html_content_size = max(12, int(16 * scale_factor))
        html_list_size = max(12, int(16 * scale_factor))
        
        instructions_content = f"""
<div style="font-size: {html_content_size}px; line-height: 1.7;">
<h3 style="color: #0066cc; font-size: {html_title_size}px; margin-bottom: 15px;">Hva gjør AkademiTrack?</h3>
<p style="margin-bottom: 18px; font-size: {html_content_size}px;">AkademiTrack automatiserer oppmøteregistrering for STU-timer i iSkole systemet. Programmet:</p>
<ul style="margin-bottom: 25px; padding-left: 30px; font-size: {html_list_size}px;">
<li style="margin-bottom: 12px;"><b>🔍 Overvåker timeplan</b> - Henter automatisk dagens STU-timer</li>
<li style="margin-bottom: 12px;"><b>⏰ Registrerer oppmøte</b> - Registrerer deg automatisk i registreringsvinduet</li>
<li style="margin-bottom: 12px;"><b>🎯 Intelligent timing</b> - Bare aktiv under STU-timer og registreringsperioder</li>
<li style="margin-bottom: 12px;"><b>📱 Notifikasjoner</b> - Gir beskjed når registrering er fullført</li>
</ul>

<h3 style="color: #0066cc; font-size: {html_title_size}px; margin-top: 30px; margin-bottom: 15px;">Slik bruker du programmet:</h3>
<ol style="margin-bottom: 25px; padding-left: 30px; font-size: {html_list_size}px;">
<li style="margin-bottom: 15px;"><b>Oppsett og innlogging:</b> Klikk først på "Oppsett og innlogging" for å logge inn i iSkole. Etter du har logget inn på iSkole så kommer nettleseren til å lukke seg automatisk. Hvis du allerede har nettleseren åpen så lukkes den automatisk.</li>
<li style="margin-bottom: 15px;"><b>Start automatisering:</b> Klikk på "Start automatisering" når du er klar</li>
<li style="margin-bottom: 15px;"><b>La det kjøre:</b> Programmet kjører i bakgrunnen og registrerer automatisk</li>
<li style="margin-bottom: 15px;"><b>Stopp når ferdig:</b> Stopper automatisk når alle timer er fullført</li>
</ol>

<h3 style="color: #dc3545; font-size: {html_title_size}px; margin-top: 30px; margin-bottom: 15px;">⚠️ Viktig informasjon:</h3>
<ul style="margin-bottom: 25px; padding-left: 30px; font-size: {html_list_size}px;">
<li style="margin-bottom: 12px;">Programmet fungerer kun for <b>STU-timer</b> (studietimer)</li>
<li style="margin-bottom: 12px;">Du må ha tilgang til iSkole med Feide-innlogging</li>
<li style="margin-bottom: 12px;">Programmet kan kjøre i bakgrunnen når det er minimert</li>
<li style="margin-bottom: 12px;">Bruk "Innstillinger" for å se detaljerte logger og teste manuell registrering</li>
</ul>

<p style="color: #28a745; font-weight: bold; font-size: {int(html_content_size * 1.1)}px; margin-top: 30px; text-align: center; padding: 20px; background-color: #d4edda; border-radius: 10px; border: 1px solid #c3e6cb;">
✅ Klar til å starte? Klikk "Forstått, start programmet" nedenfor!
</p>
</div>
        """
        
        instructions_text.setHtml(instructions_content)
        layout.addWidget(instructions_text)
        
        # Don't show again checkbox
        self.dont_show_checkbox = QCheckBox("Ikke vis denne meldingen igjen")
        self.dont_show_checkbox.setStyleSheet("margin-top: 15px;")
        layout.addWidget(self.dont_show_checkbox)
        
        # Buttons with responsive sizing
        button_layout = QHBoxLayout()
        button_layout.addStretch()
        
        self.close_button = QPushButton("Lukk")
        self.close_button.setObjectName("secondaryButton")
        self.close_button.clicked.connect(self.close_app)
        button_layout.addWidget(self.close_button)
        
        self.continue_button = QPushButton("Forstått, start programmet")
        self.continue_button.clicked.connect(self.continue_to_app)
        button_layout.addWidget(self.continue_button)
        
        layout.addLayout(button_layout)
        
    def close_app(self):
        """Close the entire application"""
        self.reject()
        
    def continue_to_app(self):
        """Continue to main application"""
        self.accept()

class SettingsManager:
    """Manages application settings including first-time user experience"""
    
    def __init__(self):
        self.base_dir = Path(__file__).resolve().parent
        self.settings_file = self.base_dir / "settings.json"
        self.default_settings = {
            "first_time_user": True,
            "show_welcome_dialog": True,
            "window_position": {"x": 200, "y": 200},
            "window_size": {"width": 600, "height": 440},
            "auto_start_on_login_success": False,
            "notification_duration": 5000,
            "log_level": "INFO",
            "theme": "light",
            "check_interval_seconds": 60,
            "max_registration_attempts": 3
        }
        self.settings = self.load_settings()
    
    def load_settings(self):
        """Load settings from file or create with defaults"""
        try:
            if self.settings_file.exists():
                with open(self.settings_file, 'r', encoding='utf-8') as f:
                    loaded_settings = json.load(f)
                
                # Merge with defaults to handle new settings in updates
                settings = self.default_settings.copy()
                settings.update(loaded_settings)
                
                # Save back to include any new defaults
                self.save_settings(settings)
                return settings
            else:
                # First time - create with defaults
                self.save_settings(self.default_settings)
                return self.default_settings.copy()
                
        except Exception as e:
            print(f"Error loading settings: {e}")
            return self.default_settings.copy()
    
    def save_settings(self, settings=None):
        """Save settings to file"""
        try:
            if settings is None:
                settings = self.settings
                
            self.settings_file.parent.mkdir(parents=True, exist_ok=True)
            
            with open(self.settings_file, 'w', encoding='utf-8') as f:
                json.dump(settings, f, indent=2, ensure_ascii=False)
            
            return True
        except Exception as e:
            print(f"Error saving settings: {e}")
            return False
    
    def get(self, key, default=None):
        """Get a setting value"""
        return self.settings.get(key, default)
    
    def set(self, key, value):
        """Set a setting value and save"""
        self.settings[key] = value
        self.save_settings()
    
    def is_first_time_user(self):
        """Check if this is a first-time user"""
        return self.get("first_time_user", True)
    
    def should_show_welcome(self):
        """Check if welcome dialog should be shown"""
        return self.get("show_welcome_dialog", True)
    
    def mark_welcome_shown(self, dont_show_again=False):
        """Mark welcome dialog as shown"""
        self.set("first_time_user", False)
        if dont_show_again:
            self.set("show_welcome_dialog", False)
    
    def save_window_geometry(self, window):
        """Save window position and size"""
        try:
            pos = window.pos()
            size = window.size()
            self.settings["window_position"] = {"x": pos.x(), "y": pos.y()}
            self.settings["window_size"] = {"width": size.width(), "height": size.height()}
            self.save_settings()
        except Exception as e:
            print(f"Error saving window geometry: {e}")
    
    def restore_window_geometry(self, window):
        """Restore window position and size"""
        try:
            pos = self.settings.get("window_position", {"x": 200, "y": 200})
            size = self.settings.get("window_size", {"width": 600, "height": 440})
            
            window.resize(size["width"], size["height"])
            window.move(pos["x"], pos["y"])
        except Exception as e:
            print(f"Error restoring window geometry: {e}")

class NotificationWidget(QWidget):
    """Professional notification widget with modern design and smooth animations - FIXED TOP-ONLY STACKING"""
    
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
        """FIXED: Show notification at top position only - hide existing notifications first"""
        try:
            app = QApplication.instance()
            screen = app.primaryScreen().availableGeometry()
            
            # FIXED: Hide all existing notifications to make room at the top
            if hasattr(app, '_active_notifications'):
                for notification in app._active_notifications[:]:  # Copy list to avoid modification during iteration
                    if notification != self and notification.isVisible():
                        try:
                            notification.hide_notification()
                        except:
                            pass
                app._active_notifications = []
            else:
                app._active_notifications = []
            
            # FIXED: Always position at the same top location
            margin = 20
            start_x = screen.right()
            end_x = screen.right() - self.width() - margin
            y = screen.top() + margin  # FIXED: Always at the top
            
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
        """Handle window close - now saves settings"""
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
                
                # Save window geometry before closing
                if hasattr(self, 'settings_manager'):
                    self.settings_manager.save_window_geometry(self)
                
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
            # Save window geometry before closing
            if hasattr(self, 'settings_manager'):
                self.settings_manager.save_window_geometry(self)
                
            try:
                if self._orig_stdout:
                    sys.stdout = self._orig_stdout
                if self._orig_stderr:
                    sys.stderr = self._orig_stderr
            except Exception:
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
        """Initialize the POST request window UI with responsive design and larger inputs"""
        self.setWindowTitle("Manual POST Request")
        
        # Get screen dimensions for responsive sizing
        try:
            app = QApplication.instance()
            screen = app.primaryScreen().availableGeometry()
            screen_width = screen.width()
            screen_height = screen.height()
            
            # Calculate responsive size (40% of screen width, 65% of screen height)
            window_width = max(700, min(900, int(screen_width * 0.45)))
            window_height = max(600, min(750, int(screen_height * 0.70)))
            
            # Center the window
            x = (screen_width - window_width) // 2
            y = (screen_height - window_height) // 2
            
            self.setGeometry(x, y, window_width, window_height)
            
        except Exception as e:
            # Fallback to larger fixed size if screen detection fails
            self.setGeometry(300, 300, 750, 650)
        
        # Set minimum and maximum sizes for better UX
        self.setMinimumSize(650, 550)
        self.setMaximumSize(1100, 900)
        
        self.setStyleSheet("""
            QDialog {
                background-color: white;
                font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', system-ui, sans-serif;
            }
            QLabel {
                color: #212529;
                font-size: 14px;
                font-weight: 500;
                margin-bottom: 4px;
            }
            QLineEdit {
                background-color: #f8f9fa;
                border: 1px solid #dee2e6;
                border-radius: 6px;
                padding: 12px 16px;
                font-size: 15px;
                color: #495057;
                margin-bottom: 8px;
                min-height: 24px;
            }
            QLineEdit:focus {
                border-color: #0066cc;
                background-color: white;
            }
            QPushButton {
                background-color: #0066cc;
                color: white;
                border: none;
                border-radius: 6px;
                padding: 12px 18px;
                font-size: 14px;
                font-weight: 600;
                min-height: 20px;
            }
            QPushButton:hover {
                background-color: #0052a3;
            }
            QPushButton#closeButton {
                background-color: #6c757d;
            }
            QPushButton#closeButton:hover {
                background-color: #545b62;
            }
            QTextEdit#console {
                background-color: #0b0c10;
                color: #e6edf3;
                font-family: 'SF Mono', 'Consolas', 'Fira Code', monospace;
                font-size: 13px;
                border: 1px solid #1f2833;
                border-radius: 6px;
                padding: 10px;
            }
        """)

        layout = QVBoxLayout()
        layout.setSpacing(10)
        layout.setContentsMargins(16, 16, 16, 16)

        # Title with better spacing
        title = QLabel("Manual POST Request")
        title.setFont(QFont("SF Pro Display", 16, QFont.Bold))
        title.setStyleSheet("color: #212529; margin-bottom: 8px;")
        layout.addWidget(title)

        # Scrollable form area for better space usage
        scroll_area = QWidget()
        form_layout = QVBoxLayout(scroll_area)
        form_layout.setSpacing(8)
        
        # Create form fields in a more compact layout
        fields = [
            ("Fylke ID:", "fylkeid_input", "00"),
            ("Skole ID:", "skoleid_input", "312"),
            ("Plan Periode:", "planperi_input", "2025-26"),
            ("ST Kode:", "stkode_input", "PB"),
            ("Klasse Trinn:", "kl_trinn_input", "3"),
            ("Klasse ID:", "kl_id_input", "A"),
            ("K Navn:", "k_navn_input", "STU"),
            ("Gruppe Nr:", "gruppe_nr_input", "$"),
            ("Time Nr:", "timenr_input", "1")
        ]
        
        # Create form in a grid-like layout for better space usage
        form_grid = QVBoxLayout()
        
        for i in range(0, len(fields), 2):  # Process 2 fields per row
            row_layout = QHBoxLayout()
            row_layout.setSpacing(16)
            
            # First field in row
            field_label, field_attr, field_default = fields[i]
            left_widget = QWidget()
            left_layout = QVBoxLayout(left_widget)
            left_layout.setContentsMargins(0, 0, 0, 0)
            left_layout.setSpacing(4)
            
            left_layout.addWidget(QLabel(field_label))
            field_input = QLineEdit(field_default)
            # FIXED: Increased height significantly for better readability
            field_input.setFixedHeight(48)
            field_input.setFont(QFont("SF Pro Text", 15))  # Explicit font size
            setattr(self, field_attr, field_input)
            left_layout.addWidget(field_input)
            
            row_layout.addWidget(left_widget)
            
            # Second field in row (if exists)
            if i + 1 < len(fields):
                field_label, field_attr, field_default = fields[i + 1]
                right_widget = QWidget()
                right_layout = QVBoxLayout(right_widget)
                right_layout.setContentsMargins(0, 0, 0, 0)
                right_layout.setSpacing(4)
                
                right_layout.addWidget(QLabel(field_label))
                field_input = QLineEdit(field_default)
                # FIXED: Increased height significantly for better readability
                field_input.setFixedHeight(48)
                field_input.setFont(QFont("SF Pro Text", 15))  # Explicit font size
                setattr(self, field_attr, field_input)
                right_layout.addWidget(field_input)
                
                row_layout.addWidget(right_widget)
            else:
                row_layout.addStretch()  # Fill remaining space if odd number of fields
            
            form_grid.addLayout(row_layout)
        
        form_layout.addLayout(form_grid)
        layout.addWidget(scroll_area)

        # Buttons with better layout
        button_layout = QHBoxLayout()
        button_layout.setSpacing(10)
        
        button_layout.addStretch()
        
        self.send_button = QPushButton("Send POST Request")
        self.send_button.clicked.connect(self.send_post_request)
        self.send_button.setFixedHeight(44)  # Slightly larger button
        button_layout.addWidget(self.send_button)

        self.close_button = QPushButton("Close")
        self.close_button.setObjectName("closeButton")
        self.close_button.clicked.connect(self.close)
        self.close_button.setFixedHeight(44)  # Slightly larger button
        button_layout.addWidget(self.close_button)

        layout.addLayout(button_layout)
        
        # Console output - takes remaining space
        console_label = QLabel("Response:")
        console_label.setFont(QFont("SF Pro Text", 13, QFont.Bold))
        console_label.setStyleSheet("margin-top: 8px; margin-bottom: 4px;")
        layout.addWidget(console_label)
        
        self.console = QTextEdit()
        self.console.setObjectName("console")
        self.console.setReadOnly(True)
        
        # Make console responsive to window size but don't let it dominate
        self.console.setMinimumHeight(150)
        self.console.setMaximumHeight(200)
        
        layout.addWidget(self.console, 1)  # Give console the remaining space

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
                self.log("=" * 40)
                
                # FIXED: Output complete JSON without any limits
                complete_json = json.dumps(response_data, indent=2, ensure_ascii=False)
                
                # Split into smaller chunks to avoid GUI truncation
                chunk_size = 1000
                for i in range(0, len(complete_json), chunk_size):
                    chunk = complete_json[i:i + chunk_size]
                    self.console.append(chunk)
                    # Force GUI update for each chunk
                    self.console.repaint()
                    
                self.log("=" * 40)
                
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

    def resizeEvent(self, event):
        """Handle window resize to adjust console size"""
        super().resizeEvent(event)
        # Keep console at a reasonable fixed size instead of percentage
        if hasattr(self, 'console'):
            # Fixed console height that doesn't change much with window size
            self.console.setMinimumHeight(150)
            self.console.setMaximumHeight(220)

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
            self.status_signal.emit("Kjører")
            if self.bot.running:
                self.bot.run_scheduler()
            self.status_signal.emit("Stoppet")
        except Exception as e:
            self.message_signal.emit(f"Scheduler feil: {e}")
            self.status_signal.emit("Feil")
    
    def stop(self):
        """Stop the thread gracefully"""
        self.should_stop = True
        if self.bot:
            self.bot.stop_scheduler()


class AkademiTrackWindow(QMainWindow):
    """Simple, clean main window with notification system - FIXED FOR TOP-ONLY NOTIFICATIONS"""
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
        """FIXED: Show enhanced professional notification with top-only positioning and better duplicate prevention"""
        try:
            # FIXED: Enhanced spam prevention with special handling for automation messages
            current_time = time.time()
            
            if not hasattr(self, '_last_notifications'):
                self._last_notifications = {}
            
            # FIXED: Better deduplication logic for stop messages
            message_lower = message.lower().strip()
            
            # Special handling for automation start/stop messages
            automation_keywords = {
                'start': ['automatisering startet', 'automation started', 'startet'],
                'stop': ['automatisering stoppet', 'automation stopped', 'stoppet', 'scheduler stopped']
            }
            
            # Determine if this is an automation message
            is_start_message = any(keyword in message_lower for keyword in automation_keywords['start'])
            is_stop_message = any(keyword in message_lower for keyword in automation_keywords['stop'])
            
            if is_start_message or is_stop_message:
                # For automation messages, use type-specific keys with shorter cooldown
                action_type = 'start' if is_start_message else 'stop'
                message_key = f"automation_{action_type}_{notification_type}"
                cooldown = 2.0  # 2 seconds cooldown for automation messages
            else:
                # For other messages, use content-based deduplication
                message_key = f"{message_lower}_{notification_type}"
                cooldown = 3.0
            
            # Check for duplicate within cooldown period
            if message_key in self._last_notifications:
                time_since_last = current_time - self._last_notifications[message_key]
                if time_since_last < cooldown:
                    print(f"Notification blocked (duplicate): {message} (last shown {time_since_last:.1f}s ago)")
                    return
            
            # Update timestamp
            self._last_notifications[message_key] = current_time
            
            # Create and show notification
            notification = NotificationWidget(message, notification_type, duration, None)
            notification.show_notification()
            
            # Clean up old notification references periodically
            if len(self._last_notifications) > 50:
                cutoff_time = current_time - 300  # 5 minutes
                self._last_notifications = {
                    k: v for k, v in self._last_notifications.items() 
                    if v > cutoff_time
                }
            
            print(f"Notification shown: {message} ({notification_type})")
            
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
        
        subtitle = QLabel("Automatisk oppmøteregistrering")
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
        """FIXED: Enhanced log message handler with better duplicate prevention for stop notifications"""
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
            
            # AUTOMATION STATUS - FIXED: Better detection for start messages
            elif ('🚀 automatisering startet' in message_lower or 
                ('automation' in message_lower and 'started' in message_lower) or
                ('scheduler started' in message_lower)):
                should_notify = True
                notification_type = "info"
                notification_message = "Automatisering startet"
            
            # FIXED: Consolidated stop detection to prevent duplicates
            elif (any(stop_phrase in message_lower for stop_phrase in [
                'scheduler stopped',
                '🛑 scheduler stopped', 
                '🛑 stopping scheduler',
                'automatisering stoppet'
            ])):
                # FIXED: Only show stop notification once per stop event
                if not hasattr(self, '_current_stop_event_time'):
                    self._current_stop_event_time = time.time()
                    should_notify = True
                    notification_type = "error"  # Red color for stopped
                    notification_message = "Automatisering stoppet"
                elif time.time() - self._current_stop_event_time > 5.0:
                    # Reset after 5 seconds to allow new stop events
                    self._current_stop_event_time = time.time()
                    should_notify = True
                    notification_type = "error"
                    notification_message = "Automatisering stoppet"
                else:
                    # Block duplicate stop notifications within 5 seconds
                    print(f"Blocked duplicate stop notification: {message}")
            
            # Show professional notification
            if should_notify and notification_message:
                try:
                    print(f"Triggering notification: {notification_message} ({notification_type})")
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
            
            # FIXED: Better status indicator updates with specific conditions
            elif ('scheduler stopped' in message_lower and 
                  not hasattr(self, '_stopping_in_progress')):
                self.status_indicator.set_status("Klar", "#6c757d")
            elif (('started' in message_lower and 'automatisering' in message_lower) or 
                  'scheduler started' in message_lower):
                self.status_indicator.set_status("Kjører", "#28a745")
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
        """Toggle automation - FIXED for better notification consistency"""
        try:
            # FIXED: Allow quick actions but ensure notifications show
            if hasattr(self, '_processing_any_action') and self._processing_any_action:
                if self.is_running:
                    # Allow stop even if action is in progress
                    self._processing_any_action = False
                    print("Forcing stop action...")
                else:
                    print("Start action already in progress, ignoring...")
                    return
                    
            self._processing_any_action = True
            
            if not self.is_running:
                print("Toggle: Starting automation...")
                self.start_button.setEnabled(False)
                self.start_button.setText("Starter...")
                
                # Clear any previous stop event timing
                if hasattr(self, '_current_stop_event_time'):
                    delattr(self, '_current_stop_event_time')
                
                # FIXED: Add small delay to ensure UI updates and clear previous state
                QTimer.singleShot(100, self._safe_start_automation)
            else:
                print("Toggle: Stopping automation...")  
                self.start_button.setEnabled(False)
                self._stopping_in_progress = True
                
                # FIXED: Add small delay to ensure UI updates and clear previous state
                QTimer.singleShot(100, self._safe_stop_automation)
                    
        except Exception as e:
            print(f"CRITICAL ERROR in toggle_automation: {e}")
            self._emergency_reset()
        finally:
            # FIXED: Shorter delay for responsiveness
            QTimer.singleShot(500, self._release_action_lock)


    def start_automation(self):
        """Public start method - delegates to safe internal method"""
        print("Public start_automation called")
        self._safe_start_automation()

    def _safe_start_automation(self):
        """Internal safe start method with guaranteed notifications"""
        try:
            # FIXED: Clear any previous notification state
            if hasattr(self, '_last_notifications'):
                # Remove old automation notifications to allow new ones
                keys_to_remove = [k for k in self._last_notifications.keys() 
                                if 'automatisering' in k.lower() or 'automation' in k.lower()]
                for key in keys_to_remove:
                    del self._last_notifications[key]
            
            # Check cookies with better error handling
            cookies_ok = False
            cookies_exist = os.path.exists(self.bot.cookies_file)
            
            if cookies_exist:
                try:
                    if self.bot.load_cookies_from_file() and self.bot.test_cookies():
                        cookies_ok = True
                        self.log_message("✅ Using existing valid cookies")
                    else:
                        self.log_message("🔑 Existing cookies are invalid")
                except Exception as e:
                    self.log_message(f"Cookie validation error: {e}")
            else:
                self.log_message("🔑 No cookies file found")

            if not cookies_ok:
                self.show_notification("Trenger oppsett først", "warning", 3000)
                self.log_message("❌ Please run 'Oppsett og innlogging' first before starting automation")
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
            self.status_indicator.set_status("Kjører", "#28a745")
            
            # FIXED: Ensure start notification always shows with unique timing
            self.log_message("🚀 Automatisering startet")
            
            print("Safe start completed")
            
        except Exception as e:
            print(f"Error in _safe_start_automation: {e}")
            self.show_notification("Feil ved oppstart", "error", 3000)
            self._emergency_reset()

    
    def stop_automation(self):
        """Public stop method - delegates to safe internal method"""  
        print("Kode: stopp_automatisering kalt")
        self._safe_stop_automation()

    def _safe_stop_automation(self):
        """Internal safe stop method with guaranteed notifications"""
        try:
            print("Safe stop starting...")
            
            # FIXED: Clear any previous notification state for stop messages
            if hasattr(self, '_last_notifications'):
                keys_to_remove = [k for k in self._last_notifications.keys() 
                                if 'stoppet' in k.lower() or 'stopped' in k.lower()]
                for key in keys_to_remove:
                    del self._last_notifications[key]
            
            # Set stopping state immediately
            self.is_running = False
            
            # Update button immediately
            self.start_button.setEnabled(False)
            
            # Stop bot immediately
            if hasattr(self, 'bot') and self.bot:
                try:
                    self.bot.running = False
                    self.bot.stop_scheduler()
                    print("Bot stopped")
                except Exception as e:
                    print(f"Error stopping bot: {e}")
            
            # Force kill thread immediately
            self._force_kill_scheduler_thread()
            
            # FIXED: Complete stop process after very short delay
            QTimer.singleShot(200, self._complete_stop_process)
            
            print("Safe stop initiated")
            
        except Exception as e:
            print(f"Error in _safe_stop_automation: {e}")
            self._emergency_reset()

    def _complete_stop_process(self):
        """Complete the stop process with guaranteed notification"""
        try:
            # Reset UI
            self._reset_start_button()
            self.setup_button.setEnabled(True)
            self.status_indicator.set_status("Stoppet", "#8a010f")
            
            # FIXED: Ensure stop notification always shows
            self.log_message("🛑 Scheduler stopped")
            
            print("Stop process completed")
            
        except Exception as e:
            print(f"Error completing stop process: {e}")
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
        """)

    def _set_stop_button_style(self):
        """Set button to stop style - FIXED WITH PROPER HOVER EFFECTS"""
        self.start_button.setText("Stopp automatisering")
        self.start_button.setEnabled(True)
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
                background-color: #8a010f;
            }
            QPushButton:pressed {
                background-color: #66000a;
            }
            QPushButton:disabled {
                background-color: #6c757d;
                color: #dee2e6;
            }
        """)

    def _release_action_lock(self):
        """Release the action lock and stop flag - FASTER"""
        try:
            self._processing_any_action = False
            if hasattr(self, '_stopping_in_progress'):
                self._stopping_in_progress = False
            print("Action lock released")
        except Exception as e:
            print(f"Error releasing lock: {e}")
            # Force clear anyway
            self._processing_any_action = False

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
            self.status_indicator.set_status("Kjører", "#28a745")
        elif status == "Feil":
            self.status_indicator.set_status("Feil", "#dc3545")
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
    """Main function with welcome dialog - FIXED: Always center window properly"""
    app = QApplication(sys.argv)
    app.setStyle('Fusion')
    
    try:
        # Initialize settings manager
        settings_manager = SettingsManager()
        
        # Show welcome dialog for first-time users
        if settings_manager.should_show_welcome():
            welcome_dialog = WelcomeDialog()
            
            if welcome_dialog.exec_() == QDialog.Accepted:
                # User clicked "Forstått, start programmet"
                dont_show_again = welcome_dialog.dont_show_checkbox.isChecked()
                settings_manager.mark_welcome_shown(dont_show_again)
            else:
                # User clicked "Lukk" or closed dialog
                print("User cancelled setup")
                sys.exit(0)
        
        # Create main window (don't show yet)
        window = AkademiTrackWindow()
        window.settings_manager = settings_manager
        
        # FIXED: Always center the window regardless of saved geometry
        try:
            screen = app.primaryScreen().availableGeometry()
            window_width = window.width()
            window_height = window.height()
            
            # Calculate center position
            x = (screen.width() - window_width) // 2
            y = (screen.height() - window_height) // 2
            
            # Ensure window doesn't go off-screen
            x = max(0, min(x, screen.width() - window_width))
            y = max(0, min(y, screen.height() - window_height))
            
            # Set position before showing
            window.move(x, y)
            
            print(f"Window centered at position: {x}, {y}")
            
        except Exception as e:
            print(f"Could not center window: {e}")
            # Fallback to default position
            window.move(200, 200)
        
        # Show window after positioning is complete
        window.show()
        
        sys.exit(app.exec_())
        
    except Exception as e:
        print(f"Could not start application: {e}")
        sys.exit(1)

if __name__ == "__main__":
    main()