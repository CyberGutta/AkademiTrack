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
    
    func debugPrintCredentials() {
        if let credentials = getCredentials() {
            print("Stored Credentials - Username: \(credentials.username), Password: \(credentials.password)")
        } else {
            print("No credentials found in Keychain")
        }
    }
    
    private func itemExists(key: String) -> Bool {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: key,
            kSecAttrSynchronizable as String: kCFBooleanTrue as Any,
            kSecMatchLimit as String: kSecMatchLimitOne,
            kSecReturnAttributes as String: true
        ]
        
        var result: AnyObject?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        return status == errSecSuccess
    }
    
    private func saveToKeychain(key: String, value: String) -> Bool {
        guard let data = value.data(using: .utf8) else {
            print("Keychain Error: Failed to convert \(key) to data")
            return false
        }
        
        let baseQuery: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: key,
            kSecAttrSynchronizable as String: kCFBooleanTrue as Any,
            kSecAttrAccessible as String: kSecAttrAccessibleAfterFirstUnlock
        ]
        
        if itemExists(key: key) {
            // Update existing item
            let updateQuery: [String: Any] = [
                kSecValueData as String: data
            ]
            
            let status = SecItemUpdate(baseQuery as CFDictionary, updateQuery as CFDictionary)
            if status == errSecSuccess {
                print("Keychain: Successfully updated \(key)")
                return true
            } else {
                print("Keychain Error: Failed to update \(key) with status: \(status)")
                return false
            }
        } else {
            // Add new item
            var addQuery = baseQuery
            addQuery[kSecValueData as String] = data
            
            let status = SecItemAdd(addQuery as CFDictionary, nil)
            if status == errSecSuccess {
                print("Keychain: Successfully saved \(key)")
                return true
            } else {
                print("Keychain Error: Failed to save \(key) with status: \(status)")
                return false
            }
        }
    }
    
    private func getFromKeychain(key: String) -> String? {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: key,
            kSecReturnData as String: true,
            kSecMatchLimit as String: kSecMatchLimitOne,
            kSecAttrSynchronizable as String: kCFBooleanTrue as Any
        ]
        
        var result: AnyObject?
        let status = SecItemCopyMatching(query as CFDictionary, &result)
        
        switch status {
        case errSecSuccess:
            guard let data = result as? Data else {
                print("Keychain Error: No data found for key: \(key)")
                return nil
            }
            guard let value = String(data: data, encoding: .utf8) else {
                print("Keychain Error: Failed to decode data for key: \(key)")
                return nil
            }
            return value
        case errSecItemNotFound:
            print("Keychain Error: Item not found for key: \(key)")
            return nil
        case errSecAuthFailed:
            print("Keychain Error: Authentication failed for key: \(key)")
            return nil
        default:
            print("Keychain Error: Failed to retrieve \(key) with status: \(status)")
            return nil
        }
    }
    
    private func deleteFromKeychain(key: String) {
        let query: [String: Any] = [
            kSecClass as String: kSecClassGenericPassword,
            kSecAttrService as String: service,
            kSecAttrAccount as String: key,
            kSecAttrSynchronizable as String: kCFBooleanTrue as Any
        ]
        
        let status = SecItemDelete(query as CFDictionary)
        if status != errSecSuccess && status != errSecItemNotFound {
            print("Keychain Error: Failed to delete \(key) with status: \(status)")
        } else {
            print("Keychain: Successfully deleted or no item found for key: \(key)")
        }
    }
}
