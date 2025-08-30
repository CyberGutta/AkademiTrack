import Foundation
import WebKit

struct ClassInfo {
    let id: Int
    let subject: String
    let date: String
    let startTime: String
    let endTime: String
    let timenr: Int
}

class NetworkManager {
    private var sessionCookies: [HTTPCookie] = []
    private var jsessionId: String?
    private var authCookie: String?
    
    // Create a new URLSession for each login attempt to ensure clean state
    private var urlSession: URLSession {
        let config = URLSessionConfiguration.default
        config.httpCookieStorage = nil // Don't use shared cookie storage
        config.httpCookieAcceptPolicy = .never // We'll handle cookies manually
        return URLSession(configuration: config)
    }
    
    func login(username: String, password: String) async throws -> Bool {
        print("🌐 Starting FRESH iSkole login process...")
        
        // Complete reset of all state
        await resetAllState()
        
        // Start the OAuth flow step by step
        return await performLoginFlow(username: username, password: password)
    }
    
    private func resetAllState() async {
        sessionCookies.removeAll()
        jsessionId = nil
        authCookie = nil
        
        // Also clear any system cookies for these domains
        if let cookieStorage = HTTPCookieStorage.shared.cookies {
            for cookie in cookieStorage {
                if cookie.domain.contains("iskole.net") ||
                   cookie.domain.contains("dataporten.no") ||
                   cookie.domain.contains("feide.no") {
                    HTTPCookieStorage.shared.deleteCookie(cookie)
                    print("🗑️ Deleted system cookie: \(cookie.name)")
                }
            }
        }
        
        print("🔄 COMPLETE RESET - All session data and system cookies cleared")
    }
    
    private func performLoginFlow(username: String, password: String) async -> Bool {
        print("📡 Starting OAuth flow from scratch...")
        
        // Step 1: Get initial page and cookies
        let result1 = await accessInitialPage()
        print("📊 Step 1 result: \(result1)")
        if !result1 { return false }
        
        // Step 2: Parse the dataporten login page and extract the OAuth URL
        let result2 = await parseDataportenPage()
        print("📊 Step 2 result: \(result2)")
        if !result2 { return false }
        
        // Step 3: Submit credentials to Feide
        let result3 = await submitCredentials(username: username, password: password)
        print("📊 Step 3 result: \(result3)")
        if !result3 { return false }
        
        // Step 4: Complete the callback
        let result4 = await completeCallback()
        print("📊 Step 4 result: \(result4)")
        
        return result4
    }
    
    private func accessInitialPage() async -> Bool {
        print("🔗 Accessing initial iSkole page...")
        
        let url = "https://iskole.net/elev/?isFeideinnlogget=true&ojr=login"
        
        do {
            let (data, response) = await makeRequest(url: url, method: "GET")
            
            guard let httpResponse = response as? HTTPURLResponse else {
                print("❌ No HTTP response")
                return false
            }
            
            print("📊 Initial page response: \(httpResponse.statusCode)")
            return httpResponse.statusCode == 200
            
        } catch {
            print("💥 Error accessing initial page: \(error)")
            return false
        }
    }
    
    private func parseDataportenPage() async -> Bool {
        print("🔍 Parsing dataporten login page...")
        
        let url = "https://iskole.net/iskole_login/dataporten_login?RelayState=/elev"
        
        do {
            let (data, response) = await makeRequest(url: url, method: "GET")
            
            guard let httpResponse = response as? HTTPURLResponse else {
                print("❌ No HTTP response for dataporten")
                return false
            }
            
            print("📊 Dataporten page response: \(httpResponse.statusCode)")
            
            guard let htmlContent = String(data: data, encoding: .utf8) else {
                print("❌ Could not decode HTML content")
                return false
            }
            
            debugHtmlContent(htmlContent, context: "Dataporten page")
            
            // NEW: Look for auto-redirect first
            if let autoRedirect = extractAutoRedirect(from: htmlContent) {
                print("🔗 Found auto-redirect: \(autoRedirect.prefix(100))...")
                return await followOAuthFlow(oauthUrl: autoRedirect)
            }
            
            // Try multiple extraction methods
            var oauthUrl: String?
            
            // Method 1: Extract from returnTo hidden field
            if let url = extractOAuthUrl(from: htmlContent) {
                oauthUrl = url
                print("✅ Method 1 success: OAuth URL from hidden field")
            }
            
            // Method 2: Try form action with parameters
            if oauthUrl == nil {
                if let url = extractFormActionWithParameters(from: htmlContent) {
                    oauthUrl = url
                    print("✅ Method 2 success: OAuth URL from form")
                }
            }
            
            // Method 3: NEW - Look for JavaScript redirects
            if oauthUrl == nil {
                if let url = extractJavaScriptRedirect(from: htmlContent) {
                    oauthUrl = url
                    print("✅ Method 3 success: JavaScript redirect")
                }
            }
            
            if let finalUrl = oauthUrl {
                print("🔗 Using OAuth URL: \(finalUrl)")
                return await followOAuthFlow(oauthUrl: finalUrl)
            } else {
                print("❌ Could not extract any valid OAuth URL")
                return false
            }
            
        } catch {
            print("💥 Error parsing dataporten page: \(error)")
            return false
        }
    }
    
    private func extractAutoRedirect(from html: String) -> String? {
        // Look for meta refresh
        let metaPattern = "<meta[^>]*http-equiv=['\"]refresh['\"][^>]*content=['\"][^;]*;\\s*url=([^'\"]+)['\"]"
        if let regex = try? NSRegularExpression(pattern: metaPattern, options: [.caseInsensitive]),
           let match = regex.firstMatch(in: html, options: [], range: NSRange(html.startIndex..., in: html)),
           let urlRange = Range(match.range(at: 1), in: html) {
            var url = String(html[urlRange])
            if !url.hasPrefix("http") {
                url = "https://auth.dataporten.no" + (url.hasPrefix("/") ? url : "/" + url)
            }
            return url
        }
        
        return nil
    }

    // NEW: Extract JavaScript redirects
    private func extractJavaScriptRedirect(from html: String) -> String? {
        let patterns = [
            "window\\.location\\s*=\\s*['\"]([^'\"]+)['\"]",
            "location\\.href\\s*=\\s*['\"]([^'\"]+)['\"]",
            "document\\.location\\s*=\\s*['\"]([^'\"]+)['\"]"
        ]
        
        for pattern in patterns {
            if let regex = try? NSRegularExpression(pattern: pattern, options: [.caseInsensitive]),
               let match = regex.firstMatch(in: html, options: [], range: NSRange(html.startIndex..., in: html)),
               let urlRange = Range(match.range(at: 1), in: html) {
                
                var url = String(html[urlRange])
                if !url.hasPrefix("http") {
                    url = "https://auth.dataporten.no" + (url.hasPrefix("/") ? url : "/" + url)
                }
                return url
            }
        }
        
        return nil
    }
    
    
    private func debugHtmlContent(_ html: String, context: String) {
        print("🔍 DEBUG: \(context)")
        print("📄 HTML length: \(html.count)")
        
        // Look for OAuth URLs specifically
        let oauthPatterns = [
            "auth.dataporten.no/oauth/authorization[^\\s\"'<>]+",
            "returnTo\"[^>]+value=\"[^\"]+\"",
            "client_id=[^&\\s\"'<>]+",
            "redirect_uri=[^&\\s\"'<>]+"
        ]
        
        for pattern in oauthPatterns {
            if let regex = try? NSRegularExpression(pattern: pattern, options: []) {
                let matches = regex.matches(in: html, options: [], range: NSRange(html.startIndex..., in: html))
                for match in matches {
                    let range = Range(match.range, in: html)!
                    let matchText = String(html[range])
                    print("🎯 OAuth pattern '\(pattern)': \(matchText)")
                }
            }
        }
        
        // Also look for the complete returnTo value
        if let returnToStart = html.range(of: "returnTo\" value=\"") {
            let afterReturnTo = html[returnToStart.upperBound...]
            if let returnToEnd = afterReturnTo.range(of: "\"") {
                let returnToValue = String(afterReturnTo[..<returnToEnd.lowerBound])
                print("🔗 Complete returnTo value: \(returnToValue)")
            }
        }
    }
    
    private func extractOAuthUrl(from html: String) -> String? {
        print("🔍 Extracting OAuth URL from HTML...")
        
        // Look for the complete OAuth URL in hidden form fields or href attributes
        let patterns = [
            // Look for returnTo value which contains the full OAuth URL
            "returnTo\"\\s*value=\"([^\"]+)\"",
            // Look for href with OAuth URL
            "href=\"(https://auth\\.dataporten\\.no/oauth/authorization[^\"]+)\"",
            // Look for action with OAuth URL
            "action=\"(https://auth\\.dataporten\\.no/oauth/authorization[^\"]+)\"",
            // Direct OAuth URL pattern (more comprehensive)
            "(https://auth\\.dataporten\\.no/oauth/authorization[^\\s\"'<>]+)"
        ]
        
        for pattern in patterns {
            if let regex = try? NSRegularExpression(pattern: pattern, options: []) {
                let range = NSRange(html.startIndex..., in: html)
                if let match = regex.firstMatch(in: html, options: [], range: range) {
                    // Get the first capture group if it exists, otherwise use the full match
                    let matchRange = match.numberOfRanges > 1 ?
                        Range(match.range(at: 1), in: html)! :
                        Range(match.range, in: html)!
                    
                    var url = String(html[matchRange])
                    
                    print("📍 Raw extracted URL: \(url)")
                    
                    // CRITICAL FIX: Properly decode HTML entities
                    url = decodeHtmlEntities(url)
                    print("📍 HTML entities decoded: \(url)")
                    
                    // Clean up any remaining artifacts
                    url = url.replacingOccurrences(of: "\\\"", with: "")
                    url = url.replacingOccurrences(of: "\"", with: "")
                    url = url.replacingOccurrences(of: "'", with: "")
                    
                    // Remove any trailing HTML artifacts
                    if let endIndex = url.firstIndex(where: { $0 == "<" || $0 == ">" || $0 == ";" }) {
                        url = String(url[..<endIndex])
                    }
                    
                    // Ensure proper protocol
                    if !url.hasPrefix("http") {
                        url = "https://" + url
                    }
                    
                    print("✅ Final cleaned OAuth URL: \(url)")
                    return url
                }
            }
        }
        
        print("❌ No complete OAuth URL found")
        return nil
    }
    
    private func decodeHtmlEntities(_ text: String) -> String {
        var result = text
        
        // Common HTML entities
        let entities: [String: String] = [
            "&amp;": "&",
            "&lt;": "<",
            "&gt;": ">",
            "&quot;": "\"",
            "&apos;": "'",
            "&#x27;": "'",
            "&#39;": "'",
            "&#x2F;": "/",
            "&#47;": "/"
        ]
        
        for (entity, replacement) in entities {
            result = result.replacingOccurrences(of: entity, with: replacement)
        }
        
        return result
    }
    
    private func extractFormActionWithParameters(from html: String) -> String? {
        print("🔍 Looking for form with OAuth parameters...")
        
        // Look for form element and extract action + hidden fields
        if let formStart = html.range(of: "<form"),
           let formEnd = html.range(of: "</form>", range: formStart.upperBound..<html.endIndex) {
            let formHtml = String(html[formStart.lowerBound..<formEnd.upperBound])
            
            print("📝 Found form HTML (length: \(formHtml.count))")
            
            // Extract action URL
            var actionUrl: String?
            if let actionRange = formHtml.range(of: "action=\"") {
                let afterAction = formHtml[actionRange.upperBound...]
                if let endQuote = afterAction.range(of: "\"") {
                    actionUrl = String(afterAction[..<endQuote.lowerBound])
                }
            }
            
            // If we have an action URL, try to extract hidden fields
            if let action = actionUrl {
                print("📍 Form action: \(action)")
                
                // Look for hidden inputs with OAuth parameters
                let hiddenInputPattern = "<input[^>]*type=\"hidden\"[^>]*>"
                if let regex = try? NSRegularExpression(pattern: hiddenInputPattern, options: []) {
                    let matches = regex.matches(in: formHtml, options: [], range: NSRange(formHtml.startIndex..., in: formHtml))
                    
                    var parameters: [String: String] = [:]
                    
                    for match in matches {
                        let matchRange = Range(match.range, in: formHtml)!
                        let inputHtml = String(formHtml[matchRange])
                        
                        // Extract name and value
                        if let name = extractAttribute("name", from: inputHtml),
                           let value = extractAttribute("value", from: inputHtml) {
                            parameters[name] = value
                            print("📍 Hidden field: \(name) = \(value.prefix(50))...")
                        }
                    }
                    
                    // Build complete URL with parameters
                    if !parameters.isEmpty {
                        var urlComponents = URLComponents(string: action)
                        var queryItems: [URLQueryItem] = []
                        
                        for (key, value) in parameters {
                            queryItems.append(URLQueryItem(name: key, value: value))
                        }
                        
                        urlComponents?.queryItems = queryItems
                        
                        if let completeUrl = urlComponents?.url?.absoluteString {
                            print("✅ Built complete URL from form: \(completeUrl.prefix(100))...")
                            return completeUrl
                        }
                    }
                }
            }
        }
        
        return nil
    }
    
