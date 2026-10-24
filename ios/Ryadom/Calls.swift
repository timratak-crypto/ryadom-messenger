import SwiftUI
import AVFoundation
import AudioToolbox

// Serial, bounded writes keep slow connections from accumulating microphone audio.
final class CallPipe: NSObject, URLSessionWebSocketDelegate {
    private var network: URLSession!
    private var task: URLSessionWebSocketTask?
    private let lock = NSLock()
    private var pending: [(URLSessionWebSocketTask.Message, Bool)] = []
    private var writing = false
    private var stopped = false
    var control: (([String: Any]) -> Void)?
    var audio: ((Data) -> Void)?
    var closed: (() -> Void)?
    init(token: String) {
        super.init()
        network = URLSession(configuration: .ephemeral, delegate: self, delegateQueue: nil)
        var request = URLRequest(url: URL(string: "wss://ryadom-family-chat.onrender.com/api/calls")!, timeoutInterval: 90)
        request.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization")
        task = network.webSocketTask(with: request); task?.maximumMessageSize = 6400
    }
    func start() { task?.resume(); receive() }
    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse, newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) { completionHandler(nil) }
    func urlSession(_ session: URLSession, webSocketTask: URLSessionWebSocketTask, didOpenWithProtocol protocol: String?) {}
    func urlSession(_ session: URLSession, webSocketTask: URLSessionWebSocketTask, didCloseWith closeCode: URLSessionWebSocketTask.CloseCode, reason: Data?) { fail() }
    func receive() {
        task?.receive { [weak self] result in
            guard let self else { return }
            self.lock.lock(); let stop = self.stopped; self.lock.unlock(); if stop { return }
            switch result {
            case .failure: self.fail()
            case .success(let message):
                switch message {
                case .data(let data): if data.count > 0 && data.count <= 6400 && data.count % 2 == 0 { self.audio?(data) }
                case .string(let text): if let data = text.data(using: .utf8), let value = try? JSONSerialization.jsonObject(with: data) as? [String: Any] { self.control?(value) }
                @unknown default: break
                }
                self.receive()
            }
        }
    }
    func send(_ value: [String: String]) {
        guard let data = try? JSONSerialization.data(withJSONObject: value), let text = String(data: data, encoding: .utf8) else { return }
        enqueue(.string(text), audio: false)
    }
    func pcm(_ data: Data) { if !data.isEmpty && data.count <= 6400 && data.count % 2 == 0 { enqueue(.data(data), audio: true) } }
    private func enqueue(_ message: URLSessionWebSocketTask.Message, audio: Bool) {
        lock.lock()
        if stopped || pending.count >= (audio ? 3 : 16) { lock.unlock(); return }
        pending.append((message, audio)); let start = !writing; if start { writing = true }; lock.unlock()
        if start { writeNext() }
    }
    private func writeNext() {
        lock.lock()
        guard !stopped, !pending.isEmpty else { writing = false; lock.unlock(); return }
        let message = pending.removeFirst().0; lock.unlock()
        task?.send(message) { [weak self] error in if error != nil { self?.fail() } else { self?.writeNext() } }
    }
    private func fail() { lock.lock(); let notify = !stopped; lock.unlock(); if notify { close(); closed?() } }
    func close() {
        lock.lock(); if stopped { lock.unlock(); return }; stopped = true; pending = []; lock.unlock()
        task?.cancel(with: .goingAway, reason: nil); network.invalidateAndCancel()
    }
}

