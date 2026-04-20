//
//  LyricPlayer.swift
//  LyricDrop
//
//  Core logic: LRC parser + audio player + lyric sync
//

import Foundation
import AVFoundation
import Combine
import SwiftUI
import ServiceManagement
import MediaPlayer

// MARK: - LRC Line Model

struct LyricLine: Identifiable {
    let id = UUID()
    let time: TimeInterval  // seconds
    let text: String
}

// MARK: - Lyric Theme

enum LyricTheme: String, CaseIterable, Identifiable {
    case neon
    case sunset
    case ocean
    case forest
    case pureWhite
    case sakura

    var id: String { rawValue }

    var label: String {
        switch self {
        case .neon: "霓虹"
        case .sunset: "日落"
        case .ocean: "海洋"
        case .forest: "森林"
        case .pureWhite: "纯白"
        case .sakura: "樱花"
        }
    }

    var colors: [Color] {
        switch self {
        case .neon: [.purple, .pink, .cyan]
        case .sunset: [.yellow, .orange, .red]
        case .ocean: [.cyan, .blue, .indigo]
        case .forest: [.green, .mint, .teal]
        case .pureWhite: [.white, .white]
        case .sakura: [.pink, .white, .pink]
        }
    }
}

// MARK: - Lyric Scroll Mode

enum LyricScrollMode: Int, CaseIterable, Identifiable {
    case karaoke = 0      // 水平扫过
    case vertical = 1     // 逐行滚动
    case marquee = 2      // 水平滚动（跑马灯）

    var id: Int { rawValue }

    var label: String {
        switch self {
        case .karaoke: "水平扫过"
        case .vertical: "逐行滚动"
        case .marquee: "水平滚动"
        }
    }
}

// MARK: - LyricPlayer

@MainActor
class LyricPlayer: ObservableObject {
    // Current lyric line (full text)
    @Published var currentLine: String = ""
    // All parsed lyric lines
    @Published var lines: [LyricLine] = []
    // Current line index
    @Published var currentIndex: Int = -1
    // Playback state
    @Published var isPlaying: Bool = false
    // Current playback time
    @Published var currentTime: TimeInterval = 0
    // Total duration
    @Published var duration: TimeInterval = 0
    // Loaded file names
    @Published var audioFileName: String = ""
    @Published var lrcFileName: String = ""
    // Loop playback
    @Published var isLooping: Bool = false
    // Playback speed
    @Published var playbackRate: Float = 1.0 {
        didSet {
            audioPlayer?.rate = playbackRate
            Self.ud.set(playbackRate, forKey: "playbackRate")
        }
    }
    // Volume
    @Published var volume: Float = 1.0 {
        didSet {
            audioPlayer?.volume = isMuted ? 0 : volume
            Self.ud.set(volume, forKey: "volume")
        }
    }
    @Published var isMuted: Bool = false {
        didSet {
            audioPlayer?.volume = isMuted ? 0 : volume
            Self.ud.set(isMuted, forKey: "isMuted")
        }
    }
    // Karaoke mode
    @Published var karaokeEnabled: Bool = false
    // Desktop floating lyric
    @Published var showDesktopLyric: Bool = false
    @Published var desktopLyricLocked: Bool = false
    // Desktop lyric settings
    @Published var lyricFontSize: CGFloat = 28
    @Published var lyricLineCount: Int = 3
    @Published var lyricTheme: LyricTheme = .neon
    @Published var lyricShadowEnabled: Bool = true
    @Published var lyricBgAlways: Bool = false
    @Published var lyricScrollMode: LyricScrollMode = .karaoke
    @Published var lyricSmoothScroll: Bool = true
    // Lyric time offset (seconds, positive = lyrics earlier)
    @Published var lyricOffset: TimeInterval = 0
    // Custom font family name (empty = system default)
    @Published var lyricFontName: String = ""
    // Desktop lyric width ratio (0.3 ~ 0.8)
    @Published var lyricWidthRatio: CGFloat = 0.45
    // Launch at login
    @Published var launchAtLogin: Bool = false
    // Menu bar display
    @Published var menuBarText: String = "♪ LyricDrop"
    @Published var menuBarImage: NSImage?

