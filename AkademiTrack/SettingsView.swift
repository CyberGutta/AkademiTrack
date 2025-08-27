import SwiftUI

struct SettingsView: View {
    @StateObject private var keychainManager = KeychainManager.shared
    @State private var username = ""
    @State private var password = ""
    @State private var showPassword = false
    @State private var saveStatus = ""
    @Environment(\.dismiss) private var dismiss
    
    var body: some View {
        NavigationView {
            Form {
                Section(header: Text("Login Credentials").font(.system(size: 14, weight: .medium))) {
                    TextField("Username", text: $username)
                        .textContentType(.username)
                        .autocorrectionDisabled()
                        .font(.system(size: 14))
                    
                    HStack {
                        if showPassword {
                            TextField("Password", text: $password)
                                .font(.system(size: 14))
                        } else {
                            SecureField("Password", text: $password)
                                .font(.system(size: 14))
                        }
                        
                        Button(action: {
                            showPassword.toggle()
                        }) {
                            Image(systemName: showPassword ? "eye.slash" : "eye")
                                .foregroundColor(.secondary)
                                .font(.system(size: 14))
                        }
                    }
                }
                
                Section(header: Text("Actions").font(.system(size: 14, weight: .medium))) {
                    Button("Save Credentials") {
                        saveCredentials()
                    }
                    .disabled(username.isEmpty || password.isEmpty)
                    .font(.system(size: 14))
                    
                    Button("Clear Credentials") {
                        clearCredentials()
                    }
                    .foregroundColor(.red)
                    .font(.system(size: 14))
                }
                
                if !saveStatus.isEmpty {
                    Section {
                        Text(saveStatus)
                            .foregroundColor(saveStatus.contains("successfully") ? .green : .red)
                            .font(.system(size: 13))
                    }
                }
                
                Section(header: Text("Information").font(.system(size: 14, weight: .medium))) {
                    VStack(alignment: .leading, spacing: 8) {
                        Text("Your credentials are encrypted and stored securely in the device keychain.")
                            .font(.system(size: 12))
                            .foregroundColor(.secondary)
                        
                        Text("The app will automatically register your attendance for study periods (STU) during scheduled times.")
                            .font(.system(size: 12))
                            .foregroundColor(.secondary)
                    }
                    .padding(.vertical, 4)
                }
            }
            .navigationTitle("Settings")
            #if os(iOS)
            .navigationBarTitleDisplayMode(.inline)
            #endif
            .toolbar {
                #if os(iOS)
                ToolbarItem(placement: .navigationBarLeading) {
                    Button("Cancel") {
                        dismiss()
                    }
                    .font(.system(size: 15))
                }
                
                ToolbarItem(placement: .navigationBarTrailing) {
                    Button("Done") {
                        dismiss()
                    }
                    .font(.system(size: 15))
                }
                #else
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") {
                        dismiss()
                    }
                    .font(.system(size: 15))
                }
                
                ToolbarItem(placement: .confirmationAction) {
                    Button("Done") {
                        dismiss()
                    }
                    .font(.system(size: 15))
                }
                #endif
            }
        }
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