    private func extractAnyDataportenUrl(from html: String) -> String? {
        print("🔍 Extracting any dataporten URL...")
        
        // More aggressive search for any dataporten URL
        let patterns = [
            "https://[^\\s\"'<>]*dataporten[^\\s\"'<>]*",
            "https://auth\\.dataporten\\.no[^\\s\"'<>]*",
            "['\"]https://[^'\"]*dataporten[^'\"]*['\"]"
        ]
        
        for pattern in patterns {
            if let regex = try? NSRegularExpression(pattern: pattern, options: []) {
                let range = NSRange(html.startIndex..., in: html)
                if let match = regex.firstMatch(in: html, options: [], range: range) {
                    let matchRange = Range(match.range, in: html)!
                    var url = String(html[matchRange])
                    
                    // Clean up quotes and HTML entities
                    url = url.replacingOccurrences(of: "&amp;", with: "&")
                    url = url.replacingOccurrences(of: "\"", with: "")
                    url = url.replacingOccurrences(of: "'", with: "")
                    
                    // URL decode if necessary
                    if url.contains("%3A") || url.contains("%2F") {
                        url = url.removingPercentEncoding ?? url
                    }
                    
                    print("✅ Found any dataporten URL: \(url)")
                    return url
                }
            }
        }
        
        print("❌ No dataporten URL found")
        return nil
    }
    
    private func extractAttribute(_ attributeName: String, from html: String) -> String? {
        let pattern = "\(attributeName)=\"([^\"]*)\""
        if let regex = try? NSRegularExpression(pattern: pattern, options: []),
           let match = regex.firstMatch(in: html, options: [], range: NSRange(html.startIndex..., in: html)) {
            let range = Range(match.range(at: 1), in: html)!
            return String(html[range])
        }
        return nil
    }

    
    private func followOAuthFlow(oauthUrl: String) async -> Bool {
        print("🔗 Following OAuth flow: \(oauthUrl.prefix(100))...")
        
        do {
            let (data, response) = await makeRequest(url: oauthUrl, method: "GET")
            
            guard let httpResponse = response as? HTTPURLResponse else {
                print("❌ No HTTP response for OAuth")
                return false
            }
            
            print("📊 OAuth response: \(httpResponse.statusCode)")
            
            // Check for redirect to Feide
            if let location = httpResponse.allHeaderFields["Location"] as? String {
                print("🔗 OAuth redirect to: \(location.prefix(100))...")
                return await followRedirect(location)
            }
            
            // If no redirect, check if we got HTML with a form or another redirect
            if let htmlContent = String(data: data, encoding: .utf8) {
                print("📄 OAuth HTML length: \(htmlContent.count)")
                
                // Look for Feide redirect or form
                if htmlContent.contains("idp.feide.no") {
                    print("✅ Found Feide reference in OAuth response")
                    if let feideUrl = extractFeideUrl(from: htmlContent) {
                        print("🔗 Found Feide URL: \(feideUrl.prefix(100))...")
                        return await followRedirect(feideUrl)
                    }
                }
                
                // Look for auto-submit form or JavaScript redirect
                if htmlContent.contains("form") && htmlContent.contains("submit") {
                    print("📝 Found form in OAuth response")
                    return await handleOAuthForm(html: htmlContent)
                }
            }
            
            return httpResponse.statusCode == 200
            
        } catch {
            print("💥 Error in OAuth flow: \(error)")
            return false
        }
    }
    
    private func extractFeideUrl(from html: String) -> String? {
        print("🔍 Extracting Feide URL from HTML...")
        
        // More comprehensive patterns for Feide URLs
        let patterns = [
            "https://idp\\.feide\\.no/simplesaml/[^\"'\\s<>&;]*",
            "https://idp\\.feide\\.no/[^\"'\\s<>&;]*",
            "idp\\.feide\\.no/simplesaml/[^\"'\\s<>&;]*",
            "idp\\.feide\\.no/[^\"'\\s<>&;]*"
        ]
        
        for pattern in patterns {
            if let regex = try? NSRegularExpression(pattern: pattern, options: []) {
                let range = NSRange(html.startIndex..., in: html)
                if let match = regex.firstMatch(in: html, options: [], range: range) {
                    let matchRange = Range(match.range, in: html)!
                    var url = String(html[matchRange])
                    
                    // Clean up the URL properly
                    url = url.replacingOccurrences(of: "&amp;", with: "&")
                    url = url.replacingOccurrences(of: "\\\"", with: "")
                    url = url.replacingOccurrences(of: "\"", with: "")
                    url = url.replacingOccurrences(of: "'", with: "")
                    
                    // Remove any trailing HTML artifacts
                    if let endIndex = url.firstIndex(where: { $0 == "<" || $0 == ">" || $0 == ";" }) {
                        url = String(url[..<endIndex])
                    }
                    
                    // Ensure proper protocol
                    if !url.hasPrefix("http") {
                        url = "https://" + url
                    }
                    
                    print("✅ Extracted Feide URL: \(url)")
                    return url
                }
            }
        }
        
        print("❌ No Feide URL found")
        return nil
    }
    
    private func handleOAuthForm(html: String) -> Bool {
        print("📝 Handling OAuth form submission...")
        // This would require parsing the form and submitting it
        // For now, let's try to proceed to credentials submission
        return true
    }
    
    private func followRedirect(_ location: String) async -> Bool {
        print("🔗 Following redirect: \(location.prefix(100))...")
        
        do {
            let (data, response) = await makeRequest(url: location, method: "GET")
            
            guard let httpResponse = response as? HTTPURLResponse else {
                print("❌ No HTTP response for redirect")
                return false
            }
            
            print("📊 Redirect response: \(httpResponse.statusCode)")
            
            if let responseBody = String(data: data, encoding: .utf8) {
                print("📄 Redirect response length: \(responseBody.count)")
                print("📄 Contains 'feide': \(responseBody.contains("feide"))")
                print("📄 Contains 'login': \(responseBody.contains("login"))")
                print("📄 Contains 'password': \(responseBody.contains("password"))")
            }
            
            // Continue following redirects
            if let nextLocation = httpResponse.allHeaderFields["Location"] as? String {
                print("🔗 Next redirect: \(nextLocation.prefix(100))...")
                return await followRedirect(nextLocation)
            }
            
            return httpResponse.statusCode == 200
            
        } catch {
            print("💥 Error following redirect: \(error)")
            return false
        }
    }
    
    private func submitCredentials(username: String, password: String) async -> Bool {
        print("🔐 Attempting credentials submission...")
        
        // Instead of going directly to Feide, we need to follow the OAuth flow properly
        // The OAuth URL from step 2 should lead us to the correct login form
        return await followProperOAuthFlow(username: username, password: password)
    }
    
    private func followProperOAuthFlow(username: String, password: String) async -> Bool {
        print("🌐 Following proper OAuth flow to get to login form...")
        
        // We need to go through the dataporten OAuth URL that we extracted
        // This should redirect us to the actual Feide login form
        let oauthUrl = "https://auth.dataporten.no/oauth/authorization"
        
        // Build the full OAuth request with proper parameters
        var urlComponents = URLComponents(string: oauthUrl)!
        urlComponents.queryItems = [
            URLQueryItem(name: "client_id", value: "d37eff0f-5ca3-44a8-9990-3e22150f0fd7"),
            URLQueryItem(name: "redirect_uri", value: "https://iskole.net/iskole_login/dataporten_login"),
            URLQueryItem(name: "response_type", value: "code"),
            URLQueryItem(name: "scope", value: "openid userid-nin userid-feide userid email userinfo-name"),
            URLQueryItem(name: "state", value: extractStateFromLastRequest() ?? "default_state")
        ]
        
        guard let fullOAuthUrl = urlComponents.url?.absoluteString else {
            print("❌ Failed to build OAuth URL")
            return false
        }
        
        print("🔗 Starting OAuth authorization flow...")
        return await performOAuthAuthorization(fullOAuthUrl, username: username, password: password)
    }

    private func extractStateFromLastRequest() -> String? {
        // Extract state from the last OAuth URL we found
        // This should be stored from the previous step, but for now use a default
        return nil // This will use "default_state" from above
    }

    private func performOAuthAuthorization(_ oauthUrl: String, username: String, password: String) async -> Bool {
        print("🌐 GET \(oauthUrl.prefix(80))...")
        
        do {
            let (data, response) = await makeRequest(url: oauthUrl, method: "GET")
            
            guard let httpResponse = response as? HTTPURLResponse else {
                print("❌ No HTTP response from OAuth")
                return false
            }
            
            print("📊 OAuth authorization response: \(httpResponse.statusCode)")
            
            // Handle redirects
            if let location = httpResponse.allHeaderFields["Location"] as? String {
                print("🔗 OAuth redirect to: \(location.prefix(100))...")
                return await handleOAuthRedirect(location, username: username, password: password)
            }
            
            // If we got HTML, check what kind of page it is
            if let htmlContent = String(data: data, encoding: .utf8) {
                print("📄 OAuth response length: \(htmlContent.count)")
                print("📄 Contains 'feide': \(htmlContent.contains("feide"))")
                print("📄 Contains 'login': \(htmlContent.contains("login"))")
                print("📄 Contains 'password': \(htmlContent.contains("password"))")
                print("📄 Contains 'form': \(htmlContent.contains("form"))")
                
                // Check for identity provider selection
                if htmlContent.contains("feide") && htmlContent.contains("select") {
                    print("🎯 Found identity provider selection page")
                    return await handleIdentityProviderSelection(htmlContent, username: username, password: password)
                }
                
                // Check for direct login form
                if htmlContent.contains("password") && htmlContent.contains("form") {
                    print("🎯 Found login form directly")
                    return await handleLoginForm(htmlContent, username: username, password: password)
                }
                
                // Look for any Feide-related links or forms
                if let feideUrl = extractAnyFeideLoginUrl(from: htmlContent) {
                    print("🔗 Found Feide login URL: \(feideUrl.prefix(100))...")
                    return await accessFeideLoginForm(feideUrl, username: username, password: password)
                }
            }
            
            return httpResponse.statusCode >= 200 && httpResponse.statusCode < 400
            
        } catch {
            print("💥 Error in OAuth authorization: \(error)")
            return false
        }
    }
    
    private func handleOAuthRedirect(_ location: String, username: String, password: String) async -> Bool {
        print("🔗 Handling OAuth redirect: \(location.prefix(100))...")
        
        // If it's a Feide URL, handle it specially
        if location.contains("idp.feide.no") {
            return await accessFeideLoginForm(location, username: username, password: password)
        }
        
        // Otherwise follow the redirect normally
        return await performOAuthAuthorization(location, username: username, password: password)
    }

    private func handleIdentityProviderSelection(_ html: String, username: String, password: String) async -> Bool {
        print("🎯 Handling identity provider selection...")
        
        // Save HTML for debugging
        let debugFile = "/tmp/feide_debug.html"
        try? html.write(toFile: debugFile, atomically: true, encoding: .utf8)
        print("🔍 HTML saved to: \(debugFile)")
        
        // Method 1: Look for direct Feide links with organization
        if let directFeideUrl = findDirectFeideUrl(from: html) {
            print("🔗 Found direct Feide URL: \(directFeideUrl.prefix(100))...")
            return await accessFeideLoginForm(directFeideUrl, username: username, password: password)
        }
        
        // Method 2: Look for organization selection form
        if let orgForm = findOrganizationForm(from: html) {
            print("📝 Found organization form")
            return await submitOrganizationForm(orgForm, username: username, password: password)
        }
        
        // Method 3: Try clicking/submitting any Feide-related button or link
        if let feideAction = findFeideAction(from: html) {
            print("🖱️ Found Feide action: \(feideAction)")
            return await executeFeideAction(feideAction, username: username, password: password)
        }
        
        // Method 4: Construct direct URL with organization
        let constructedUrl = constructFeideUrlWithOrg()
        print("🔗 Trying constructed URL: \(constructedUrl.prefix(100))...")
        return await accessFeideLoginForm(constructedUrl, username: username, password: password)
    }

