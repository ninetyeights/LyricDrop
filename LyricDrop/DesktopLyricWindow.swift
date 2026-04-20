//
//  DesktopLyricWindow.swift
//  LyricDrop
//
//  Floating desktop lyric overlay
//

import SwiftUI
import Combine
import UniformTypeIdentifiers

// MARK: - Desktop Lyric Panel (NSPanel)

class DesktopLyricPanel: NSPanel {
    private var lockCancellable: AnyCancellable?
    private var sizeCancellable: AnyCancellable?
    private var unlockPanel: NSPanel?
    private var localMonitor: Any?
    private var globalMonitor: Any?
    private weak var player: LyricPlayer?

    init(player: LyricPlayer) {
        let h = Self.idealHeight(fontSize: player.lyricFontSize, lineCount: player.lyricLineCount, smoothScroll: player.lyricSmoothScroll)
        let screenW = NSScreen.main?.frame.width ?? 800
        let w = screenW * player.lyricWidthRatio
        super.init(
            contentRect: NSRect(x: 0, y: 0, width: w, height: h),
            styleMask: [.nonactivatingPanel, .borderless],
            backing: .buffered,
            defer: false
        )

        self.player = player
        isFloatingPanel = true
        level = .floating
        isOpaque = false
        backgroundColor = .clear
        hasShadow = false
        isMovableByWindowBackground = true
        collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]

        let hostingView = NSHostingView(rootView: DesktopLyricView(player: player))
        hostingView.frame = contentView!.bounds
        hostingView.autoresizingMask = [.width, .height]
        contentView?.addSubview(hostingView)

        // Restore saved position or default to center-bottom
        let ud = UserDefaults.standard
        if ud.object(forKey: "lyricWindowX") != nil {
            let x = ud.double(forKey: "lyricWindowX")
            let y = ud.double(forKey: "lyricWindowY")
            setFrameOrigin(NSPoint(x: x, y: y))
        } else if let screen = NSScreen.main {
            let x = (screen.frame.width - frame.width) / 2
            let y = screen.frame.height * 0.15
            setFrameOrigin(NSPoint(x: x, y: y))
        }

        setupUnlockPanel(player: player)
        setupMouseMonitors()

        // Save position when window moves
        NotificationCenter.default.addObserver(
            forName: NSWindow.didMoveNotification,
            object: self,
            queue: .main
        ) { [weak self] _ in
            guard let origin = self?.frame.origin else { return }
            ud.set(origin.x, forKey: "lyricWindowX")
            ud.set(origin.y, forKey: "lyricWindowY")
        }

        applyLock(player.desktopLyricLocked)
        lockCancellable = player.$desktopLyricLocked
            .receive(on: RunLoop.main)
            .sink { [weak self] locked in
                self?.applyLock(locked)
            }

