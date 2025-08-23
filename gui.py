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
    """Complete notification widget with proper implementation"""
    
    def __init__(self, message, notification_type="info", duration=5000, parent=None):
        super().__init__(parent)
        self.message = message
        self.notification_type = notification_type
        self.duration = duration
        
        # Set window flags for overlay behavior
        self.setWindowFlags(Qt.FramelessWindowHint | Qt.WindowStaysOnTopHint | Qt.Tool)
        self.setAttribute(Qt.WA_TranslucentBackground)
        self.setAttribute(Qt.WA_ShowWithoutActivating)
        
        self.init_ui()
        self.setup_animation()
        
    def init_ui(self):
        """Initialize notification UI"""
        # Set fixed size
        self.setFixedSize(350, 80)
        
        # Create main layout
        layout = QHBoxLayout(self)
        layout.setContentsMargins(0, 0, 0, 0)
        layout.setSpacing(0)
        
        # Create main container
        container = QFrame()
        container.setObjectName("notificationContainer")
        
        # Set styles based on type
        if self.notification_type == "success":
            bg_color = "#d4edda"
            border_color = "#c3e6cb"
            text_color = "#155724"
            icon = "✅"
        elif self.notification_type == "error":
            bg_color = "#f8d7da"
            border_color = "#f5c6cb"
            text_color = "#721c24"
            icon = "❌"
        elif self.notification_type == "warning":
            bg_color = "#fff3cd"
            border_color = "#ffeaa7"
            text_color = "#856404"
            icon = "⚠️"
        elif self.notification_type == "completed":
            bg_color = "#d1ecf1"
            border_color = "#bee5eb"
            text_color = "#0c5460"
            icon = "🎊"
        else:  # info
            bg_color = "#d1ecf1"
            border_color = "#bee5eb"
            text_color = "#0c5460"
            icon = "ℹ️"
        
        container.setStyleSheet(f"""
            QFrame#notificationContainer {{
                background-color: {bg_color};
                border: 1px solid {border_color};
                border-radius: 8px;
                padding: 12px;
            }}
            QLabel {{
                color: {text_color};
                font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', system-ui, sans-serif;
                font-size: 14px;
                font-weight: 500;
            }}
        """)
        
        # Container layout
        container_layout = QHBoxLayout(container)
        container_layout.setContentsMargins(8, 8, 8, 8)
        container_layout.setSpacing(8)
        
        # Icon label
        icon_label = QLabel(icon)
        icon_label.setFont(QFont("SF Pro Text", 16))
        container_layout.addWidget(icon_label)
        
        # Message label
        message_label = QLabel(self.message)
        message_label.setWordWrap(True)
        container_layout.addWidget(message_label, 1)
        
        # Add container to main layout
        layout.addWidget(container)
        
    def setup_animation(self):
        """Setup slide-in animation"""
        self.slide_in_animation = QPropertyAnimation(self, b"geometry")
        self.slide_in_animation.setDuration(300)
        self.slide_in_animation.setEasingCurve(QEasingCurve.OutCubic)
        
    def show_notification(self):
        """Show notification with slide-in animation"""
        try:
            app = QApplication.instance()
            if not app:
                return
                
            screen = app.primaryScreen().availableGeometry()
            
            # Manage existing notifications
            if not hasattr(app, '_active_notifications'):
                app._active_notifications = []
            
            # Hide older notifications
            for notification in app._active_notifications[:]:
                if notification != self and notification.isVisible():
                    try:
                        notification.hide_notification()
                    except:
                        pass
            
            app._active_notifications = [self]  # Keep only this notification
            
            # Position calculation
            margin = 20
            start_x = screen.right()
            end_x = screen.right() - self.width() - margin
            y = screen.top() + margin
            
            # Set initial position and show
            self.setGeometry(start_x, y, self.width(), self.height())
            self.show()
            self.raise_()
            
            # Start slide animation
            self.slide_in_animation.setStartValue(QRect(start_x, y, self.width(), self.height()))
            self.slide_in_animation.setEndValue(QRect(end_x, y, self.width(), self.height()))
            self.slide_in_animation.start()
            
            # Auto-hide after duration
            QTimer.singleShot(self.duration, self.hide_notification)
            
        except Exception as e:
            print(f"Error showing notification: {e}")
    
    def show_notification_original(self):
        """Alias for show_notification for compatibility"""
        self.show_notification()
        
    def hide_notification(self):
        """Hide notification with fade out"""
        try:
            self.hide()
            
            # Remove from active notifications
            app = QApplication.instance()
            if app and hasattr(app, '_active_notifications'):
                try:
                    app._active_notifications.remove(self)
                except ValueError:
                    pass
                    
        except Exception as e:
            print(f"Error hiding notification: {e}")

class PostRequestWindow(QDialog):
    finished = pyqtSignal()
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
    """FIXED: Simple button with minimum size constraints"""
    def __init__(self, text, primary=False):
        super().__init__(text)
        
        # FIXED: Set minimum size to prevent shrinking
        self.setMinimumSize(140, 44)
        self.setMaximumHeight(60)
        
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
                    min-width: 140px;
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
                    min-width: 140px;
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
    """Simple status indicator with working stylesheet and FIXED sizing"""
    def __init__(self):
        super().__init__("● Klar") 
        
        # FIXED: Force exact size - never changes
        self.setFixedSize(140, 36)  # Exact fixed size
        self.setSizePolicy(QSizePolicy.Fixed, QSizePolicy.Fixed)
        
        # Set initial style
        self.setStyleSheet("""
            QLabel {
                color: #6c757d;
                font-size: 14px;
                font-weight: bold;
                padding: 8px 12px;
                background-color: #f8f9fa;
                border-radius: 18px;
                border: 1px solid #dee2e6;
            }
        """)
        
        # FIXED: Center alignment to prevent text shifting
        self.setAlignment(Qt.AlignCenter)
        
    def set_status(self, status, color="#6c757d"):
        """Set status text and color - FIXED METHOD with exact sizing"""
        self.setText(f"● {status}")
        
        # FIXED: Remove all size properties from CSS - rely on setFixedSize only
        if color == "#28a745":  # Green/Running
            style = """
                QLabel {
                    color: #28a745;
                    font-size: 14px;
                    font-weight: bold;
                    padding: 8px 12px;
                    background-color: #d4edda;
                    border-radius: 18px;
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
                    border-radius: 18px;
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
                    border-radius: 18px;
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
                    border-radius: 18px;
                    border: 1px solid #dee2e6;
                }
            """
        
        self.setStyleSheet(style)
        
        # FIXED: Ensure size never changes
        self.setFixedSize(140, 36)  # Force same size after every update