final class CallAudioEngine {
    private let engine = AVAudioEngine()
    private let player = AVAudioPlayerNode()
    private let lock = NSLock()
    private let playback = DispatchQueue(label: "app.ryadom.call.playback")
    private var running = false, muted = false, tapped = false
    private var queued = 0
    private let renderFormat = AVAudioFormat(commonFormat: .pcmFormatFloat32, sampleRate: 16000, channels: 1, interleaved: false)!
    func mute(_ value: Bool) { lock.lock(); muted = value; lock.unlock() }
    func start(send: @escaping (Data) -> Void) throws {
        let session = AVAudioSession.sharedInstance()
        try session.setCategory(.playAndRecord, mode: .voiceChat, options: [.allowBluetooth])
        try session.setPreferredIOBufferDuration(0.02); try session.setActive(true)
        do {
            let input = engine.inputNode
            try input.setVoiceProcessingEnabled(true)
            let format = input.outputFormat(forBus: 0)
            guard format.sampleRate > 0, format.channelCount > 0,
                  let pcm = AVAudioFormat(commonFormat: .pcmFormatInt16, sampleRate: 16000, channels: 1, interleaved: true),
                  let converter = AVAudioConverter(from: format, to: pcm) else { throw ChatError("Микрофон недоступен.") }
            engine.attach(player); engine.connect(player, to: engine.mainMixerNode, format: renderFormat)
            lock.lock(); running = true; lock.unlock()
            input.installTap(onBus: 0, bufferSize: 1024, format: format) { [weak self] buffer, _ in
                guard let self else { return }
                self.lock.lock(); let enabled = self.running && !self.muted; self.lock.unlock(); guard enabled else { return }
                let capacity = AVAudioFrameCount(ceil(Double(buffer.frameLength) * 16000 / format.sampleRate) + 32)
                guard let output = AVAudioPCMBuffer(pcmFormat: pcm, frameCapacity: capacity) else { return }
                var supplied = false, error: NSError?
                converter.convert(to: output, error: &error) { _, status in
                    if supplied { status.pointee = .noDataNow; return nil }
                    supplied = true; status.pointee = .haveData; return buffer
                }
                if error == nil, output.frameLength > 0, let pointer = output.int16ChannelData?[0] {
                    let data = Data(bytes: pointer, count: Int(output.frameLength) * 2)
                    // Some audio routes deliver larger taps than requested.
                    for offset in stride(from: 0, to: data.count, by: 1280) { send(data.subdata(in: offset..<min(offset + 1280, data.count))) }
                }
            }
            tapped = true; engine.prepare(); try engine.start(); player.play()
        } catch { stop(); throw error }
    }
    func receive(_ data: Data) {
        guard data.count > 0 && data.count <= 6400 && data.count % 2 == 0 else { return }
        lock.lock(); guard running && queued < 6 else { lock.unlock(); return }; queued += 1; lock.unlock()
        playback.async { [weak self] in
            guard let self else { return }
            self.lock.lock(); let active = self.running; self.lock.unlock()
            guard active, let buffer = AVAudioPCMBuffer(pcmFormat: self.renderFormat, frameCapacity: AVAudioFrameCount(data.count / 2)), let channel = buffer.floatChannelData?[0] else { self.completed(); return }
            buffer.frameLength = buffer.frameCapacity
            data.withUnsafeBytes { bytes in
                let u = bytes.bindMemory(to: UInt8.self)
                for i in 0..<Int(buffer.frameLength) { channel[i] = Float(Int16(bitPattern: UInt16(u[2*i]) | UInt16(u[2*i+1]) << 8)) / 32768 }
            }
            self.player.scheduleBuffer(buffer, completionCallbackType: .dataPlayedBack) { [weak self] _ in self?.completed() }
        }
    }
    private func completed() { lock.lock(); queued = max(0, queued - 1); lock.unlock() }
    func stop() {
        lock.lock(); running = false; lock.unlock()
        if tapped { engine.inputNode.removeTap(onBus: 0); tapped = false }
        playback.sync { player.stop(); engine.stop() }
        try? AVAudioSession.sharedInstance().setActive(false, options: .notifyOthersOnDeactivation)
    }
}

