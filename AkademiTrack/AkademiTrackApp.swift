import SwiftUI

@main
struct AkademiTrackApp: App {
    let persistenceController = PersistenceController.shared

    var body: some Scene {
        WindowGroup {
            MainView()
                .environment(\.managedObjectContext, persistenceController.container.viewContext)
        }
        .windowResizability(.contentSize)   // ✅ belongs on WindowGroup
        .defaultSize(width: 580, height: 680)
    }
}