    private func findDirectFeideUrl(from html: String) -> String? {
        let patterns = [
            // Look for complete URLs with organization
            "href=['\"]([^'\"]*idp\\.feide\\.no[^'\"]*org[^'\"]*)['\"]",
            "href=['\"]([^'\"]*idp\\.feide\\.no[^'\"]*SSOService[^'\"]*)['\"]",
            // Look for data attributes or onclick handlers
            "data-href=['\"]([^'\"]*feide[^'\"]*)['\"]",
            "onclick=[^'\"]*location[^'\"]*=[^'\"]*['\"]([^'\"]*feide[^'\"]*)['\"]",
            // Look for form actions
            "action=['\"]([^'\"]*feide[^'\"]*)['\"]"
        ]
        
        for pattern in patterns {
            if let regex = try? NSRegularExpression(pattern: pattern, options: [.caseInsensitive]),
               let match = regex.firstMatch(in: html, options: [], range: NSRange(html.startIndex..., in: html)),
               let urlRange = Range(match.range(at: 1), in: html) {
                
                var url = String(html[urlRange])
                url = decodeHtmlEntities(url)
                
                if url.hasPrefix("/") {
                    url = "https://auth.dataporten.no" + url
                } else if !url.hasPrefix("http") {
                    url = "https://" + url
                }
                
                return url
            }
        }
        
        return nil
    }

    // NEW: Find organization selection forms more accurately
    private func findOrganizationForm(from html: String) -> [String: String]? {
        // Look for forms that might contain organization selection
        let formPattern = "(?s)<form[^>]*>.*?</form>"
        guard let regex = try? NSRegularExpression(pattern: formPattern, options: [.dotMatchesLineSeparators]) else {
            return nil
        }
        
        let matches = regex.matches(in: html, options: [], range: NSRange(html.startIndex..., in: html))
        
        for match in matches {
            guard let matchRange = Range(match.range, in: html) else { continue }
            let formHtml = String(html[matchRange])
            
            // Check if this form relates to identity provider selection
            if formHtml.lowercased().contains("feide") ||
               formHtml.lowercased().contains("provider") ||
               formHtml.lowercased().contains("identity") ||
               formHtml.lowercased().contains("org") ||
               formHtml.contains("drammen") ||
               formHtml.contains("akademiet") {
                
                var formData: [String: String] = [:]
                
                // Extract action - FIXED VERSION
                if let action = extractAttribute("action", from: formHtml) {
                    var actionUrl = action.trimmingCharacters(in: .whitespacesAndNewlines)
                    
                    // Handle empty action (submit to current page)
                    if actionUrl.isEmpty {
                        actionUrl = "https://auth.dataporten.no/oauth/authorization"
                    } else if actionUrl.hasPrefix("/") {
                        actionUrl = "https://auth.dataporten.no" + actionUrl
                    } else if !actionUrl.hasPrefix("http") {
                        actionUrl = "https://auth.dataporten.no/" + actionUrl
                    }
                    
                    formData["action"] = actionUrl
                    print("📍 Found form action: \(actionUrl)")
                } else {
                    // No action attribute found - use current URL as default
                    formData["action"] = "https://auth.dataporten.no/oauth/authorization"
                    print("📍 No form action found, using default")
                }
                
                // Extract method
                formData["method"] = extractAttribute("method", from: formHtml)?.uppercased() ?? "POST"
                
                // Extract ALL inputs, not just hidden ones
                extractAllFormInputs(from: formHtml, into: &formData)
                
                // Add Feide organization parameter if not present
                let hasOrgParam = formData.keys.contains { key in
                    key.lowercased().contains("org") ||
                    key.lowercased().contains("idp") ||
                    key.lowercased().contains("provider")
                }
                
                if !hasOrgParam {
                    // Try different parameter names that might work
                    formData["org"] = "feide.drammen.akademiet.no"
                    formData["idp"] = "https://idp.feide.no"
                    formData["selectedIdP"] = "https://idp.feide.no"
                    print("📍 Added default org parameters")
                }
                
                return formData
            }
        }
        
        return nil
    }

    // NEW: Extract all form inputs including selects, radios, etc.
    private func extractAllFormInputs(from html: String, into formData: inout [String: String]) {
        // Extract input fields
        let inputPattern = "<input[^>]*>"
        if let regex = try? NSRegularExpression(pattern: inputPattern, options: [.caseInsensitive]) {
            let matches = regex.matches(in: html, options: [], range: NSRange(html.startIndex..., in: html))
            
            for match in matches {
                guard let matchRange = Range(match.range, in: html) else { continue }
                let inputHtml = String(html[matchRange])
                
                if let name = extractAttribute("name", from: inputHtml) {
                    let value = extractAttribute("value", from: inputHtml) ?? ""
                    let inputType = extractAttribute("type", from: inputHtml)?.lowercased() ?? "text"
                    
                    // Handle different input types
                    switch inputType {
                    case "radio":
                        // For radio buttons, only use if checked
                        if inputHtml.contains("checked") {
                            formData[name] = value
                            print("📍 Radio button: \(name) = \(value)")
                        }
                    case "checkbox":
                        // For checkboxes, only use if checked
                        if inputHtml.contains("checked") {
                            formData[name] = value
                            print("📍 Checkbox: \(name) = \(value)")
                        }
                    case "submit", "button":
                        // Skip submit buttons
                        continue
                    default:
                        formData[name] = value
                        print("📍 Input: \(name) = \(value)")
                    }
                }
            }
        }
        
        // Extract select fields
        let selectPattern = "(?s)<select[^>]*name=['\"]([^'\"]*)['\"][^>]*>.*?</select>"
        if let regex = try? NSRegularExpression(pattern: selectPattern, options: [.dotMatchesLineSeparators]) {
            let matches = regex.matches(in: html, options: [], range: NSRange(html.startIndex..., in: html))
            
            for match in matches {
                guard match.numberOfRanges >= 2,
                      let nameRange = Range(match.range(at: 1), in: html),
                      let selectRange = Range(match.range, in: html) else { continue }
                
                let selectName = String(html[nameRange])
                let selectHtml = String(html[selectRange])
                
                // Look for Feide-related options first
                if let feideValue = findFeideSelectValue(from: selectHtml) {
                    formData[selectName] = feideValue
                    print("📍 Select (Feide): \(selectName) = \(feideValue)")
                } else if let selectedValue = extractSelectedOption(from: selectHtml) {
                    formData[selectName] = selectedValue
                    print("📍 Select: \(selectName) = \(selectedValue)")
                }
            }
        }
    }
    // NEW: Find Feide-specific values in select options
    private func findFeideSelectValue(from selectHtml: String) -> String? {
        let optionPattern = "<option[^>]*value=['\"]([^'\"]*feide[^'\"]*)['\"]"
        
        if let regex = try? NSRegularExpression(pattern: optionPattern, options: [.caseInsensitive]),
           let match = regex.firstMatch(in: selectHtml, options: [], range: NSRange(selectHtml.startIndex..., in: selectHtml)),
           let valueRange = Range(match.range(at: 1), in: selectHtml) {
            return String(selectHtml[valueRange])
        }
        
        return nil
    }

    // NEW: Find clickable Feide actions (buttons, links)
    private func findFeideAction(from html: String) -> String? {
        let patterns = [
            // Button onclick handlers
            "<button[^>]*onclick=['\"]([^'\"]*feide[^'\"]*)['\"]",
            // Link href with Feide
            "<a[^>]*href=['\"]([^'\"]*feide[^'\"]*)['\"]",
            // Form with Feide in action
            "<form[^>]*action=['\"]([^'\"]*feide[^'\"]*)['\"]"
        ]
        
        for pattern in patterns {
            if let regex = try? NSRegularExpression(pattern: pattern, options: [.caseInsensitive]),
               let match = regex.firstMatch(in: html, options: [], range: NSRange(html.startIndex..., in: html)),
               let actionRange = Range(match.range(at: 1), in: html) {
                
                var action = String(html[actionRange])
                
                // Clean up onclick handlers to extract URLs
                if action.contains("location") {
                    // Extract URL from location assignment
                    let urlPattern = "location[^'\"]*=[^'\"]*['\"]([^'\"]+)['\"]"
                    if let urlRegex = try? NSRegularExpression(pattern: urlPattern, options: []),
                       let urlMatch = urlRegex.firstMatch(in: action, options: [], range: NSRange(action.startIndex..., in: action)),
                       let urlRange = Range(urlMatch.range(at: 1), in: action) {
                        action = String(action[urlRange])
                    }
                }
                
                if !action.hasPrefix("http") && action.hasPrefix("/") {
                    action = "https://auth.dataporten.no" + action
                }
                
                return action
            }
        }
        
        return nil
    }

    // NEW: Execute Feide actions (navigate to URLs)
    private func executeFeideAction(_ action: String, username: String, password: String) async -> Bool {
        return await accessFeideLoginForm(action, username: username, password: password)
    }

    // NEW: Construct Feide URL with organization parameter
    private func constructFeideUrlWithOrg() -> String {
        return "https://idp.feide.no/simplesaml/saml2/idp/SSOService.php?spentityid=https://auth.dataporten.no&RelayState=/&org=feide.drammen.akademiet.no"
    }
    
    private func findFeideOrganizationUrl(from html: String) -> String? {
        let patterns = [
            // Look for links with org parameter pointing to Feide
            "href=\"([^\"]*[?&]org=[^\"]*feide[^\"]*?)\"",
            "href=\"([^\"]*idp\\.feide\\.no[^\"]*?)\"",
            // Look for data attributes that might contain URLs
            "data-href=\"([^\"]*feide[^\"]*?)\"",
            "data-url=\"([^\"]*feide[^\"]*?)\"",
            // Look for JavaScript that sets location
            "location\\s*=\\s*['\"]([^'\"]*feide[^'\"]*?)['\"]"
        ]
        
        for pattern in patterns {
            if let regex = try? NSRegularExpression(pattern: pattern, options: [.caseInsensitive]) {
                let range = NSRange(html.startIndex..., in: html)
                if let match = regex.firstMatch(in: html, options: [], range: range) {
                    let matchRange = Range(match.range(at: 1), in: html)!
                    var url = String(html[matchRange])
                    
                    // Decode HTML entities
                    url = decodeHtmlEntities(url)
                    
                    // Make absolute if relative
                    if url.hasPrefix("/") {
                        url = "https://auth.dataporten.no" + url
                    } else if !url.hasPrefix("http") {
                        url = "https://" + url
                    }
                    
                    return url
                }
            }
        }
        
        return nil
    }

    // Find organization selection form
    private func findOrganizationSelectionForm(from html: String) -> [String: String]? {
        // Look for forms that might handle organization selection
        let formPattern = "(?s)<form[^>]*>.*?</form>"
        guard let regex = try? NSRegularExpression(pattern: formPattern, options: [.dotMatchesLineSeparators]) else {
            return nil
        }
        
        let matches = regex.matches(in: html, options: [], range: NSRange(html.startIndex..., in: html))
        
        for match in matches {
            guard let matchRange = Range(match.range, in: html) else { continue }
            let formHtml = String(html[matchRange])
            
            // Check if this form is related to organization/identity provider selection
            if formHtml.lowercased().contains("org") ||
               formHtml.lowercased().contains("provider") ||
               formHtml.lowercased().contains("identity") {
                
                var formData: [String: String] = [:]
                
                // Extract action
                if let action = extractAttribute("action", from: formHtml) {
                    var actionUrl = action
                    if actionUrl.isEmpty {
                        actionUrl = "https://auth.dataporten.no/oauth/authorization"
                    } else if actionUrl.hasPrefix("/") {
                        actionUrl = "https://auth.dataporten.no" + actionUrl
                    }
                    formData["action"] = actionUrl
                }
                
                // Extract method
                formData["method"] = extractAttribute("method", from: formHtml)?.uppercased() ?? "GET"
                
                // Extract all input fields
                extractAllInputs(from: formHtml, into: &formData)
                
                // Add organization hint for Feide if not present
                if !formData.keys.contains(where: { $0.lowercased().contains("org") }) {
                    formData["org"] = "feide.drammen.akademiet.no"
                }
                
                return formData
            }
        }
        
        return nil
    }
    
