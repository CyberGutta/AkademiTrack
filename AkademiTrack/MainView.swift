import SwiftUI
import CoreData

struct MainView: View {
    @StateObject private var automationManager = AutomationManager()
    @State private var showingSettings = false
    @State private var isAutomationRunning = false
    @State private var statusMessage = "Ready to start automation"
    @State private var lastUpdateTime: Date?
    
    var body: some View {
        NavigationView {
            VStack(spacing: 25) {
                // Header
                VStack(spacing: 12) {
                    Image(systemName: "graduationcap.fill")
                        .font(.system(size: 80))
                        .foregroundColor(.blue)
                    
                    Text("AkademiTrack")
                        .font(.system(size: 32, weight: .bold))
                    
                    Text("iSkole Automation")
                        .font(.system(size: 18))
                        .foregroundColor(.secondary)
                }
                .padding(.top, 30)
                
                // Status Section
                VStack(spacing: 12) {
                    HStack(spacing: 10) {
                        Circle()
                            .fill(isAutomationRunning ? Color.green : Color.gray)
                            .frame(width: 16, height: 16)
                        
                        Text(statusMessage)
                            .font(.system(size: 18, weight: .medium))
                            .foregroundColor(isAutomationRunning ? .green : .primary)
                    }
                    
                    if let lastUpdate = lastUpdateTime {
                        Text("Last update: \(lastUpdate.formatted(date: .omitted, time: .shortened))")
                            .font(.system(size: 14))
                            .foregroundColor(.secondary)
                    }
                }
                .padding(20)
                .background(Color.gray.opacity(0.1))
                .cornerRadius(16)
                
                Spacer()
                
                // Main Action Button
                Button(action: {
                    if isAutomationRunning {
                        stopAutomation()
                    } else {
                        startAutomation()
                    }
                }) {
                    HStack(spacing: 12) {
                        Image(systemName: isAutomationRunning ? "stop.fill" : "play.fill")
                            .font(.system(size: 20))
                        Text(isAutomationRunning ? "Stop Automation" : "Start Automation")
                            .font(.system(size: 20, weight: .semibold))
                    }
                    .foregroundColor(.white)
                    .frame(maxWidth: .infinity, minHeight: 56)
                    .background(isAutomationRunning ? Color.red : Color.blue)
                    .cornerRadius(16)
                }
                .disabled(!automationManager.hasCredentials())
                
                if !automationManager.hasCredentials() {
                    Text("Please configure your login credentials in Settings")
                        .font(.system(size: 16))
                        .foregroundColor(.orange)
                        .multilineTextAlignment(.center)
                        .padding(.horizontal, 20)
                }
                
                Spacer()
            }
            .padding(25)
            #if os(iOS)
            .navigationBarTitleDisplayMode(.inline)
            #endif
            .toolbar {
                #if os(iOS)
                ToolbarItem(placement: .navigationBarTrailing) {
                    Button("Settings") {
                        showingSettings = true
                    }
                    .font(.system(size: 17))
                }
                #else
                ToolbarItem(placement: .primaryAction) {
                    Button("Settings") {
                        showingSettings = true
                    }
                    .font(.system(size: 17))
                }
                #endif
            }
        }
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
