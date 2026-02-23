import WidgetKit
import SwiftUI

struct WidgetData: Codable {
    let dailySaldo: Double
    let weeklySaldo: Double
    let monthlySaldo: Double
    let lastUpdated: Date
    
    enum CodingKeys: String, CodingKey {
        case dailySaldo = "DailySaldo"
        case weeklySaldo = "WeeklySaldo"
        case monthlySaldo = "MonthlySaldo"
        case lastUpdated = "LastUpdated"
    }
}

struct Provider: TimelineProvider {
    func placeholder(in context: Context) -> SimpleEntry {
        SimpleEntry(date: Date(), widgetData: WidgetData(
            dailySaldo: 0,
            weeklySaldo: 0,
            monthlySaldo: 0,
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
        let fileManager = FileManager.default
        guard let containerURL = fileManager.containerURL(forSecurityApplicationGroupIdentifier: "6SF4T9DUN4.com.CyberBrothers.akademitrack") else {
            return WidgetData(dailySaldo: 0, weeklySaldo: 0, monthlySaldo: 0, lastUpdated: Date())
        }
        
        let fileURL = containerURL.appendingPathComponent("widget-data.json")
        
        guard let data = try? Data(contentsOf: fileURL),
              let widgetData = try? JSONDecoder().decode(WidgetData.self, from: data) else {
            return WidgetData(dailySaldo: 0, weeklySaldo: 0, monthlySaldo: 0, lastUpdated: Date())
        }
        
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
        default:
            SmallWidgetView(data: entry.widgetData)
        }
    }
}

struct SmallWidgetView: View {
    let data: WidgetData
    
    var body: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("AkademiTrack")
                .font(.caption)
                .fontWeight(.semibold)
                .foregroundColor(.secondary)
            
            Spacer()
            
            BalanceRow(label: "I dag", value: data.dailySaldo)
            BalanceRow(label: "Uke", value: data.weeklySaldo)
            BalanceRow(label: "Måned", value: data.monthlySaldo)
            
            Spacer()
        }
        .padding()
        .background(Color(white: 0.95))
    }
}

struct MediumWidgetView: View {
    let data: WidgetData
    
    var body: some View {
        HStack(spacing: 16) {
            VStack(alignment: .leading, spacing: 4) {
                Text("AkademiTrack")
                    .font(.headline)
                    .fontWeight(.semibold)
                
                Text("Fremmøte")
                    .font(.caption)
                    .foregroundColor(.secondary)
            }
            
            Spacer()
            
            HStack(spacing: 20) {
                BalanceCard(label: "I dag", value: data.dailySaldo)
                BalanceCard(label: "Uke", value: data.weeklySaldo)
                BalanceCard(label: "Måned", value: data.monthlySaldo)
            }
        }
        .padding()
        .background(Color(white: 0.95))
    }
}

struct BalanceRow: View {
    let label: String
    let value: Double
    
    var body: some View {
        HStack {
            Text(label)
                .font(.caption)
                .foregroundColor(.secondary)
            
            Spacer()
            
            Text(formatValue(value))
                .font(.caption)
                .fontWeight(.bold)
                .foregroundColor(value >= 0 ? .green : .red)
        }
    }
    
    private func formatValue(_ value: Double) -> String {
        let formatted = String(format: "%.1f", abs(value))
        return value >= 0 ? "+\(formatted)" : "-\(formatted)"
    }
}

struct BalanceCard: View {
    let label: String
    let value: Double
    
    var body: some View {
        VStack(spacing: 4) {
            Text(label)
                .font(.caption2)
                .foregroundColor(.secondary)
            
            Text(formatValue(value))
                .font(.system(size: 18, weight: .bold))
                .foregroundColor(value >= 0 ? .green : .red)
        }
        .frame(width: 70)
        .padding(8)
        .background(Color.white)
        .cornerRadius(8)
    }
    
    private func formatValue(_ value: Double) -> String {
        let formatted = String(format: "%.1f", abs(value))
        return value >= 0 ? "+\(formatted)" : "-\(formatted)"
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
        .description("Se din fremmøte-saldo for dag, uke og måned")
        .supportedFamilies([.systemSmall, .systemMedium])
    }
}

struct AkademiTrackWidget_Previews: PreviewProvider {
    static var previews: some View {
        AkademiTrackWidgetEntryView(entry: SimpleEntry(
            date: Date(),
            widgetData: WidgetData(
                dailySaldo: 2.5,
                weeklySaldo: -1.2,
                monthlySaldo: 5.8,
                lastUpdated: Date()
            )
        ))
        .previewContext(WidgetPreviewContext(family: .systemSmall))
        
        AkademiTrackWidgetEntryView(entry: SimpleEntry(
            date: Date(),
            widgetData: WidgetData(
                dailySaldo: 2.5,
                weeklySaldo: -1.2,
                monthlySaldo: 5.8,
                lastUpdated: Date()
            )
        ))
        .previewContext(WidgetPreviewContext(family: .systemMedium))
    }
}
