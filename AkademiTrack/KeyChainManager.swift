import Foundation
import Security

class KeychainManager: ObservableObject {
    static let shared = KeychainManager()
    
    private let service = "com.akademitrack.credentials"
    private let usernameKey = "username"
    private let passwordKey = "password"
    
    struct Credentials {
        let username: String
        let password: String
    }
    
    private init() {}
    
    func saveCredentials(username: String, password: String) -> Bool {
        let usernameSuccess = saveToKeychain(key: usernameKey, value: username)
        let passwordSuccess = saveToKeychain(key: passwordKey, value: password)
        return usernameSuccess && passwordSuccess
    }
    
    func getCredentials() -> Credentials? {
        guard let username = getFromKeychain(key: usernameKey),
              let password = getFromKeychain(key: passwordKey) else {
            return nil
        }
        return Credentials(username: username, password: password)
    }
    
    func clearCredentials() {
        deleteFromKeychain(key: usernameKey)
        deleteFromKeychain(key: passwordKey)
    }
    
    func hasCredentials() -> Bool {
        return getCredentials() != nil
    }
    
    private func saveToKeychain(key: String, value: String) -> Bool {
        guard let data = value.data(using: .utf8) else { return false }
        
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: key,
            kSecValueData as String: data
        ]
        
        // Delete existing item
        SecItemDelete(query as CFDictionary)
        
        // Add new item
        let status = SecItemAdd(query as CFDictionary, nil)
        return status == errSecSuccess
    }
    
    private func getFromKeychain(key: String) -> String? {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: key,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne
        ]
        
        var result: AnyObject?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        
        guard status == errSecSuccess,
              let data = result as? Data else {
            return nil
        }
        
        return String(data: data, encoding: .utf8)
    }
    
    private func deleteFromKeychain(key: String) {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: key
        ]
        
        SecItemDelete(query as CFDictionary)
    }
}
