//
//  AkademiTrackApp.swift
//  AkademiTrack
//
//  Created by Andreas Nilsen on 27/08/2025.
//

import SwiftUI

@main
struct AkademiTrackApp: App {
    let persistenceController = PersistenceController.shared

    var body: some Scene {
        WindowGroup {
            ContentView()
                .environment(\.managedObjectContext, persistenceController.container.viewContext)
        }
    }
}
