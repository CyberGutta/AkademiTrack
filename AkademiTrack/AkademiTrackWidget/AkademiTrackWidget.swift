import WidgetKit
import SwiftUI

struct WidgetData: Codable {
    // Daily data
    let dailyRegistered: Int
    let dailyTotal: Int
    let dailyBalance: Double
    
    // Weekly data
    let weeklyRegistered: Int
    let weeklyTotal: Int
    let weeklyBalance: Double
    
    // Monthly data
    let monthlyRegistered: Int
    let monthlyTotal: Int
    let monthlyBalance: Double
    
    // Current class
    let currentClassName: String?
    let currentClassTime: String?
    let currentClassRoom: String?
    
    // Next class
    let nextClassName: String?
    let nextClassTime: String?
    let nextClassRoom: String?
    
    let lastUpdated: Date
    
    enum CodingKeys: String, CodingKey {
        case dailyRegistered = "DailyRegistered"
        case dailyTotal = "DailyTotal"
        case dailyBalance = "DailyBalance"
        case weeklyRegistered = "WeeklyRegistered"
        case weeklyTotal = "WeeklyTotal"
        case weeklyBalance = "WeeklyBalance"
        case monthlyRegistered = "MonthlyRegistered"
        case monthlyTotal = "MonthlyTotal"
        case monthlyBalance = "MonthlyBalance"
        case currentClassName = "CurrentClassName"
        case currentClassTime = "CurrentClassTime"
        case currentClassRoom = "CurrentClassRoom"
        case nextClassName = "NextClassName"
        case nextClassTime = "NextClassTime"
        case nextClassRoom = "NextClassRoom"
        case lastUpdated = "LastUpdated"
    }
}

struct Provider: TimelineProvider {
    func placeholder(in context: Context) -> SimpleEntry {
        SimpleEntry(date: Date(), widgetData: WidgetData(
            dailyRegistered: 0, dailyTotal: 0, dailyBalance: 0,
            weeklyRegistered: 0, weeklyTotal: 0, weeklyBalance: 0,
            monthlyRegistered: 0, monthlyTotal: 0, monthlyBalance: 0,
            currentClassName: nil, currentClassTime: nil, currentClassRoom: nil,
            nextClassName: nil, nextClassTime: nil, nextClassRoom: nil,
            lastUpdated: Date()
        ))
    }

    func getSnapshot(in context: Context, completion: @escaping (SimpleEntry) -> ()) {
        let entry = SimpleEntry(date: Date(), widgetData: loadWidgetData())
        completion(entry)
    }

    func getTimeline(in context: Context, completion: @escaping (Timeline<Entry>) -> ()) {
        let currentDate = Date()
        let widgetData = loadWidgetData()
        let entry = SimpleEntry(date: currentDate, widgetData: widgetData)
        
        // Update every 1 minute for near real-time updates
        let nextUpdate = Calendar.current.date(byAdding: .minute, value: 1, to: currentDate)!
        let timeline = Timeline(entries: [entry], policy: .after(nextUpdate))
        
        completion(timeline)
    }
    
