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
        return keychainManager.hasCredentials()
    }
    
    func startAutomation() async {
        guard !isRunning else { return }
        guard let credentials = keychainManager.getCredentials() else {
            statusMessage = "No credentials found"
            return
        }
        
        isRunning = true
        statusMessage = "Starting automation..."
        
        // Initial login attempt
        do {
            let success = try await networkManager.login(username: credentials.username, password: credentials.password)
            if success {
                statusMessage = "Logged in successfully"
                startPeriodicCheck()
            } else {
                statusMessage = "Login failed"
                isRunning = false
            }
        } catch {
            statusMessage = "Login error: \(error.localizedDescription)"
            isRunning = false
        }
    }
    
    func stopAutomation() {
        isRunning = false
        timer?.invalidate()
        timer = nil
        statusMessage = "Automation stopped"
    }
    
    private func startPeriodicCheck() {
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
        do {
            let schedule = try await networkManager.getTodaysSchedule()
            let currentTime = Date()
            
            // Find current or upcoming STU classes
            let stuClasses = schedule.filter { $0.subject.contains("STU") }
            
            for stuClass in stuClasses {
                if shouldRegisterAttendance(for: stuClass, at: currentTime) {
                    let success = try await networkManager.registerAttendance(for: stuClass)
                    if success {
                        statusMessage = "Attendance registered for \(stuClass.subject)"
                        lastUpdateTime = currentTime
                    }
                }
            }
            
            if stuClasses.isEmpty {
                statusMessage = "No STU classes found today"
            } else {
                statusMessage = "Monitoring \(stuClasses.count) STU class(es)"
            }
            
        } catch {
            statusMessage = "Error checking schedule: \(error.localizedDescription)"
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
        return currentMinutes >= (classStartMinutes - 5) && currentMinutes <= classStartMinutes
    }
}