    private var audioPlayer: AVAudioPlayer?
    private var timer: Timer?
    private var audioURL: URL?
    private var lrcURL: URL?

    private static let audioPathKey = "lastAudioPath"
    private static let lrcPathKey = "lastLRCPath"
    private static let karaokeKey = "karaokeEnabled"
    private static let ud = UserDefaults.standard

    var nextLine: String {
        guard currentIndex >= 0, currentIndex + 1 < lines.count else { return "" }
        return lines[currentIndex + 1].text
    }

    /// Returns lines around currentIndex with stable identity
    func nearbyLines(range: Int = 2) -> [(index: Int, line: LyricLine)] {
        guard currentIndex >= 0 else { return [] }
        var result: [(Int, LyricLine)] = []
        let start = max(0, currentIndex - range)
        let end = min(lines.count - 1, currentIndex + range)
        for i in start...end {
            result.append((i, lines[i]))
        }
        return result
    }

    init() {
        let ud = Self.ud
        karaokeEnabled = ud.bool(forKey: Self.karaokeKey)
        showDesktopLyric = ud.bool(forKey: "showDesktopLyric")
        desktopLyricLocked = ud.bool(forKey: "desktopLyricLocked")
        if ud.object(forKey: "lyricFontSize") != nil {
            lyricFontSize = ud.double(forKey: "lyricFontSize")
        }
        let savedLineCount = ud.integer(forKey: "lyricLineCount")
        lyricLineCount = [1, 3, 5, 7].contains(savedLineCount) ? savedLineCount : 3
        if let t = ud.string(forKey: "lyricTheme"), let theme = LyricTheme(rawValue: t) {
            lyricTheme = theme
        }
        lyricShadowEnabled = ud.object(forKey: "lyricShadow") == nil ? true : ud.bool(forKey: "lyricShadow")
        lyricBgAlways = ud.bool(forKey: "lyricBgAlways")
        if let mode = LyricScrollMode(rawValue: ud.integer(forKey: "lyricScrollMode")) {
            lyricScrollMode = mode
        }
        lyricSmoothScroll = ud.bool(forKey: "lyricSmoothScroll")
        isLooping = ud.bool(forKey: "isLooping")
        if ud.object(forKey: "volume") != nil {
            volume = ud.float(forKey: "volume")
        }
        isMuted = ud.bool(forKey: "isMuted")
        lyricOffset = ud.double(forKey: "lyricOffset")
        if ud.object(forKey: "playbackRate") != nil {
            playbackRate = ud.float(forKey: "playbackRate")
        }
        lyricFontName = ud.string(forKey: "lyricFontName") ?? ""
        if ud.object(forKey: "lyricWidthRatio") != nil {
            lyricWidthRatio = ud.double(forKey: "lyricWidthRatio")
        }
        launchAtLogin = ud.bool(forKey: "launchAtLogin")
        setupRemoteCommands()
        restoreLastFiles()
    }

    // MARK: - Media Key Support

    private func setupRemoteCommands() {
        let center = MPRemoteCommandCenter.shared()

        center.playCommand.addTarget { [weak self] _ in
            Task { @MainActor in self?.playPause() }
            return .success
        }
        center.pauseCommand.addTarget { [weak self] _ in
            Task { @MainActor in self?.playPause() }
            return .success
        }
        center.togglePlayPauseCommand.addTarget { [weak self] _ in
            Task { @MainActor in self?.playPause() }
            return .success
        }
        center.changePlaybackPositionCommand.addTarget { [weak self] event in
            guard let event = event as? MPChangePlaybackPositionCommandEvent else { return .commandFailed }
            Task { @MainActor in self?.seek(to: event.positionTime) }
            return .success
        }
        center.skipForwardCommand.preferredIntervals = [5]
        center.skipForwardCommand.addTarget { [weak self] _ in
            Task { @MainActor in
                guard let self = self else { return }
                self.seek(to: min(self.duration, self.currentTime + 5))
            }
            return .success
        }
        center.skipBackwardCommand.preferredIntervals = [5]
        center.skipBackwardCommand.addTarget { [weak self] _ in
            Task { @MainActor in
                guard let self = self else { return }
                self.seek(to: max(0, self.currentTime - 5))
            }
            return .success
        }
    }