        // Resize when font/line/scroll/width settings change
        sizeCancellable = Publishers.CombineLatest4(player.$lyricFontSize, player.$lyricLineCount, player.$lyricSmoothScroll, player.$lyricWidthRatio)
            .combineLatest(player.$lyricScrollMode)
            .receive(on: RunLoop.main)
            .sink { [weak self] combo, scrollMode in
                let (fontSize, lineCount, smoothScroll, widthRatio) = combo
                self?.resizeToFit(fontSize: fontSize, lineCount: lineCount, smoothScroll: smoothScroll && scrollMode != .marquee, widthRatio: widthRatio)
            }
    }

    private static func idealHeight(fontSize: CGFloat, lineCount: Int, smoothScroll: Bool) -> CGFloat {
        if smoothScroll && lineCount >= 3 {
            let mainLine = fontSize * 1.4
            let sideLines = fontSize * 0.65 * 1.3 * CGFloat(lineCount - 1)
            return mainLine + sideLines + 32
        }
        let mainLine = fontSize * 1.4
        return mainLine + 24
    }

    private func resizeToFit(fontSize: CGFloat, lineCount: Int, smoothScroll: Bool, widthRatio: CGFloat = 0.45) {
        let newH = Self.idealHeight(fontSize: fontSize, lineCount: lineCount, smoothScroll: smoothScroll)
        let screenW = NSScreen.main?.frame.width ?? 800
        let newW = screenW * widthRatio
        let centerX = (screenW - newW) / 2
        setFrame(NSRect(x: centerX, y: frame.origin.y, width: newW, height: newH), display: true)
    }

    private func setupUnlockPanel(player: LyricPlayer) {
        let panel = NSPanel(
            contentRect: NSRect(x: 0, y: 0, width: 28, height: 28),
            styleMask: [.nonactivatingPanel, .borderless],
            backing: .buffered,
            defer: false
        )
        panel.isFloatingPanel = true
        panel.level = .floating + 1
        panel.isOpaque = false
        panel.backgroundColor = .clear
        panel.hasShadow = false
        panel.collectionBehavior = [.canJoinAllSpaces, .fullScreenAuxiliary]

        let view = NSHostingView(rootView:
            Button {
                player.desktopLyricLocked = false
                player.saveLyricSettings()
            } label: {
                Image(systemName: "lock.fill")
                    .font(.system(size: 12))
                    .foregroundColor(.white.opacity(0.9))
                    .padding(6)
                    .background(.black.opacity(0.5), in: Circle())
            }
            .buttonStyle(.plain)
        )
        panel.contentView = view
        unlockPanel = panel
    }

    private func setupMouseMonitors() {
        let handler: (NSEvent) -> Void = { [weak self] _ in
            self?.updateUnlockVisibility()
        }
        localMonitor = NSEvent.addLocalMonitorForEvents(matching: .mouseMoved) { event in
            handler(event)
            return event
        }
        globalMonitor = NSEvent.addGlobalMonitorForEvents(matching: .mouseMoved, handler: handler)
    }

    private func updateUnlockVisibility() {
        guard let player = player, player.desktopLyricLocked, isVisible else {
            unlockPanel?.orderOut(nil)
            return
        }

        let mouse = NSEvent.mouseLocation
        let overUnlock = unlockPanel?.frame.contains(mouse) ?? false
        if frame.contains(mouse) || overUnlock {
            unlockPanel?.setFrameOrigin(NSPoint(
                x: frame.maxX - 32,
                y: frame.maxY - 32
            ))
            unlockPanel?.orderFront(nil)
        } else {
            unlockPanel?.orderOut(nil)
        }
    }

    private func applyLock(_ locked: Bool) {
        ignoresMouseEvents = locked
        isMovableByWindowBackground = !locked
        if !locked {
            unlockPanel?.orderOut(nil)
        }
    }

    func cleanup() {
        if let m = localMonitor { NSEvent.removeMonitor(m) }
        if let m = globalMonitor { NSEvent.removeMonitor(m) }
        unlockPanel?.orderOut(nil)
        localMonitor = nil
        globalMonitor = nil
    }

    deinit {
        cleanup()
    }
}

// MARK: - Desktop Lyric SwiftUI View

struct DesktopLyricView: View {
    @ObservedObject var player: LyricPlayer
    @Environment(\.colorScheme) private var colorScheme

    @State private var isHovering = false

    private var dimColor: Color {
        colorScheme == .dark ? .white.opacity(0.3) : .black.opacity(0.3)
    }

    /// Text outline for readability against any background
    private func textOutline(_ content: some View) -> some View {
        content
            .shadow(color: .black.opacity(0.5), radius: 0.5, x: 1, y: 1)
            .shadow(color: .black.opacity(0.5), radius: 0.5, x: -1, y: -1)
            .shadow(color: .black.opacity(0.3), radius: 2, x: 0, y: 0)
    }