    private func extractAllInputs(from html: String, into formData: inout [String: String]) {
        let inputPattern = "<input[^>]*>"
        guard let regex = try? NSRegularExpression(pattern: inputPattern, options: [.caseInsensitive]) else { return }
        
        let matches = regex.matches(in: html, options: [], range: NSRange(html.startIndex..., in: html))
        
        for match in matches {
            guard let matchRange = Range(match.range, in: html) else { continue }
            let inputHtml = String(html[matchRange])
            
            if let name = extractAttribute("name", from: inputHtml) {
                let value = extractAttribute("value", from: inputHtml) ?? ""
                formData[name] = decodeHtmlEntities(value)
            }
        }
    }

    // Submit organization selection form
    private func submitOrganizationForm(_ formData: [String: String], username: String, password: String) async -> Bool {
        guard let action = formData["action"] else {
            print("❌ No form action found")
            return false
        }
        
        let method = formData["method"] ?? "POST"
        print("📤 Submitting organization form:")
        print("   Method: \(method)")
        print("   Action: \(action)")
        
        // Build parameters, excluding metadata
        var params: [String] = []
        let excludedKeys = ["action", "method"]
        
        for (key, value) in formData where !excludedKeys.contains(key) {
            let encodedValue = value.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? value
            params.append("\(key)=\(encodedValue)")
            print("   \(key): \(value)")
        }
        
        let paramString = params.joined(separator: "&")
        
        // For GET requests, append parameters to URL
        var finalUrl = action
        var requestBody: String? = nil
        var contentType: String? = nil
        
        if method.uppercased() == "GET" && !paramString.isEmpty {
            let separator = action.contains("?") ? "&" : "?"
            finalUrl = action + separator + paramString
        } else if method.uppercased() == "POST" {
            requestBody = paramString
            contentType = "application/x-www-form-urlencoded"
        }
        
        print("📤 Final URL: \(finalUrl)")
        if let body = requestBody {
            print("📤 Request body: \(body)")
        }
        
        let (data, response) = await makeRequest(
            url: finalUrl,
            method: method,
            body: requestBody,
            contentType: contentType
        )
        
        guard let httpResponse = response as? HTTPURLResponse else {
            print("❌ No HTTP response")
            return false
        }
        
        print("📊 Organization form response: \(httpResponse.statusCode)")
        
        // Handle redirect
        if let location = httpResponse.allHeaderFields["Location"] as? String {
            print("🔗 Organization form redirect: \(location.prefix(100))...")
            return await accessFeideLoginForm(location, username: username, password: password)
        }
        
        // Check response content
        if let responseHtml = String(data: data, encoding: .utf8) {
            print("📄 Organization form response length: \(responseHtml.count)")
            
            // Success: got Feide login form
            if responseHtml.contains("password") && (responseHtml.contains("feidename") || responseHtml.contains("username")) {
                print("✅ Got Feide login form from organization selection!")
                return await handleLoginForm(responseHtml, username: username, password: password)
            }
            
            // Got another selection page - try recursion
            if responseHtml.contains("feide") && responseHtml.contains("form") && !responseHtml.contains("password") {
                print("🔄 Got another selection page, trying recursively...")
                return await handleIdentityProviderSelection(responseHtml, username: username, password: password)
            }
            
            // Check for JavaScript redirects
            if let jsRedirect = extractJavaScriptRedirect(from: responseHtml) {
                print("🔗 Found JavaScript redirect in org form response")
                return await accessFeideLoginForm(jsRedirect, username: username, password: password)
            }
        }
        
        // Consider 2xx and 3xx status codes as potential success
        return httpResponse.statusCode >= 200 && httpResponse.statusCode < 400
    }

    // Construct direct Feide URL with organization
    private func constructDirectFeideUrl(from html: String) -> String {
        // Extract the state parameter from the current page if possible
        var stateParam = ""
        if let state = extractStateParameter(from: html) {
            stateParam = "&state=\(state)"
        }
        
        // Build a direct URL to Feide with organization hint
        return "https://idp.feide.no/simplesaml/saml2/idp/SSOService.php?spentityid=https://auth.dataporten.no&RelayState=https://auth.dataporten.no/oauth/authorization?client_id=d37eff0f-5ca3-44a8-9990-3e22150f0fd7\(stateParam)"
    }

    // Extract state parameter from HTML
    private func extractStateParameter(from html: String) -> String? {
        let patterns = [
            "state=([^&\\s\"'<>]+)",
            "\"state\"\\s*:\\s*\"([^\"]+)\"",
            "name=\"state\"[^>]*value=\"([^\"]+)\""
        ]
        
        for pattern in patterns {
            if let regex = try? NSRegularExpression(pattern: pattern, options: []) {
                let range = NSRange(html.startIndex..., in: html)
                if let match = regex.firstMatch(in: html, options: [], range: range) {
                    let matchRange = Range(match.range(at: 1), in: html)!
                    return String(html[matchRange])
                }
            }
        }
        
        return nil
    }
    
    private func extractAllFormsEnhanced(from html: String) -> [[String: String]] {
        var forms: [[String: String]] = []
        
        // Use a more robust regex to find forms
        let formPattern = "(?s)<form[^>]*>.*?</form>"
        guard let regex = try? NSRegularExpression(pattern: formPattern, options: [.dotMatchesLineSeparators]) else {
            return forms
        }
        
        let matches = regex.matches(in: html, options: [], range: NSRange(html.startIndex..., in: html))
        
        for match in matches {
            guard let matchRange = Range(match.range, in: html) else { continue }
            let formHtml = String(html[matchRange])
            
            var formData: [String: String] = [:]
            
            // Extract method (default to GET if not specified)
            let method = extractAttribute("method", from: formHtml)?.uppercased() ?? "GET"
            formData["method"] = method
            
            // Extract action and make it absolute
            if let action = extractAttribute("action", from: formHtml) {
                var actionUrl = action.trimmingCharacters(in: .whitespacesAndNewlines)
                
                // Handle empty or relative actions
                if actionUrl.isEmpty {
                    actionUrl = "https://auth.dataporten.no/oauth/authorization" // Use current page
                } else if actionUrl.hasPrefix("/") {
                    actionUrl = "https://auth.dataporten.no" + actionUrl
                } else if !actionUrl.hasPrefix("http") {
                    actionUrl = "https://" + actionUrl
                }
                
                formData["action"] = actionUrl
            }
            
            // Extract all input fields with better handling
            extractInputFields(from: formHtml, into: &formData)
            
            // Extract select fields with their default values
            extractSelectFields(from: formHtml, into: &formData)
            
            if !formData.isEmpty {
                forms.append(formData)
            }
        }
        
        return forms
    }

    // Extract input fields more thoroughly
    private func extractInputFields(from html: String, into formData: inout [String: String]) {
        let inputPattern = "<input[^>]*>"
        guard let regex = try? NSRegularExpression(pattern: inputPattern, options: [.caseInsensitive]) else { return }
        
        let matches = regex.matches(in: html, options: [], range: NSRange(html.startIndex..., in: html))
        
        for match in matches {
            guard let matchRange = Range(match.range, in: html) else { continue }
            let inputHtml = String(html[matchRange])
            
            // Skip buttons and submits for now
            let inputType = extractAttribute("type", from: inputHtml)?.lowercased()
            if inputType == "button" || inputType == "submit" { continue }
            
            if let name = extractAttribute("name", from: inputHtml) {
                let value = extractAttribute("value", from: inputHtml) ?? ""
                formData[name] = value
            }
        }
    }

    // Extract select fields and their default values
    private func extractSelectFields(from html: String, into formData: inout [String: String]) {
        let selectPattern = "(?s)<select[^>]*name=\"([^\"]*?)\"[^>]*>.*?</select>"
        guard let regex = try? NSRegularExpression(pattern: selectPattern, options: [.dotMatchesLineSeparators]) else { return }
        
        let matches = regex.matches(in: html, options: [], range: NSRange(html.startIndex..., in: html))
        
        for match in matches {
            guard match.numberOfRanges >= 2,
                  let nameRange = Range(match.range(at: 1), in: html),
                  let selectRange = Range(match.range, in: html) else { continue }
            
            let selectName = String(html[nameRange])
            let selectHtml = String(html[selectRange])
            
            // Look for selected option or use first option
            if let selectedValue = extractSelectedOption(from: selectHtml) {
                formData[selectName] = selectedValue
            }
        }
    }

    // Extract the selected option value from a select element
    private func extractSelectedOption(from selectHtml: String) -> String? {
        // First try to find selected option
        let selectedPattern = "(?s)<option[^>]*selected[^>]*value=\"([^\"]*?)\""
        if let regex = try? NSRegularExpression(pattern: selectedPattern, options: [.caseInsensitive, .dotMatchesLineSeparators]),
           let match = regex.firstMatch(in: selectHtml, options: [], range: NSRange(selectHtml.startIndex..., in: selectHtml)),
           let valueRange = Range(match.range(at: 1), in: selectHtml) {
            return String(selectHtml[valueRange])
        }
        
        // Fall back to first option
        let firstOptionPattern = "<option[^>]*value=\"([^\"]*?)\""
        if let regex = try? NSRegularExpression(pattern: firstOptionPattern, options: [.caseInsensitive]),
           let match = regex.firstMatch(in: selectHtml, options: [], range: NSRange(selectHtml.startIndex..., in: selectHtml)),
           let valueRange = Range(match.range(at: 1), in: selectHtml) {
            return String(selectHtml[valueRange])
        }
        
        return nil
    }

    // Try to submit a form with intelligent parameter handling
    private func tryFormSubmission(_ formData: [String: String], username: String, password: String) async -> Bool? {
        guard let action = formData["action"] else {
            print("❌ No form action found")
            return nil
        }
        
        let method = formData["method"] ?? "GET"
        print("📤 Attempting form submission:")
        print("   Method: \(method)")
        print("   Action: \(action)")
        
        // Build parameters
        var params: [String] = []
        for (key, value) in formData where !["action", "method"].contains(key) {
            let encodedValue = value.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? value
            params.append("\(key)=\(encodedValue)")
            print("   \(key): \(value)")
        }
        
        let paramString = params.joined(separator: "&")
        
        let (data, response) = await makeRequest(
            url: action,
            method: method,
            body: method == "POST" ? paramString : nil,
            contentType: method == "POST" ? "application/x-www-form-urlencoded" : nil
        )
        
        guard let httpResponse = response as? HTTPURLResponse else { return false }
        
        print("📊 Form submission response: \(httpResponse.statusCode)")
        
        // Handle redirect
        if let location = httpResponse.allHeaderFields["Location"] as? String {
            print("🔗 Form redirect: \(location.prefix(100))...")
            return await accessFeideLoginForm(location, username: username, password: password)
        }
        
        // Check response content
        if let responseHtml = String(data: data, encoding: .utf8) {
            print("📄 Form response length: \(responseHtml.count)")
            
            // Success indicators
            if responseHtml.contains("password") && responseHtml.contains("feidename") {
                print("✅ Got Feide login form!")
                return await handleLoginForm(responseHtml, username: username, password: password)
            }
            
            // Check for another selection page (recursive handling)
            if responseHtml.contains("feide") && responseHtml.contains("form") && !responseHtml.contains("password") {
                print("🔄 Got another selection page, trying recursively...")
                return await handleIdentityProviderSelection(responseHtml, username: username, password: password)
            }
            
            // Check for direct redirect in JavaScript or meta
            if let jsUrl = extractJavaScriptNavigation(from: responseHtml) {
                print("🔗 Found JavaScript redirect in response")
                return await accessFeideLoginForm(jsUrl, username: username, password: password)
            }
        }
        
        let isSuccess = httpResponse.statusCode >= 200 && httpResponse.statusCode < 400
        return isSuccess ? true : false
    }

    // Extract JavaScript-based navigation
    private func extractJavaScriptNavigation(from html: String) -> String? {
        let patterns = [
            "window\\.location\\s*=\\s*['\"]([^'\"]+)['\"]",
            "location\\.href\\s*=\\s*['\"]([^'\"]+)['\"]",
            "document\\.location\\s*=\\s*['\"]([^'\"]+)['\"]",
            "window\\.location\\.href\\s*=\\s*['\"]([^'\"]+)['\"]"
        ]
        
        for pattern in patterns {
            if let regex = try? NSRegularExpression(pattern: pattern, options: [.caseInsensitive]),
               let match = regex.firstMatch(in: html, options: [], range: NSRange(html.startIndex..., in: html)),
               let urlRange = Range(match.range(at: 1), in: html) {
                
                var url = String(html[urlRange])
                if url.hasPrefix("/") {
                    url = "https://auth.dataporten.no" + url
                } else if !url.hasPrefix("http") {
                    url = "https://" + url
                }
                return url
            }
        }
        
        return nil
    }