    func updateNowPlayingInfo() {
        var info: [String: Any] = [
            MPNowPlayingInfoPropertyElapsedPlaybackTime: currentTime,
            MPNowPlayingInfoPropertyPlaybackRate: isPlaying ? Double(playbackRate) : 0.0,
            MPMediaItemPropertyPlaybackDuration: duration,
        ]
        if !audioFileName.isEmpty {
            info[MPMediaItemPropertyTitle] = audioFileName
        }
        MPNowPlayingInfoCenter.default().nowPlayingInfo = info
    }

    func adjustOffset(_ delta: TimeInterval) {
        lyricOffset += delta
        Self.ud.set(lyricOffset, forKey: "lyricOffset")
        updateLyric()
        refreshDisplay()
    }

    func resetOffset() {
        lyricOffset = 0
        Self.ud.set(lyricOffset, forKey: "lyricOffset")
        updateLyric()
        refreshDisplay()
    }

    func saveLyricSettings() {
        let ud = Self.ud
        ud.set(lyricFontSize, forKey: "lyricFontSize")
        ud.set(lyricLineCount, forKey: "lyricLineCount")
        ud.set(lyricTheme.rawValue, forKey: "lyricTheme")
        ud.set(lyricShadowEnabled, forKey: "lyricShadow")
        ud.set(lyricBgAlways, forKey: "lyricBgAlways")
        ud.set(lyricScrollMode.rawValue, forKey: "lyricScrollMode")
        ud.set(lyricSmoothScroll, forKey: "lyricSmoothScroll")
        ud.set(showDesktopLyric, forKey: "showDesktopLyric")
        ud.set(desktopLyricLocked, forKey: "desktopLyricLocked")
        ud.set(karaokeEnabled, forKey: Self.karaokeKey)
        ud.set(isLooping, forKey: "isLooping")
        ud.set(volume, forKey: "volume")
        ud.set(isMuted, forKey: "isMuted")
        ud.set(lyricFontName, forKey: "lyricFontName")
        ud.set(lyricWidthRatio, forKey: "lyricWidthRatio")
        ud.set(launchAtLogin, forKey: "launchAtLogin")
    }

    func toggleLaunchAtLogin() {
        launchAtLogin.toggle()
        saveLyricSettings()
        do {
            if launchAtLogin {
                try SMAppService.mainApp.register()
            } else {
                try SMAppService.mainApp.unregister()
            }
        } catch {
            print("Launch at login error: \(error)")
        }
    }

    // MARK: - Load LRC File

    func loadLRC(url: URL) {
        let accessing = url.startAccessingSecurityScopedResource()
        defer {
            if accessing {
                url.stopAccessingSecurityScopedResource()
            }
        }

        do {
            let data = try Data(contentsOf: url)

            let text = String(data: data, encoding: .utf16)
                ?? String(data: data, encoding: .utf16LittleEndian)
                ?? String(data: data, encoding: .utf8)
                ?? String(data: data, encoding: .ascii)
                ?? String(data: data, encoding: .isoLatin1)

            guard let content = text else { return }

            lrcURL = url
            lrcFileName = url.lastPathComponent
            lines = parseLRC(content)
            currentIndex = -1
            currentLine = ""
            UserDefaults.standard.set(url.path, forKey: Self.lrcPathKey)
            refreshDisplay()
        } catch {
            print("Read error: \(error)")
        }
    }

