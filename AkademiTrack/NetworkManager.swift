import Foundation

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
    
    func login(username: String, password: String) async throws -> Bool {
        // Step 1: Navigate to Dataporten discovery
        let discoveryURL = "https://auth.dataporten.no/discovery?returnTo=https%3A%2F%2Fauth.dataporten.no%2Foauth%2Fauthorization%3Fclient_id%3Dd37eff0f-5ca3-44a8-9990-3e22150f0fd7%26redirect_uri%3Dhttps%253A%252F%252Fiskole.net%252Fiskole_login%252Fdataporten_login%26response_type%3Dcode%26scope%3Dopenid%2520userid-nin%2520userid-feide%2520userid%2520email%2520userinfo-name%26state%3DGwMdS_c7u_dtOiWKWTEC-Q8-RpVdzneRFr6sB3FjraE&clientid=d37eff0f-5ca3-44a8-9990-3e22150f0fd7"
        
        var request = URLRequest(url: URL(string: discoveryURL)!)
        request.setValue("Mozilla/5.0 (iPhone; CPU iPhone OS 16_0 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/16.0 Mobile/15E148 Safari/604.1", forHTTPHeaderField: "User-Agent")
        
        let (_, response) = try await URLSession.shared.data(for: request)
        
        if let httpResponse = response as? HTTPURLResponse {
            let cookies = HTTPCookie.cookies(withResponseHeaderFields: httpResponse.allHeaderFields as! [String : String], for: request.url!)
            sessionCookies.append(contentsOf: cookies)
        }
        
        // Step 2: Submit Feide login form
        // This is a simplified version - in practice, you'd need to parse the HTML form and extract necessary fields
        let loginURL = "https://idp.feide.no/simplesaml/module.php/feide/login.php"
        var loginRequest = URLRequest(url: URL(string: loginURL)!)
        loginRequest.httpMethod = "POST"
        loginRequest.setValue("application/x-www-form-urlencoded", forHTTPHeaderField: "Content-Type")
        
        let loginData = "username=\(username)&password=\(password)".data(using: .utf8)
        loginRequest.httpBody = loginData
        
        // Add cookies
        addCookiesToRequest(&loginRequest)
        
        let (loginResponseData, _) = try await URLSession.shared.data(for: loginRequest)
        
        // Check if login was successful (this is simplified)
        if let loginResponseString = String(data: loginResponseData, encoding: .utf8) {
            return !loginResponseString.contains("error") && loginResponseString.contains("success")
        }
        
        return false
    }
    
    func getTodaysSchedule() async throws -> [ClassInfo] {
        // This URL would need to be constructed with proper session parameters
        let scheduleURL = "https://iskole.net/iskole_elev/rest/v0/VoTimeplan_elev_oppmote"
        var request = URLRequest(url: URL(string: scheduleURL)!)
        request.setValue("application/json", forHTTPHeaderField: "Accept")
        
        addCookiesToRequest(&request)
        
        let (data, _) = try await URLSession.shared.data(for: request)
        
        // Parse the JSON response
        let jsonObject = try JSONSerialization.jsonObject(with: data, options: [])
        guard let json = jsonObject as? [String: Any],
              let items = json["items"] as? [[String: Any]] else {
            return []
        }
        
        return items.compactMap { item in
            guard let id = item["Id"] as? Int,
                  let subject = item["Fag"] as? String,
                  let date = item["Dato"] as? String,
                  let startTime = item["StartKl"] as? String,
                  let endTime = item["SluttKl"] as? String,
                  let timenr = item["Timenr"] as? Int else {
                return nil
            }
            
            return ClassInfo(id: id, subject: subject, date: date, startTime: startTime, endTime: endTime, timenr: timenr)
        }
    }
    
    func registerAttendance(for classInfo: ClassInfo) async throws -> Bool {
        let attendanceURL = "https://iskole.net/iskole_elev/rest/v0/VoTimeplan_elev_oppmote/action/lagre_oppmote"
        var request = URLRequest(url: URL(string: attendanceURL)!)
        request.httpMethod = "POST"
        request.setValue("application/json", forHTTPHeaderField: "Content-Type")
        
        let attendanceData: [String: Any] = [
            "Id": classInfo.id,
            "Timenr": classInfo.timenr,
            "ElevForerTilstedevaerelse": 1
        ]
        
        request.httpBody = try JSONSerialization.data(withJSONObject: attendanceData)
        
        addCookiesToRequest(&request)
        
        let (_, response) = try await URLSession.shared.data(for: request)
        
        if let httpResponse = response as? HTTPURLResponse {
            return httpResponse.statusCode == 200
        }
        
        return false
    }
    
    private func addCookiesToRequest(_ request: inout URLRequest) {
        let cookieHeader = sessionCookies.map { "\($0.name)=\($0.value)" }.joined(separator: "; ")
        if !cookieHeader.isEmpty {
            request.setValue(cookieHeader, forHTTPHeaderField: "Cookie")
        }
    }
}