    // Extract meta refresh redirects
    private func extractMetaRefresh(from html: String) -> String? {
        let pattern = "<meta[^>]*http-equiv=['\"]refresh['\"][^>]*content=['\"][^;]*;\\s*url=([^'\"]+)['\"]"
        
        if let regex = try? NSRegularExpression(pattern: pattern, options: [.caseInsensitive]),
           let match = regex.firstMatch(in: html, options: [], range: NSRange(html.startIndex..., in: html)),
           let urlRange = Range(match.range(at: 1), in: html) {
            
            var url = String(html[urlRange])
            if url.hasPrefix("/") {
                url = "https://auth.dataporten.no" + url
            } else if !url.hasPrefix("http") {
                url = "https://" + url
            }
            return url
        }
        
        return nil
    }

    // Extract organization-specific URLs
    private func extractOrganizationUrl(from html: String) -> String? {
        let patterns = [
            "href=['\"]([^'\"]*feide[^'\"]*drammen[^'\"]*)['\"]",
            "href=['\"]([^'\"]*drammen[^'\"]*feide[^'\"]*)['\"]",
            "href=['\"]([^'\"]*org=[^'\"]*feide[^'\"]*)['\"]",
            "data-href=['\"]([^'\"]*feide[^'\"]*)['\"]",
            "onclick=[^'\"]*location[^'\"]*=[^'\"]*['\"]([^'\"]*feide[^'\"]*)['\"]"
        ]
        
        for pattern in patterns {
            if let regex = try? NSRegularExpression(pattern: pattern, options: [.caseInsensitive]),
               let match = regex.firstMatch(in: html, options: [], range: NSRange(html.startIndex..., in: html)),
               let urlRange = Range(match.range(at: 1), in: html) {
                
                var url = String(html[urlRange])
                if url.hasPrefix("/") {
                    url = "https://auth.dataporten.no" + url
                } else if !url.hasPrefix("http") {
                    url = "https://" + url
                }
                return url
            }
        }
        
        return nil
    }

    // Extract any URL that might lead to a login form
    private func extractAnyLoginUrl(from html: String) -> String? {
        let patterns = [
            "href=['\"]([^'\"]*login[^'\"]*)['\"]",
            "action=['\"]([^'\"]*login[^'\"]*)['\"]",
            "href=['\"]([^'\"]*auth[^'\"]*)['\"]",
            "href=['\"]([^'\"]*sso[^'\"]*)['\"]"
        ]
        
        for pattern in patterns {
            if let regex = try? NSRegularExpression(pattern: pattern, options: [.caseInsensitive]),
               let match = regex.firstMatch(in: html, options: [], range: NSRange(html.startIndex..., in: html)),
               let urlRange = Range(match.range(at: 1), in: html) {
                
                var url = String(html[urlRange])
                if url.hasPrefix("/") {
                    url = "https://auth.dataporten.no" + url
                } else if !url.hasPrefix("http") {
                    url = "https://" + url
                }
                return url
            }
        }
        
        return nil
    }
    
    private func extractFeideDirectLink(from html: String) -> String? {
        // Look for direct href links containing feide
        let patterns = [
            "href=\"([^\"]*idp\\.feide\\.no[^\"]*?)\"",
            "href=\"([^\"]*feide[^\"]*drammen[^\"]*?)\"",
            "href=\"([^\"]*drammen[^\"]*feide[^\"]*?)\"",
            "href=\"([^\"]*feide[^\"]*akademiet[^\"]*?)\"",
            "href=\"([^\"]*akademiet[^\"]*feide[^\"]*?)\"",
            "href=\"([^\"]*[?&]idp=[^\"]*feide[^\"]*?)\"",
            "href=\"([^\"]*[?&]org=[^\"]*feide[^\"]*?)\""
        ]
        
        for pattern in patterns {
            if let regex = try? NSRegularExpression(pattern: pattern, options: [.caseInsensitive]) {
                let range = NSRange(html.startIndex..., in: html)
                if let match = regex.firstMatch(in: html, options: [], range: range) {
                    let matchRange = Range(match.range(at: 1), in: html)!
                    var url = String(html[matchRange])
                    
                    // Clean and make absolute
                    if url.hasPrefix("/") {
                        url = "https://auth.dataporten.no" + url
                    } else if !url.hasPrefix("http") {
                        url = "https://" + url
                    }
                    
                    return url
                }
            }
        }
        
        return nil
    }

    private func extractJavaScriptFeideUrl(from html: String) -> String? {
        // Look for onclick handlers or JavaScript that might contain Feide URLs
        let patterns = [
            "onclick=\"[^\"]*location[^\"]*=\\s*['\"]([^'\"]*feide[^'\"]*?)['\"]",
            "onclick=\"[^\"]*window\\.location[^\"]*=\\s*['\"]([^'\"]*feide[^'\"]*?)['\"]",
            "javascript:[^\"']*location[^\"']*=\\s*['\"]([^'\"]*feide[^'\"]*?)['\"]"
        ]
        
        for pattern in patterns {
            if let regex = try? NSRegularExpression(pattern: pattern, options: [.caseInsensitive]) {
                let range = NSRange(html.startIndex..., in: html)
                if let match = regex.firstMatch(in: html, options: [], range: range) {
                    let matchRange = Range(match.range(at: 1), in: html)!
                    var url = String(html[matchRange])
                    
                    if !url.hasPrefix("http") {
                        url = "https://" + url
                    }
                    
                    return url
                }
            }
        }
        
        return nil
    }

    private func extractAllForms(from html: String) -> [[String: String]] {
        var forms: [[String: String]] = []
        
        // Find all form elements
        let formPattern = "<form[^>]*>.*?</form>"
        if let regex = try? NSRegularExpression(pattern: formPattern, options: [.caseInsensitive, .dotMatchesLineSeparators]) {
            let matches = regex.matches(in: html, options: [], range: NSRange(html.startIndex..., in: html))
            
            for match in matches {
                let matchRange = Range(match.range, in: html)!
                let formHtml = String(html[matchRange])
                
                var formData: [String: String] = [:]
                
                // Extract action
                if let action = extractAttribute("action", from: formHtml) {
                    var actionUrl = action
                    if actionUrl.hasPrefix("/") {
                        actionUrl = "https://auth.dataporten.no" + actionUrl
                    }
                    formData["action"] = actionUrl
                }
                
                // Extract all input fields
                let inputPattern = "<input[^>]*>"
                if let inputRegex = try? NSRegularExpression(pattern: inputPattern, options: [.caseInsensitive]) {
                    let inputMatches = inputRegex.matches(in: formHtml, options: [], range: NSRange(formHtml.startIndex..., in: formHtml))
                    
                    for inputMatch in inputMatches {
                        let inputRange = Range(inputMatch.range, in: formHtml)!
                        let inputHtml = String(formHtml[inputRange])
                        
                        if let name = extractAttribute("name", from: inputHtml),
                           let value = extractAttribute("value", from: inputHtml) {
                            formData[name] = value
                        }
                    }
                }
                
                // Extract select elements
                let selectPattern = "<select[^>]*name=\"([^\"]*)\"[^>]*>.*?</select>"
                if let selectRegex = try? NSRegularExpression(pattern: selectPattern, options: [.caseInsensitive, .dotMatchesLineSeparators]) {
                    let selectMatches = selectRegex.matches(in: formHtml, options: [], range: NSRange(formHtml.startIndex..., in: formHtml))
                    
                    for selectMatch in selectMatches {
                        let selectNameRange = Range(selectMatch.range(at: 1), in: formHtml)!
                        let selectName = String(formHtml[selectNameRange])
                        
                        // Look for selected option or default to first option
                        if let defaultValue = extractDefaultSelectValue(from: formHtml, selectName: selectName) {
                            formData[selectName] = defaultValue
                        }
                    }
                }
                
                if !formData.isEmpty {
                    forms.append(formData)
                }
            }
        }
        
        return forms
    }

    private func extractDefaultSelectValue(from html: String, selectName: String) -> String? {
        // Look for selected option first
        if let selectedRange = html.range(of: "selected"),
           let valueRange = html.range(of: "value=\"", range: html.startIndex..<selectedRange.lowerBound) {
            let afterValue = html[valueRange.upperBound...]
            if let endQuote = afterValue.range(of: "\"") {
                return String(afterValue[..<endQuote.lowerBound])
            }
        }
        
        // Fall back to first option
        let optionPattern = "<option[^>]*value=\"([^\"]*)\""
        if let regex = try? NSRegularExpression(pattern: optionPattern, options: [.caseInsensitive]) {
            let range = NSRange(html.startIndex..., in: html)
            if let match = regex.firstMatch(in: html, options: [], range: range) {
                let matchRange = Range(match.range(at: 1), in: html)!
                return String(html[matchRange])
            }
        }
        
        return nil
    }

    private func extractClickableFeideElement(from html: String) -> String? {
        // Look for any element that might be clickable and contains feide
        let patterns = [
            "<a[^>]*href=\"([^\"]*feide[^\"]*?)\"",
            "<button[^>]*onclick=\"[^\"]*location[^\"]*([^'\"]*feide[^'\"]*?)['\"]",
            "data-url=\"([^\"]*feide[^\"]*?)\""
        ]
        
        for pattern in patterns {
            if let regex = try? NSRegularExpression(pattern: pattern, options: [.caseInsensitive]) {
                let range = NSRange(html.startIndex..., in: html)
                if let match = regex.firstMatch(in: html, options: [], range: range) {
                    let matchRange = Range(match.range(at: 1), in: html)!
                    var url = String(html[matchRange])
                    
                    if url.hasPrefix("/") {
                        url = "https://auth.dataporten.no" + url
                    } else if !url.hasPrefix("http") {
                        url = "https://" + url
                    }
                    
                    return url
                }
            }
        }
        
        return nil
    }

    private func submitAnyForm(_ formData: [String: String], username: String, password: String) async -> Bool {
        guard let action = formData["action"] else {
            print("❌ No form action found")
            return false
        }
        
        print("📤 Submitting form to: \(action)")
        
        // Build form submission
        var postData: [String] = []
        for (key, value) in formData where key != "action" {
            let encodedValue = value.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? value
            postData.append("\(key)=\(encodedValue)")
            print("   \(key): \(value)")
        }
        
        let formDataString = postData.joined(separator: "&")
        
        let (data, response) = await makeRequest(url: action, method: "POST", body: formDataString, contentType: "application/x-www-form-urlencoded")
        
        if let httpResponse = response as? HTTPURLResponse {
            print("📊 Form submission response: \(httpResponse.statusCode)")
            
            // Handle redirect
            if let location = httpResponse.allHeaderFields["Location"] as? String {
                print("🔗 Form redirect: \(location.prefix(100))...")
                return await accessFeideLoginForm(location, username: username, password: password)
            }
            
            // Check response content
            if let responseHtml = String(data: data, encoding: .utf8) {
                print("📄 Form response length: \(responseHtml.count)")
                
                // If we got a login form, handle it
                if responseHtml.contains("password") && responseHtml.contains("feidename") {
                    print("✅ Got Feide login form!")
                    return await handleLoginForm(responseHtml, username: username, password: password)
                }
                
                // If we got another selection page, try again
                if responseHtml.contains("feide") && responseHtml.contains("form") {
                    print("🔄 Got another selection page, trying recursively...")
                    return await handleIdentityProviderSelection(responseHtml, username: username, password: password)
                }
            }
            
            return httpResponse.statusCode >= 200 && httpResponse.statusCode < 400
        }
        
        return false
    }
    
