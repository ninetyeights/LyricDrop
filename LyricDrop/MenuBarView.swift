//
//  MenuBarView.swift
//  LyricDrop
//
//  Menu bar popup panel UI
//

import SwiftUI
import UniformTypeIdentifiers

struct MenuBarView: View {
    @ObservedObject var player: LyricPlayer
    @State private var showURLInput = false
    @State private var urlText = ""

    var body: some View {
        VStack(spacing: 12) {
            // Header
            HStack {
                Text("LyricDrop")
                    .font(.headline)
                Spacer()
                Button {
                    player.showDesktopLyric.toggle()
                    player.saveLyricSettings()
                } label: {
                    Image(systemName: "text.bubble")
                        .font(.caption)
                        .foregroundColor(player.showDesktopLyric ? .accentColor : .secondary)
                }
                .buttonStyle(.plain)
                .help("桌面歌词")

                if player.showDesktopLyric {
                    Button {
                        player.desktopLyricLocked.toggle()
                        player.saveLyricSettings()
                    } label: {
                        Image(systemName: player.desktopLyricLocked ? "lock.fill" : "lock.open")
                            .font(.caption)
                            .foregroundColor(player.desktopLyricLocked ? .accentColor : .secondary)
                    }
                    .buttonStyle(.plain)
                    .help(player.desktopLyricLocked ? "解锁桌面歌词" : "锁定桌面歌词")
                }

                Button {
                    player.toggleKaraoke()
                } label: {
                    Image(systemName: "paintbrush.pointed")
                        .font(.caption)
                        .foregroundColor(player.karaokeEnabled ? .accentColor : .secondary)
                }
                .buttonStyle(.plain)
                .help("卡拉OK模式")

                Button("Quit") {
                    NSApplication.shared.terminate(nil)
                }
                .buttonStyle(.plain)
                .foregroundColor(.secondary)
                .font(.caption)
            }

            Divider()

            // File loader buttons
            HStack(spacing: 8) {
                Button {
                    openAudioFile()
                } label: {
                    Label(player.audioFileName.isEmpty ? "Open Audio" : player.audioFileName, systemImage: "music.note")
                        .lineLimit(1)
                        .frame(maxWidth: .infinity)
                }

                Button {
                    openLRCFile()
                } label: {
                    Label(player.lrcFileName.isEmpty ? "Open LRC" : player.lrcFileName, systemImage: "text.quote")
                        .lineLimit(1)
                        .frame(maxWidth: .infinity)
                }
            }

            // URL input
            HStack(spacing: 6) {
                if showURLInput {
                    TextField("输入音频 URL", text: $urlText)
                        .textFieldStyle(.roundedBorder)
                        .font(.caption)
                        .onSubmit {
                            if !urlText.isEmpty {
                                player.loadAudioFromURL(urlText)
                                showURLInput = false
                            }
                        }
                }

                Button {
                    if showURLInput {
                        if !urlText.isEmpty {
                            player.loadAudioFromURL(urlText)
                        }
                        showURLInput = false
                    } else {
                        showURLInput = true
                    }
                } label: {
                    Label(showURLInput ? "加载" : "从 URL 加载", systemImage: "link")
                        .lineLimit(1)
                        .font(.caption)
                }
                .disabled(player.isLoadingURL)
            }

            // Current lyric display
            Text(player.currentLine.isEmpty ? "No lyrics loaded" : player.currentLine)
                .font(.title3)
                .fontWeight(.medium)
                .multilineTextAlignment(.center)
                .frame(maxWidth: .infinity, minHeight: 50)
                .padding(.vertical, 4)

            // Progress bar
            if player.duration > 0 {
                VStack(spacing: 4) {
                    Slider(
                        value: Binding(
                            get: { player.currentTime },
                            set: { player.seek(to: $0) }
                        ),
                        in: 0...player.duration
                    )

                    HStack(spacing: 4) {
                        Text(LyricPlayer.formatTime(player.currentTime))
                            .font(.caption)
                            .foregroundColor(.secondary)
                            .monospacedDigit()
                        Spacer()

                        Button {
                            player.isMuted.toggle()
                        } label: {
                            Image(systemName: player.isMuted ? "speaker.slash.fill" : player.volume > 0.5 ? "speaker.wave.2.fill" : "speaker.wave.1.fill")
                                .font(.caption)
                                .foregroundColor(.secondary)
                        }
                        .buttonStyle(.plain)

                        Slider(value: $player.volume, in: 0...1)
                            .frame(width: 60)

                        Spacer()
                        Text(LyricPlayer.formatTime(player.duration))
                            .font(.caption)
                            .foregroundColor(.secondary)
                            .monospacedDigit()
                    }
                }
            }

            // Playback controls
            HStack(spacing: 16) {
                Button {
                    player.stop()
                } label: {
                    Image(systemName: "stop.fill")
                        .font(.title2)
                }
                .buttonStyle(.plain)
                .disabled(player.audioFileName.isEmpty)

                Button {
                    player.clearAll()
                } label: {
                    Image(systemName: "trash")
                        .font(.title2)
                        .foregroundColor(.secondary)
                }
                .buttonStyle(.plain)
                .disabled(player.audioFileName.isEmpty && player.lrcFileName.isEmpty)
                .help("清除音频和歌词")

                // Rewind 5s
                Button {
                    let newTime = max(0, player.currentTime - 5)
                    player.seek(to: newTime)
                } label: {
                    Image(systemName: "gobackward.5")
                        .font(.title2)
                }
                .buttonStyle(.plain)
                .disabled(player.audioFileName.isEmpty)

                Button {
                    player.playPause()
                } label: {
                    Image(systemName: player.isPlaying ? "pause.circle.fill" : "play.circle.fill")
                        .font(.largeTitle)
                }
                .buttonStyle(.plain)
                .disabled(player.audioFileName.isEmpty)

                // Forward 5s
                Button {
                    let newTime = min(player.duration, player.currentTime + 5)
                    player.seek(to: newTime)
                } label: {
                    Image(systemName: "goforward.5")
                        .font(.title2)
                }
                .buttonStyle(.plain)
                .disabled(player.audioFileName.isEmpty)

                // Loop toggle
                Button {
                    player.isLooping.toggle()
                    player.saveLyricSettings()
                } label: {
                    Image(systemName: "repeat")
                        .font(.title2)
                        .foregroundColor(player.isLooping ? .accentColor : .secondary)
                }
                .buttonStyle(.plain)
            }
            .padding(.vertical, 4)

            // Playback speed
            if player.duration > 0 {
                HStack(spacing: 8) {
                    Text("倍速")
                        .font(.caption)
                        .foregroundColor(.secondary)
                    ForEach([0.5, 0.75, 1.0, 1.25, 1.5, 2.0], id: \.self) { rate in
                        Button {
                            player.playbackRate = Float(rate)
                        } label: {
                            Text(rate == 1.0 ? "1x" : "\(rate, specifier: rate == floor(rate) ? "%.0fx" : "%.2gx")")
                                .font(.caption2)
                                .padding(.horizontal, 4)
                                .padding(.vertical, 2)
                                .background(
                                    RoundedRectangle(cornerRadius: 4)
                                        .fill(player.playbackRate == Float(rate) ? Color.accentColor.opacity(0.2) : .clear)
                                )
                        }
                        .buttonStyle(.plain)
                        .foregroundColor(player.playbackRate == Float(rate) ? .accentColor : .secondary)
                    }
                }
            }

            // Lyric offset control
            if !player.lines.isEmpty {
                HStack(spacing: 6) {
                    Text("歌词偏移")
                        .font(.caption)
                        .foregroundColor(.secondary)

                    Button { player.adjustOffset(-0.5) } label: {
                        Image(systemName: "minus.circle")
                            .font(.caption)
                    }
                    .buttonStyle(.plain)
                    .help("-0.5s")

                    Text(String(format: "%+.1fs", player.lyricOffset))
                        .font(.caption)
                        .monospacedDigit()
                        .frame(width: 45)
                        .onTapGesture { player.resetOffset() }
                        .help("点击归零")

                    Button { player.adjustOffset(0.5) } label: {
                        Image(systemName: "plus.circle")
                            .font(.caption)
                    }
                    .buttonStyle(.plain)
                    .help("+0.5s")
                }
            }

            // Lyrics list (scrollable)
            if !player.lines.isEmpty {
                Divider()

                ScrollViewReader { proxy in
                    ScrollView {
                        LazyVStack(spacing: 6) {
                            ForEach(Array(player.lines.enumerated()), id: \.element.id) { index, line in
                                Text(line.text)
                                    .font(index == player.currentIndex ? .body.bold() : .caption)
                                    .foregroundColor(index == player.currentIndex ? .primary : .secondary)
                                    .frame(maxWidth: .infinity, alignment: .center)
                                    .padding(.vertical, 2)
                                    .id(line.id)
                                    .onTapGesture {
                                        player.seek(to: line.time)
                                    }
                            }
                        }
                        .padding(.horizontal, 4)
                    }
                    .frame(height: 200)
                    .onChange(of: player.currentIndex) { _, newIndex in
                        if newIndex >= 0 && newIndex < player.lines.count {
                            withAnimation {
                                proxy.scrollTo(player.lines[newIndex].id, anchor: .center)
                            }
                        }
                    }
                }
            }
        }
        .padding()
        .frame(width: 320)
        .onDrop(of: [.fileURL], isTargeted: nil) { providers in
            for provider in providers {
                _ = provider.loadObject(ofClass: URL.self) { url, _ in
                    guard let url = url else { return }
                    Task { @MainActor in
                        let ext = url.pathExtension.lowercased()
                        if ext == "lrc" {
                            player.loadLRC(url: url)
                        } else if ["mp3", "wav", "aiff", "aac", "m4a", "flac", "ogg", "wma"].contains(ext) {
                            player.loadAudio(url: url)
                        }
                    }
                }
            }
            return true
        }
    }

    // MARK: - File Pickers

    private func openAudioFile() {
        let panel = NSOpenPanel()
        panel.allowedContentTypes = [.audio, .mp3, .wav, .aiff]
        panel.allowsMultipleSelection = false
        panel.canChooseDirectories = false

        if panel.runModal() == .OK, let url = panel.url {
            player.loadAudio(url: url)
        }
    }

    private func openLRCFile() {
        let panel = NSOpenPanel()
        panel.allowsMultipleSelection = false
        panel.canChooseDirectories = false

        if panel.runModal() == .OK, let url = panel.url {
            player.loadLRC(url: url)
        }
    }
}