@MainActor final class CallsModel: ObservableObject {
    @Published var phase = ""
    @Published var peer = ""
    @Published var note = ""
    @Published var muted = false
    @Published var speaker = false
    @Published var started = Date()
    var busy: Bool { !phase.isEmpty }
    var canRing: () -> Bool = { true }
    private var token: String?, pipe: CallPipe?, audio: CallAudioEngine?
    private var id = "", ready = false, desired = false
    private var retry: Task<Void, Never>?, permission = UUID(), ring: Timer?
    init() {
        NotificationCenter.default.addObserver(forName: AVAudioSession.interruptionNotification, object: nil, queue: .main) { [weak self] notification in
            if let type = notification.userInfo?[AVAudioSessionInterruptionTypeKey] as? UInt, type == AVAudioSession.InterruptionType.began.rawValue {
                Task { @MainActor in if self?.busy == true { self?.hangup() } }
            }
        }
    }
    func sync(token: String?, active: Bool) {
        if token != self.token || !active { disconnect(); self.token = token }
        desired = active && token != nil
        if desired { connect() }
    }
    private func connect() {
        guard desired, pipe == nil, let token else { return }
        let next = CallPipe(token: token); pipe = next
        next.control = { [weak self, weak next] value in Task { @MainActor in guard let self, self.pipe === next else { return }; self.event(value) } }
        next.closed = { [weak self, weak next] in Task { @MainActor in
            guard let self, self.pipe === next else { return }
            self.end(self.busy ? "Связь прервалась. Повторите звонок." : ""); self.pipe = nil; self.ready = false
            self.retry?.cancel(); self.retry = Task { try? await Task.sleep(nanoseconds: 5_000_000_000); if !Task.isCancelled { self.connect() } }
        } }
        next.start()
    }
    func dial(_ username: String) {
        guard !busy else { return }
        guard ready else { connect(); note = "Подключаем звонки… Повторите через несколько секунд."; return }
        microphone { [weak self] in guard let self, self.ready, !self.busy else { return }; self.peer = username; self.phase = "dialing"; self.note = ""; self.pipe?.send(["type": "call", "username": username]) }
    }
    func accept() {
        let call = id
        microphone { [weak self] in guard let self, self.id == call, self.phase == "incoming" else { return }; self.stopRing(); self.phase = "connecting"; self.pipe?.send(["type": "accept", "id": call]) }
    }
    private func microphone(_ action: @escaping () -> Void) {
        let request = UUID(); permission = request
        AVAudioSession.sharedInstance().requestRecordPermission { [weak self] allowed in Task { @MainActor in
            guard let self, self.desired, self.permission == request else { return }
            if allowed { action() } else { self.note = "Разрешите микрофон в Настройках → Рядом → Микрофон." }
        } }
    }
    func toggleMute() { muted.toggle(); audio?.mute(muted) }
    func toggleSpeaker() { do { let next = !speaker; try AVAudioSession.sharedInstance().overrideOutputAudioPort(next ? .speaker : .none); speaker = next } catch { note = "Не удалось переключить динамик." } }
    private func event(_ value: [String: Any]) {
        let type = value["type"] as? String ?? "", call = value["id"] as? String ?? ""
        if type == "ready" { ready = true; return }
        if type == "incoming" || type == "ringing" {
            if type == "incoming" && (busy || !canRing()) { pipe?.send(["type": "hangup", "id": call]); return }
            id = call; peer = value["username"] as? String ?? ""; phase = type; note = ""
            if type == "incoming" { LocalNotices.shared.call(peer, id: call); AudioServicesPlaySystemSound(1005); ring = Timer.scheduledTimer(withTimeInterval: 3, repeats: true) { _ in AudioServicesPlaySystemSound(1005) } }
            return
        }
        if type == "error" { end(Self.reason(value["reason"] as? String ?? "")); return }
        guard call == id, !id.isEmpty else { return }
        if type == "ended" { end(Self.reason(value["reason"] as? String ?? "")); return }
        if type == "active" {
            stopRing(); phase = "active"; started = Date(); muted = false; speaker = false
            do { let next = CallAudioEngine(); audio = next; pipe?.audio = { [weak next] data in next?.receive(data) }; let target = pipe; try next.start { [weak target] data in target?.pcm(data) } }
            catch { hangup(); note = "Не удалось включить звук. Проверьте микрофон и повторите звонок." }
        }
    }
    static func reason(_ value: String) -> String {
        switch value {
        case "offline": return "Собеседник не в сети. Попросите открыть «Рядом»."
        case "busy": return "Собеседник уже разговаривает."
        case "declined": return "Вызов отклонён."
        case "unanswered": return "Нет ответа."
        case "rate": return "Подождите минуту перед следующим вызовом."
        case "answered_elsewhere": return "Звонок принят на другом устройстве."
        case "finished", "cancelled": return "Звонок завершён."
        case "disconnected": return "Связь прервалась."
        default: return "Не удалось позвонить. Повторите попытку."
        }
    }
    func hangup() { pipe?.send(id.isEmpty ? ["type": "cancel"] : ["type": "hangup", "id": id]); end("Звонок завершён.") }
    private func stopRing() { LocalNotices.shared.removeCall(id); ring?.invalidate(); ring = nil }
    private func end(_ message: String) { stopRing(); pipe?.audio = nil; audio?.stop(); audio = nil; phase = ""; id = ""; permission = UUID(); UIApplication.shared.isIdleTimerDisabled = false; if !message.isEmpty { note = message } }
    private func disconnect() { desired = false; retry?.cancel(); pipe?.close(); pipe = nil; ready = false; end("") }
}