    private func extractFeideSelectionForm(from html: String) -> [String: String]? {
        // Look for form that submits to select Feide as identity provider
        // This would typically have a value like "feide.drammen.akademiet.no" or similar
        
        if let formStart = html.range(of: "<form"),
           let formEnd = html.range(of: "</form>", range: formStart.upperBound..<html.endIndex) {
            let formHtml = String(html[formStart.lowerBound..<formEnd.upperBound])
            
            // Check if this form is related to Feide/Drammen
            if formHtml.contains("feide") || formHtml.contains("drammen") || formHtml.contains("akademiet") {
                
                // Extract form action
                if let action = extractAttribute("action", from: formHtml) {
                    var formData: [String: String] = ["action": action]
                    
                    // Extract hidden inputs
                    let hiddenInputPattern = "<input[^>]*type=\"hidden\"[^>]*>"
                    if let regex = try? NSRegularExpression(pattern: hiddenInputPattern, options: []) {
                        let matches = regex.matches(in: formHtml, options: [], range: NSRange(formHtml.startIndex..., in: formHtml))
                        
                        for match in matches {
                            let matchRange = Range(match.range, in: formHtml)!
                            let inputHtml = String(formHtml[matchRange])
                            
                            if let name = extractAttribute("name", from: inputHtml),
                               let value = extractAttribute("value", from: inputHtml) {
                                formData[name] = value
                            }
                        }
                    }
                    
                    return formData
                }
            }
        }
        
        return nil
    }

    private func extractFeideSelectionUrl(from html: String) -> String? {
        // Look for links that contain feide and drammen/akademiet
        let patterns = [
            "href=\"([^\"]*feide[^\"]*drammen[^\"]*?)\"",
            "href=\"([^\"]*drammen[^\"]*feide[^\"]*?)\"",
            "href=\"([^\"]*feide[^\"]*akademiet[^\"]*?)\"",
            "href=\"([^\"]*idp\\.feide\\.no[^\"]*?)\""
        ]
        
        for pattern in patterns {
            if let regex = try? NSRegularExpression(pattern: pattern, options: []) {
                let range = NSRange(html.startIndex..., in: html)
                if let match = regex.firstMatch(in: html, options: [], range: range) {
                    let matchRange = Range(match.range(at: 1), in: html)!
                    var url = String(html[matchRange])
                    
                    // Make absolute if relative
                    if url.hasPrefix("/") {
                        url = "https://auth.dataporten.no" + url
                    } else if !url.hasPrefix("http") {
                        url = "https://" + url
                    }
                    
                    return url
                }
            }
        }
        
        return nil
    }

    private func extractAnyFeideLoginUrl(from html: String) -> String? {
        // More aggressive search for any Feide-related URL
        let patterns = [
            "https://idp\\.feide\\.no[^\\s\"'<>]*",
            "idp\\.feide\\.no[^\\s\"'<>]*",
            "https://[^\\s\"'<>]*feide[^\\s\"'<>]*",
            "href=\"([^\"]*feide[^\"]*)\""
        ]
        
        for pattern in patterns {
            if let regex = try? NSRegularExpression(pattern: pattern, options: []) {
                let range = NSRange(html.startIndex..., in: html)
                if let match = regex.firstMatch(in: html, options: [], range: range) {
                    let matchRange = match.numberOfRanges > 1 ?
                        Range(match.range(at: 1), in: html)! :
                        Range(match.range, in: html)!
                    
                    var url = String(html[matchRange])
                    
                    if !url.hasPrefix("http") {
                        url = "https://" + url
                    }
                    
                    return url
                }
            }
        }
        
        return nil
    }

    private func submitFeideSelection(_ formData: [String: String], username: String, password: String) async -> Bool {
        guard let action = formData["action"] else {
            print("❌ No form action found")
            return false
        }
        
        // Build form submission
        var postData: [String] = []
        for (key, value) in formData where key != "action" {
            postData.append("\(key)=\(value.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? value)")
        }
        
        let formDataString = postData.joined(separator: "&")
        
        let (data, response) = await makeRequest(url: action, method: "POST", body: formDataString, contentType: "application/x-www-form-urlencoded")
        
        if let httpResponse = response as? HTTPURLResponse {
            print("📊 Feide selection response: \(httpResponse.statusCode)")
            
            if let location = httpResponse.allHeaderFields["Location"] as? String {
                return await accessFeideLoginForm(location, username: username, password: password)
            }
            
            if let responseHtml = String(data: data, encoding: .utf8) {
                if responseHtml.contains("password") && responseHtml.contains("form") {
                    return await handleLoginForm(responseHtml, username: username, password: password)
                }
            }
        }
        
        return false
    }

    private func accessFeideLoginForm(_ url: String, username: String, password: String) async -> Bool {
        print("🔑 Accessing Feide login form: \(url.prefix(80))...")
        
        let (data, response) = await makeRequest(url: url, method: "GET")
        
        guard let httpResponse = response as? HTTPURLResponse,
              httpResponse.statusCode >= 200 && httpResponse.statusCode < 400 else {
            print("❌ Failed to access Feide login form: \((response as? HTTPURLResponse)?.statusCode ?? 0)")
            return false
        }
        
        // Handle redirects
        if let location = httpResponse.allHeaderFields["Location"] as? String {
            print("🔗 Feide login redirect: \(location.prefix(100))...")
            return await accessFeideLoginForm(location, username: username, password: password)
        }
        
        // Try to decode the response with multiple encodings
        var htmlContent: String?
        
        htmlContent = String(data: data, encoding: .utf8)
        if htmlContent == nil {
            htmlContent = String(data: data, encoding: .isoLatin1)
        }
        if htmlContent == nil {
            htmlContent = String(data: data, encoding: .windowsCP1252)
        }
        
        guard let html = htmlContent else {
            print("❌ Could not decode Feide login form")
            return false
        }
        
        print("📝 Got Feide login form, length: \(html.count)")
        print("🔍 HTML preview: \(String(html.prefix(300)))")
        
        return await handleLoginForm(html, username: username, password: password)
    }

    private func handleLoginForm(_ html: String, username: String, password: String) async -> Bool {
        print("📝 Handling login form...")
        
        // Extract all form data properly
        let authState = extractAuthState(from: html)
        let formAction = extractFormAction(from: html) ?? "https://idp.feide.no/simplesaml/module.php/feide/login"
        let samlRequest = extractSAMLRequest(from: html)
        let relayState = extractRelayState(from: html)
        
        print("🔑 AuthState: \(authState?.prefix(20) ?? "not found")...")
        print("🔑 SAMLRequest: \(samlRequest?.prefix(20) ?? "not found")...")
        print("🔑 RelayState: \(relayState?.prefix(20) ?? "not found")...")
        print("📝 Form action: \(formAction)")
        
        // Build comprehensive form data
        var formParams: [String: String] = [:]
        
        // Essential login fields
        formParams["has_js"] = "0"
        formParams["feidename"] = username
        formParams["password"] = password
        
        // Add all extracted hidden fields
        if let state = authState {
            formParams["AuthState"] = state
        }
        if let saml = samlRequest {
            formParams["SAMLRequest"] = saml
        }
        if let relay = relayState {
            formParams["RelayState"] = relay
        }
        
        // Extract ALL other hidden fields
        extractAllHiddenFields(from: html, into: &formParams)
        
        // Build form data string
        var formDataComponents: [String] = []
        for (key, value) in formParams {
            if let encodedValue = value.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) {
                formDataComponents.append("\(key)=\(encodedValue)")
            }
        }
        let formDataString = formDataComponents.joined(separator: "&")
        
        print("📤 Submitting login form with \(formParams.count) parameters")
        
        let (submitData, submitResponse) = await makeRequest(
            url: formAction,
            method: "POST",
            body: formDataString,
            contentType: "application/x-www-form-urlencoded"
        )
        
        if let httpSubmitResponse = submitResponse as? HTTPURLResponse {
            print("📊 Login submit response: \(httpSubmitResponse.statusCode)")
            
            // Handle different response types
            if let location = httpSubmitResponse.allHeaderFields["Location"] as? String {
                print("🔗 Login success redirect: \(location.prefix(100))...")
                return await followRedirect(location)
            }
            
            // Check response content for errors or success
            if let responseBody = String(data: submitData, encoding: .utf8) {
                print("📄 Login response length: \(responseBody.count)")
                
                // Check for specific error indicators
                if responseBody.contains("Feil brukernavn eller passord") ||
                   responseBody.contains("Invalid username or password") ||
                   responseBody.contains("authentication failed") ||
                   responseBody.contains("login error") {
                    print("❌ Login failed - authentication error detected")
                    return false
                }
                
                // Check for success indicators
                if responseBody.contains("SAMLResponse") ||
                   responseBody.contains("callback") ||
                   responseBody.contains("RelayState") {
                    print("✅ Login successful - found SAML response")
                    return await handleSamlResponse(responseBody)
                }
                
                // If we get another form, it might be 2FA or additional step
                if responseBody.contains("<form") && responseBody.contains("submit") {
                    print("📝 Got another form - checking if it's auto-submit")
                    return await handleAutoSubmitForm(responseBody)
                }
            }
            
            return httpSubmitResponse.statusCode >= 200 && httpSubmitResponse.statusCode < 400
        }
        
