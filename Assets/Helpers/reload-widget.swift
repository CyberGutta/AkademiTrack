#!/usr/bin/env swift

import WidgetKit

// Force reload all widgets immediately
WidgetCenter.shared.reloadAllTimelines()
print("Widget reload triggered")
