import Foundation
import Combine

@MainActor
class AutomationManager: ObservableObject {
    @Published var isRunning = false
    @Published var statusMessage = "Ready to start automation"
    @Published var lastUpdateTime: Date?
    
    private var timer: Timer?
    private let networkManager = NetworkManager()
    private let keychainManager = KeychainManager.shared
    
    func hasCredentials() -> Bool {
        let hasCredentials = keychainManager.hasCredentials()
        print("🔑 Checking credentials: \(hasCredentials)")
        return hasCredentials
    }
    
    func startAutomation() async {
        guard !isRunning else {
            print("⚠️ Automation already running, ignoring start request")
            return
        }
        
        guard let credentials = keychainManager.getCredentials() else {
            statusMessage = "No credentials found"
            print("❌ No credentials found in keychain")
            return
        }
        
        print("🚀 Starting automation with username: \(credentials.username)")
        isRunning = true
        statusMessage = "Starting automation..."
        
        // Initial login attempt
        do {
            print("🔐 Attempting login...")
            let success = try await networkManager.login(username: credentials.username, password: credentials.password)
            if success {
                statusMessage = "Logged in successfully"
                print("✅ Login successful - starting periodic check")
                startPeriodicCheck()
            } else {
                statusMessage = "Login failed"
                print("❌ Login failed")
                isRunning = false
            }
        } catch {
            statusMessage = "Login error: \(error.localizedDescription)"
            print("💥 Login error: \(error.localizedDescription)")
            isRunning = false
        }
    }
    
    func stopAutomation() {
        print("🛑 Stopping automation")
        isRunning = false
        timer?.invalidate()
        timer = nil
        statusMessage = "Automation stopped"
    }
    
    private func startPeriodicCheck() {
        print("⏰ Starting periodic check (60 second intervals)")
        timer = Timer.scheduledTimer(withTimeInterval: 60, repeats: true) { [weak self] _ in
            Task { @MainActor in
                await self?.checkAndRegisterAttendance()
            }
        }
        
        // Run initial check
        Task {
            await checkAndRegisterAttendance()
        }
    }
    
    private func checkAndRegisterAttendance() async {
        print("🔍 Checking schedule for STU classes...")
        do {
            let schedule = try await networkManager.getTodaysSchedule()
            let currentTime = Date()
            
            print("📅 Retrieved \(schedule.count) classes from schedule")
            
            // Find current or upcoming STU classes
            let stuClasses = schedule.filter { $0.subject.contains("STU") }
            print("📚 Found \(stuClasses.count) STU classes")
            
            for stuClass in stuClasses {
                print("📖 Checking STU class: \(stuClass.subject) at \(stuClass.startTime)")
                if shouldRegisterAttendance(for: stuClass, at: currentTime) {
                    print("✋ Registering attendance for \(stuClass.subject)")
                    let success = try await networkManager.registerAttendance(for: stuClass)
                    if success {
                        statusMessage = "Attendance registered for \(stuClass.subject)"
                        lastUpdateTime = currentTime
                        print("✅ Attendance successfully registered for \(stuClass.subject)")
                    } else {
                        print("❌ Failed to register attendance for \(stuClass.subject)")
                    }
                } else {
                    print("⏳ Not time to register for \(stuClass.subject) yet")
                }
            }
            
            if stuClasses.isEmpty {
                statusMessage = "No STU classes found today"
                print("📭 No STU classes found today")
            } else {
                statusMessage = "Monitoring \(stuClasses.count) STU class(es)"
                print("👀 Currently monitoring \(stuClasses.count) STU class(es)")
            }
            
        } catch {
            statusMessage = "Error checking schedule: \(error.localizedDescription)"
            print("💥 Error checking schedule: \(error.localizedDescription)")
        }
    }
    
    private func shouldRegisterAttendance(for classInfo: ClassInfo, at currentTime: Date) -> Bool {
        let calendar = Calendar.current
        let now = calendar.dateComponents([.hour, .minute], from: currentTime)
        let currentMinutes = (now.hour ?? 0) * 60 + (now.minute ?? 0)
        
        // Parse class start time
        let startHour = Int(classInfo.startTime.prefix(2)) ?? 0
        let startMinute = Int(classInfo.startTime.suffix(2)) ?? 0
        let classStartMinutes = startHour * 60 + startMinute
        
        // Register attendance 5 minutes before class starts
        let shouldRegister = currentMinutes >= (classStartMinutes - 5) && currentMinutes <= classStartMinutes
        
        let formatter = DateFormatter()
        formatter.timeStyle = .short
        let currentTimeString = formatter.string(from: currentTime)
        
        print("⏰ Time check - Current: \(currentTimeString) (\(currentMinutes)min), Class: \(classInfo.startTime) (\(classStartMinutes)min), Should register: \(shouldRegister)")
        
        return shouldRegister
    }
}