    // MARK: - Parse LRC Format

    private func parseLRC(_ text: String) -> [LyricLine] {
        var result: [LyricLine] = []
        let pattern = #"\[(\d{2}):(\d{2})(?:\.(\d{2,3}))?\](.*)"#
        let regex = try? NSRegularExpression(pattern: pattern)

        for line in text.components(separatedBy: .newlines) {
            let range = NSRange(line.startIndex..., in: line)
            guard let match = regex?.firstMatch(in: line, range: range) else { continue }

            let min = Double((line as NSString).substring(with: match.range(at: 1))) ?? 0
            let sec = Double((line as NSString).substring(with: match.range(at: 2))) ?? 0

            var ms: Double = 0
            if match.range(at: 3).location != NSNotFound {
                let msStr = (line as NSString).substring(with: match.range(at: 3))
                ms = (Double(msStr) ?? 0) / (msStr.count == 2 ? 100 : 1000)
            }

            let time = min * 60 + sec + ms
            let text = (line as NSString).substring(with: match.range(at: 4)).trimmingCharacters(in: .whitespaces)

            if !text.isEmpty {
                result.append(LyricLine(time: time, text: text))
            }
        }

        return result.sorted { $0.time < $1.time }
    }

    // MARK: - Load Audio from URL

    @Published var isLoadingURL: Bool = false

    func loadAudioFromURL(_ urlString: String) {
        guard let url = URL(string: urlString.trimmingCharacters(in: .whitespacesAndNewlines)) else { return }
        isLoadingURL = true
        audioFileName = "Loading..."

        Task {
            let config = URLSessionConfiguration.default
            config.httpAdditionalHeaders = [
                "User-Agent": "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.0 Safari/605.1.15"
            ]
            let session = URLSession(configuration: config)

            // Check if it's an HTML page — parse to find audio/lrc URLs
            let audioURL: URL
            let lrcURL: URL?
            let ext = url.pathExtension.lowercased()

            if ext == "html" || ext == "htm" || ext.isEmpty {
                // Try to parse as a webpage
                if let parsed = await parseWebpageForMedia(url: url, session: session) {
                    audioURL = parsed.audio
                    lrcURL = parsed.lrc
                } else {
                    // Fallback: treat as direct audio URL
                    audioURL = url
                    lrcURL = nil
                }
            } else {
                audioURL = url
                lrcURL = nil
            }

            await downloadAndLoadAudio(audioURL: audioURL, lrcURL: lrcURL, saveURL: url, session: session)
        }
    }