class SettingsWindow(QWidget):
    """Settings window as a separate independent window"""
    finished = pyqtSignal()

    def __init__(self, parent=None):
        super().__init__(parent, Qt.Window)  # Make it a proper window
        self.parent_window = parent
        self.console_widget = None
        self.post_window = None  # Track POST window instance
        self.post_window_open = False  # Track POST window state
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
        """Open the POST request window from settings - SINGLE INSTANCE"""
        if not self.parent_window or not self.parent_window.bot:
            self.append_text("❌ No bot instance available")
            return
            
        if self.parent_window.is_running:
            self.append_text("⚠️ Stop automation first before using POST Request")
            return
        
        # Check if POST window is already open
        if self.post_window_open:
            # If window exists and is visible, just bring it to front
            if self.post_window and self.post_window.isVisible():
                self.post_window.raise_()
                self.post_window.activateWindow()
                return
            else:
                # Window was closed but flag wasn't reset
                self.post_window_open = False
        
        # Don't check login status - just check if cookies exist
        if not os.path.exists(self.parent_window.bot.cookies_file):
            self.append_text("❌ No cookies found - please run Setup & Login first")
            return
        
        self.append_text("🚀 Opening POST Request window...")
        
        # Create new window only if none exists
        self.post_window = PostRequestWindow(self.parent_window.bot, self)
        self.post_window_open = True
        
        # Connect close event to reset flag
        self.post_window.finished.connect(self._on_post_window_closed)
        
        self.post_window.show()

    def _on_post_window_closed(self):
        """Reset POST window flag when closed"""
        self.post_window_open = False
        self.post_window = None
    
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
        self.finished.emit()  # Emit finished signal
        event.accept()

    def close(self):
        """Override close method to emit signal"""
        self.finished.emit()
        super().close()

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
    """FIXED: More robust thread that handles termination better"""
    message_signal = pyqtSignal(str)
    
    def __init__(self, bot):
        super().__init__()
        self.bot = bot
        self.setTerminationEnabled(True)
        self._should_stop = False
    
    def run(self):
        """Enhanced run method with better error handling"""
        try:
            print("Scheduler thread starting...")
            
            # Quick start notification
            try:
                self.message_signal.emit("🚀 Automatisering startet")
            except:
                pass
            
            # Main loop with frequent stop checks
            while not self._should_stop and self.bot and self.bot.running:
                try:
                    # Check if we should stop every iteration
                    if self._should_stop:
                        break
                        
                    # Run bot scheduler
                    if hasattr(self.bot, 'run_scheduler'):
                        self.bot.run_scheduler()
                    
                    # Small sleep to prevent CPU spinning
                    self.msleep(100)
                    
                except Exception as e:
                    print(f"Scheduler error: {e}")
                    try:
                        self.message_signal.emit(f"Scheduler error: {e}")
                    except:
                        pass
                    break
            
            print("Scheduler thread ending...")
            
        except Exception as e:
            print(f"Thread run error: {e}")
        finally:
            try:
                self.message_signal.emit("🛑 Scheduler stopped")
            except:
                pass
    
    def stop(self):
        """Graceful stop method"""
        self._should_stop = True


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
        self._toggle_lock = False  # Prevent spam clicking
        self._last_notifications = {}

        self._orig_stdout = None
        self._orig_stderr = None
        self.active_notifications = []  # Track active notifications

        self.post_window = None
        self.settings_window = None
        self.post_window_open = False
        self.settings_window_open = False
        
        # Create a hidden console for logging (not displayed in main UI)
        self.console = QPlainTextEdit()
        self.console.setReadOnly(True)
        self.console.setLineWrapMode(QPlainTextEdit.NoWrap)
        self.console.setMaximumBlockCount(0)  # Remove limits
        self.console.hide()
        
        self.init_ui()
        self.init_bot()
        self.log_message("Klikk 'Oppsett og innlogging' først, deretter 'Start automatisering' eller 'POST Request'")
        
        if hasattr(self, 'bot') and self.bot:
            try:
            # Use queued connection for thread safety
                self.bot.scheduler_stopped_signal.connect(
                    self.on_scheduler_stopped, 
                    Qt.QueuedConnection
                )
            except:
                print("Could not connect scheduler signal - continuing anyway")

        self.console_signal.connect(self.append_console)
        self._redirect_std_streams()

        
    
    def show_notification(self, message, notification_type="info", duration=5000):
        """FIXED: Create and show notification properly"""
        try:
            # Simple spam prevention
            current_time = time.time()
            
            if not hasattr(self, '_last_notification_time'):
                self._last_notification_time = 0
            
            # Only block if same message within 1 second
            if (current_time - self._last_notification_time) < 1.0:
                last_message = getattr(self, '_last_notification_message', '')
                if last_message == message:
                    return
            
            self._last_notification_time = current_time
            self._last_notification_message = message
            
            # Create notification widget with proper parameters
            notification = NotificationWidget(message, notification_type, duration, None)
            
            # Show the notification
            notification.show_notification()
            
            print(f"Notification shown: {message} ({notification_type})")
            
        except Exception as e:
            print(f"Error showing notification: {e}")
            import traceback
            traceback.print_exc()
        
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
        """Open settings window as independent window - SINGLE INSTANCE"""
        # Check if settings window is already open
        if self.settings_window_open:
            # If window exists and is visible, just bring it to front
            if self.settings_window and self.settings_window.isVisible():
                self.settings_window.raise_()
                self.settings_window.activateWindow()
                return
            else:
                # Window was closed but flag wasn't reset
                self.settings_window_open = False
        
        # Create new window only if none exists
        self.settings_window = SettingsWindow(self)
        self.settings_window_open = True
        
        # Connect close event to reset flag
        self.settings_window.finished.connect(self._on_settings_window_closed)
        
        self.settings_window.show()
        
        # Position it relative to main window
        main_pos = self.pos()
        self.settings_window.move(main_pos.x() + 50, main_pos.y() + 50)

    def _on_settings_window_closed(self):
        """Reset settings window flag when closed"""
        self.settings_window_open = False
        self.settings_window = None
    
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
        """FIXED: Enhanced log message handler with working notifications"""
        try:
            should_notify = False
            notification_type = "info"
            notification_message = ""
            
            message_lower = message.lower()
            
            # SUCCESS NOTIFICATIONS
            if ('🎯 registrert studietid' in message_lower or 
                'attendance registered successfully' in message_lower or
                'studietid registrert' in message_lower):
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
                '🎊 all stu classes for the day are completed' in message_lower or
                'alle studietimer fullført' in message_lower):
                should_notify = True
                notification_type = "completed"
                notification_message = "Alle studietimer fullført i dag!"
            
            # SETUP SUCCESS
            elif ('🎉 setup completed successfully' in message_lower or
                'setup completed successfully' in message_lower or
                'oppsett fullført' in message_lower):
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
                'session expired' in message_lower or
                'økt utløpt' in message_lower):
                should_notify = True
                notification_type = "warning"
                notification_message = "Økt utløpt - Kjør oppsett på nytt"
            
            # AUTOMATION STATUS
            elif ('🚀 automatisering startet' in message_lower or 
                ('automation' in message_lower and 'started' in message_lower) or
                ('scheduler started' in message_lower)):
                should_notify = True
                notification_type = "info"
                notification_message = "Automatisering startet"
            
            # STOP MESSAGES (with duplicate prevention)
            elif (any(stop_phrase in message_lower for stop_phrase in [
                'scheduler stopped',
                '🛑 scheduler stopped', 
                '🛑 stopping scheduler',
                'automatisering stoppet'
            ])):
                # Only show stop notification once per stop event
                if not hasattr(self, '_current_stop_event_time'):
                    self._current_stop_event_time = time.time()
                    should_notify = True
                    notification_type = "error"
                    notification_message = "Automatisering stoppet"
                elif time.time() - self._current_stop_event_time > 5.0:
                    # Reset after 5 seconds to allow new stop events
                    self._current_stop_event_time = time.time()
                    should_notify = True
                    notification_type = "error"
                    notification_message = "Automatisering stoppet"
            
            # SHOW NOTIFICATION if conditions met
            if should_notify and notification_message:
                print(f"Triggering notification: {notification_message} ({notification_type})")
                self.show_notification(notification_message, notification_type)
            
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
            import traceback
            traceback.print_exc()
    
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
        """FIXED: Spam-proof automation toggle with proper locking"""
        try:
            # Prevent multiple simultaneous toggles
            if self._toggle_lock:
                print("Toggle already in progress, ignoring...")
                return
                
            self._toggle_lock = True
            
            # Disable button immediately to prevent spam
            if hasattr(self, 'start_button'):
                self.start_button.setEnabled(False)
            
            if not self.is_running:
                # Start automation
                QTimer.singleShot(50, self._safe_start_automation)
            else:
                # Stop automation
                QTimer.singleShot(50, self._safe_stop_automation)
                
        except Exception as e:
            print(f"Toggle error: {e}")
            self._release_toggle_lock()

    def _async_start_automation(self):
        """Async start - runs in next event loop cycle"""
        try:
            print("🚀 Async start...")
            
            # Quick checks only
            if not self.bot or not os.path.exists(self.bot.cookies_file):
                self.show_notification("Trenger oppsett først", "warning")
                self._reset_button_to_start()
                return
            
            # Set states immediately
            self.is_running = True
            self.bot.running = True
            
            # Kill old thread WITHOUT waiting (async)
            self._async_cleanup_thread()
            
            # Create new thread - NO signal connections yet
            self.scheduler_thread = SchedulerThread(self.bot)
            
            # Start thread immediately - connect signals AFTER starting
            self.scheduler_thread.start()
            
            # Connect signals AFTER thread is running
            QTimer.singleShot(50, self._connect_thread_signals)
            
            # Update UI immediately
            self._set_button_to_stop()
            if hasattr(self, 'status_indicator'):
                self.status_indicator.set_status("Kjører", "#28a745")
            
            print("✅ Start completed")
            
        except Exception as e:
            print(f"Async start error: {e}")
            QTimer.singleShot(50, self._force_reset_everything)

    def _async_stop_automation(self):
        """Async stop - runs in next event loop cycle"""
        try:
            print("🛑 Async stop...")
            
            # Set states immediately - no blocking operations
            self.is_running = False
            
            if hasattr(self, 'bot') and self.bot:
                self.bot.running = False
            
            # Update UI immediately - don't wait for thread
            self._reset_button_to_start()
            if hasattr(self, 'status_indicator'):
                self.status_indicator.set_status("Stoppet", "#dc3545")
            
            # Kill thread asynchronously - don't block GUI
            QTimer.singleShot(10, self._async_cleanup_thread)
            
            print("✅ Stop completed")
            
        except Exception as e:
            print(f"Async stop error: {e}")
            QTimer.singleShot(50, self._force_reset_everything)

    def _async_cleanup_thread(self):
        """Cleanup thread asynchronously to prevent UI blocking"""
        try:
            if hasattr(self, 'scheduler_thread') and self.scheduler_thread:
                # Terminate without waiting
                try:
                    self.scheduler_thread.terminate()
                except:
                    pass
                self.scheduler_thread = None
                print("Async thread cleanup completed")
        except:
            if hasattr(self, 'scheduler_thread'):
                self.scheduler_thread = None

    def _connect_thread_signals(self):
        """Connect thread signals after thread is running"""
        try:
            if hasattr(self, 'scheduler_thread') and self.scheduler_thread:
                # Use Qt.QueuedConnection for thread safety
                self.scheduler_thread.message_signal.connect(
                    self._ultra_safe_message_handler,
                    Qt.QueuedConnection
                )
                print("Signals connected")
        except Exception as e:
            print(f"Signal connection error: {e}")

    def _ultra_safe_message_handler(self, message):
        """Ultra-safe message handler - cannot block or crash"""
        try:
            # Process message in next event loop to prevent blocking
            QTimer.singleShot(0, lambda: self._process_message_safe(message))
        except:
            pass

    def _process_message_safe(self, message):
        """Process message safely without blocking"""
        try:
            if message and hasattr(self, 'log_message'):
                self.log_message(message)
        except:
            try:
                print(f"Log: {message}")
            except:
                pass

    def _stop_automation_bulletproof(self):
        """BULLETPROOF stop that CANNOT crash"""
        try:
            print("🛑 Stopping automation...")
            
            # Set flags IMMEDIATELY - no delays
            self.is_running = False
            
            # Stop bot immediately
            if hasattr(self, 'bot') and self.bot:
                self.bot.running = False
            
            # NUCLEAR thread cleanup - no mercy
            self._nuclear_thread_cleanup()
            
            # Reset UI immediately
            self._reset_button_to_start()
            
            # Update status
            if hasattr(self, 'status_indicator'):
                self.status_indicator.set_status("Stoppet", "#dc3545")
            
            # Log stop - but don't let this crash anything
            try:
                print("🛑 Automation stopped successfully")
            except:
                pass
            
        except Exception as e:
            print(f"Stop error: {e}")
            # Even if stop fails, force reset
            self._force_reset_everything()

    def _reset_button_to_start(self):
        """Reset button - ultra fast"""
        try:
            if hasattr(self, 'start_button'):
                self.start_button.setText("Start automatisering")
                self.start_button.setEnabled(True)
                
            if hasattr(self, 'setup_button'):
                self.setup_button.setEnabled(True)
                
        except:
            pass

    def _nuclear_thread_cleanup(self):
        """Nuclear option - destroy all threads"""
        try:
            if hasattr(self, 'scheduler_thread') and self.scheduler_thread:
                print("💥 Nuclear thread cleanup...")
                
                # Don't even try to disconnect signals - just kill
                try:
                    self.scheduler_thread.terminate()
                    # Give it 500ms max, then move on
                    self.scheduler_thread.wait(500)
                except:
                    pass
                
                # Clear reference no matter what
                self.scheduler_thread = None
                print("Thread destroyed")
        except:
            # Always clear reference
            if hasattr(self, 'scheduler_thread'):
                self.scheduler_thread = None

    def _start_automation_bulletproof(self):
        """Start automation with zero crash possibility"""
        try:
            print("Starting automation...")
            
            # Check cookies
            if not self.bot or not os.path.exists(self.bot.cookies_file):
                self.show_notification("Trenger oppsett først", "warning")
                self._reset_button_to_start()
                return
            
            # Kill any existing threads FIRST
            self._nuclear_thread_cleanup()
            
            # Set bot running
            self.bot.running = True
            self.is_running = True
            
            # Create new thread WITHOUT signal connections initially
            self.scheduler_thread = SchedulerThread(self.bot)
            
            # Connect signals using QueuedConnection for thread safety
            self.scheduler_thread.message_signal.connect(
                self._safe_message_handler, 
                Qt.QueuedConnection
            )
            
            # Start thread
            self.scheduler_thread.start()
            
            # Update UI immediately
            self._set_button_to_stop()
            if hasattr(self, 'status_indicator'):
                self.status_indicator.set_status("Kjører", "#28a745")
            
            # Log success
            print("🚀 Automation started successfully")
            
        except Exception as e:
            print(f"Start error: {e}")
            self._force_reset_everything()

    def _set_button_to_stop(self):
        """Set button to stop - ultra fast"""
        try:
            if hasattr(self, 'start_button'):
                self.start_button.setText("Stopp automatisering")  
                self.start_button.setEnabled(True)
                
            if hasattr(self, 'setup_button'):
                self.setup_button.setEnabled(False)
                
        except:
            pass

    def _force_reset_everything(self):
        """Force reset - async version"""
        try:
            print("🚨 ASYNC FORCE RESET")
            
            # Set states immediately
            self.is_running = False
            
            # Stop bot
            if hasattr(self, 'bot'):
                try:
                    self.bot.running = False
                except:
                    pass
            
            # Reset UI immediately
            self._reset_button_to_start()
            
            # Cleanup thread asynchronously
            QTimer.singleShot(10, self._async_cleanup_thread)
            
            print("Async reset completed")
            
        except:
            pass

    def _safe_message_handler(self, message):
        """Thread-safe message handler that cannot crash"""
        try:
            if message and hasattr(self, 'log_message'):
                self.log_message(message)
        except:
            # Silent fail - never crash on logging
            print(f"Log: {message}")

    def _emergency_recovery(self):
        """Ultimate crash recovery"""
        try:
            print("🚨 EMERGENCY RECOVERY")
            
            # Force all states
            self.is_running = False
            
            if hasattr(self, 'bot') and self.bot:
                self.bot.running = False
            
            # Kill threads
            self._force_cleanup_threads()
            
            # Reset UI
            try:
                self.start_button.setText("Start automatisering")
                self.start_button.setEnabled(True)
                if hasattr(self, 'setup_button'):
                    self.setup_button.setEnabled(True)
            except:
                pass
                
            print("Emergency recovery completed")
            
        except Exception as e:
            print(f"Emergency recovery failed: {e}")

    def _update_status_safe(self, status):
        """Safe status update that won't crash"""
        try:
            if status == "Stoppet" and self.is_running:
                # Auto-stop if scheduler reports stopped
                self.is_running = False
                self._update_ui_stopped()
                
        except Exception as e:
            print(f"Status update error: {e}")

    def _update_ui_running(self):
        """Update UI to running state"""
        try:
            self.start_button.setText("Stopp automatisering")
            self.start_button.setStyleSheet("""
                QPushButton {
                    background-color: #dc3545;
                    color: white;
                    border: none;
                    border-radius: 8px;
                    padding: 12px 24px;
                    font-size: 14px;
                    font-weight: 600;
                }
                QPushButton:hover { background-color: #c82333; }
            """)
            self.start_button.setEnabled(True)
            
            if hasattr(self, 'setup_button'):
                self.setup_button.setEnabled(False)
                
            if hasattr(self, 'status_indicator'):
                self.status_indicator.set_status("Kjører", "#28a745")
                
        except Exception as e:
            print(f"UI update error: {e}")

    def _update_ui_stopped(self):
        """Update UI to stopped state"""
        try:
            self.start_button.setText("Start automatisering")
            self.start_button.setStyleSheet("""
                QPushButton {
                    background-color: #0066cc;
                    color: white;
                    border: none;
                    border-radius: 8px;
                    padding: 12px 24px;
                    font-size: 14px;
                    font-weight: 600;
                }
                QPushButton:hover { background-color: #0052a3; }
            """)
            self.start_button.setEnabled(True)
            
            if hasattr(self, 'setup_button'):
                self.setup_button.setEnabled(True)
                
            if hasattr(self, 'status_indicator'):
                self.status_indicator.set_status("Stoppet", "#dc3545")
                
        except Exception as e:
            print(f"UI update error: {e}")

    def _safe_signal_handler(self, func):
        """Wrapper to prevent signal crashes"""
        try:
            func()
        except Exception as e:
            print(f"Signal handler error: {e}")

    def _force_cleanup_threads(self):
        """Brutally clean up any existing threads"""
        try:
            if hasattr(self, 'scheduler_thread') and self.scheduler_thread:
                print("🔪 Killing existing thread...")
                
                # Don't bother with graceful disconnect - just terminate
                try:
                    if self.scheduler_thread.isRunning():
                        self.scheduler_thread.terminate()
                        # Short wait only
                        self.scheduler_thread.wait(1000)
                except:
                    pass
                
                self.scheduler_thread = None
                print("Thread killed")
        except:
            self.scheduler_thread = None


    def crash_safe_reset(self):
        """Ultimate crash-safe reset"""
        try:
            print("CRASH SAFE RESET")
            
            # Force all states
            self.is_running = False
            
            # Kill thread without any ceremony
            if hasattr(self, 'scheduler_thread'):
                try:
                    if self.scheduler_thread:
                        self.scheduler_thread.terminate()
                except:
                    pass
                self.scheduler_thread = None
            
            # Reset bot
            if hasattr(self, 'bot') and self.bot:
                try:
                    self.bot.running = False
                except:
                    pass
            
            # Reset UI - each element separately with try/catch
            try:
                if hasattr(self, 'start_button'):
                    self.start_button.setText("Start automatisering") 
                    self.start_button.setEnabled(True)
            except:
                pass
                
            try:
                if hasattr(self, 'setup_button'):
                    self.setup_button.setEnabled(True)
            except:
                pass
                
            try:
                if hasattr(self, 'status_indicator'):
                    self.status_indicator.set_status("Reset", "#dc3545")
            except:
                pass
            
            print("Crash safe reset completed")
            
        except Exception as e:
            print(f"CRITICAL ERROR in crash safe reset: {e}")
            # Ultimate fallback
            try:
                self.is_running = False
            except:
                pass

    def stop_automation_safe(self):
        """Crash-safe stop method"""
        try:
            print("Safe stop initiated...")
            
            # Set states immediately
            self.is_running = False
            
            # Stop bot immediately
            if hasattr(self, 'bot') and self.bot:
                try:
                    self.bot.running = False
                except:
                    pass
            
            # Kill thread brutally - no waiting
            self.kill_thread_brutally()
            
            # Update UI
            self.update_ui_to_stopped()
            
            # Log stop
            try:
                self.log_message("🛑 Scheduler stopped")
            except:
                print("🛑 Scheduler stopped")
            
            print("Safe stop completed")
            
        except Exception as e:
            print(f"Error in stop_automation_safe: {e}")
            import traceback
            traceback.print_exc()
            self.crash_safe_reset()

    def kill_thread_brutally(self):
        """Brutally kill any existing thread - no mercy"""
        try:
            if hasattr(self, 'scheduler_thread') and self.scheduler_thread:
                print("Killing existing thread...")
                
                # Don't try to disconnect signals - just kill
                try:
                    if self.scheduler_thread.isRunning():
                        self.scheduler_thread.terminate()
                        # Don't wait - just move on
                except:
                    pass
                
                # Clear reference
                self.scheduler_thread = None
                print("Thread killed")
                
        except Exception as e:
            print(f"Error killing thread: {e}")
            # Always clear reference
            self.scheduler_thread = None

    def start_automation_safe(self):
        """Crash-safe start method"""
        try:
            # Check cookies first
            if not hasattr(self, 'bot') or not self.bot:
                print("No bot instance")
                self.crash_safe_reset()
                return
                
            if not os.path.exists(self.bot.cookies_file):
                self.show_notification("Trenger oppsett først", "warning", 3000)
                self.crash_safe_reset()
                return

            # Kill any existing thread BEFORE creating new one
            self.kill_thread_brutally()
            
            # Start bot
            self.bot.running = True
            
            # Create new thread with error handling
            try:
                self.scheduler_thread = SchedulerThread(self.bot)
                
                # Connect signals with error handling
                if hasattr(self.scheduler_thread, 'message_signal'):
                    self.scheduler_thread.message_signal.connect(self.safe_log_message)
                if hasattr(self.scheduler_thread, 'status_signal'):
                    self.scheduler_thread.status_signal.connect(self.safe_update_status)
                    
                self.scheduler_thread.start()
                print("Thread started successfully")
                
            except Exception as e:
                print(f"Error creating/starting thread: {e}")
                self.crash_safe_reset()
                return

            # Update state
            self.is_running = True
            self.update_ui_to_running()
            
            # Log success
            try:
                self.log_message("🚀 Automatisering startet")
            except:
                print("🚀 Automatisering startet")
            
        except Exception as e:
            print(f"Error in start_automation_safe: {e}")
            import traceback
            traceback.print_exc()
            self.crash_safe_reset()

    def safe_log_message(self, message):
        """Safe wrapper for log_message to prevent signal crashes"""
        try:
            self.log_message(message)
        except Exception as e:
            print(f"Error in safe_log_message: {e}")
            print(f"Original message: {message}")

    def safe_update_status(self, status):
        """Safe wrapper for update_status to prevent signal crashes"""
        try:
            self.update_status(status)
        except Exception as e:
            print(f"Error in safe_update_status: {e}")
            print(f"Original status: {status}")

    def update_ui_to_running(self):
        """Update UI to running state - crash safe"""
        try:
            if hasattr(self, 'start_button'):
                self.start_button.setText("Stopp automatisering")
                self.start_button.setEnabled(True)
                
            if hasattr(self, 'setup_button'):
                self.setup_button.setEnabled(False)
                
            if hasattr(self, 'status_indicator'):
                self.status_indicator.set_status("Kjører", "#28a745")
                
        except Exception as e:
            print(f"Error updating UI to running: {e}")

    def update_ui_to_stopped(self):
        """Update UI to stopped state - crash safe"""
        try:
            if hasattr(self, 'start_button'):
                self.start_button.setText("Start automatisering")
                self.start_button.setEnabled(True)
                
            if hasattr(self, 'setup_button'):
                self.setup_button.setEnabled(True)
                
            if hasattr(self, 'status_indicator'):
                self.status_indicator.set_status("Stoppet", "#dc3545")
                
        except Exception as e:
            print(f"Error updating UI to stopped: {e}")

    def stop_automation_simple(self):
        """Simplified stop method that avoids complex threading issues"""
        try:
            print("Simple stop initiated...")
            
            # Immediately set states
            self.is_running = False
            
            # Update button immediately and safely
            if hasattr(self, 'start_button'):
                try:
                    self.start_button.setEnabled(False)
                    self.start_button.setText("Stopper...")
                except:
                    pass
            
            # Stop bot immediately
            if hasattr(self, 'bot') and self.bot:
                try:
                    self.bot.running = False
                    print("Bot stopped")
                except:
                    pass
            
            # Simple thread cleanup - no timeouts or complex logic
            if hasattr(self, 'scheduler_thread') and self.scheduler_thread:
                try:
                    # Just terminate - don't wait for graceful shutdown
                    self.scheduler_thread.terminate()
                    self.scheduler_thread = None
                    print("Thread terminated")
                except:
                    self.scheduler_thread = None
                    pass
            
            # Reset UI immediately - no timers
            self.reset_ui_simple()
            
            print("Simple stop completed")
            
        except Exception as e:
            print(f"Error in simple stop: {e}")
            self.simple_reset()

    def start_automation_simple(self):
        """Simplified start method"""
        try:
            # Check cookies exist
            if not os.path.exists(self.bot.cookies_file):
                self.show_notification("Trenger oppsett først", "warning", 3000)
                return

            # Start bot
            self.bot.running = True
            
            # Kill any existing thread simply
            if hasattr(self, 'scheduler_thread') and self.scheduler_thread:
                try:
                    self.scheduler_thread.terminate()
                except:
                    pass
                self.scheduler_thread = None
            
            # Create new thread
            self.scheduler_thread = SchedulerThread(self.bot)
            self.scheduler_thread.message_signal.connect(self.log_message)
            self.scheduler_thread.status_signal.connect(self.update_status)
            self.scheduler_thread.start()

            # Update state
            self.is_running = True
            self.update_button_to_stop()
            
            if hasattr(self, 'setup_button'):
                self.setup_button.setEnabled(False)
            if hasattr(self, 'status_indicator'):
                self.status_indicator.set_status("Kjører", "#28a745")
            
            self.log_message("🚀 Automatisering startet")
            
        except Exception as e:
            print(f"Error in simple start: {e}")
            self.simple_reset()

    def update_button_to_stop(self):
        """Update button to stop state - simplified"""
        try:
            if hasattr(self, 'start_button'):
                self.start_button.setText("Stopp automatisering")
                self.start_button.setEnabled(True)
        except Exception as e:
            print(f"Error updating button: {e}")

    def simple_reset(self):
        """Simplest possible reset - no complex operations"""
        try:
            print("SIMPLE RESET")
            
            # Force all states
            self.is_running = False
            
            # Kill thread brutally
            if hasattr(self, 'scheduler_thread'):
                try:
                    if self.scheduler_thread:
                        self.scheduler_thread.terminate()
                except:
                    pass
                self.scheduler_thread = None
            
            # Reset bot
            if hasattr(self, 'bot') and self.bot:
                try:
                    self.bot.running = False
                except:
                    pass
            
            # Reset button
            if hasattr(self, 'start_button'):
                try:
                    self.start_button.setText("Start automatisering") 
                    self.start_button.setEnabled(True)
                except:
                    pass
                    
            if hasattr(self, 'setup_button'):
                try:
                    self.setup_button.setEnabled(True)
                except:
                    pass
            
            print("Simple reset completed")
            
        except Exception as e:
            print(f"Error in simple reset: {e}")

    def reset_ui_simple(self):
        """Simple UI reset without complex styling"""
        try:
            if hasattr(self, 'start_button'):
                self.start_button.setText("Start automatisering")
                self.start_button.setEnabled(True)
                
            if hasattr(self, 'setup_button'):
                self.setup_button.setEnabled(True)
                
            if hasattr(self, 'status_indicator'):
                self.status_indicator.set_status("Stoppet", "#dc3545")
                
            print("UI reset completed")
            
        except Exception as e:
            print(f"Error in UI reset: {e}")



    def _release_toggle_lock(self):
        """Release toggle lock to allow next action"""
        try:
            QTimer.singleShot(500, self._do_release_lock)  # Small delay to prevent immediate spam
        except:
            self._toggle_lock = False

    def _do_release_lock(self):
        """Actually release the lock"""
        self._toggle_lock = False
        print("Toggle lock released")

    def start_automation(self):
        """Public method redirect"""
        self.start_automation_safe()

    def _safe_start_automation(self):
        """FIXED: Bulletproof start method"""
        try:
            print("🚀 Starting automation...")
            
            # Check prerequisites
            if not self.bot or not os.path.exists(self.bot.cookies_file):
                self.show_notification("Trenger oppsett først", "warning")
                self._release_toggle_lock()
                return
            
            # Kill any existing threads FIRST
            self._force_cleanup_existing_threads()
            
            # Set states
            self.is_running = True
            self.bot.running = True
            
            # Create new thread
            self.scheduler_thread = SchedulerThread(self.bot)
            
            # Connect signals with proper error handling
            try:
                self.scheduler_thread.message_signal.connect(
                    self._thread_safe_log_message, 
                    Qt.QueuedConnection
                )
            except Exception as e:
                print(f"Signal connection error: {e}")
            
            # Start thread
            self.scheduler_thread.start()
            
            # Update UI
            self._update_ui_to_running()
            
            # Show notification
            self.show_notification("Automatisering startet", "info")
            
            print("✅ Start completed successfully")
            
        except Exception as e:
            print(f"Start error: {e}")
            self._emergency_reset()
        finally:
            self._release_toggle_lock()

    def _thread_safe_log_message(self, message):
        """Thread-safe message handler"""
        try:
            # Use QTimer to process in main thread
            QTimer.singleShot(0, lambda: self.log_message(message))
        except Exception as e:
            print(f"Thread message error: {e}")


    def _update_ui_to_running(self):
        """FIXED: Update UI to running state with proper button sizing"""
        try:
            if hasattr(self, 'start_button'):
                self.start_button.setText("Stopp automatisering")
                # FIXED: Added min-width and height constraints
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
                        min-width: 160px;
                    }
                    QPushButton:hover { 
                        background-color: #c82333; 
                    }
                    QPushButton:disabled {
                        background-color: #6c757d;
                        color: #dee2e6;
                    }
                """)
                self.start_button.setEnabled(True)
                
            if hasattr(self, 'setup_button'):
                self.setup_button.setEnabled(False)
                
            if hasattr(self, 'status_indicator'):
                self.status_indicator.set_status("Kjører", "#28a745")
                
        except Exception as e:
            print(f"UI update error: {e}")

    def _force_cleanup_existing_threads(self):
        """FIXED: Aggressive thread cleanup that won't hang"""
        try:
            if hasattr(self, 'scheduler_thread') and self.scheduler_thread:
                print("💀 Killing existing thread...")
                
                # Don't try to disconnect signals - just terminate
                try:
                    if self.scheduler_thread.isRunning():
                        self.scheduler_thread.terminate()
                        # Very short wait - don't block UI
                        self.scheduler_thread.wait(200)  # Max 200ms
                except:
                    pass
                
                # Clear reference
                self.scheduler_thread = None
                print("Thread killed")
                
        except Exception as e:
            print(f"Thread cleanup error: {e}")
            # Always clear reference
            if hasattr(self, 'scheduler_thread'):
                self.scheduler_thread = None

    def setup_and_login_safe(self):
        """Replace the original setup_and_login method"""
        try:
            if self.is_running:
                self.show_warning("Stopp automatisering først")
                return
                
            if hasattr(self, 'setup_thread') and self.setup_thread and self.setup_thread.isRunning():
                print("Setup already running")
                return
            
            # Kill any automation first
            self.kill_thread_brutally()
            
            # Disable buttons
            if hasattr(self, 'setup_button'):
                self.setup_button.setEnabled(False)
                self.setup_button.setText("Setter opp...")
                
            if hasattr(self, 'start_button'):
                self.start_button.setEnabled(False)
            
            # Start setup
            if hasattr(self, 'bot') and self.bot:
                self.bot.last_setup_cancelled = False
                self.setup_thread = SetupThread(self.bot)
                self.setup_thread.message_signal.connect(self.safe_log_message)
                self.setup_thread.finished_signal.connect(self.on_setup_finished_safe)
                if hasattr(self, 'progress_bar'):
                    self.progress_bar.setVisible(True)
                    self.setup_thread.progress_signal.connect(self.progress_bar.setValue)
                self.setup_thread.start()
            
        except Exception as e:
            print(f"Error in setup_and_login_safe: {e}")
            self.reset_setup_ui()
    
    def on_setup_finished_safe(self, success):
        """Safe setup finish handler"""
        try:
            if hasattr(self, 'progress_bar'):
                self.progress_bar.setVisible(False)
            self.reset_setup_ui()
            
            if success:
                if hasattr(self, 'status_indicator'):
                    self.status_indicator.set_status("Klar", "#28a745")
                print("Setup completed successfully")
            else:
                if hasattr(self, 'status_indicator'):
                    self.status_indicator.set_status("Oppsett mislyktes", "#dc3545")
                print("Setup failed")
        except Exception as e:
            print(f"Error in setup finish: {e}")
            self.reset_setup_ui()

    def reset_setup_ui(self):
        """Reset setup UI elements"""
        try:
            if hasattr(self, 'setup_button'):
                self.setup_button.setText("Oppsett og innlogging")
                self.setup_button.setEnabled(True)
            if hasattr(self, 'start_button'):
                self.start_button.setEnabled(True)
        except Exception as e:
            print(f"Error resetting setup UI: {e}")

    def stop_automation(self):
        """Public method redirect"""
        self.stop_automation_safe()

    def _safe_stop_automation(self):
        """FIXED: Bulletproof stop method"""
        try:
            print("🛑 Stopping automation...")
            
            # Set states immediately
            self.is_running = False
            
            if hasattr(self, 'bot') and self.bot:
                self.bot.running = False
            
            # Update UI immediately
            self._update_ui_to_stopped()
            
            # Kill thread asynchronously to prevent blocking
            QTimer.singleShot(10, self._async_cleanup_thread)
            
            # Show notification
            self.show_notification("Automatisering stoppet", "error")
            
            print("✅ Stop completed successfully")
            
        except Exception as e:
            print(f"Stop error: {e}")
            self._emergency_reset()
        finally:
            self._release_toggle_lock()

    def _update_ui_to_stopped(self):
        """FIXED: Update UI to stopped state with proper button sizing"""
        try:
            if hasattr(self, 'start_button'):
                self.start_button.setText("Start automatisering")
                # FIXED: Added min-width and height constraints
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
                        min-width: 160px;
                    }
                    QPushButton:hover { 
                        background-color: #0052a3; 
                    }
                    QPushButton:disabled {
                        background-color: #cccccc;
                        color: #666666;
                    }
                """)
                self.start_button.setEnabled(True)
                
            if hasattr(self, 'setup_button'):
                self.setup_button.setEnabled(True)
                
            if hasattr(self, 'status_indicator'):
                self.status_indicator.set_status("Stoppet", "#dc3545")
                
        except Exception as e:
            print(f"UI update error: {e}")

    def _cleanup_scheduler_thread(self):
        """Safely cleanup scheduler thread - FIXED to prevent exceptions"""
        try:
            if not hasattr(self, 'scheduler_thread') or not self.scheduler_thread:
                print("No scheduler thread to cleanup")
                return
                
            print("Cleaning up scheduler thread...")
            
            # Disconnect signals safely
            try:
                if self.scheduler_thread.message_signal:
                    self.scheduler_thread.message_signal.disconnect()
            except:
                pass
                
            try:
                if self.scheduler_thread.status_signal:
                    self.scheduler_thread.status_signal.disconnect()
            except:
                pass
            
            # Stop the thread gracefully
            if self.scheduler_thread.isRunning():
                try:
                    # Try to stop gracefully first
                    self.scheduler_thread.stop()
                    
                    # Wait for graceful stop (max 2 seconds)
                    if self.scheduler_thread.wait(2000):
                        print("Thread stopped gracefully")
                    else:
                        print("Graceful stop timeout, terminating thread...")
                        self.scheduler_thread.terminate()
                        # Wait for termination (max 1 second)
                        if self.scheduler_thread.wait(1000):
                            print("Thread terminated successfully")
                        else:
                            print("Thread termination timeout")
                            
                except Exception as e:
                    print(f"Error during thread cleanup: {e}")
                    # Force terminate as last resort
                    try:
                        self.scheduler_thread.terminate()
                        self.scheduler_thread.wait(500)
                    except:
                        print("Force termination failed")
            
            # Clear the thread reference
            self.scheduler_thread = None
            print("Scheduler thread cleanup completed")
            
        except Exception as e:
            print(f"Error in _cleanup_scheduler_thread: {e}")
            # Always clear the reference even if cleanup fails
            self.scheduler_thread = None

    def _complete_stop_process(self):
        """Complete the stop process - FIXED to prevent exceptions"""
        try:
            print("Completing stop process...")
            
            # Reset UI elements safely
            self._reset_start_button()
            
            # Re-enable setup button
            if hasattr(self, 'setup_button'):
                self.setup_button.setEnabled(True)
            
            # Update status indicator
            if hasattr(self, 'status_indicator'):
                self.status_indicator.set_status("Stoppet", "#dc3545")
            
            # Clear any stopping flags
            if hasattr(self, '_stopping_in_progress'):
                self._stopping_in_progress = False
            
            # Log the stop message
            self.log_message("🛑 Scheduler stopped")
            
            print("Stop process completed successfully")
            
        except Exception as e:
            print(f"Error completing stop process: {e}")
            # Force emergency reset if completion fails
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
        """Reset start button to initial state - FIXED to prevent exceptions"""
        try:
            if hasattr(self, 'start_button'):
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
                print("Start button reset successfully")
        except Exception as e:
            print(f"Error resetting start button: {e}")

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
        """Emergency reset for crashes"""
        try:
            print("🚨 EMERGENCY RESET")
            
            # Force all states
            self.is_running = False
            self._toggle_lock = False
            
            # Kill thread
            if hasattr(self, 'scheduler_thread'):
                try:
                    if self.scheduler_thread:
                        self.scheduler_thread.terminate()
                except:
                    pass
                self.scheduler_thread = None
            
            # Stop bot
            if hasattr(self, 'bot') and self.bot:
                try:
                    self.bot.running = False
                except:
                    pass
            
            # Reset UI
            self._update_ui_to_stopped()
            
            print("Emergency reset completed")
            
        except Exception as e:
            print(f"Emergency reset failed: {e}")
            # Ultimate fallback
            self._toggle_lock = False
            self.is_running = False

    def on_scheduler_stopped(self):
        """Handle stop signal - async version"""
        try:
            # Just set flag - use timer for UI updates
            self.is_running = False
            QTimer.singleShot(100, self._handle_stop_ui_update)
        except:
            pass
    
    def _handle_stop_ui_update(self):
        """Update UI after stop - runs async"""
        try:
            if not self.is_running:
                self._reset_button_to_start()
                if hasattr(self, 'status_indicator'):
                    self.status_indicator.set_status("Stoppet", "#dc3545")
        except:
            pass

    def _handle_scheduler_stopped_ui(self):
        """Handle UI updates for scheduler stopped - runs in main thread"""
        try:
            if not self.is_running:  # Double check
                self._update_ui_stopped()
        except Exception as e:
            print(f"Error updating UI after scheduler stop: {e}")      

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