//
//  DesktopLyricSettings.swift
//  LyricDrop
//
//  Desktop lyric settings editor
//

import SwiftUI

// MARK: - Settings Window

class LyricSettingsWindow {
    private static var window: NSWindow?

    static func show(player: LyricPlayer) {
        if let w = window, w.isVisible {
            w.makeKeyAndOrderFront(nil)
            return
        }

        let w = NSWindow(
            contentRect: NSRect(x: 0, y: 0, width: 360, height: 650),
            styleMask: [.titled, .closable],
            backing: .buffered,
            defer: false
        )
        w.title = "桌面歌词设置"
        w.center()
        w.contentView = NSHostingView(rootView: LyricSettingsView(player: player))
        w.isReleasedWhenClosed = false
        w.makeKeyAndOrderFront(nil)
        NSApp.activate(ignoringOtherApps: true)
        window = w
    }
}

// MARK: - Settings View

struct LyricSettingsView: View {
    @ObservedObject var player: LyricPlayer

    var body: some View {
        Form {
            // MARK: Theme
            Section {
                VStack(alignment: .leading, spacing: 8) {
                    Text("主题颜色")
                        .font(.headline)
                    LazyVGrid(columns: Array(repeating: GridItem(.flexible(), spacing: 8), count: 3), spacing: 8) {
                        ForEach(LyricTheme.allCases) { theme in
                            themeCard(theme)
                        }
                    }
                }
            }

            Divider()

            // MARK: Font
            Section {
                VStack(alignment: .leading, spacing: 6) {
                    HStack {
                        Text("字体大小")
                            .font(.headline)
                        Spacer()
                        Text("\(Int(player.lyricFontSize))pt")
                            .foregroundColor(.secondary)
                            .monospacedDigit()
                    }
                    Slider(value: $player.lyricFontSize, in: 16...48, step: 2)
                }
            }

            Divider()

            // MARK: Font
            Section {
                VStack(alignment: .leading, spacing: 6) {
                    Text("字体")
                        .font(.headline)
                    Picker("", selection: $player.lyricFontName) {
                        Text("系统默认").tag("")
                        ForEach(availableFonts, id: \.self) { name in
                            Text(name).tag(name)
                        }
                    }
                    .labelsHidden()
                }
            }

            Divider()

            // MARK: Display
            Section {
                Text("显示")
                    .font(.headline)

                Picker("显示模式", selection: $player.lyricScrollMode) {
                    ForEach(LyricScrollMode.allCases) { mode in
                        Text(mode.label).tag(mode)
                    }
                }
                .pickerStyle(.segmented)

                if player.lyricScrollMode != .marquee {
                    Picker("显示行数", selection: $player.lyricLineCount) {
                        Text("1行").tag(1)
                        Text("3行").tag(3)
                        Text("5行").tag(5)
                        Text("7行").tag(7)
                    }
                    .pickerStyle(.segmented)
                }

                VStack(alignment: .leading, spacing: 6) {
                    HStack {
                        Text("窗口宽度")
                        Spacer()
                        Text("\(Int(player.lyricWidthRatio * 100))%")
                            .foregroundColor(.secondary)
                            .monospacedDigit()
                    }
                    Slider(value: $player.lyricWidthRatio, in: 0.3...0.8, step: 0.05)
                }
            }

            Divider()

            // MARK: Effects
            Section {
                Text("效果")
                    .font(.headline)

                Toggle("文字阴影", isOn: $player.lyricShadowEnabled)
                Toggle("始终显示背景", isOn: $player.lyricBgAlways)
            }

            Divider()

            // MARK: General
            Section {
                Text("通用")
                    .font(.headline)

                Toggle("开机自启动", isOn: Binding(
                    get: { player.launchAtLogin },
                    set: { _ in player.toggleLaunchAtLogin() }
                ))
            }

            Divider()

            // MARK: Preview
            Section {
                Text("预览")
                    .font(.headline)
                previewArea
            }
        }
        .formStyle(.grouped)
        .frame(width: 360, height: 650)
        .onChange(of: player.lyricFontSize) { _, _ in player.saveLyricSettings() }
        .onChange(of: player.lyricLineCount) { _, _ in player.saveLyricSettings() }
        .onChange(of: player.lyricTheme) { _, _ in player.saveLyricSettings() }
        .onChange(of: player.lyricShadowEnabled) { _, _ in player.saveLyricSettings() }
        .onChange(of: player.lyricBgAlways) { _, _ in player.saveLyricSettings() }
        .onChange(of: player.lyricScrollMode) { _, _ in player.saveLyricSettings() }

        .onChange(of: player.lyricFontName) { _, _ in player.saveLyricSettings() }
        .onChange(of: player.lyricWidthRatio) { _, _ in player.saveLyricSettings() }
    }

    private var availableFonts: [String] {
        NSFontManager.shared.availableFontFamilies.sorted()
    }

    // MARK: - Theme Card

    private func themeCard(_ theme: LyricTheme) -> some View {
        let isSelected = player.lyricTheme == theme
        return Button {
            player.lyricTheme = theme
        } label: {
            VStack(spacing: 4) {
                RoundedRectangle(cornerRadius: 6)
                    .fill(LinearGradient(colors: theme.colors, startPoint: .leading, endPoint: .trailing))
                    .frame(height: 28)
                    .overlay(
                        RoundedRectangle(cornerRadius: 6)
                            .stroke(isSelected ? Color.accentColor : .clear, lineWidth: 2)
                    )
                Text(theme.label)
                    .font(.caption2)
                    .foregroundColor(isSelected ? .accentColor : .secondary)
            }
        }
        .buttonStyle(.plain)
    }

    // MARK: - Preview

    private var previewArea: some View {
        let sampleText = player.currentLine.isEmpty ? "示例歌词文字" : player.currentLine
        let gradient = LinearGradient(colors: player.lyricTheme.colors, startPoint: .leading, endPoint: .trailing)

        return ZStack {
            RoundedRectangle(cornerRadius: 8)
                .fill(.black.opacity(0.8))

            VStack(spacing: 4) {
                Text(sampleText)
                    .font(player.lyricFont(size: player.lyricFontSize * 0.6))
                    .foregroundStyle(gradient)
                    .shadow(color: player.lyricShadowEnabled ? .black.opacity(0.6) : .clear, radius: 3)

                if player.lyricLineCount >= 3 {
                    let next = player.nextLine.isEmpty ? "下一句歌词" : player.nextLine
                    Text(next)
                        .font(.system(size: player.lyricFontSize * 0.4, weight: .medium))
                        .foregroundColor(.white.opacity(0.3))
                    if player.lyricLineCount >= 5 {
                        Text("...")
                            .font(.system(size: player.lyricFontSize * 0.35))
                            .foregroundColor(.white.opacity(0.2))
                    }
                }
            }
            .padding(8)
        }
        .frame(height: 60)
    }
}
