//
//  LyricDropApp.swift
//  LyricDrop
//
//  Menu bar LRC lyrics player
//

import SwiftUI
import Combine

@main
struct LyricDropApp: App {
    @StateObject private var player = LyricPlayer()
    @State private var lyricPanel: DesktopLyricPanel?
    @State private var didLaunch = false

    var body: some Scene {
        MenuBarExtra {
            MenuBarView(player: player)
                .onAppear {
                    if !didLaunch {
                        didLaunch = true
                        if player.showDesktopLyric {
                            showOrHidePanel(true)
                        }
                    }
                }
        } label: {
            if let image = player.menuBarImage {
                Image(nsImage: image)
            } else {
                Text(player.menuBarText)
                    .lineLimit(1)
            }
        }
        .menuBarExtraStyle(.window)
        .onChange(of: player.showDesktopLyric) { _, show in
            showOrHidePanel(show)
        }
    }

    private func showOrHidePanel(_ show: Bool) {
        if show {
            if lyricPanel == nil {
                lyricPanel = DesktopLyricPanel(player: player)
            }
            lyricPanel?.orderFront(nil)
        } else {
            lyricPanel?.orderOut(nil)
        }
    }
}
