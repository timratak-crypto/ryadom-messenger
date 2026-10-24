import SwiftUI
import Security
import UIKit

struct Profile: Codable, Identifiable { let id: String; let username: String }
struct AccountSession: Codable { let token: String; let expiresAt: Double; let user: Profile }
struct Conversation: Codable, Identifiable { let id: String; let peerId: String; let username: String; let lastText: String; let at: Double; let unread: Int }
struct ChatMessage: Codable, Identifiable {
    let seq: Int64; let sender: String; let clientId: String; let text: String; let at: Double
    var photo: String? = nil; var width: Int? = nil; var height: Int? = nil
    var voice: String? = nil; var duration: Int? = nil
    var id: Int64 { seq }
}
struct Inbox: Decodable { let user: Profile; let chats: [Conversation] }
struct SearchResult: Decodable { let users: [Profile] }
struct Opened: Decodable { let conversation: String; let peer: Profile }
struct Feed: Decodable { let messages: [ChatMessage]; let peerRead: Int64; let hasMore: Bool }
struct PhotoResponse: Decodable { let data: String }
struct Accepted: Decodable { let ok: Bool }
struct ErrorBody: Decodable { let error: String }
struct PollingContext: Equatable {
    let active: Bool
    let token: String?
    let conversation: String?
}
struct ChatError: LocalizedError {
    let message: String; let code: Int
    init(_ message: String, code: Int = 0) { self.message = message; self.code = code }
    var errorDescription: String? { message }
}
enum SecureStore {
    static var query: [String: Any] { [kSecClass as String: kSecClassGenericPassword, kSecAttrService as String: "app.ryadom.family", kSecAttrAccount as String: "account-v2"] }
    static func load() throws -> AccountSession? {
        var q = query; q[kSecReturnData as String] = true; q[kSecMatchLimit as String] = kSecMatchLimitOne
        var result: CFTypeRef?; let status = SecItemCopyMatching(q as CFDictionary, &result)
        if status == errSecItemNotFound { return nil }
        guard status == errSecSuccess, let data = result as? Data else { throw ChatError("Не удалось открыть сохранённый аккаунт.") }
        let s = try JSONDecoder().decode(AccountSession.self, from: data)
        return s.expiresAt > Date().timeIntervalSince1970 * 1000 ? s : nil
    }
    static func save(_ s: AccountSession?) throws {
        guard let s else { let status = SecItemDelete(query as CFDictionary); guard status == errSecSuccess || status == errSecItemNotFound else { throw ChatError("Не удалось выйти из аккаунта.") }; return }
        let data = try JSONEncoder().encode(s)
        let status = SecItemUpdate(query as CFDictionary, [kSecValueData as String: data] as CFDictionary)
        if status == errSecItemNotFound {
            var q = query; q[kSecValueData as String] = data; q[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly
            guard SecItemAdd(q as CFDictionary, nil) == errSecSuccess else { throw ChatError("Не удалось сохранить вход.") }
        } else if status != errSecSuccess { throw ChatError("Не удалось сохранить вход.") }
    }
}
// Explicitly reject redirects so account tokens and passwords stay on this server.
final class NoRedirect: NSObject, URLSessionTaskDelegate {
    func urlSession(_ session: URLSession, task: URLSessionTask, willPerformHTTPRedirection response: HTTPURLResponse, newRequest request: URLRequest, completionHandler: @escaping (URLRequest?) -> Void) { completionHandler(nil) }
}
@MainActor final class ChatModel: ObservableObject {
    @Published var session: AccountSession?
    @Published var chats: [Conversation] = []
    @Published var users: [Profile] = []
    @Published var selected: Conversation?
    @Published var messages: [ChatMessage] = []
    @Published var query = ""
    @Published var draft = ""
    @Published var voice: Data?
    @Published var recording = false
    private var voiceDrafts: [String: Data] = [:]
    private var retryVoice: Data?
    @Published var photo: Data?
    private var photoDrafts: [String: Data] = [:]
    @Published var error = ""
    @Published var busy = false
    @Published var peerRead: Int64 = 0
    var onInbox: (([Conversation]) -> Void)?
    private var polling = false
    private var cursor: Int64 = 0
    private var selectionGeneration = UUID()
    private var drafts: [String: String] = [:]
    private var retryText = "", retryID = "", retryChat = ""
    private var retryPhoto: Data?
    private let network: URLSession
    init(network: URLSession = URLSession(configuration: .ephemeral, delegate: NoRedirect(), delegateQueue: nil), restoreSession: Bool = true) {
        self.network = network
        if restoreSession { do { session = try SecureStore.load() } catch { self.error = error.localizedDescription } }
    }
    func request<T: Decodable>(token: String? = nil, body: [String: Any]? = nil, query: [URLQueryItem] = []) async throws -> T {
        var url = URLComponents(string: "https://ryadom-family-chat.onrender.com/api/v2")!
        if !query.isEmpty { url.queryItems = query }
        var req = URLRequest(url: url.url!, cachePolicy: .reloadIgnoringLocalCacheData, timeoutInterval: 90)
        req.httpMethod = body == nil ? "GET" : "POST"
        if let token { req.setValue("Bearer \(token)", forHTTPHeaderField: "Authorization") }
        if let body { req.httpBody = try JSONSerialization.data(withJSONObject: body); req.setValue("application/json", forHTTPHeaderField: "Content-Type") }
        let (data, response) = try await network.data(for: req)
        guard let http = response as? HTTPURLResponse, http.value(forHTTPHeaderField: "Content-Type")?.contains("application/json") == true else { throw ChatError("Сервер просыпается. Подождите минуту и повторите.") }
        guard (200..<300).contains(http.statusCode) else { throw ChatError((try? JSONDecoder().decode(ErrorBody.self, from: data).error) ?? "Ошибка сервера.", code: http.statusCode) }
        return try JSONDecoder().decode(T.self, from: data)
    }
    func failed(_ e: Error, authenticating: Bool = false) {
        if (e as? URLError)?.code == .cancelled { return }
        if !authenticating, (e as? ChatError)?.code == 401 { try? SecureStore.save(nil); reset() }
        error = e is URLError ? "Нет связи с сервером. Первый запуск может занять минуту. Повторите попытку." : e.localizedDescription
    }
    func reset() { session = nil; selected = nil; chats = []; users = []; messages = []; draft = ""; query = ""; drafts = [:]; photoDrafts = [:]; voiceDrafts = [:]; voice = nil; retryVoice = nil; recording = false; photo = nil; retryPhoto = nil; cursor = 0; selectionGeneration = UUID() }
    func authenticate(username: String, password: String, register: Bool) async {
        guard !busy else { return }; busy = true; error = ""; defer { busy = false }
        do {
            let next: AccountSession = try await request(body: ["action": register ? "register" : "login", "username": username, "password": password])
            try SecureStore.save(next); session = next; await refresh()
        } catch { failed(error, authenticating: true) }
    }
    func logout(password: String) async -> Bool {
        guard !busy, let target = session else { return false }; busy = true; error = ""; defer { busy = false }
        do {
            let _: Accepted = try await request(token: target.token, body: ["action": "logout", "password": password])
            try SecureStore.save(nil); reset(); error = ""; return true
        } catch { failed(error); return false }
    }
    func search() async {
        guard !busy, let target = session else { return }; busy = true; error = ""; defer { busy = false }
        do {
            let result: SearchResult = try await request(token: target.token, query: [.init(name: "view", value: "search"), .init(name: "q", value: query)])
            guard session?.token == target.token else { return }; users = result.users
            if users.isEmpty { error = "Пользователь не найден. Проверьте юзернейм." }
        } catch { failed(error) }
    }
    func open(_ profile: Profile) async {
        guard !busy, let target = session else { return }; busy = true; error = ""; defer { busy = false }
        do {
            let result: Opened = try await request(token: target.token, body: ["action": "open", "username": profile.username])
            guard session?.token == target.token else { return }
            select(Conversation(id: result.conversation, peerId: profile.id, username: profile.username, lastText: "", at: 0, unread: 0)); await refresh()
        } catch { failed(error) }
    }
    func select(_ chat: Conversation?) {
        if let old = selected { drafts[old.id] = draft; photoDrafts[old.id] = photo; voiceDrafts[old.id] = voice }
        selectionGeneration = UUID()
        selected = chat; messages = []; cursor = 0; peerRead = 0; draft = chat.flatMap { drafts[$0.id] } ?? ""
        photo = chat.flatMap { photoDrafts[$0.id] }; retryPhoto = nil
        voice = chat.flatMap { voiceDrafts[$0.id] }; retryVoice = nil
        retryText = ""; retryID = ""; retryChat = ""; error = ""
    }
    func poll(active: Bool, intervalNanoseconds: UInt64 = 3_000_000_000) async {
        guard active else { return }
        while !Task.isCancelled {
            await refresh()
            do { try await Task.sleep(nanoseconds: intervalNanoseconds) }
            catch { return }
        }
    }
    func refresh() async {
        guard !polling, !Task.isCancelled, let target = session else { return }; polling = true; defer { polling = false }
        let chat = selected, generation = selectionGeneration, after = cursor
        do {
            let inbox: Inbox = try await request(token: target.token)
            guard session?.token == target.token else { return }; chats = inbox.chats; onInbox?(inbox.chats)
            guard selectionGeneration == generation else { return }
            if let chat {
                let feed: Feed = try await request(token: target.token, query: [.init(name: "view", value: "messages"), .init(name: "conversation", value: chat.id), .init(name: "after", value: String(after))])
                guard session?.token == target.token, selectionGeneration == generation else { return }
                for m in feed.messages where !messages.contains(where: { $0.seq == m.seq }) { messages.append(m); cursor = max(cursor, m.seq) }
                peerRead = feed.peerRead
            }
        } catch { if session?.token == target.token { failed(error) } }
    }
    func send() async {
        guard !busy, !recording, let target = session, let chat = selected else { return }
        let text = draft.trimmingCharacters(in: .whitespacesAndNewlines), image = photo, audio = voice; guard !text.isEmpty || image != nil || audio != nil else { return }
        guard text.utf16.count <= 4000 else { error = "Максимум 4000 символов."; return }
        if retryText != text || retryChat != chat.id || retryPhoto != image || retryVoice != audio { retryVoice = audio; retryText = text; retryChat = chat.id; retryPhoto = image; retryID = UUID().uuidString.lowercased() }
        busy = true; error = ""; defer { busy = false }
        do {
            var body: [String: Any] = ["action": audio != nil ? "sendVoice" : (image == nil ? "send" : "sendPhoto"), "conversation": chat.id, "clientId": retryID, "text": text]
            if let image { body["data"] = image.base64EncodedString() }
            if let audio { body["data"] = audio.base64EncodedString() }
            let _: Accepted = try await request(token: target.token, body: body)
            guard session?.token == target.token, selected?.id == chat.id else { return }
            if draft.trimmingCharacters(in: .whitespacesAndNewlines) == text { draft = "" }
            if photo == image { photo = nil }; photoDrafts[chat.id] = photo
            if voice == audio { voice = nil }; voiceDrafts[chat.id] = voice; retryVoice = nil
            drafts[chat.id] = draft; retryText = ""; retryID = ""; retryPhoto = nil; await refresh()
        } catch { failed(error) }
    }
}
import PhotosUI
import AVFoundation
import ImageIO
import UniformTypeIdentifiers


// One recorder/player for the visible conversation; never record or play automatically.
@MainActor final class VoiceAudio: NSObject, ObservableObject, AVAudioRecorderDelegate, AVAudioPlayerDelegate {
    @Published var capturing = false
    @Published var elapsed = 0.0
    @Published var playingID: String?
    @Published var loadingID: String?
    private var recorder: AVAudioRecorder?
    private var player: AVAudioPlayer?
    private var file: URL?
    private var timer: Timer?
    private var completion: ((Result<Data, Error>) -> Void)?
    private var generation = UUID()
    private var recordGeneration = UUID()
    override init() {
        super.init()
        NotificationCenter.default.addObserver(self, selector: #selector(interrupted), name: AVAudioSession.interruptionNotification, object: nil)
    }
    @objc private func interrupted(_ notification: Notification) {
        guard let type = notification.userInfo?[AVAudioSessionInterruptionTypeKey] as? UInt,
              type == AVAudioSession.InterruptionType.began.rawValue else { return }
        finish(save: true); stopPlayback()
    }
    func start(completion: @escaping (Result<Data, Error>) -> Void) {
        guard !capturing else { return }; stopPlayback(); capturing = true; elapsed = 0; self.completion = completion
        let attempt = UUID(); recordGeneration = attempt
        AVAudioSession.sharedInstance().requestRecordPermission { allowed in
            Task { @MainActor in
                guard self.capturing, self.recordGeneration == attempt else { return }
                guard allowed else { self.fail(ChatError("Разрешите микрофон в Настройках → Рядом → Микрофон.")); return }
                do {
                    let session = AVAudioSession.sharedInstance()
                    try session.setCategory(.playAndRecord, mode: .default, options: [.defaultToSpeaker, .allowBluetooth])
                    try session.setActive(true)
                    let url = FileManager.default.temporaryDirectory.appendingPathComponent("ryadom-voice-\(UUID().uuidString).wav"); self.file = url
                    let next = try AVAudioRecorder(url: url, settings: [AVFormatIDKey: kAudioFormatLinearPCM, AVSampleRateKey: 16000, AVNumberOfChannelsKey: 1, AVLinearPCMBitDepthKey: 16, AVLinearPCMIsBigEndianKey: false, AVLinearPCMIsFloatKey: false, AVLinearPCMIsNonInterleaved: false])
                    next.delegate = self; self.recorder = next
                    guard next.record(forDuration: 60) else { throw ChatError("Не удалось начать запись. Проверьте микрофон.") }
                    self.timer = Timer.scheduledTimer(withTimeInterval: 0.1, repeats: true) { [weak self] _ in
                        Task { @MainActor in guard let self else { return }; self.elapsed = self.recorder?.currentTime ?? 0 }
                    }
                } catch { self.fail(error) }
            }
        }
    }
    func finish(save: Bool) {
        guard capturing else { return }
        capturing = false; recordGeneration = UUID(); timer?.invalidate(); timer = nil
        let target = file, done = completion; completion = nil; file = nil
        recorder?.delegate = nil; recorder?.stop(); recorder = nil
        defer { if let target { try? FileManager.default.removeItem(at: target) }; try? AVAudioSession.sharedInstance().setActive(false, options: .notifyOthersOnDeactivation) }
        if save, let target {
            do {
                let data = try Data(contentsOf: target)
                guard Self.seconds(data) >= 0.25, data.count <= 2 * 1024 * 1024 else { throw ChatError("Запись слишком короткая. Нажмите микрофон и скажите сообщение.") }
                done?(.success(data))
            } catch { done?(.failure(error)) }
        }
    }
    private func fail(_ error: Error) { let done = completion; finish(save: false); done?(.failure(error)) }
    func audioRecorderDidFinishRecording(_ recorder: AVAudioRecorder, successfully flag: Bool) {
        if flag { finish(save: true) } else { fail(ChatError("Запись прервалась. Попробуйте ещё раз.")) }
    }
    func audioRecorderEncodeErrorDidOccur(_ recorder: AVAudioRecorder, error: Error?) { fail(error ?? ChatError("Не удалось сохранить запись.")) }
    func stopPlayback() { let hadPlayback = player != nil; generation = UUID(); loadingID = nil; player?.stop(); player = nil; playingID = nil; if !capturing && hadPlayback { try? AVAudioSession.sharedInstance().setActive(false, options: .notifyOthersOnDeactivation) } }
    func play(_ data: Data, id: String) throws {
        if playingID == id { stopPlayback(); return }
        stopPlayback()
        do {
            let session = AVAudioSession.sharedInstance(); try session.setCategory(.playback, mode: .spokenAudio); try session.setActive(true)
            let next = try AVAudioPlayer(data: data); next.delegate = self
            guard next.prepareToPlay(), next.play() else { throw ChatError("Не удалось воспроизвести запись.") }
            player = next; playingID = id
        } catch { stopPlayback(); throw error }
    }
    func fetch(model: ChatModel, id: String, conversation: String) async {
        if playingID == id || loadingID == id { stopPlayback(); return }
        guard !capturing, let token = model.session?.token else { return }; stopPlayback()
        let attempt = UUID(); generation = attempt; loadingID = id
        do {
            let response: PhotoResponse = try await model.request(token: token, query: [.init(name: "view", value: "voice"), .init(name: "conversation", value: conversation), .init(name: "voice", value: id)])
            guard generation == attempt, model.session?.token == token, model.selected?.id == conversation else { return }
            loadingID = nil
            guard let data = Data(base64Encoded: response.data), data.count <= 2 * 1024 * 1024 else { throw ChatError("Запись повреждена.") }
            do { try play(data, id: id) } catch { model.failed(error) }
        } catch { if generation == attempt { loadingID = nil; model.failed(error) } }
    }
    func audioPlayerDidFinishPlaying(_ player: AVAudioPlayer, successfully flag: Bool) { stopPlayback() }
    func audioPlayerDecodeErrorDidOccur(_ player: AVAudioPlayer, error: Error?) { stopPlayback() }
    static func seconds(_ data: Data) -> Double {
        let b = [UInt8](data); guard b.count >= 44 else { return 0 }; var at = 12
        func number(_ n: Int) -> Int { Int(b[n]) | Int(b[n+1]) << 8 | Int(b[n+2]) << 16 | Int(b[n+3]) << 24 }
        while at + 8 <= b.count { let count = number(at+4); guard count <= b.count-at-8 else { return 0 }; if String(bytes: b[at..<at+4], encoding: .ascii) == "data" { return Double(count)/32000 }; at += 8+count+count%2 }
        return 0
    }
    static func time(_ seconds: Double) -> String { let n = max(0, Int(seconds.rounded())); return String(format: "%d:%02d", n/60, n%60) }
}

enum PhotoEncoding {
    static func jpeg(_ image: UIImage) throws -> Data {
        let scale = min(1, 1600 / max(image.size.width, image.size.height))
        let size = CGSize(width: max(1, image.size.width * scale), height: max(1, image.size.height * scale))
        let format = UIGraphicsImageRendererFormat(); format.scale = 1; format.opaque = true
        let normalized = UIGraphicsImageRenderer(size: size, format: format).image { context in
            UIColor.white.setFill(); context.fill(CGRect(origin: .zero, size: size)); image.draw(in: CGRect(origin: .zero, size: size))
        }
        for quality in [0.82, 0.65, 0.45, 0.25] {
            if let data = normalized.jpegData(compressionQuality: quality), data.count <= 1024 * 1024 { return data }
        }
        throw ChatError("Не удалось уменьшить фотографию. Выберите другой снимок.")
    }
    static func file(_ url: URL) throws -> Data {
        guard let source = CGImageSourceCreateWithURL(url as CFURL, nil),
              let thumb = CGImageSourceCreateThumbnailAtIndex(source, 0, [kCGImageSourceCreateThumbnailFromImageAlways: true, kCGImageSourceCreateThumbnailWithTransform: true, kCGImageSourceThumbnailMaxPixelSize: 1600] as CFDictionary)
        else { throw ChatError("Не удалось открыть фотографию. Попробуйте загрузить её из iCloud и выбрать ещё раз.") }
        return try jpeg(UIImage(cgImage: thumb))
    }
}
struct GalleryPicker: UIViewControllerRepresentable {
    let completion: (Result<Data, Error>?) -> Void
    func makeCoordinator() -> Coordinator { Coordinator(completion) }
    func makeUIViewController(context: Context) -> PHPickerViewController {
        var config = PHPickerConfiguration(); config.filter = .images; config.selectionLimit = 1
        let picker = PHPickerViewController(configuration: config); picker.delegate = context.coordinator; return picker
    }
    func updateUIViewController(_ picker: PHPickerViewController, context: Context) {}
    final class Coordinator: NSObject, PHPickerViewControllerDelegate {
        let done: (Result<Data, Error>?) -> Void
        init(_ done: @escaping (Result<Data, Error>?) -> Void) { self.done = done }
        func picker(_ picker: PHPickerViewController, didFinishPicking results: [PHPickerResult]) {
            guard let provider = results.first?.itemProvider else { done(nil); return }
            provider.loadFileRepresentation(forTypeIdentifier: UTType.image.identifier) { url, error in
                let result: Result<Data, Error>
                if let url { result = Result { try PhotoEncoding.file(url) } }
                else { result = .failure(error ?? ChatError("Не удалось загрузить фото.")) }
                DispatchQueue.main.async { self.done(result) }
            }
        }
    }
}
struct CameraPicker: UIViewControllerRepresentable {
    let completion: (Result<Data, Error>?) -> Void
    func makeCoordinator() -> Coordinator { Coordinator(completion) }
    func makeUIViewController(context: Context) -> UIImagePickerController {
        let picker = UIImagePickerController(); picker.sourceType = .camera; picker.cameraCaptureMode = .photo; picker.delegate = context.coordinator; return picker
    }
    func updateUIViewController(_ picker: UIImagePickerController, context: Context) {}
    final class Coordinator: NSObject, UIImagePickerControllerDelegate, UINavigationControllerDelegate {
        let done: (Result<Data, Error>?) -> Void
        init(_ done: @escaping (Result<Data, Error>?) -> Void) { self.done = done }
        func imagePickerControllerDidCancel(_ picker: UIImagePickerController) { done(nil) }
        func imagePickerController(_ picker: UIImagePickerController, didFinishPickingMediaWithInfo info: [UIImagePickerController.InfoKey: Any]) {
            guard let image = info[.originalImage] as? UIImage else { done(.failure(ChatError("Не удалось получить снимок."))); return }
            done(Result { try PhotoEncoding.jpeg(image) })
        }
    }
}
struct MessagePhoto: View {
    @ObservedObject var model: ChatModel
    let photo: String; let conversation: String
    @State private var image: UIImage?
    @State private var failed = false
    @State private var attempt = 0
    @State private var expanded = false
    var body: some View {
        Group {
            if let image { Button { expanded = true } label: { Image(uiImage: image).resizable().scaledToFit().frame(maxWidth: 280, maxHeight: 320).clipShape(RoundedRectangle(cornerRadius: 10)) }.buttonStyle(.plain).accessibilityLabel("Открыть фотографию") }
            else if failed { Button("Загрузить фото ещё раз") { attempt += 1 }.font(.caption) }
            else { ProgressView().frame(width: 180, height: 130) }
        }.task(id: attempt) {
            guard image == nil, let token = model.session?.token else { return }; failed = false
            do {
                let response: PhotoResponse = try await model.request(token: token, query: [.init(name: "view", value: "photo"), .init(name: "conversation", value: conversation), .init(name: "photo", value: photo)])
                guard model.session?.token == token else { return }
                guard let data = Data(base64Encoded: response.data), let source = CGImageSourceCreateWithData(data as CFData, nil),
                      let thumb = CGImageSourceCreateThumbnailAtIndex(source, 0, [kCGImageSourceCreateThumbnailFromImageAlways: true, kCGImageSourceThumbnailMaxPixelSize: 600] as CFDictionary)
                else { throw ChatError("Не удалось открыть фото.") }
                image = UIImage(cgImage: thumb)
            } catch { if !Task.isCancelled { failed = true } }
        }.sheet(isPresented: $expanded) {
            NavigationStack { ScrollView([.horizontal, .vertical]) { if let image { Image(uiImage: image).resizable().scaledToFit().frame(width: max(300, UIScreen.main.bounds.width - 24)) } }.navigationTitle("Фотография").navigationBarTitleDisplayMode(.inline).toolbar { Button("Готово") { expanded = false } } }
        }
    }
}
let pine = Color(red: 38/255, green: 92/255, blue: 72/255)
let paper = Color(red: 245/255, green: 245/255, blue: 239/255)
struct LoginView: View {
    @ObservedObject var model: ChatModel
    @State private var username = ""
    @State private var password = ""
    @State private var confirm = ""
    @State private var register = false
    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 18) {
                Text(register ? "Создать аккаунт" : "С возвращением").font(.largeTitle.bold())
                Text("Найдите человека по юзернейму и напишите ему.").foregroundStyle(.secondary)
                VStack(alignment: .leading, spacing: 7) {
                    Text("Юзернейм").font(.headline)
                    TextField("username", text: $username).textInputAutocapitalization(.never).autocorrectionDisabled().textContentType(.username)
                    Text("3–24 символа: a–z, 0–9, _").font(.caption).foregroundStyle(.secondary)
                }
                SecureField("Пароль — от 8 символов", text: $password).textContentType(register ? .newPassword : .password)
                if register { SecureField("Повторите пароль", text: $confirm).textContentType(.newPassword) }
                Button {
                    guard !register || password == confirm else { model.error = "Пароли не совпадают."; return }
                    Task { await model.authenticate(username: username, password: password, register: register); password = ""; confirm = "" }
                } label: { Text(register ? "Зарегистрироваться" : "Войти").frame(maxWidth: .infinity).padding(.vertical, 9) }
                .buttonStyle(.borderedProminent).disabled(model.busy)
                Button(register ? "Уже есть аккаунт? Войти" : "Нет аккаунта? Регистрация") { register.toggle(); password = ""; confirm = ""; model.error = "" }.disabled(model.busy)
                Text("Запомните пароль: восстановление через почту пока недоступно.").font(.caption).foregroundStyle(.secondary)
            }.textFieldStyle(.roundedBorder).padding(24)
        }
    }
}
struct InboxView: View {
    @ObservedObject var model: ChatModel
    @State private var profile: Profile?
    var body: some View {
        List {
            Section("Ваш аккаунт") { Text("@\(model.session?.user.username ?? "")").font(.headline) }
            Section("Найти человека") {
                HStack {
                    TextField("@username", text: $model.query).textInputAutocapitalization(.never).autocorrectionDisabled().submitLabel(.search).onSubmit { Task { await model.search() } }
                    Button("Найти") { Task { await model.search() } }.disabled(model.busy)
                }
                ForEach(model.users) { user in Button { profile = user } label: { Label("@\(user.username)", systemImage: "person.crop.circle") } }
            }
            Section("Сообщения") {
                if model.chats.isEmpty { Text("Найдите человека по юзернейму, чтобы начать переписку.").foregroundStyle(.secondary) }
                ForEach(model.chats) { chat in
                    Button { model.select(chat); Task { await model.refresh() } } label: {
                        HStack {
                            VStack(alignment: .leading, spacing: 6) {
                                Text("@\(chat.username)").font(.headline).foregroundStyle(.primary)
                                Text(chat.lastText.isEmpty ? "Напишите первое сообщение" : chat.lastText).lineLimit(1).font(.subheadline).foregroundStyle(.secondary)
                            }
                            Spacer()
                            if chat.unread > 0 { Text("\(chat.unread)").font(.caption.bold()).padding(7).background(pine).foregroundStyle(.white).clipShape(Circle()) }
                        }.padding(.vertical, 5)
                    }.disabled(model.busy)
                }
            }
        }.scrollContentBackground(.hidden)
        .sheet(item: $profile) { p in
            VStack(spacing: 24) {
                Image(systemName: "person.crop.circle.fill").font(.system(size: 72)).foregroundStyle(pine)
                Text("@\(p.username)").font(.title.bold())
                Button("Написать") { profile = nil; Task { await model.open(p) } }.buttonStyle(.borderedProminent)
                Button("Закрыть") { profile = nil }
            }.padding(28).presentationDetents([.medium])
        }
    }
}
struct ConversationView: View {
    @EnvironmentObject var calls: CallsModel
    @ObservedObject var model: ChatModel
    @StateObject private var audio = VoiceAudio()
    @Environment(\.scenePhase) private var scenePhase
    @State private var gallery = false
    @State private var camera = false
    @State private var pickerChat: String?
    func picked(_ result: Result<Data, Error>?) {
        gallery = false; camera = false
        guard pickerChat == model.selected?.id, let result else { return }
        do { model.photo = try result.get() } catch { model.error = error.localizedDescription }
    }
    func openCamera() {
        guard UIImagePickerController.isSourceTypeAvailable(.camera) else { model.error = "Камера недоступна на этом устройстве."; return }
        pickerChat = model.selected?.id
        Task {
            let allowed = await AVCaptureDevice.requestAccess(for: .video)
            if allowed { camera = true }
            else { model.error = "Разрешите доступ к камере в Настройках → Рядом → Камера." }
        }
    }
    var body: some View {
        VStack(spacing: 0) {
            ScrollViewReader { proxy in
                ScrollView {
                    LazyVStack(spacing: 12) {
                        if model.messages.isEmpty { Text("Напишите первое сообщение.").foregroundStyle(.secondary).padding(.top, 40) }
                        ForEach(model.messages) { m in
                            let own = m.sender == model.session?.user.id
                            HStack {
                                if own { Spacer(minLength: 38) }
                                VStack(alignment: .leading, spacing: 8) {
                                    if let photo = m.photo, let chat = model.selected { MessagePhoto(model: model, photo: photo, conversation: chat.id) }
                                    if let voice = m.voice, let chat = model.selected {
                                        Button { Task { await audio.fetch(model: model, id: voice, conversation: chat.id) } } label: {
                                            Label(audio.loadingID == voice ? "Загрузка…" : "Голосовое · \(VoiceAudio.time(Double(m.duration ?? 0)/1000))", systemImage: audio.playingID == voice ? "stop.fill" : "play.fill")
                                        }.buttonStyle(.plain).disabled(audio.capturing).padding(.vertical, 8)
                                    }
                                    if !m.text.isEmpty { Text(m.text).textSelection(.enabled) }
                                    Text(Date(timeIntervalSince1970: m.at / 1000), format: .dateTime.day().month().hour().minute()).font(.caption2).opacity(0.7)
                                    if own { Text(model.peerRead >= m.seq ? "Прочитано" : "Отправлено").font(.caption2).opacity(0.7) }
                                }.padding(14).background(own ? pine : Color.white).foregroundStyle(own ? Color.white : Color.primary).clipShape(RoundedRectangle(cornerRadius: 16))
                                if !own { Spacer(minLength: 38) }
                            }.id(m.seq)
                        }
                    }.padding(16)
                }.onChange(of: model.messages.count) { _ in if let last = model.messages.last { proxy.scrollTo(last.seq, anchor: .bottom) } }
            }
            if let data = model.photo, let image = UIImage(data: data) {
                HStack {
                    Image(uiImage: image).resizable().scaledToFit().frame(width: 80, height: 70).clipShape(RoundedRectangle(cornerRadius: 8))
                    Text("Фото готово к отправке").font(.caption); Spacer()
                    Button { model.photo = nil } label: { Image(systemName: "xmark.circle.fill") }.disabled(model.busy).accessibilityLabel("Убрать фотографию")
                }.padding(.horizontal, 16).padding(.top, 8)
            }
            if audio.capturing {
                HStack {
                    Text("● Запись \(VoiceAudio.time(audio.elapsed)) / 1:00").foregroundStyle(.red).font(.subheadline)
                    Spacer(); Button("Отмена") { audio.finish(save: false) }
                }.padding(.horizontal, 16)
            } else if let recording = model.voice {
                HStack {
                    Button { do { try audio.play(recording, id: "draft") } catch { model.error = error.localizedDescription } } label: { Label("\(VoiceAudio.time(VoiceAudio.seconds(recording))) · Прослушать", systemImage: audio.playingID == "draft" ? "stop.fill" : "play.fill") }
                    Spacer(); Button { audio.stopPlayback(); model.voice = nil } label: { Image(systemName: "xmark.circle.fill") }.accessibilityLabel("Удалить запись")
                }.font(.subheadline).padding(.horizontal, 16).disabled(model.busy)
            }
            HStack(alignment: .bottom) {
                Menu {
                    Button { pickerChat = model.selected?.id; gallery = true } label: { Label("Фото из галереи", systemImage: "photo") }
                    Button { openCamera() } label: { Label("Снять на камеру", systemImage: "camera") }
                } label: { Image(systemName: "plus").font(.title2).padding(12) }.disabled(model.busy || audio.capturing || model.voice != nil).accessibilityLabel("Прикрепить фотографию")
                TextField("Сообщение…", text: $model.draft, axis: .vertical).lineLimit(1...5).padding(12).background(Color.white).clipShape(RoundedRectangle(cornerRadius: 12))
                Button {
                    if audio.capturing { audio.finish(save: true) }
                    else { let chat = model.selected?.id; audio.start { result in guard model.selected?.id == chat else { return }; do { model.voice = try result.get() } catch { model.error = error.localizedDescription } } }
                } label: { Image(systemName: audio.capturing ? "stop.circle.fill" : "mic.fill").font(.title2).padding(8) }
                .disabled(model.busy || model.photo != nil || model.voice != nil).accessibilityLabel(audio.capturing ? "Остановить запись" : "Записать голосовое")
                Button { audio.stopPlayback(); Task { await model.send() } } label: { Image(systemName: "arrow.up").font(.title2).padding(12) }.buttonStyle(.borderedProminent).disabled(model.busy || audio.capturing || (model.draft.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty && model.photo == nil && model.voice == nil)).accessibilityLabel("Отправить")
            }.padding(12)
        }.sheet(isPresented: $gallery) { GalleryPicker(completion: picked).ignoresSafeArea() }
        .fullScreenCover(isPresented: $camera) { CameraPicker(completion: picked).ignoresSafeArea() }
        .onChange(of: calls.busy) { if $0 { audio.stopPlayback() } }
        .onChange(of: audio.capturing) { model.recording = $0 }
        .onChange(of: scenePhase) { if $0 == .background { audio.finish(save: true) }; if $0 != .active { audio.stopPlayback() } }
        .onDisappear { audio.finish(save: false); audio.stopPlayback(); model.recording = false }
    }
}
struct LogoutView: View {
    @ObservedObject var model: ChatModel
    @Environment(\.dismiss) private var dismiss
    @State private var password = ""
    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 20) {
                Text("Подтвердите выход").font(.title.bold())
                Text("Введите пароль от @\(model.session?.user.username ?? "").")
                SecureField("Пароль", text: $password).textContentType(.password).textFieldStyle(.roundedBorder)
                if !model.error.isEmpty { Text(model.error).foregroundStyle(.red).font(.footnote) }
                Button {
                    Task { let done = await model.logout(password: password); password = ""; if done || model.session == nil { dismiss() } }
                } label: { Text(model.busy ? "Проверяем пароль…" : "Выйти из аккаунта").frame(maxWidth: .infinity) }
                .buttonStyle(.borderedProminent).disabled(model.busy || password.isEmpty)
                Button("Отмена") { password = ""; dismiss() }.disabled(model.busy)
            }.padding(24)
        }.interactiveDismissDisabled(model.busy)
    }
}
struct ContentView: View {
    @StateObject private var model = ChatModel()
    @StateObject private var calls = CallsModel()
    @State private var logoutPrompt = false
    @Environment(\.scenePhase) private var scenePhase
    @Environment(\.accessibilityReduceMotion) private var reduceMotion
    var body: some View {
        let polling = PollingContext(active: scenePhase == .active, token: model.session?.token, conversation: model.selected?.id)
        NavigationStack {
            VStack(spacing: 0) {
                if model.session == nil { LoginView(model: model) }
                else if model.selected == nil { InboxView(model: model) }
                else { ConversationView(model: model).environmentObject(calls).id(model.selected?.id).transition(.asymmetric(insertion: .move(edge: .trailing).combined(with: .opacity), removal: .opacity)) }
                if !calls.note.isEmpty && !calls.busy { HStack { Text(calls.note).font(.footnote); Spacer(); Button { calls.note = "" } label: { Image(systemName: "xmark") } }.padding(12) }
                if model.busy { ProgressView("Подключаемся…").font(.caption).padding(6) }
                if !model.error.isEmpty {
                    HStack { Text(model.error).font(.footnote).foregroundStyle(.red); Spacer(); Button { model.error = "" } label: { Image(systemName: "xmark") } }.padding(12).background(Color.white)
                }
                Text("На iPhone уведомления доступны, пока «Рядом» открыт.").font(.caption2).foregroundStyle(.secondary).padding(.bottom, 6)
            }.background(paper).navigationTitle(model.selected.map { "@\($0.username)" } ?? "Рядом.").navigationBarTitleDisplayMode(.inline)
            .toolbar {
                if let chat = model.selected { ToolbarItem(placement: .navigationBarTrailing) { Button { calls.dial(chat.username) } label: { Image(systemName: "phone.fill") }.disabled(model.busy || model.recording || calls.busy).accessibilityLabel("Позвонить") } }
                if model.selected != nil { ToolbarItem(placement: .navigationBarLeading) { Button("Назад") { model.select(nil) }.disabled(model.busy || model.recording) } }
                else if model.session != nil { ToolbarItem(placement: .navigationBarLeading) { Button { LocalNotices.shared.settings() } label: { Image(systemName: "bell") }.accessibilityLabel("Уведомления") }; ToolbarItem(placement: .navigationBarTrailing) { Button("Выйти") { model.error = ""; logoutPrompt = true }.disabled(model.busy) } }
            }
        }.tint(pine)
        .animation(reduceMotion ? nil : .easeInOut(duration: 0.2), value: model.selected?.id)
        .animation(reduceMotion ? nil : .easeInOut(duration: 0.2), value: model.session?.user.id)
        // Restart with fresh scene/account/chat state instead of capturing the launch phase forever.
        .task(id: polling) {
            await model.poll(active: polling.active)
        }
        .onAppear {
            LocalNotices.shared.configure(account: model.session?.user.id)
            LocalNotices.shared.open = { id in if !model.busy && !model.recording, let chat = model.chats.first(where: { $0.id == id }) { model.select(chat) } }
            model.onInbox = { LocalNotices.shared.observe($0, visible: model.selected?.id) }
            calls.canRing = { !model.recording }; calls.sync(token: model.session?.token, active: scenePhase != .background)
        }
        .onChange(of: model.session?.token) { LocalNotices.shared.configure(account: model.session?.user.id); calls.sync(token: $0, active: scenePhase != .background) }
        .onChange(of: scenePhase) { if $0 != .inactive { calls.sync(token: model.session?.token, active: $0 == .active) } }
        .fullScreenCover(isPresented: Binding(get: { calls.busy }, set: { if !$0 && calls.busy { calls.hangup() } })) { CallView(calls: calls) }
        .sheet(isPresented: $logoutPrompt) { LogoutView(model: model).presentationDetents([.medium, .large]) }
    }
}
@main struct RyadomApp: App { var body: some Scene { WindowGroup { ContentView() } } }