    /// Parse HTML page to extract audio and LRC URLs from embedded JavaScript
    private func parseWebpageForMedia(url: URL, session: URLSession) async -> (audio: URL, lrc: URL?)? {
        guard let (data, _) = try? await session.data(from: url),
              let html = String(data: data, encoding: .utf8) else { return nil }

        // Extract base URIs from gospelHymns.items
        let highURI = extractJSString(from: html, pattern: #""high_uri"\s*:\s*"([^"]+)""#)
        let lrcURI = extractJSString(from: html, pattern: #""lrc_uri"\s*:\s*"([^"]+)""#)

        // Extract filenames from pageHymnPlayer.hymn
        let highFile = extractJSString(from: html, pattern: #""high"\s*:\s*"([^"]+)""#)
        let lrcFile = extractJSString(from: html, pattern: #""lrc"\s*:\s*"([^"]+)""#)

        print("Parsed page - highURI: \(highURI ?? "nil"), highFile: \(highFile ?? "nil"), lrcURI: \(lrcURI ?? "nil"), lrcFile: \(lrcFile ?? "nil")")

        guard let baseURI = highURI, let file = highFile,
              let audioURL = URL(string: baseURI + file) else { return nil }

        var lrcURL: URL? = nil
        if let lrcBase = lrcURI, let lrcName = lrcFile {
            lrcURL = URL(string: lrcBase + lrcName)
        }

        return (audioURL, lrcURL)
    }

    private func extractJSString(from html: String, pattern: String) -> String? {
        guard let regex = try? NSRegularExpression(pattern: pattern),
              let match = regex.firstMatch(in: html, range: NSRange(html.startIndex..., in: html)),
              match.numberOfRanges >= 2 else { return nil }
        let range = match.range(at: 1)
        guard let swiftRange = Range(range, in: html) else { return nil }
        // JSON uses \/ as escaped slash — unescape it
        return String(html[swiftRange]).replacingOccurrences(of: "\\/", with: "/")
    }

    private func downloadAndLoadAudio(audioURL: URL, lrcURL: URL?, saveURL: URL, session: URLSession) async {
        print("Downloading audio: \(audioURL)")
        // Download audio data
        guard let (data, response) = try? await session.data(from: audioURL),
              let httpResponse = response as? HTTPURLResponse,
              httpResponse.statusCode == 200 else {
            print("Audio download failed: \(audioURL)")
            await MainActor.run {
                audioFileName = ""
                isLoadingURL = false
            }
            return
        }

        let ext = audioURL.pathExtension.isEmpty ? "mp3" : audioURL.pathExtension
        let dest = FileManager.default.temporaryDirectory
            .appendingPathComponent(UUID().uuidString)
            .appendingPathExtension(ext)
        try? data.write(to: dest)
        print("Audio saved to: \(dest), size: \(data.count) bytes")

        await MainActor.run {
            do {
                audioPlayer = try AVAudioPlayer(contentsOf: dest)
                audioPlayer?.enableRate = true
                audioPlayer?.rate = playbackRate
                audioPlayer?.prepareToPlay()
                audioPlayer?.volume = isMuted ? 0 : volume
                duration = audioPlayer?.duration ?? 0
                self.audioURL = dest
                audioFileName = audioURL.lastPathComponent
                currentTime = 0
                UserDefaults.standard.set(saveURL.absoluteString, forKey: Self.audioPathKey)
            } catch {
                audioFileName = ""
                isLoadingURL = false
                return
            }
        }

        // Load LRC
        if let lrcURL = lrcURL {
            if let (lrcData, response) = try? await session.data(from: lrcURL),
               let httpResponse = response as? HTTPURLResponse,
               httpResponse.statusCode == 200 {
                let text = String(data: lrcData, encoding: .utf8)
                    ?? String(data: lrcData, encoding: .utf16)
                if let content = text {
                    await MainActor.run {
                        lrcFileName = lrcURL.lastPathComponent
                        lines = parseLRC(content)
                        currentIndex = -1
                        currentLine = ""
                        refreshDisplay()
                    }
                }
            }
        } else {
            await tryLoadRemoteLRC(audioURL: audioURL, session: session)
        }

        await MainActor.run {
            isLoadingURL = false
        }
    }

    private func tryLoadRemoteLRC(audioURL: URL, session: URLSession) async {
        let baseName = audioURL.deletingPathExtension().lastPathComponent

        // Build candidate LRC URLs:
        // 1. Same path, different extension: .../audio/file.lrc
        // 2. Replace /audio/ with /lrc/:  .../lrc/file.lrc
        var candidates: [URL] = []
        candidates.append(audioURL.deletingPathExtension().appendingPathExtension("lrc"))

        let urlString = audioURL.absoluteString
        if urlString.contains("/audio/") {
            let lrcString = urlString
                .replacingOccurrences(of: "/audio/", with: "/lrc/")
                .replacingOccurrences(of: ".\(audioURL.pathExtension)", with: ".lrc")
            if let lrcURL = URL(string: lrcString) {
                candidates.append(lrcURL)
            }
        }

        for lrcURL in candidates {
            guard let (data, response) = try? await session.data(from: lrcURL),
                  let httpResponse = response as? HTTPURLResponse,
                  httpResponse.statusCode == 200 else { continue }

            let text = String(data: data, encoding: .utf8)
                ?? String(data: data, encoding: .utf16)
            guard let content = text else { continue }

            lrcFileName = baseName + ".lrc"
            lines = parseLRC(content)
            currentIndex = -1
            currentLine = ""
            refreshDisplay()
            return
        }
    }

    // MARK: - Load Audio File

    func loadAudio(url: URL) {
        let accessing = url.startAccessingSecurityScopedResource()
        defer {
            if accessing {
                url.stopAccessingSecurityScopedResource()
            }
        }

        do {
            audioPlayer = try AVAudioPlayer(contentsOf: url)
            audioPlayer?.enableRate = true
            audioPlayer?.rate = playbackRate
            audioPlayer?.prepareToPlay()
            audioPlayer?.volume = isMuted ? 0 : volume
            duration = audioPlayer?.duration ?? 0
            audioURL = url
            audioFileName = url.lastPathComponent
            currentTime = 0
            UserDefaults.standard.set(url.path, forKey: Self.audioPathKey)
            updateNowPlayingInfo()
        } catch {
            print("Audio load error: \(error)")
        }

        let lrcURL = url.deletingPathExtension().appendingPathExtension("lrc")
        if FileManager.default.fileExists(atPath: lrcURL.path) {
            loadLRC(url: lrcURL)
        }
    }

    // MARK: - Playback Controls

    func playPause() {
        guard let player = audioPlayer else { return }

        if player.isPlaying {
            player.pause()
            stopTimer()
            isPlaying = false
        } else {
            player.play()
            startTimer()
            isPlaying = true
        }
        updateNowPlayingInfo()
    }

    func stop() {
        audioPlayer?.stop()
        audioPlayer?.currentTime = 0
        stopTimer()
        isPlaying = false
        currentTime = 0
        currentIndex = -1
        currentLine = ""
        refreshDisplay()
        updateNowPlayingInfo()
    }

    func clearAll() {
        stop()
        audioPlayer = nil
        audioURL = nil
        lrcURL = nil
        audioFileName = ""
        lrcFileName = ""
        lines = []
        duration = 0
        Self.ud.removeObject(forKey: Self.audioPathKey)
        Self.ud.removeObject(forKey: Self.lrcPathKey)
        refreshDisplay()
    }

    func seek(to time: TimeInterval) {
        audioPlayer?.currentTime = time
        currentTime = time
        updateLyric()
        refreshDisplay()
        updateNowPlayingInfo()
    }

    func toggleKaraoke() {
        karaokeEnabled.toggle()
        UserDefaults.standard.set(karaokeEnabled, forKey: Self.karaokeKey)
        refreshDisplay()
    }

    // MARK: - Timer for Lyric Sync

    private func startTimer() {
        timer = Timer.scheduledTimer(withTimeInterval: 0.1, repeats: true) { [weak self] _ in
            Task { @MainActor in
                self?.tick()
            }
        }
    }

    private func stopTimer() {
        timer?.invalidate()
        timer = nil
    }

    private func tick() {
        guard let player = audioPlayer else { return }

        currentTime = player.currentTime

        if !player.isPlaying && isPlaying {
            if isLooping {
                player.currentTime = 0
                player.play()
                currentTime = 0
                currentIndex = -1
            } else {
                isPlaying = false
                stopTimer()
            }
            return
        }

        updateLyric()
        refreshDisplay()
    }

    private func updateLyric() {
        guard !lines.isEmpty else { return }

        let adjustedTime = currentTime + lyricOffset
        var newIndex = -1
        for (i, line) in lines.enumerated() {
            if line.time <= adjustedTime {
                newIndex = i
            } else {
                break
            }
        }

        if newIndex != currentIndex {
            currentIndex = newIndex
            currentLine = newIndex >= 0 ? lines[newIndex].text : ""
        }
    }

    // MARK: - Line Progress

    var lineProgress: Double {
        guard currentIndex >= 0, currentIndex < lines.count else { return 0 }
        let adjustedTime = currentTime + lyricOffset
        let lineStart = lines[currentIndex].time
        let lineEnd = (currentIndex + 1 < lines.count) ? lines[currentIndex + 1].time : duration
        let lineDuration = lineEnd - lineStart
        guard lineDuration > 0 else { return 1 }
        return min(1, max(0, (adjustedTime - lineStart) / lineDuration))
    }

    // MARK: - Menu Bar Display

    func refreshDisplay() {
        guard !currentLine.isEmpty else {
            menuBarImage = nil
            menuBarText = "♪ LyricDrop"
            return
        }

        if karaokeEnabled {
            menuBarText = ""
            renderKaraoke()
        } else {
            menuBarImage = nil
            menuBarText = "♪ " + currentLine
        }
    }

    private func renderKaraoke() {
        let progress = lineProgress
        let fullText = "♪ " + currentLine
        let font = NSFont.systemFont(ofSize: 13, weight: .medium)
        let textWidth = ceil((fullText as NSString).size(withAttributes: [.font: font]).width)
        let sungWidth = textWidth * progress

        let sungGradient = LinearGradient(
            colors: lyricTheme.colors,
            startPoint: .leading,
            endPoint: .trailing
        )

        let view = Text(fullText)
            .font(.system(size: 13, weight: .medium))
            .foregroundColor(.gray.opacity(0.5))
            .overlay(alignment: .leading) {
                Text(fullText)
                    .font(.system(size: 13, weight: .medium))
                    .foregroundStyle(sungGradient)
                    .fixedSize()
                    .frame(width: sungWidth, alignment: .leading)
                    .clipped()
            }
            .fixedSize()
            .padding(.horizontal, 4)
            .padding(.vertical, 2)

        let renderer = ImageRenderer(content: view)
        renderer.scale = 2.0
        guard let cgImage = renderer.cgImage else { return }
        let nsImage = NSImage(cgImage: cgImage, size: NSSize(
            width: CGFloat(cgImage.width) / 2.0,
            height: CGFloat(cgImage.height) / 2.0
        ))
        nsImage.isTemplate = false
        menuBarImage = nsImage
    }

    // MARK: - Restore Last Files

    private func restoreLastFiles() {
        if let saved = UserDefaults.standard.string(forKey: Self.audioPathKey) {
            if saved.hasPrefix("http://") || saved.hasPrefix("https://") {
                // Remote URL
                loadAudioFromURL(saved)
            } else if FileManager.default.fileExists(atPath: saved) {
                // Local file
                loadAudio(url: URL(fileURLWithPath: saved))
            }
        } else if let lrcPath = UserDefaults.standard.string(forKey: Self.lrcPathKey) {
            let url = URL(fileURLWithPath: lrcPath)
            if FileManager.default.fileExists(atPath: lrcPath) {
                loadLRC(url: url)
            }
        }
    }

    // MARK: - Font Helper

    func lyricFont(size: CGFloat, weight: Font.Weight = .bold) -> Font {
        if lyricFontName.isEmpty {
            return .system(size: size, weight: weight)
        }
        return .custom(lyricFontName, size: size)
    }

    func lyricNSFont(size: CGFloat, weight: NSFont.Weight = .bold) -> NSFont {
        if lyricFontName.isEmpty {
            return NSFont.systemFont(ofSize: size, weight: weight)
        }
        return NSFont(name: lyricFontName, size: size) ?? NSFont.systemFont(ofSize: size, weight: weight)
    }

    // MARK: - Format Time

    static func formatTime(_ time: TimeInterval) -> String {
        let min = Int(time) / 60
        let sec = Int(time) % 60
        return String(format: "%d:%02d", min, sec)
    }
}