struct CallView: View {
    @ObservedObject var calls: CallsModel
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    @State private var pulse = false
    var body: some View {
        VStack(spacing: 24) {
            Text("ГОЛОСОВОЙ ЗВОНОК").font(.caption2.weight(.semibold)).tracking(2).foregroundStyle(Color.white.opacity(0.65)).padding(.top, 30)
            Spacer()
            ZStack { Circle().stroke(Color.white.opacity(0.10), lineWidth: 1).frame(width: 188, height: 188).scaleEffect(pulse && !reduceMotion ? 1.08 : 1); Circle().fill(Color.white.opacity(0.06)).frame(width: 154, height: 154); AvatarView(name: calls.peer, size: 110) }
            Text("@\(calls.peer)").font(.system(.largeTitle, design: .rounded).weight(.semibold)).foregroundStyle(.white)
            if calls.phase == "active" { Text(calls.started, style: .timer).font(.title2).monospacedDigit().foregroundStyle(Color(rgb: 0xB8DBCD)) }
            else { Text(calls.phase == "incoming" ? "Входящий звонок" : calls.phase == "ringing" ? "Вызываем…" : "Подключаемся…").font(.title3).foregroundStyle(Color(rgb: 0xB8DBCD)) }
            Spacer()
            if calls.phase == "incoming" { Button { calls.accept() } label: { Label("Принять", systemImage: "phone.fill").frame(maxWidth: .infinity) }.buttonStyle(SoftPressStyle()) }
            if calls.phase == "active" {
                HStack(spacing: 14) {
                    Button { calls.toggleMute() } label: { VStack(spacing: 10) { Image(systemName: calls.muted ? "mic.slash" : "mic").font(.title2); Text(calls.muted ? "Микрофон выкл." : "Микрофон вкл.").font(.caption2) }.frame(maxWidth: .infinity).padding(18).background(Color.white.opacity(0.12), in: RoundedRectangle(cornerRadius: 22)) }
                    Button { calls.toggleSpeaker() } label: { VStack(spacing: 10) { Image(systemName: calls.speaker ? "speaker.wave.2.fill" : "speaker").font(.title2); Text(calls.speaker ? "Громкая связь" : "Динамик").font(.caption2) }.frame(maxWidth: .infinity).padding(18).background(Color.white.opacity(0.12), in: RoundedRectangle(cornerRadius: 22)) }
                }.buttonStyle(.plain).foregroundStyle(.white)
            }
            Button { calls.hangup() } label: { Label("Завершить", systemImage: "phone.down.fill").font(.headline).frame(maxWidth: .infinity).padding(18).foregroundStyle(.white).background(Color(rgb: 0xC65B60), in: RoundedRectangle(cornerRadius: 20)) }.buttonStyle(.plain)
            if !calls.note.isEmpty { Text(calls.note).font(.footnote).foregroundStyle(.white) }
            Text("Оставьте приложение открытым во время звонка").font(.caption2).foregroundStyle(Color.white.opacity(0.5)).multilineTextAlignment(.center)
        }.padding(28).background(LinearGradient(colors: [Color(rgb: 0x123D33), Color(rgb: 0x092A23)], startPoint: .topLeading, endPoint: .bottomTrailing).ignoresSafeArea()).tint(pine)
        .onAppear { UIApplication.shared.isIdleTimerDisabled = true; if !reduceMotion { withAnimation(.easeInOut(duration: 1.5).repeatForever(autoreverses: true)) { pulse = true } } }
        .interactiveDismissDisabled()
    }
}