    private func loadWidgetData() -> WidgetData {
        // Use App Group container - this is the ONLY way widgets can access shared data
        let fileManager = FileManager.default
        guard let containerURL = fileManager.containerURL(forSecurityApplicationGroupIdentifier: "group.com.akademitrack.widget") else {
            print("❌ Widget: Failed to get App Group container")
            return WidgetData(
                dailyRegistered: 0, dailyTotal: 0, dailyBalance: 0,
                weeklyRegistered: 0, weeklyTotal: 0, weeklyBalance: 0,
                monthlyRegistered: 0, monthlyTotal: 0, monthlyBalance: 0,
                currentClassName: nil, currentClassTime: nil, currentClassRoom: nil,
                nextClassName: nil, nextClassTime: nil, nextClassRoom: nil,
                lastUpdated: Date()
            )
        }
        
        let widgetFile = containerURL.appendingPathComponent("widget-data.json")
        
        print("📂 Widget: Attempting to read from \(widgetFile.path)")
        
        // Check if file exists
        if !fileManager.fileExists(atPath: widgetFile.path) {
            print("❌ Widget: File does not exist at \(widgetFile.path)")
            return WidgetData(
                dailyRegistered: 0, dailyTotal: 0, dailyBalance: 0,
                weeklyRegistered: 0, weeklyTotal: 0, weeklyBalance: 0,
                monthlyRegistered: 0, monthlyTotal: 0, monthlyBalance: 0,
                currentClassName: nil, currentClassTime: nil, currentClassRoom: nil,
                nextClassName: nil, nextClassTime: nil, nextClassRoom: nil,
                lastUpdated: Date()
            )
        }
        
        print("✅ Widget: File exists")
        
        guard let data = try? Data(contentsOf: widgetFile) else {
            print("❌ Widget: Failed to read file data")
            return WidgetData(
                dailyRegistered: 0, dailyTotal: 0, dailyBalance: 0,
                weeklyRegistered: 0, weeklyTotal: 0, weeklyBalance: 0,
                monthlyRegistered: 0, monthlyTotal: 0, monthlyBalance: 0,
                currentClassName: nil, currentClassTime: nil, currentClassRoom: nil,
                nextClassName: nil, nextClassTime: nil, nextClassRoom: nil,
                lastUpdated: Date()
            )
        }
        
        print("✅ Widget: Read \(data.count) bytes")
        print("📄 Widget: JSON content: \(String(data: data, encoding: .utf8) ?? "unable to decode")")
        
        let decoder = JSONDecoder()
        decoder.dateDecodingStrategy = .iso8601
        
        guard let widgetData = try? decoder.decode(WidgetData.self, from: data) else {
            print("❌ Widget: Failed to decode JSON")
            return WidgetData(
                dailyRegistered: 0, dailyTotal: 0, dailyBalance: 0,
                weeklyRegistered: 0, weeklyTotal: 0, weeklyBalance: 0,
                monthlyRegistered: 0, monthlyTotal: 0, monthlyBalance: 0,
                currentClassName: nil, currentClassTime: nil, currentClassRoom: nil,
                nextClassName: nil, nextClassTime: nil, nextClassRoom: nil,
                lastUpdated: Date()
            )
        }
        
        print("✅ Widget: Successfully loaded - Daily: \(widgetData.dailyRegistered)/\(widgetData.dailyTotal), Next: \(widgetData.nextClassName ?? "None")")
        return widgetData
    }
}

struct SimpleEntry: TimelineEntry {
    let date: Date
    let widgetData: WidgetData
}

struct AkademiTrackWidgetEntryView : View {
    var entry: Provider.Entry
    @Environment(\.widgetFamily) var family

    var body: some View {
        switch family {
        case .systemSmall:
            SmallWidgetView(data: entry.widgetData)
        case .systemMedium:
            MediumWidgetView(data: entry.widgetData)
        case .systemLarge:
            LargeWidgetView(data: entry.widgetData)
        default:
            SmallWidgetView(data: entry.widgetData)
        }
    }
}

struct SmallWidgetView: View {
    let data: WidgetData
    