        return false
    }
    
    private func handleSamlResponse(_ html: String) async -> Bool {
        print("✅ Handling SAML response...")
        
        // Extract SAML form data
        guard let formAction = extractFormAction(from: html) else {
            print("❌ No SAML form action found")
            return false
        }
        
        var formData: [String: String] = [:]
        
        // Extract SAML response
        if let samlResponse = extractSamlResponse(from: html) {
            formData["SAMLResponse"] = samlResponse
            print("🔑 Found SAMLResponse: \(samlResponse.prefix(30))...")
        }
        
        // Extract RelayState if present
        if let relayState = extractRelayState(from: html) {
            formData["RelayState"] = relayState
            print("🔑 Found RelayState: \(relayState.prefix(30))...")
        }
        
        // Extract any other hidden fields
        extractAllHiddenFields(from: html, into: &formData)
        
        // Build form submission
        var postData: [String] = []
        for (key, value) in formData {
            if let encodedValue = value.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) {
                postData.append("\(key)=\(encodedValue)")
            }
        }
        
        let formDataString = postData.joined(separator: "&")
        print("📤 Submitting SAML form to: \(formAction)")
        print("📤 Form data contains \(formData.count) fields")
        
        let (data, response) = await makeRequest(
            url: formAction,
            method: "POST",
            body: formDataString,
            contentType: "application/x-www-form-urlencoded"
        )
        
        if let httpResponse = response as? HTTPURLResponse {
            print("📊 SAML submit response: \(httpResponse.statusCode)")
            
            // Handle redirect (typical after SAML submission)
            if let location = httpResponse.allHeaderFields["Location"] as? String {
                print("🔗 SAML redirect: \(location.prefix(100))...")
                return await followRedirect(location)
            }
            
            // Check if we got another form or success page
            if let responseBody = String(data: data, encoding: .utf8) {
                print("📄 SAML response length: \(responseBody.count)")
                
                // Check for another auto-submit form
                if responseBody.contains("<form") && responseBody.contains("submit") {
                    print("📝 Got another form after SAML - handling auto-submit")
                    return await handleAutoSubmitForm(responseBody)
                }
                
                // Check for success indicators (being redirected to iSkole)
                if responseBody.contains("iskole.net") ||
                   responseBody.contains("elev") ||
                   responseBody.contains("authenticated") {
                    print("✅ SAML authentication appears successful")
                    return true
                }
            }
            
            return httpResponse.statusCode >= 200 && httpResponse.statusCode < 400
        }
        
        return false
    }
    
    private func extractSAMLRequest(from html: String) -> String? {
        let patterns = [
            "name=\"SAMLRequest\"\\s*value=\"([^\"]+)\"",
            "SAMLRequest\"\\s*value=\"([^\"]+)\""
        ]
        
        for pattern in patterns {
            if let regex = try? NSRegularExpression(pattern: pattern, options: []),
               let match = regex.firstMatch(in: html, options: [], range: NSRange(html.startIndex..., in: html)),
               let valueRange = Range(match.range(at: 1), in: html) {
                return String(html[valueRange])
            }
        }
        return nil
    }

    // NEW: Extract ALL hidden fields, not just specific ones
    private func extractAllHiddenFields(from html: String, into params: inout [String: String]) {
        let hiddenInputPattern = "<input[^>]*type=['\"]hidden['\"][^>]*>"
        
        if let regex = try? NSRegularExpression(pattern: hiddenInputPattern, options: [.caseInsensitive]) {
            let matches = regex.matches(in: html, options: [], range: NSRange(html.startIndex..., in: html))
            
            for match in matches {
                let matchRange = Range(match.range, in: html)!
                let inputHtml = String(html[matchRange])
                
                if let name = extractAttribute("name", from: inputHtml),
                   let value = extractAttribute("value", from: inputHtml) {
                    // Don't override already set parameters
                    if params[name] == nil {
                        params[name] = decodeHtmlEntities(value)
                        print("🔍 Found hidden field: \(name) = \(value.prefix(30))...")
                    }
                }
            }
        }
    }

    // NEW: Handle auto-submit forms (common after login)
    private func handleAutoSubmitForm(_ html: String) async -> Bool {
        print("📝 Handling auto-submit form...")
        
        // Look for JavaScript that auto-submits
        if html.contains("document.forms[0].submit()") ||
           html.contains("form.submit()") ||
           html.contains("onload") && html.contains("submit") {
            
            // Extract the form data
            if let formAction = extractFormAction(from: html) {
                var formData: [String: String] = [:]
                extractAllHiddenFields(from: html, into: &formData)
                
                // Build form submission
                var postData: [String] = []
                for (key, value) in formData {
                    if let encodedValue = value.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) {
                        postData.append("\(key)=\(encodedValue)")
                    }
                }
                
                let formDataString = postData.joined(separator: "&")
                print("📤 Auto-submitting form to: \(formAction)")
                
                let (_, response) = await makeRequest(
                    url: formAction,
                    method: "POST",
                    body: formDataString,
                    contentType: "application/x-www-form-urlencoded"
                )
                
                if let httpResponse = response as? HTTPURLResponse {
                    print("📊 Auto-submit response: \(httpResponse.statusCode)")
                    
                    if let location = httpResponse.allHeaderFields["Location"] as? String {
                        return await followRedirect(location)
                    }
                    
                    return httpResponse.statusCode >= 200 && httpResponse.statusCode < 400
                }
            }
        }
        
        return true
    }

    private func extractSamlFormData(from html: String) -> [String: String]? {
        var formData: [String: String] = [:]
        
        // Extract form action
        if let action = extractFormAction(from: html) {
            formData["action"] = action
        }
        
        // Extract SAML response
        if let samlResponse = extractSamlResponse(from: html) {
            formData["SAMLResponse"] = samlResponse
        }
        
        // Extract RelayState if present
        if let relayState = extractRelayState(from: html) {
            formData["RelayState"] = relayState
        }
        
        return formData.isEmpty ? nil : formData
    }

    private func extractRelayState(from html: String) -> String? {
        if let range = html.range(of: "RelayState\" value=\"") {
            let afterRange = html[range.upperBound...]
            if let endRange = afterRange.range(of: "\"") {
                return String(afterRange[..<endRange.lowerBound])
            }
        }
        return nil
    }

    private func submitSamlForm(_ formData: [String: String]) async -> Bool {
        guard let action = formData["action"] else {
            print("❌ No SAML form action")
            return false
        }
        
        var postData: [String] = []
        for (key, value) in formData where key != "action" {
            postData.append("\(key)=\(value.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? value)")
        }
        
        let formDataString = postData.joined(separator: "&")
        
        let (_, response) = await makeRequest(url: action, method: "POST", body: formDataString, contentType: "application/x-www-form-urlencoded")
        
        if let httpResponse = response as? HTTPURLResponse {
            print("📊 SAML submit response: \(httpResponse.statusCode)")
            
            if let location = httpResponse.allHeaderFields["Location"] as? String {
                return await followRedirect(location)
            }
            
            return httpResponse.statusCode >= 200 && httpResponse.statusCode < 400
        }
        
        return false
    }
    
    private func tryDirectFeideLogin(username: String, password: String) async -> Bool {
        print("🎯 Trying direct Feide login...")
        
        // Start with the Feide preselect org page
        let preselectUrl = "https://idp.feide.no/simplesaml/module.php/feide/preselectOrg.php?HomeOrg=feide.drammen.akademiet.no"
        
        do {
            let (formData, formResponse) = await makeRequest(url: preselectUrl, method: "GET")
            
            guard let httpResponse = formResponse as? HTTPURLResponse else {
                print("❌ No HTTP response from Feide preselect")
                return false
            }
            
            print("📊 Feide preselect response: \(httpResponse.statusCode)")
            print("📄 Response data length: \(formData.count)")
            
            // Check for redirects first
            if let location = httpResponse.allHeaderFields["Location"] as? String {
                print("🔗 Feide redirect to: \(location)")
                return await followFeideRedirect(location, username: username, password: password)
            }
            
            // Accept various success codes, not just 200
            guard httpResponse.statusCode >= 200 && httpResponse.statusCode < 400 else {
                print("❌ Feide preselect failed with status: \(httpResponse.statusCode)")
                return false
            }
            
            // Try different encodings to decode the response
            var formHtml: String?
            
            // First try UTF-8
            formHtml = String(data: formData, encoding: .utf8)
            
            // If UTF-8 fails, try ISO Latin 1
            if formHtml == nil {
                print("⚠️ UTF-8 decode failed, trying ISO Latin 1...")
                formHtml = String(data: formData, encoding: .isoLatin1)
            }
            
            // If that fails, try Windows-1252
            if formHtml == nil {
                print("⚠️ ISO Latin 1 decode failed, trying Windows-1252...")
                formHtml = String(data: formData, encoding: .windowsCP1252)
            }
            
            // If that fails, try to extract charset from Content-Type header
            if formHtml == nil {
                if let contentType = httpResponse.allHeaderFields["Content-Type"] as? String {
                    print("📄 Content-Type: \(contentType)")
                    
                    let charset = extractCharsetFromContentType(contentType)
                    print("🔤 Detected charset: \(charset ?? "none")")
                    
                    if let charsetName = charset {
                        let encoding = encodingFromCharset(charsetName)
                        formHtml = String(data: formData, encoding: encoding)
                        print("🔤 Decode with detected charset: \(formHtml != nil ? "success" : "failed")")
                    }
                }
            }
            
            // Last resort: try ASCII
            if formHtml == nil {
                print("⚠️ All encodings failed, trying ASCII...")
                formHtml = String(data: formData, encoding: .ascii)
            }
            
            guard let decodedHtml = formHtml else {
                print("❌ Could not decode Feide response with any encoding")
                print("🔍 Raw data preview: \(formData.prefix(100))")
                return false
            }
            
            print("📝 Got Feide response, length: \(decodedHtml.count)")
            print("📄 Contains 'AuthState': \(decodedHtml.contains("AuthState"))")
            print("📄 Contains 'password': \(decodedHtml.contains("password"))")
            print("📄 Contains 'feidename': \(decodedHtml.contains("feidename"))")
            print("📄 Contains 'login': \(decodedHtml.contains("login"))")
            print("📄 Contains 'form': \(decodedHtml.contains("form"))")
            
            // Debug: print first 500 characters to see what we got
            print("🔍 HTML preview: \(String(decodedHtml.prefix(500)))")
            
            // Look for the actual login form URL in the HTML
            let loginFormUrl = extractFeideLoginUrl(from: decodedHtml) ?? "https://idp.feide.no/simplesaml/module.php/feide/login"
            
            // Extract AuthState and form action
            let authState = extractAuthState(from: decodedHtml)
            
            print("🔑 AuthState: \(authState?.prefix(20) ?? "not found")...")
            print("📝 Login form URL: \(loginFormUrl)")
            
            // Submit the login form
            var formDataString = "has_js=0&feidename=\(username.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? username)&password=\(password.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? password)"
            
            if let state = authState {
                formDataString += "&AuthState=\(state.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? state)"
            }
            
            print("📤 Submitting login form to: \(loginFormUrl)")
            print("📤 Form data: \(formDataString.replacingOccurrences(of: password, with: "***"))")
            
            let (submitData, submitResponse) = await makeRequest(url: loginFormUrl, method: "POST", body: formDataString, contentType: "application/x-www-form-urlencoded")
            
            if let httpSubmitResponse = submitResponse as? HTTPURLResponse {
                print("📊 Login submit response: \(httpSubmitResponse.statusCode)")
                
                // Check for redirect (successful login)
                if let location = httpSubmitResponse.allHeaderFields["Location"] as? String {
                    print("🔗 Login success redirect: \(location.prefix(100))...")
                    return await followRedirect(location)
                }
                
                // Check response content
                if let responseBody = String(data: submitData, encoding: .utf8) {
                    print("📄 Submit response length: \(responseBody.count)")
                    
                    // Look for error indicators
                    if responseBody.lowercased().contains("error") ||
                       responseBody.lowercased().contains("invalid") ||
                       responseBody.lowercased().contains("wrong") ||
                       responseBody.contains("feil") { // Norwegian for "error"
                        print("❌ Login response contains error")
                        return false
                    }
                    
                    // Look for success indicators
                    if responseBody.contains("SAMLResponse") ||
                       responseBody.contains("callback") ||
                       responseBody.contains("RelayState") {
                        print("✅ Found success indicators in response")
                        return await handleFeideSuccess(responseBody)
                    }
                    
                    // If we got a form back, try to handle it
                    if responseBody.contains("<form") && responseBody.contains("submit") {
                        print("📝 Got another form, trying to handle it...")
                        return await handleFeideForm(responseBody)
                    }
                } else {
                    print("⚠️ Could not decode submit response as UTF-8")
                    // Try other encodings for the response too if needed
                }
                
                // Accept various success status codes
                return httpSubmitResponse.statusCode >= 200 && httpSubmitResponse.statusCode < 400
            }
            
        } catch {
            print("💥 Error in direct Feide login: \(error)")
        }
        
        return false
    }

    // Helper function to extract charset from Content-Type header
    private func extractCharsetFromContentType(_ contentType: String) -> String? {
        let components = contentType.components(separatedBy: ";")
        for component in components {
            let trimmed = component.trimmingCharacters(in: .whitespaces)
            if trimmed.lowercased().hasPrefix("charset=") {
                let charset = String(trimmed.dropFirst(8)).trimmingCharacters(in: .whitespaces)
                return charset.replacingOccurrences(of: "\"", with: "")
            }
        }
        return nil
    }

    // Helper function to convert charset name to Swift encoding
    private func encodingFromCharset(_ charset: String) -> String.Encoding {
        let lowercased = charset.lowercased()
        switch lowercased {
        case "utf-8":
            return .utf8
        case "iso-8859-1", "latin1":
            return .isoLatin1
        case "windows-1252", "cp1252":
            return .windowsCP1252
        case "ascii":
            return .ascii
        case "utf-16":
            return .utf16
        default:
            return .utf8 // fallback
        }
    }
    
    private func extractFeideLoginUrl(from html: String) -> String? {
        // Look for form action in the HTML
        let patterns = [
            "action=\"([^\"]+login[^\"]*?)\"",
            "action=\"([^\"]+feide[^\"]*?)\"",
            "action=\"(/[^\"]*?)\"" // Relative URLs
        ]
        
        for pattern in patterns {
            if let regex = try? NSRegularExpression(pattern: pattern, options: []) {
                let range = NSRange(html.startIndex..., in: html)
                if let match = regex.firstMatch(in: html, options: [], range: range) {
                    let matchRange = Range(match.range(at: 1), in: html)!
                    var url = String(html[matchRange])
                    
                    // Convert relative URLs to absolute
                    if url.hasPrefix("/") {
                        url = "https://idp.feide.no" + url
                    }
                    
                    print("✅ Extracted login URL: \(url)")
                    return url
                }
            }
        }
        
        return nil
    }

    // Helper method to follow Feide redirects specifically
    private func followFeideRedirect(_ location: String, username: String, password: String) async -> Bool {
        print("🔗 Following Feide redirect: \(location.prefix(100))...")
        
        do {
            let (data, response) = await makeRequest(url: location, method: "GET")
            
            guard let httpResponse = response as? HTTPURLResponse else {
                print("❌ No HTTP response for Feide redirect")
                return false
            }
            
            print("📊 Feide redirect response: \(httpResponse.statusCode)")
            
            // Continue following redirects
            if let nextLocation = httpResponse.allHeaderFields["Location"] as? String {
                print("🔗 Next Feide redirect: \(nextLocation.prefix(100))...")
                return await followFeideRedirect(nextLocation, username: username, password: password)
            }
            
            // If we got HTML, try to extract login form
            if let responseBody = String(data: data, encoding: .utf8) {
                print("📄 Feide redirect response length: \(responseBody.count)")
                
                if responseBody.contains("password") && responseBody.contains("feidename") {
                    print("✅ Found login form in redirect response")
                    
                    let loginUrl = extractFeideLoginUrl(from: responseBody) ?? location
                    let authState = extractAuthState(from: responseBody)
                    
                    // Submit credentials
                    var formDataString = "has_js=0&feidename=\(username.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? username)&password=\(password.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? password)"
                    
                    if let state = authState {
                        formDataString += "&AuthState=\(state.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? state)"
                    }
                    
                    let (submitData, submitResponse) = await makeRequest(url: loginUrl, method: "POST", body: formDataString, contentType: "application/x-www-form-urlencoded")
                    
                    if let httpSubmitResponse = submitResponse as? HTTPURLResponse {
                        print("📊 Feide login submit response: \(httpSubmitResponse.statusCode)")
                        
                        if let submitLocation = httpSubmitResponse.allHeaderFields["Location"] as? String {
                            return await followRedirect(submitLocation)
                        }
                        
                        return httpSubmitResponse.statusCode >= 200 && httpSubmitResponse.statusCode < 400
                    }
                }
            }
            
            return httpResponse.statusCode >= 200 && httpResponse.statusCode < 400
            
        } catch {
            print("💥 Error following Feide redirect: \(error)")
            return false
        }
    }

    // Helper method to handle successful Feide responses
    private func handleFeideSuccess(_ html: String) async -> Bool {
        print("✅ Handling Feide success response...")
        
        // Look for auto-submit forms or callback URLs
        if let callbackUrl = extractCallbackUrl(from: html) {
            print("🔗 Found callback URL: \(callbackUrl.prefix(100))...")
            let (_, response) = await makeRequest(url: callbackUrl, method: "GET")
            
            if let httpResponse = response as? HTTPURLResponse {
                return httpResponse.statusCode >= 200 && httpResponse.statusCode < 400
            }
        }
        
        // Look for SAML forms that need to be auto-submitted
        if html.contains("SAMLResponse") {
            return await handleSamlForm(html)
        }
        
        return true
    }

    // Helper method to handle SAML forms
    private func handleSamlForm(_ html: String) async -> Bool {
        print("📝 Handling SAML form...")
        
        // Extract form action and SAML response
        guard let formAction = extractFormAction(from: html),
              let samlResponse = extractSamlResponse(from: html) else {
            print("❌ Could not extract SAML form data")
            return false
        }
        
        let formData = "SAMLResponse=\(samlResponse.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? samlResponse)"
        
        let (_, response) = await makeRequest(url: formAction, method: "POST", body: formData, contentType: "application/x-www-form-urlencoded")
        
        if let httpResponse = response as? HTTPURLResponse {
            print("📊 SAML form submit response: \(httpResponse.statusCode)")
            
            if let location = httpResponse.allHeaderFields["Location"] as? String {
                return await followRedirect(location)
            }
            
            return httpResponse.statusCode >= 200 && httpResponse.statusCode < 400
        }
        
        return false
    }

    // Helper methods for extraction
    private func extractCallbackUrl(from html: String) -> String? {
        let pattern = "https://iskole\\.net[^\\s\"'<>]*callback[^\\s\"'<>]*"
        
        if let regex = try? NSRegularExpression(pattern: pattern, options: []) {
            let range = NSRange(html.startIndex..., in: html)
            if let match = regex.firstMatch(in: html, options: [], range: range) {
                let matchRange = Range(match.range, in: html)!
                return String(html[matchRange])
            }
        }
        
        return nil
    }

    private func extractSamlResponse(from html: String) -> String? {
        if let range = html.range(of: "SAMLResponse\" value=\"") {
            let afterRange = html[range.upperBound...]
            if let endRange = afterRange.range(of: "\"") {
                return String(afterRange[..<endRange.lowerBound])
            }
        }
        
        return nil
    }

    private func handleFeideForm(_ html: String) async -> Bool {
        print("📝 Handling additional Feide form...")
        
        // This would handle any additional forms that might appear
        // For now, just return true to continue the flow
        return true
    }
    
    private func extractAuthState(from html: String) -> String? {
        // Look for: name="AuthState" value="..."
        if let range = html.range(of: "name=\"AuthState\" value=\"") {
            let afterRange = html[range.upperBound...]
            if let endRange = afterRange.range(of: "\"") {
                return String(afterRange[..<endRange.lowerBound])
            }
        }
        
        // Alternative pattern: AuthState" value="..."
        if let range = html.range(of: "AuthState\" value=\"") {
            let afterRange = html[range.upperBound...]
            if let endRange = afterRange.range(of: "\"") {
                return String(afterRange[..<endRange.lowerBound])
            }
        }
        
        return nil
    }
    
    private func extractFormAction(from html: String) -> String? {
        // Look for: <form action="..." or action="..."
        if let range = html.range(of: "action=\"") {
            let afterRange = html[range.upperBound...]
            if let endRange = afterRange.range(of: "\"") {
                let action = String(afterRange[..<endRange.lowerBound])
                // Make it a full URL if relative
                if action.hasPrefix("/") {
                    return "https://idp.feide.no" + action
                }
                return action
            }
        }
        
        return nil
    }
    
    private func completeCallback() async -> Bool {
        print("✅ Attempting to complete OAuth callback...")
        
        let callbackUrl = "https://iskole.net/iskole_login/dataporten_login"
        
        do {
            let (data, response) = await makeRequest(url: callbackUrl, method: "GET")
            
            guard let httpResponse = response as? HTTPURLResponse else {
                print("❌ No HTTP response for callback")
                return false
            }
            
            print("📊 Callback response: \(httpResponse.statusCode)")
            
            // Follow any final redirects
            if let location = httpResponse.allHeaderFields["Location"] as? String {
                print("🔗 Final redirect: \(location)")
                _ = await followRedirect(location)
            }
            
            // Check final authentication status
            return checkAuthenticationSuccess()
            
        } catch {
            print("💥 Error completing callback: \(error)")
            return false
        }
    }
    
    private func makeRequest(url: String, method: String, body: String? = nil, contentType: String? = nil) async -> (Data, URLResponse) {
        print("🌐 \(method) \(url.prefix(80))...")
        
        guard let requestUrl = URL(string: url) else {
            print("❌ Invalid URL: \(url)")
            return (Data(), URLResponse())
        }
        
        var request = URLRequest(url: requestUrl)
        request.httpMethod = method
        
        // Set headers
        request.setValue("Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/139.0.0.0 Safari/537.36", forHTTPHeaderField: "User-Agent")
        request.setValue("text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8", forHTTPHeaderField: "Accept")
        request.setValue("nb-NO,nb;q=0.9,en;q=0.8", forHTTPHeaderField: "Accept-Language")
        request.setValue("gzip, deflate, br", forHTTPHeaderField: "Accept-Encoding")
        request.setValue("1", forHTTPHeaderField: "Upgrade-Insecure-Requests")
        
        if let ct = contentType {
            request.setValue(ct, forHTTPHeaderField: "Content-Type")
        }
        
        if let bodyData = body?.data(using: .utf8) {
            request.httpBody = bodyData
        }
        
        // Add cookies
        addCookiesToRequest(&request)
        
        do {
            let (data, response) = try await urlSession.data(for: request)
            
            // Collect cookies from response
            if let httpResponse = response as? HTTPURLResponse {
                collectCookies(from: httpResponse, url: requestUrl)
            }
            
            return (data, response)
        } catch {
            print("💥 Request failed: \(error)")
            return (Data(), URLResponse())
        }
    }
    
    private func collectCookies(from response: HTTPURLResponse, url: URL) {
        let cookies = HTTPCookie.cookies(withResponseHeaderFields:
            response.allHeaderFields as? [String: String] ?? [:], for: url)
        
        for cookie in cookies {
            // Remove duplicates
            sessionCookies.removeAll { $0.name == cookie.name && $0.domain == cookie.domain }
            sessionCookies.append(cookie)
            
            print("🍪 Cookie: \(cookie.name) = \(String(cookie.value.prefix(10)))... (domain: \(cookie.domain))")
            
            if cookie.name.contains("JSESSIONID") {
                jsessionId = cookie.value
            }
            if cookie.name.lowercased().contains("auth") {
                authCookie = cookie.value
            }
        }
    }
    
    private func addCookiesToRequest(_ request: inout URLRequest) {
        guard let host = request.url?.host else { return }
        
        let relevantCookies = sessionCookies.filter { cookie in
            let domain = cookie.domain.hasPrefix(".") ? String(cookie.domain.dropFirst()) : cookie.domain
            return host.hasSuffix(domain) || domain == host
        }
        
        if !relevantCookies.isEmpty {
            let cookieHeader = relevantCookies.map { "\($0.name)=\($0.value)" }.joined(separator: "; ")
            request.setValue(cookieHeader, forHTTPHeaderField: "Cookie")
            print("🍪 Sent \(relevantCookies.count) cookies to \(host)")
        }
    }
    
    private func checkAuthenticationSuccess() -> Bool {
        let totalCookies = sessionCookies.count
        let hasAuth = sessionCookies.contains { $0.name.lowercased().contains("auth") }
        let hasSession = jsessionId != nil
        
        print("🔍 Final auth check:")
        print("   Total cookies: \(totalCookies)")
        print("   Has auth cookie: \(hasAuth)")
        print("   Has JSESSIONID: \(hasSession)")
        
        // List all cookies
        for cookie in sessionCookies {
            print("   - \(cookie.name): \(String(cookie.value.prefix(10)))...")
        }
        
        // Try to make a test request to see if we're authenticated
        return totalCookies >= 2 // At least we should have some cookies
    }
    
    // Test authentication by trying to access a protected endpoint
    func testAuthentication() async -> Bool {
        print("🧪 Testing authentication with schedule endpoint...")
        
        let scheduleURL = "https://iskole.net/iskole_elev/rest/v0/VoTimeplan_elev_oppmote"
        let (data, response) = await makeRequest(url: scheduleURL, method: "GET")
        
        if let httpResponse = response as? HTTPURLResponse {
            print("📊 Test request status: \(httpResponse.statusCode)")
            
            if httpResponse.statusCode == 200 {
                if let responseString = String(data: data, encoding: .utf8) {
                    print("📄 Test response length: \(responseString.count)")
                    print("✅ Authentication test successful!")
                    return true
                }
            }
        }
        
        print("❌ Authentication test failed")
        return false
    }
    
    // Keep existing methods for schedule and attendance
    func getTodaysSchedule() async throws -> [ClassInfo] {
        print("📅 Fetching today's schedule...")
        
        let scheduleURL = "https://iskole.net/iskole_elev/rest/v0/VoTimeplan_elev_oppmote"
        let (data, response) = await makeRequest(url: scheduleURL, method: "GET")
        
        guard let httpResponse = response as? HTTPURLResponse,
              httpResponse.statusCode == 200 else {
            print("❌ Schedule request failed with status: \((response as? HTTPURLResponse)?.statusCode ?? 0)")
            return []
        }
        
        do {
            guard let json = try JSONSerialization.jsonObject(with: data) as? [String: Any],
                  let items = json["items"] as? [[String: Any]] else {
                print("❌ Invalid JSON structure")
                return []
            }
            
            let classes: [ClassInfo] = items.compactMap { item in
                guard let id = item["Id"] as? Int,
                      let subject = item["Fag"] as? String,
                      let date = item["Dato"] as? String,
                      let startTime = item["StartKl"] as? String,
                      let endTime = item["SluttKl"] as? String,
                      let timenr = item["Timenr"] as? Int else {
                    return nil
                }
                return ClassInfo(id: id, subject: subject, date: date,
                               startTime: startTime, endTime: endTime, timenr: timenr)
            }
            
            print("✅ Found \(classes.count) classes")
            return classes
            
        } catch {
            print("💥 JSON parsing error: \(error)")
            return []
        }
    }
    
    func registerAttendance(for classInfo: ClassInfo) async throws -> Bool {
        print("✋ Registering attendance for: \(classInfo.subject)")
        
        let attendanceURL = "https://iskole.net/iskole_elev/rest/v0/VoTimeplan_elev_oppmote/action/lagre_oppmote"
        let attendanceData: [String: Any] = [
            "Id": classInfo.id,
            "Timenr": classInfo.timenr,
            "ElevForerTilstedevaerelse": 1
        ]
        
        let jsonData = try JSONSerialization.data(withJSONObject: attendanceData)
        let jsonString = String(data: jsonData, encoding: .utf8) ?? ""
        
        let (_, response) = await makeRequest(url: attendanceURL, method: "POST",
                                            body: jsonString, contentType: "application/json")
        
        if let httpResponse = response as? HTTPURLResponse {
            let success = httpResponse.statusCode == 200
            print(success ? "✅ Attendance registered" : "❌ Attendance failed")
            return success
        }
        
        return false
    }
}
