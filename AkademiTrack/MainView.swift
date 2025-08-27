import SwiftUI
import CoreData

struct MainView: View {
    @StateObject private var automationManager = AutomationManager()
    @State private var showingSettings = false
    @State private var isAutomationRunning = false
    @State private var statusMessage = "Ready to start automation"
    @State private var lastUpdateTime: Date?
    
    var body: some View {
        VStack(spacing: 6) {
            // Header - Ultra compact
            VStack(spacing: 2) {
                Image(systemName: "graduationcap.fill")
                    .font(.system(size: 20))
                    .foregroundColor(.blue)
                
                Text("AkademiTrack")
                    .font(.system(size: 14, weight: .bold))
                
                Text("iSkole Automation")
                    .font(.system(size: 9))
                    .foregroundColor(.secondary)
            }
            .padding(.top, 4)
            
            // Status Section - Ultra compact
            VStack(spacing: 2) {
                HStack(spacing: 4) {
                    Circle()
                        .fill(isAutomationRunning ? Color.green : Color.gray)
                        .frame(width: 6, height: 6)
                    
                    Text(statusMessage)
                        .font(.system(size: 11, weight: .medium))
                        .foregroundColor(isAutomationRunning ? .green : .primary)
                        .lineLimit(1)
                }
                
                if let lastUpdate = lastUpdateTime {
                    Text("Last update: \(lastUpdate.formatted(date: .omitted, time: .shortened))")
                        .font(.system(size: 8))
                        .foregroundColor(.secondary)
                }
            }
            .padding(.horizontal, 8)
            .padding(.vertical, 4)
            .background(Color.gray.opacity(0.15))
            .cornerRadius(5)
            
            // Main Action Button - Compact
            Button(action: {
                if isAutomationRunning {
                    stopAutomation()
                } else {
                    startAutomation()
                }
            }) {
                HStack(spacing: 4) {
                    Image(systemName: isAutomationRunning ? "stop.fill" : "play.fill")
                        .font(.system(size: 12))
                    Text(isAutomationRunning ? "Stop Automation" : "Start Automation")
                        .font(.system(size: 13, weight: .semibold))
                }
                .foregroundColor(.white)
                .frame(maxWidth: .infinity, minHeight: 28)
                .background(isAutomationRunning ? Color.red.opacity(0.9) : Color.blue.opacity(0.9))
                .cornerRadius(6)
            }
            .disabled(!automationManager.hasCredentials())
            
            // Settings Button
            Button(action: {
                showingSettings = true
            }) {
                HStack(spacing: 4) {
                    Image(systemName: "gearshape.fill")
                        .font(.system(size: 12))
                    Text("Settings")
                        .font(.system(size: 13, weight: .semibold))
                }
                .foregroundColor(.blue.opacity(0.9))
                .frame(maxWidth: .infinity, minHeight: 28)
                .background(Color.blue.opacity(0.15))
                .cornerRadius(6)
            }
            
            if !automationManager.hasCredentials() {
                Text("Please configure your login credentials in Settings")
                    .font(.system(size: 9))
                    .foregroundColor(.orange.opacity(0.9))
                    .multilineTextAlignment(.center)
                    .padding(.horizontal, 6)
                    .padding(.top, 2)
            }
            
            Spacer(minLength: 0)
        }
        .padding(.horizontal, 12)
        .padding(.vertical, 8)
        .frame(width: 460, height: 240) // Even smaller content size
        .sheet(isPresented: $showingSettings) {
            SettingsView()
        }
        .onReceive(automationManager.$statusMessage) { message in
            statusMessage = message
        }
        .onReceive(automationManager.$isRunning) { running in
            isAutomationRunning = running
        }
        .onReceive(automationManager.$lastUpdateTime) { time in
            lastUpdateTime = time
        }
    }
    
    private func startAutomation() {
        Task {
            await automationManager.startAutomation()
        }
    }
    
    private func stopAutomation() {
        automationManager.stopAutomation()
    }
}
