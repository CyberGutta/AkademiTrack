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
            
            // Debug the HTML content
            debugHtmlContent(htmlContent, context: "Dataporten page")
            
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
            
            // Method 3: Fall back to any dataporten URL
            if oauthUrl == nil {
                if let url = extractAnyDataportenUrl(from: htmlContent) {
                    oauthUrl = url
                    print("✅ Method 3 success: Any dataporten URL")
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
            "(https://auth\\.dataporten\\.no/oauth/authorization[^\\s\"'<>]+)",
            // URL-encoded version
            "(https%3A%2F%2Fauth\\.dataporten\\.no%2Foauth%2Fauthorization[^\\s\"'<>]+)"
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
                    
                    // Clean up the URL
                    url = url.replacingOccurrences(of: "&amp;", with: "&")
                    url = url.replacingOccurrences(of: "\\\"", with: "")
                    url = url.replacingOccurrences(of: "\"", with: "")
                    url = url.replacingOccurrences(of: "'", with: "")
                    
                    // URL decode if necessary
                    if url.contains("%3A") || url.contains("%2F") {
                        url = url.removingPercentEncoding ?? url
                        print("📍 URL decoded: \(url)")
                    }
                    
                    // Remove any trailing HTML artifacts
                    if let endIndex = url.firstIndex(where: { $0 == "<" || $0 == ">" || $0 == ";" }) {
                        url = String(url[..<endIndex])
                    }
                    
                    // Ensure proper protocol
                    if !url.hasPrefix("http") {
                        url = "https://" + url
                    }
                    
                    print("✅ Extracted complete OAuth URL: \(url)")
                    return url
                }
            }
        }
        
        print("❌ No complete OAuth URL found")
        return nil
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
        
        // Try the direct Feide approach first
        return await tryDirectFeideLogin(username: username, password: password)
    }
    
    private func tryDirectFeideLogin(username: String, password: String) async -> Bool {
        print("🎯 Trying direct Feide login...")
        
        // Start with the Feide preselect org page
        let preselectUrl = "https://idp.feide.no/simplesaml/module.php/feide/preselectOrg.php?HomeOrg=feide.drammen.akademiet.no"
        
        do {
            let (formData, formResponse) = await makeRequest(url: preselectUrl, method: "GET")
            
            guard let httpResponse = formResponse as? HTTPURLResponse,
                  httpResponse.statusCode == 200,
                  let formHtml = String(data: formData, encoding: .utf8) else {
                print("❌ Could not get Feide login form")
                return false
            }
            
            print("📝 Got Feide form, length: \(formHtml.count)")
            print("📄 Contains 'AuthState': \(formHtml.contains("AuthState"))")
            print("📄 Contains 'password': \(formHtml.contains("password"))")
            
            // Extract AuthState and form action
            let authState = extractAuthState(from: formHtml)
            let formAction = extractFormAction(from: formHtml)
            
            print("🔑 AuthState: \(authState?.prefix(20) ?? "not found")...")
            print("📝 Form action: \(formAction ?? "not found")")
            
            // Submit the login form
            let loginUrl = formAction ?? "https://idp.feide.no/simplesaml/module.php/feide/login"
            
            var formDataString = "has_js=0&feidename=\(username)&password=\(password.addingPercentEncoding(withAllowedCharacters: .urlQueryAllowed) ?? password)"
            
            if let state = authState {
                formDataString += "&AuthState=\(state)"
            }
            
            let (submitData, submitResponse) = await makeRequest(url: loginUrl, method: "POST", body: formDataString, contentType: "application/x-www-form-urlencoded")
            
            if let httpSubmitResponse = submitResponse as? HTTPURLResponse {
                print("📊 Login submit response: \(httpSubmitResponse.statusCode)")
                
                // Check for redirect (successful login)
                if let location = httpSubmitResponse.allHeaderFields["Location"] as? String {
                    print("🔗 Login success redirect: \(location.prefix(100))...")
                    return await followRedirect(location)
                }
                
                // Check response content for errors
                if let responseBody = String(data: submitData, encoding: .utf8) {
                    print("📄 Submit response length: \(responseBody.count)")
                    if responseBody.contains("error") || responseBody.contains("invalid") {
                        print("❌ Login response contains error")
                        return false
                    }
                    if responseBody.contains("SAMLResponse") {
                        print("✅ Found SAML response - login successful")
                        return true
                    }
                }
                
                return httpSubmitResponse.statusCode == 200
            }
            
        } catch {
            print("💥 Error in direct Feide login: \(error)")
        }
        
        return false
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