    var body: some View {
        VStack(alignment: .leading, spacing: 5) {
            Text("AkademiTrack")
                .font(.system(size: 12, weight: .semibold))
                .foregroundColor(.white)
            
            // Status message
            Text(getBalanceText(data.dailyBalance))
                .font(.system(size: 9, weight: .semibold))
                .foregroundColor(getColor(for: data.dailyBalance))
                .lineLimit(1)
                .minimumScaleFactor(0.7)
                .padding(.bottom, 1)
            
            Divider()
                .background(Color.white.opacity(0.2))
            
            VStack(alignment: .leading, spacing: 3) {
                // Daily
                HStack(spacing: 2) {
                    Text("I dag")
                        .font(.system(size: 9))
                        .foregroundColor(.white.opacity(0.6))
                        .frame(width: 30, alignment: .leading)
                    Text("\(data.dailyRegistered)/\(data.dailyTotal)")
                        .font(.system(size: 9, weight: .medium))
                        .foregroundColor(.white.opacity(0.8))
                        .frame(width: 28, alignment: .leading)
                    Spacer()
                    Text(formatBalance(data.dailyBalance))
                        .font(.system(size: 10, weight: .bold))
                        .foregroundColor(getColor(for: data.dailyBalance))
                        .lineLimit(1)
                        .minimumScaleFactor(0.8)
                }
                
                // Weekly
                HStack(spacing: 2) {
                    Text("Uke")
                        .font(.system(size: 9))
                        .foregroundColor(.white.opacity(0.6))
                        .frame(width: 30, alignment: .leading)
                    Text("\(data.weeklyRegistered)/\(data.weeklyTotal)")
                        .font(.system(size: 9, weight: .medium))
                        .foregroundColor(.white.opacity(0.8))
                        .frame(width: 28, alignment: .leading)
                    Spacer()
                    Text(formatBalance(data.weeklyBalance))
                        .font(.system(size: 10, weight: .bold))
                        .foregroundColor(getColor(for: data.weeklyBalance))
                        .lineLimit(1)
                        .minimumScaleFactor(0.8)
                }
                
                // Monthly
                HStack(spacing: 2) {
                    Text("Mnd")
                        .font(.system(size: 9))
                        .foregroundColor(.white.opacity(0.6))
                        .frame(width: 30, alignment: .leading)
                    Text("\(data.monthlyRegistered)/\(data.monthlyTotal)")
                        .font(.system(size: 9, weight: .medium))
                        .foregroundColor(.white.opacity(0.8))
                        .frame(width: 28, alignment: .leading)
                    Spacer()
                    Text(formatBalance(data.monthlyBalance))
                        .font(.system(size: 10, weight: .bold))
                        .foregroundColor(getColor(for: data.monthlyBalance))
                        .lineLimit(1)
                        .minimumScaleFactor(0.8)
                }
            }
            
            Spacer()
        }
        .padding(12)
        .containerBackground(Color(red: 0.18, green: 0.18, blue: 0.20), for: .widget)
    }
    
    private func formatBalance(_ value: Double) -> String {
        let absValue = abs(value)
        let formatted: String
        
        if absValue.truncatingRemainder(dividingBy: 1) == 0 {
            formatted = String(format: "%.0f", absValue)
        } else {
            formatted = String(format: "%.2f", absValue)
                .replacingOccurrences(of: #"\.?0+$"#, with: "", options: .regularExpression)
        }
        
        return value >= 0 ? "+\(formatted)" : "-\(formatted)"
    }
    
    private func getBalanceText(_ value: Double) -> String {
        if value > 0 {
            return "Du ligger i overtid"
        } else if value < 0 {
            return "Du ligger under"
        } else {
            return "Du ligger på målet"
        }
    }
    
    private func getColor(for value: Double) -> Color {
        if value > 0 {
            return Color(red: 0.2, green: 0.85, blue: 0.3)
        } else if value < 0 {
            return Color(red: 1.0, green: 0.4, blue: 0.3)
        } else {
            return Color.white.opacity(0.8)
        }
    }
}

struct MediumWidgetView: View {
    let data: WidgetData
    
