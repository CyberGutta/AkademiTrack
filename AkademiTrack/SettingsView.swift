import SwiftUI

struct SettingsView: View {
    @StateObject private var keychainManager = KeychainManager.shared
    @State private var username = ""
    @State private var password = ""
    @State private var showPassword = false
    @State private var saveStatus = ""
    @Environment(\.dismiss) private var dismiss
    
    var body: some View {
        VStack(spacing: 15) {
            // Header
            VStack(spacing: 6) {
                Image(systemName: "gearshape.fill")
                    .font(.system(size: 30))
                    .foregroundColor(.blue.opacity(0.9))
                
                Text("Settings")
                    .font(.system(size: 18, weight: .bold))
            }
            .padding(.top, 10)
            
            // Login Credentials Section
            VStack(spacing: 12) {
                Text("Login Credentials")
                    .font(.system(size: 16, weight: .semibold))
                    .foregroundColor(.primary)
                
                // Username Field
                VStack(alignment: .leading, spacing: 6) {
                    Text("Username")
                        .font(.system(size: 12, weight: .medium))
                        .foregroundColor(.secondary.opacity(0.9))
                    TextField("Enter your username", text: $username)
                        .textFieldStyle(.roundedBorder)
                        .textContentType(.username)
                        .autocorrectionDisabled()
                        .font(.system(size: 14))
                        .frame(height: 35)
                }
                
                // Password Field
                VStack(alignment: .leading, spacing: 6) {
                    Text("Password")
                        .font(.system(size: 12, weight: .medium))
                        .foregroundColor(.secondary.opacity(0.9))
                    HStack {
                        if showPassword {
                            TextField("Enter your password", text: $password)
                                .textFieldStyle(.roundedBorder)
                                .font(.system(size: 14))
                                .frame(height: 35)
                        } else {
                            SecureField("Enter your password", text: $password)
                                .textFieldStyle(.roundedBorder)
                                .font(.system(size: 14))
                                .frame(height: 35)
                        }
                        
                        Button(action: {
                            showPassword.toggle()
                        }) {
                            Image(systemName: showPassword ? "eye.slash" : "eye")
                                .foregroundColor(.secondary.opacity(0.8))
                                .font(.system(size: 16))
                        }
                        .padding(.leading, -30)
                    }
                }
            }
            .padding(16)
            .background(Color.gray.opacity(0.12))
            .cornerRadius(12)
            
            // Action Buttons
            VStack(spacing: 12) {
                Button("Save Credentials") {
                    saveCredentials()
                }
                .disabled(username.isEmpty || password.isEmpty)
                .frame(maxWidth: .infinity, minHeight: 40)
                .background(username.isEmpty || password.isEmpty ? Color.gray.opacity(0.4) : Color.blue.opacity(0.9))
                .foregroundColor(.white)
                .font(.system(size: 14, weight: .semibold))
                .cornerRadius(10)
                
                Button("Clear Credentials") {
                    clearCredentials()
                }
                .frame(maxWidth: .infinity, minHeight: 40)
                .background(Color.red.opacity(0.15))
                .foregroundColor(.red.opacity(0.9))
                .font(.system(size: 14, weight: .semibold))
                .cornerRadius(10)
                
                // New button to verify credentials
                Button("Verify Credentials") {
                    keychainManager.debugPrintCredentials()
                }
                .frame(maxWidth: .infinity, minHeight: 40)
                .background(Color.green.opacity(0.15))
                .foregroundColor(.green.opacity(0.9))
                .font(.system(size: 14, weight: .semibold))
                .cornerRadius(10)
            }
            
            // Status Message
            if !saveStatus.isEmpty {
                Text(saveStatus)
                    .foregroundColor(saveStatus.contains("successfully") ? .green.opacity(0.9) : .red.opacity(0.9))
                    .font(.system(size: 12, weight: .medium))
                    .padding(8)
                    .background(saveStatus.contains("successfully") ? Color.green.opacity(0.15) : Color.red.opacity(0.15))
                    .cornerRadius(6)
            }
            
            Spacer()
            
            // Done Button
            Button("Done") {
                dismiss()
            }
            .frame(maxWidth: .infinity, minHeight: 40)
            .background(Color.gray.opacity(0.15))
            .foregroundColor(.blue.opacity(0.9))
            .font(.system(size: 14, weight: .semibold))
            .cornerRadius(10)
        }
        .padding(24)
        .frame(width: 350, height: 500)
        .onAppear {
            loadCredentials()
        }
    }
    
    private func loadCredentials() {
        if let storedUsername = keychainManager.getCredentials()?.username {
            username = storedUsername
        }
        if let storedPassword = keychainManager.getCredentials()?.password {
            password = storedPassword
        }
    }
    
    private func saveCredentials() {
        let success = keychainManager.saveCredentials(username: username, password: password)
        saveStatus = success ? "Credentials saved successfully" : "Failed to save credentials"
        
        DispatchQueue.main.asyncAfter(deadline: .now() + 2) {
            saveStatus = ""
        }
    }
    
    private func clearCredentials() {
        keychainManager.clearCredentials()
        username = ""
        password = ""
        saveStatus = "Credentials cleared"
        
        DispatchQueue.main.asyncAfter(deadline: .now() + 2) {
            saveStatus = ""
        }
    }
}