    var body: some View {
        ZStack {
            // Background
            if player.lyricBgAlways || (isHovering && !player.desktopLyricLocked) {
                RoundedRectangle(cornerRadius: 12)
                    .fill(.ultraThinMaterial)
            }

            // Lyrics
            textOutline(Group {
                if player.currentLine.isEmpty {
                    Text("♪ LyricDrop")
                        .font(player.lyricFont(size: player.lyricFontSize))
                        .foregroundStyle(.gray.opacity(0.5))
                } else if player.lyricScrollMode == .marquee {
                    marqueeLyric
                } else if player.lyricSmoothScroll && player.lyricLineCount >= 3 {
                    smoothScrollLyric
                } else {
                    VStack(spacing: 4) {
                        if player.lyricScrollMode == .vertical {
                            verticalLyric
                        } else {
                            karaokeLyric(text: player.currentLine, progress: player.lineProgress, fontSize: player.lyricFontSize)
                        }

                        if player.lyricLineCount >= 2 && !player.nextLine.isEmpty {
                            Text(player.nextLine)
                                .font(player.lyricFont(size: player.lyricFontSize * 0.7, weight: .medium))
                                .foregroundColor(dimColor)
                                .shadow(color: player.lyricShadowEnabled ? .black.opacity(0.4) : .clear, radius: 3)
                        }
                    }
                }
            })

            // Toolbar on hover (unlocked)
            if isHovering && !player.desktopLyricLocked {
                VStack {
                    HStack(spacing: 6) {
                        Spacer()

                        // Settings button
                        Button {
                            LyricSettingsWindow.show(player: player)
                        } label: {
                            Image(systemName: "gearshape.fill")
                                .font(.system(size: 12))
                                .foregroundColor(.white.opacity(0.8))
                                .padding(6)
                                .background(.black.opacity(0.4), in: Circle())
                        }
                        .buttonStyle(.plain)

                        // Lock button
                        Button {
                            player.desktopLyricLocked = true
                            player.saveLyricSettings()
                        } label: {
                            Image(systemName: "lock.open.fill")
                                .font(.system(size: 12))
                                .foregroundColor(.white.opacity(0.8))
                                .padding(6)
                                .background(.black.opacity(0.4), in: Circle())
                        }
                        .buttonStyle(.plain)
                    }
                    Spacer()
                }
                .padding(4)
            }
        }
        .frame(maxWidth: .infinity, maxHeight: .infinity)
        .onHover { hovering in
            withAnimation(.easeInOut(duration: 0.2)) {
                isHovering = hovering
            }
        }
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

    // MARK: - Smooth multi-line scroll

    private var smoothScrollLyric: some View {
        let range = player.lyricLineCount / 2
        let nearby = player.nearbyLines(range: range)
        let shadowColor = player.lyricShadowEnabled ? Color.black.opacity(0.5) : .clear

        return Group {
            if player.lyricScrollMode == .vertical {
                // Vertical mode: direct color, instant switch
                scrollLinesDirect(nearby: nearby, shadowColor: shadowColor)
            } else {
                // Karaoke mode: overlay with width clipping
                scrollLinesKaraoke(nearby: nearby, shadowColor: shadowColor)
            }
        }
    }

    // For karaoke sweep: same overlay approach + horizontal clipping
    private func scrollLinesKaraoke(nearby: [(index: Int, line: LyricLine)], shadowColor: Color) -> some View {
        let smallSize = player.lyricFontSize * 0.65
        let gradient = LinearGradient(colors: player.lyricTheme.colors, startPoint: .leading, endPoint: .trailing)
        let anim = Animation.spring(duration: 0.6, bounce: 0.1)

        return VStack(spacing: 8) {
            ForEach(nearby, id: \.line.id) { item in
                let isCurrent = item.index == player.currentIndex
                let fontSize = isCurrent ? player.lyricFontSize : smallSize
                let font = player.lyricNSFont(size: fontSize)
                let textWidth = ceil((item.line.text as NSString).size(withAttributes: [.font: font]).width)
                let sungWidth = isCurrent ? textWidth * player.lineProgress : 0

                Text(item.line.text)
                    .font(player.lyricFont(size: fontSize))
                    .foregroundColor(dimColor)
                    .overlay(alignment: .leading) {
                        Text(item.line.text)
                            .font(player.lyricFont(size: fontSize))
                            .foregroundStyle(gradient)
                            .fixedSize()
                            .animation(anim, value: player.currentIndex) // font size: spring
                            .frame(width: max(0, isCurrent ? sungWidth : textWidth), alignment: .leading)
                            .clipped()
                            .opacity(isCurrent ? 1 : 0)
                            .animation(nil, value: player.currentIndex) // width + opacity: instant
                    }
                    .fixedSize()
                    .shadow(color: shadowColor, radius: 3)
                    .scaleEffect(isCurrent ? 1.0 : 0.85)
                    .animation(anim, value: player.currentIndex)
                    .transition(.asymmetric(
                        insertion: .opacity.combined(with: .move(edge: .bottom)),
                        removal: .opacity.combined(with: .move(edge: .top))
                    ))
            }
        }
        .animation(anim, value: player.currentIndex)
    }

    // For vertical scroll: overlay with instant opacity toggle
    private func scrollLinesDirect(nearby: [(index: Int, line: LyricLine)], shadowColor: Color) -> some View {
        let smallSize = player.lyricFontSize * 0.65
        let gradient = LinearGradient(colors: player.lyricTheme.colors, startPoint: .leading, endPoint: .trailing)
        let anim = Animation.spring(duration: 0.6, bounce: 0.1)

        return VStack(spacing: 8) {
            ForEach(nearby, id: \.line.id) { item in
                let isCurrent = item.index == player.currentIndex
                let fontSize = isCurrent ? player.lyricFontSize : smallSize

                Text(item.line.text)
                    .font(player.lyricFont(size: fontSize))
                    .foregroundColor(dimColor)
                    .overlay {
                        Text(item.line.text)
                            .font(player.lyricFont(size: fontSize))
                            .foregroundStyle(gradient)
                            .fixedSize()
                            .animation(anim, value: player.currentIndex) // font size: spring
                            .opacity(isCurrent ? 1 : 0)
                            .animation(nil, value: player.currentIndex) // opacity: instant
                    }
                    .fixedSize()
                    .shadow(color: shadowColor, radius: 3)
                    .scaleEffect(isCurrent ? 1.0 : 0.85)
                    .animation(anim, value: player.currentIndex)
                    .transition(.asymmetric(
                        insertion: .opacity.combined(with: .move(edge: .bottom)),
                        removal: .opacity.combined(with: .move(edge: .top))
                    ))
            }
        }
        .animation(anim, value: player.currentIndex)
    }

    // MARK: - Vertical scroll (line by line, no karaoke sweep)

    private var verticalLyric: some View {
        let gradient = LinearGradient(colors: player.lyricTheme.colors, startPoint: .leading, endPoint: .trailing)
        return Text(player.currentLine)
            .font(player.lyricFont(size: player.lyricFontSize))
            .foregroundStyle(gradient)
            .shadow(color: player.lyricShadowEnabled ? .black.opacity(0.6) : .clear, radius: 4, x: 0, y: 2)
            .transition(.push(from: .bottom))
            .animation(.easeInOut(duration: 0.3), value: player.currentLine)
            .id(player.currentLine)
    }

    // MARK: - Horizontal marquee scroll

    /// Clamped-gap layout: time-proportional gaps, but clamped to [minGap, maxGap]
    private func marqueeClampedLayout() -> (positions: [CGFloat], widths: [CGFloat], stripWidth: CGFloat) {
        let font = player.lyricNSFont(size: player.lyricFontSize)
        let minGap: CGFloat = 40
        let maxGap: CGFloat = 120
        let dur = player.duration

        let widths: [CGFloat] = player.lines.map {
            ceil(($0.text as NSString).size(withAttributes: [.font: font]).width)
        }
        guard !widths.isEmpty else { return ([], [], 0) }

        // First pass: calculate time-proportional gaps, then clamp
        // We need a reference: how many pixels per second?
        // Use total text width + average gap to estimate strip, then derive scale
        let avgGap = (minGap + maxGap) / 2
        let estStrip = widths.reduce(0, +) + avgGap * CGFloat(max(0, widths.count - 1))
        let scale = dur > 0 ? estStrip / dur : 1

        var positions: [CGFloat] = []
        var x: CGFloat = 0
        for (i, w) in widths.enumerated() {
            if i > 0 {
                let timeDelta = player.lines[i].time - player.lines[i - 1].time
                let naturalGap = timeDelta * scale - widths[i - 1]
                let gap = min(maxGap, max(minGap, naturalGap))
                x += gap
            }
            positions.append(x)
            x += w
        }
        return (positions, widths, x)
    }

    private var marqueeLyric: some View {
        let gradient = LinearGradient(colors: player.lyricTheme.colors, startPoint: .leading, endPoint: .trailing)
        let shadowColor = player.lyricShadowEnabled ? Color.black.opacity(0.6) : .clear
        let idx = player.currentIndex
        let time = player.currentTime
        let dur = player.duration

        let layout = marqueeClampedLayout()

        return GeometryReader { geo in
            let containerWidth = geo.size.width
            let centerX = containerWidth / 2

            // Smoothly scroll: interpolate from current line center to next line center
            let progress = player.lineProgress
            let scrollOffset: CGFloat = {
                guard idx >= 0, idx < layout.positions.count else { return containerWidth }
                let curCenter = layout.positions[idx] + layout.widths[idx] / 2
                if idx + 1 < layout.positions.count {
                    let nextCenter = layout.positions[idx + 1] + layout.widths[idx + 1] / 2
                    let target = curCenter + (nextCenter - curCenter) * progress
                    return centerX - target
                }
                return centerX - curCenter
            }()

            ZStack(alignment: .leading) {
                ForEach(Array(player.lines.enumerated()), id: \.element.id) { i, line in
                    let isCurrent = i == idx

                    Text(line.text)
                        .font(player.lyricFont(size: player.lyricFontSize))
                        .foregroundStyle(isCurrent ? AnyShapeStyle(gradient) : AnyShapeStyle(dimColor))
                        .shadow(color: isCurrent ? shadowColor : .clear, radius: 4, x: 0, y: 2)
                        .fixedSize()
                        .offset(x: scrollOffset + layout.positions[i])
                }
            }
            .frame(width: containerWidth, alignment: .leading)
            .clipped()
            .frame(maxHeight: .infinity, alignment: .center)
            .animation(.linear(duration: 0.15), value: scrollOffset)
        }
    }

    // MARK: - Karaoke sweep (horizontal)

    private func karaokeLyric(text: String, progress: Double, fontSize: CGFloat) -> some View {
        let font = player.lyricNSFont(size: fontSize)
        let textWidth = ceil((text as NSString).size(withAttributes: [.font: font]).width)
        let sungWidth = textWidth * progress
        let gradient = LinearGradient(colors: player.lyricTheme.colors, startPoint: .leading, endPoint: .trailing)

        return Text(text)
            .font(player.lyricFont(size: fontSize))
            .foregroundColor(dimColor)
            .overlay(alignment: .leading) {
                Text(text)
                    .font(player.lyricFont(size: fontSize))
                    .foregroundStyle(gradient)
                    .fixedSize()
                    .frame(width: sungWidth, alignment: .leading)
                    .clipped()
            }
            .shadow(color: player.lyricShadowEnabled ? .black.opacity(0.6) : .clear, radius: 4, x: 0, y: 2)
            .fixedSize()
    }
}