import UserNotifications

// Local alerts for data received while this Sideloadly build is running.
// Remote APNs delivery requires separate Apple provisioning and is not claimed here.
@MainActor final class LocalNotices: NSObject, UNUserNotificationCenterDelegate {
    static let shared = LocalNotices()
    private var account = ""
    private var counts: [String: Int] = [:]
    var open: ((String) -> Void)?
    func configure(account next: String?) {
        let next = next ?? ""
        if account != next { counts = [:]; UNUserNotificationCenter.current().removeAllDeliveredNotifications(); UNUserNotificationCenter.current().removeAllPendingNotificationRequests() }
        account = next
        UNUserNotificationCenter.current().delegate = self
        if !next.isEmpty { UNUserNotificationCenter.current().requestAuthorization(options: [.alert, .sound, .badge]) { _, _ in } }
    }
    func settings() {
        if let url = URL(string: UIApplication.openSettingsURLString) { UIApplication.shared.open(url) }
    }
    func observe(_ chats: [Conversation], visible: String?) {
        guard !account.isEmpty else { return }; var shown = 0
        for chat in chats {
            let previous = counts[chat.id] ?? 0; counts[chat.id] = chat.unread
            let id = "message:" + chat.id
            if chat.unread == 0 || chat.id == visible { UNUserNotificationCenter.current().removeDeliveredNotifications(withIdentifiers: [id]); continue }
            if chat.unread > previous && shown < 5 { shown += 1; show(id: id, title: "@" + chat.username, body: chat.unread == 1 ? "Новое сообщение" : "Новых сообщений: \(chat.unread)", chat: chat.id, sound: true) }
        }
    }
    func call(_ peer: String, id: String) { show(id: "call:" + id, title: "Входящий звонок", body: "@" + peer, chat: "", sound: false) }
    func removeCall(_ id: String) { UNUserNotificationCenter.current().removeDeliveredNotifications(withIdentifiers: ["call:" + id]); UNUserNotificationCenter.current().removePendingNotificationRequests(withIdentifiers: ["call:" + id]) }
    private func show(id: String, title: String, body: String, chat: String, sound: Bool) {
        guard !account.isEmpty else { return }
        let content = UNMutableNotificationContent(); content.title = title; content.body = body
        if sound { content.sound = .default }; content.userInfo = ["account": account, "chat": chat]
        UNUserNotificationCenter.current().add(UNNotificationRequest(identifier: id, content: content, trigger: nil))
    }
    nonisolated func userNotificationCenter(_ center: UNUserNotificationCenter, willPresent notification: UNNotification, withCompletionHandler completionHandler: @escaping (UNNotificationPresentationOptions) -> Void) { completionHandler([.banner, .sound]) }
    nonisolated func userNotificationCenter(_ center: UNUserNotificationCenter, didReceive response: UNNotificationResponse, withCompletionHandler completionHandler: @escaping () -> Void) {
        let info = response.notification.request.content.userInfo
        let account = info["account"] as? String ?? "", chat = info["chat"] as? String ?? ""
        Task { @MainActor in if self.account == account && !chat.isEmpty { self.open?(chat) }; completionHandler() }
    }
}