    var body: some View {
        VStack(spacing: 12) {
            Text("AkademiTrack")
                .font(.system(size: 14, weight: .bold))
                .foregroundColor(.white)
            
            HStack(spacing: 12) {
                AttendanceCard(label: "I dag", registered: data.dailyRegistered, total: data.dailyTotal, balance: data.dailyBalance)
                AttendanceCard(label: "Uke", registered: data.weeklyRegistered, total: data.weeklyTotal, balance: data.weeklyBalance)
                AttendanceCard(label: "Måned", registered: data.monthlyRegistered, total: data.monthlyTotal, balance: data.monthlyBalance)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .padding(16)
        .containerBackground(Color(red: 0.18, green: 0.18, blue: 0.20), for: .widget)
    }
}

struct LargeWidgetView: View {
    let data: WidgetData
    
    var body: some View {
        VStack(alignment: .leading, spacing: 10) {
            // Header
            Text("AkademiTrack")
                .font(.system(size: 15, weight: .bold))
                .foregroundColor(.white)
            
            // Current Class Section (always show)
            VStack(alignment: .leading, spacing: 4) {
                HStack {
                    Text("NÅVÆRENDE TIME")
                        .font(.system(size: 9, weight: .semibold))
                        .foregroundColor(.white.opacity(0.6))
                    Spacer()
                    Text("📚")
                        .font(.system(size: 12))
                }
                
                if let currentName = data.currentClassName, currentName != "Ingen time" {
                    Text(currentName)
                        .font(.system(size: 14, weight: .bold))
                        .foregroundColor(.white)
                        .lineLimit(1)
                        .minimumScaleFactor(0.7)
                    
                    Text(data.currentClassTime ?? "--:-- - --:--")
                        .font(.system(size: 11))
                        .foregroundColor(.white.opacity(0.8))
                    
                    if let room = data.currentClassRoom, !room.isEmpty {
                        Text(room)
                            .font(.system(size: 10))
                            .foregroundColor(.white.opacity(0.6))
                    }
                } else {
                    Text("Ingen time")
                        .font(.system(size: 14, weight: .bold))
                        .foregroundColor(.white)
                    
                    Text("--:-- - --:--")
                        .font(.system(size: 11))
                        .foregroundColor(.white.opacity(0.8))
                }
            }
            .padding(8)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(data.currentClassName != nil && data.currentClassName != "Ingen time" ? Color.green.opacity(0.15) : Color.white.opacity(0.08))
            .cornerRadius(10)
            
            // Next Class Section
            VStack(alignment: .leading, spacing: 4) {
                HStack {
                    Text("NESTE TIME")
                        .font(.system(size: 9, weight: .semibold))
                        .foregroundColor(.white.opacity(0.6))
                    Spacer()
                    Text("⏰")
                        .font(.system(size: 12))
                }
                
                Text(data.nextClassName ?? "Ingen time")
                    .font(.system(size: 14, weight: .bold))
                    .foregroundColor(.white)
                    .lineLimit(1)
                    .minimumScaleFactor(0.7)
                
                Text(data.nextClassTime ?? "--:-- - --:--")
                    .font(.system(size: 11))
                    .foregroundColor(.white.opacity(0.8))
                
                if let room = data.nextClassRoom, !room.isEmpty {
                    Text(room)
                        .font(.system(size: 10))
                        .foregroundColor(.white.opacity(0.6))
                }
            }
            .padding(8)
            .frame(maxWidth: .infinity, alignment: .leading)
            .background(Color.white.opacity(0.08))
            .cornerRadius(10)
            
            // Attendance Section
            VStack(alignment: .leading, spacing: 6) {
                Text("FREMMØTE")
                    .font(.system(size: 9, weight: .semibold))
                    .foregroundColor(.white.opacity(0.6))
                
                AttendanceRow(label: "I dag", registered: data.dailyRegistered, total: data.dailyTotal, balance: data.dailyBalance)
                AttendanceRow(label: "Uke", registered: data.weeklyRegistered, total: data.weeklyTotal, balance: data.weeklyBalance)
                AttendanceRow(label: "Måned", registered: data.monthlyRegistered, total: data.monthlyTotal, balance: data.monthlyBalance)
            }
            
            Spacer()
        }
        .padding(14)
        .containerBackground(Color(red: 0.18, green: 0.18, blue: 0.20), for: .widget)
    }
}

struct AttendanceRow: View {
    let label: String
    let registered: Int
    let total: Int
    let balance: Double
    
    var body: some View {
        HStack(spacing: 8) {
            Text(label)
                .font(.system(size: 11))
                .foregroundColor(.white.opacity(0.6))
                .frame(width: 45, alignment: .leading)
            
            Spacer()
            
            // Show session count
            Text("\(registered)/\(total)")
                .font(.system(size: 12, weight: .semibold))
                .foregroundColor(.white.opacity(0.9))
                .frame(minWidth: 40, alignment: .trailing)
            
            // Show balance
            Text(formatBalance(balance))
                .font(.system(size: 12, weight: .bold))
                .foregroundColor(getColor(for: balance))
                .frame(minWidth: 45, alignment: .trailing)
        }
    }
    
    private func formatBalance(_ value: Double) -> String {
        let absValue = abs(value)
        let formatted: String
        
        if absValue.truncatingRemainder(dividingBy: 1) == 0 {
            formatted = String(format: "%.0f", absValue)
        } else {
            formatted = String(format: "%.2f", absValue)
                .replacingOccurrences(of: #"\.?0+$"#, with: "", options: .regularExpression)
        }
        
        return value >= 0 ? "+\(formatted)" : "-\(formatted)"
    }
    
    private func getColor(for value: Double) -> Color {
        if value > 0 {
            return Color(red: 0.2, green: 0.85, blue: 0.3)
        } else if value < 0 {
            return Color(red: 1.0, green: 0.4, blue: 0.3)
        } else {
            return Color.white.opacity(0.8)
        }
    }
}

struct AttendanceCard: View {
    let label: String
    let registered: Int
    let total: Int
    let balance: Double
    
    var body: some View {
        VStack(spacing: 4) {
            Text(label)
                .font(.system(size: 10))
                .foregroundColor(.white.opacity(0.6))
            
            // Session count
            Text("\(registered)/\(total)")
                .font(.system(size: 14, weight: .semibold))
                .foregroundColor(.white.opacity(0.9))
            
            // Balance
            Text(formatBalance(balance))
                .font(.system(size: 16, weight: .bold))
                .foregroundColor(getColor(for: balance))
        }
        .frame(width: 70)
        .padding(.vertical, 10)
        .padding(.horizontal, 6)
        .background(Color.white.opacity(0.08))
        .cornerRadius(10)
    }
    
    private func formatBalance(_ value: Double) -> String {
        let absValue = abs(value)
        let formatted: String
        
        if absValue.truncatingRemainder(dividingBy: 1) == 0 {
            formatted = String(format: "%.0f", absValue)
        } else {
            formatted = String(format: "%.2f", absValue)
                .replacingOccurrences(of: #"\.?0+$"#, with: "", options: .regularExpression)
        }
        
        return value >= 0 ? "+\(formatted)" : "-\(formatted)"
    }
    
    private func getColor(for value: Double) -> Color {
        if value > 0 {
            return Color(red: 0.2, green: 0.85, blue: 0.3)
        } else if value < 0 {
            return Color(red: 1.0, green: 0.4, blue: 0.3)
        } else {
            return Color.white.opacity(0.8)
        }
    }
}

struct HourRow: View {
    let label: String
    let value: Double
    
    var body: some View {
        HStack {
            Text(label)
                .font(.system(size: 13))
                .foregroundColor(.white.opacity(0.7))
            
            Spacer()
            
            Text(formatHours(value))
                .font(.system(size: 15, weight: .bold))
                .foregroundColor(getColor(for: value))
        }
    }
    
    private func formatHours(_ value: Double) -> String {
        // Match C# format "0.##" - show up to 2 decimals, remove trailing zeros
        let absValue = abs(value)
        let formatted: String
        
        // Check if it's a whole number
        if absValue.truncatingRemainder(dividingBy: 1) == 0 {
            formatted = String(format: "%.0f", absValue)
        } else {
            // Show up to 2 decimals, remove trailing zeros
            formatted = String(format: "%.2f", absValue)
                .replacingOccurrences(of: #"\.?0+$"#, with: "", options: .regularExpression)
        }
        
        return value >= 0 ? "+\(formatted)" : "-\(formatted)"
    }
    
    private func getColor(for value: Double) -> Color {
        if value > 0 {
            return Color(red: 0.2, green: 0.85, blue: 0.3) // Bright green like your app
        } else if value < 0 {
            return Color(red: 1.0, green: 0.4, blue: 0.3) // Orange/red
        } else {
            return Color.white.opacity(0.8)
        }
    }
}

struct HourCard: View {
    let label: String
    let value: Double
    
    var body: some View {
        VStack(spacing: 6) {
            Text(label)
                .font(.system(size: 11))
                .foregroundColor(.white.opacity(0.6))
            
            Text(formatHours(value))
                .font(.system(size: 18, weight: .bold))
                .foregroundColor(getColor(for: value))
        }
        .frame(width: 75)
        .padding(.vertical, 12)
        .padding(.horizontal, 8)
        .background(Color.white.opacity(0.08))
        .cornerRadius(10)
    }
    
    private func formatHours(_ value: Double) -> String {
        // Match C# format "0.##" - show up to 2 decimals, remove trailing zeros
        let absValue = abs(value)
        let formatted: String
        
        // Check if it's a whole number
        if absValue.truncatingRemainder(dividingBy: 1) == 0 {
            formatted = String(format: "%.0f", absValue)
        } else {
            // Show up to 2 decimals, remove trailing zeros
            formatted = String(format: "%.2f", absValue)
                .replacingOccurrences(of: #"\.?0+$"#, with: "", options: .regularExpression)
        }
        
        return value >= 0 ? "+\(formatted)" : "-\(formatted)"
    }
    
    private func getColor(for value: Double) -> Color {
        if value > 0 {
            return Color(red: 0.2, green: 0.85, blue: 0.3)
        } else if value < 0 {
            return Color(red: 1.0, green: 0.4, blue: 0.3)
        } else {
            return Color.white.opacity(0.8)
        }
    }
}

@main
struct AkademiTrackWidget: Widget {
    let kind: String = "AkademiTrackWidget"

    var body: some WidgetConfiguration {
        StaticConfiguration(kind: kind, provider: Provider()) { entry in
            AkademiTrackWidgetEntryView(entry: entry)
        }
        .configurationDisplayName("AkademiTrack")
        .description("Se dine timer over/under målet og neste time")
        .supportedFamilies([.systemSmall, .systemMedium, .systemLarge])
    }
}

struct AkademiTrackWidget_Previews: PreviewProvider {
    static var previews: some View {
        let sampleData = WidgetData(
            dailyRegistered: 0, dailyTotal: 0, dailyBalance: 22.5,
            weeklyRegistered: 12, weeklyTotal: 18, weeklyBalance: -6,
            monthlyRegistered: 30, monthlyTotal: 52, monthlyBalance: -22,
            currentClassName: "Matematikk 1", currentClassTime: "08:30 - 10:00", currentClassRoom: "Rom 101",
            nextClassName: "Programmering", nextClassTime: "10:15 - 12:00", nextClassRoom: "Rom 205",
            lastUpdated: Date()
        )
        
        AkademiTrackWidgetEntryView(entry: SimpleEntry(date: Date(), widgetData: sampleData))
            .previewContext(WidgetPreviewContext(family: .systemSmall))
        
        AkademiTrackWidgetEntryView(entry: SimpleEntry(date: Date(), widgetData: sampleData))
            .previewContext(WidgetPreviewContext(family: .systemMedium))
        
        AkademiTrackWidgetEntryView(entry: SimpleEntry(date: Date(), widgetData: sampleData))
            .previewContext(WidgetPreviewContext(family: .systemLarge))
    }
}
