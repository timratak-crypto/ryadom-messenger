import Foundation

// URLProtocol keeps these tests offline while exercising the production ChatModel and decoder.
final class ChatServer: @unchecked Sendable {
    let lock = NSLock()
    private var rows: [String: [[String: Any]]] = [:]
    private var requests = 0
    private var feeds = 0
    private var failNext = false
    private var delayNext = false
    private var largestCursor: Int64 = 0
    private var photoIDs: [String] = []
    private var voiceIDs: [String] = []
    var sentVoiceIDs: [String] { lock.lock(); defer { lock.unlock() }; return voiceIDs }
    var sentPhotoIDs: [String] { lock.lock(); defer { lock.unlock() }; return photoIDs }
    func append(_ seq: Int, to chat: String) {
        lock.lock(); defer { lock.unlock() }
        rows[chat, default: []].append(["seq": seq, "sender": "bob", "clientId": "m\(seq)", "text": "Message \(seq)", "at": 1000])
    }
    func failOneFeed() { lock.lock(); failNext = true; lock.unlock() }
    func delayOneFeed() { lock.lock(); delayNext = true; lock.unlock() }
    var counts: (requests: Int, feeds: Int, cursor: Int64) {
        lock.lock(); defer { lock.unlock() }; return (requests, feeds, largestCursor)
    }
    func response(_ request: URLRequest) -> (Int, Data, TimeInterval) {
        lock.lock(); defer { lock.unlock() }; requests += 1
        precondition(request.value(forHTTPHeaderField: "Authorization") == "Bearer test-token")
        var requestData = request.httpBody
        if requestData == nil, let stream = request.httpBodyStream {
            stream.open(); defer { stream.close() }
            var data = Data(), buffer = [UInt8](repeating: 0, count: 4096)
            while stream.hasBytesAvailable { let n = stream.read(&buffer, maxLength: buffer.count); if n <= 0 { break }; data.append(buffer, count: n) }
            requestData = data
        }
        if let data = requestData, let body = try? JSONSerialization.jsonObject(with: data) as? [String: Any], body["action"] as? String == "sendPhoto" {
            precondition(body["data"] as? String == Data([1,2,3]).base64EncodedString())
            photoIDs.append(body["clientId"] as! String)
            let status = photoIDs.count == 1 ? 503 : 200
            return (status, try! JSONSerialization.data(withJSONObject: status == 200 ? ["ok": true] : ["error": "Temporary upload failure"]), 0)
        }
        if let data = requestData, let body = try? JSONSerialization.jsonObject(with: data) as? [String: Any], body["action"] as? String == "sendVoice" {
            precondition(body["data"] as? String == Data([4,5,6]).base64EncodedString())
            voiceIDs.append(body["clientId"] as! String)
            let status = voiceIDs.count == 1 ? 503 : 200
            return (status, try! JSONSerialization.data(withJSONObject: status == 200 ? ["ok": true] : ["error": "Temporary upload failure"]), 0)
        }
        if let data = requestData,
           let body = try? JSONSerialization.jsonObject(with: data) as? [String: Any],
           body["action"] as? String == "logout" {
            let valid = body["password"] as? String == "correct-password"
            let result: [String: Any] = valid ? ["ok": true] : ["error": "Неверный пароль. Вы остались в аккаунте."]
            return (valid ? 200 : 403, try! JSONSerialization.data(withJSONObject: result), 0)
        }
        let query = URLComponents(url: request.url!, resolvingAgainstBaseURL: false)!.queryItems ?? []
        func value(_ name: String) -> String? { query.first { $0.name == name }?.value }
        var status = 200, delay: TimeInterval = 0
        let body: [String: Any]
        if value("view") == "messages" {
            feeds += 1
            let after = Int64(value("after") ?? "0")!
            largestCursor = max(largestCursor, after)
            if failNext { failNext = false; status = 503; body = ["error": "Temporary outage"] }
            else {
                let messages = (rows[value("conversation")!, default: []]).filter { Int64($0["seq"] as! Int) > after }
                body = ["messages": messages, "peerRead": 0, "hasMore": false]
            }
            if delayNext { delayNext = false; delay = 0.15 }
        } else { body = ["user": ["id": "alice", "username": "alice"], "chats": []] }
        return (status, try! JSONSerialization.data(withJSONObject: body), delay)
    }
}

final class StubProtocol: URLProtocol, @unchecked Sendable {
    static let server = ChatServer()
    private var delivery: DispatchWorkItem?
    override class func canInit(with request: URLRequest) -> Bool { true }
    override class func canonicalRequest(for request: URLRequest) -> URLRequest { request }
    override func startLoading() {
        let (status, data, delay) = Self.server.response(request)
        let work = DispatchWorkItem { [weak self] in
            guard let self else { return }
            let response = HTTPURLResponse(url: self.request.url!, statusCode: status, httpVersion: nil, headerFields: ["Content-Type": "application/json"])!
            self.client?.urlProtocol(self, didReceive: response, cacheStoragePolicy: .notAllowed)
            self.client?.urlProtocol(self, didLoad: data)
            self.client?.urlProtocolDidFinishLoading(self)
        }
        delivery = work
        DispatchQueue.global().asyncAfter(deadline: .now() + delay, execute: work)
    }
    override func stopLoading() { delivery?.cancel() }
}

@main struct PollingRegression {
    @MainActor static func expect(_ condition: @autoclosure () -> Bool, _ label: String) {
        precondition(condition(), label)
        print("PASS: \(label)")
    }
    @MainActor static func waitUntil(_ label: String, _ condition: () -> Bool) async throws {
        for _ in 0..<300 {
            if condition() { return }
            try await Task.sleep(nanoseconds: 10_000_000)
        }
        fatalError("Timed out: \(label)")
    }
    @MainActor static func main() async throws {
        let configuration = URLSessionConfiguration.ephemeral
        configuration.protocolClasses = [StubProtocol.self]
        let network = URLSession(configuration: configuration)
        defer { network.invalidateAndCancel() }
        let model = ChatModel(network: network, restoreSession: false)
        model.session = AccountSession(token: "test-token", expiresAt: .greatestFiniteMagnitude, user: Profile(id: "alice", username: "alice"))
        let chat = Conversation(id: "a-b", peerId: "bob", username: "bob", lastText: "", at: 0, unread: 0)
        model.select(chat)
        model.draft = "Unsent draft"
        let server = StubProtocol.server
        server.append(1, to: chat.id)

        await model.poll(active: false, intervalNanoseconds: 20_000_000)
        expect(server.counts.requests == 0, "inactive launch does not start polling")
        let inactive = PollingContext(active: false, token: "test-token", conversation: chat.id)
        let active = PollingContext(active: true, token: "test-token", conversation: chat.id)
        expect(inactive != active, "foreground transition changes SwiftUI task identity")
        let loop = Task { await model.poll(active: active.active, intervalNanoseconds: 20_000_000) }
        try await waitUntil("initial history") { model.messages.count == 1 }
        server.append(2, to: chat.id)
        try await waitUntil("incoming message in open chat") { model.messages.count == 2 }
        expect(model.selected?.id == chat.id && model.messages.map(\.seq) == [1, 2], "incoming message arrives without reopening the conversation")
        try await waitUntil("read acknowledgement") { server.counts.cursor == 2 }
        expect(model.draft == "Unsent draft", "polling preserves the composer draft")

        server.failOneFeed()
        try await waitUntil("temporary error") { model.error == "Temporary outage" }
        server.append(3, to: chat.id)
        try await waitUntil("automatic recovery") { model.messages.count == 3 }
        expect(model.messages.map(\.seq) == [1, 2, 3], "polling recovers from a failed request without duplicates")
        loop.cancel(); await loop.value
        let stopped = server.counts.requests
        server.append(4, to: chat.id)
        try await Task.sleep(nanoseconds: 100_000_000)
        expect(server.counts.requests == stopped && model.messages.count == 3, "background cancellation stops the loop")
        let resumed = Task { await model.poll(active: true, intervalNanoseconds: 20_000_000) }
        try await waitUntil("foreground resume") { model.messages.count == 4 }
        resumed.cancel(); await resumed.value
        expect(model.selected?.id == chat.id && model.messages.map(\.seq) == [1, 2, 3, 4], "foreground resume loads missed messages in the same chat")

        // Simulate A -> B -> A while a response for A with an old cursor is still pending.
        server.append(5, to: chat.id)
        server.delayOneFeed()
        let feeds = server.counts.feeds
        let pending = Task { await model.refresh() }
        try await waitUntil("pending feed") { server.counts.feeds > feeds }
        model.select(Conversation(id: "a-c", peerId: "carol", username: "carol", lastText: "", at: 0, unread: 0))
        model.select(chat)
        await pending.value
        expect(model.messages.isEmpty, "a response from an earlier selection cannot skip history after reopening")
        await model.refresh()
        expect(model.messages.map(\.seq) == [1, 2, 3, 4, 5], "reopened chat receives complete history from its own cursor")
        let old = try JSONDecoder().decode(ChatMessage.self, from: Data("{\"seq\":1,\"sender\":\"bob\",\"clientId\":\"old\",\"text\":\"old\",\"at\":1}".utf8))
        expect(old.photo == nil, "text history without photo fields stays compatible")
        model.photo = Data([1,2,3]); model.draft = "Photo caption"
        model.select(nil); model.select(chat)
        expect(model.photo == Data([1,2,3]) && model.draft == "Photo caption", "attachment draft stays with its conversation")
        await model.send()
        expect(model.photo == Data([1,2,3]) && model.draft == "Photo caption", "failed photo upload preserves photo and caption")
        await model.send()
        expect(model.photo == nil && model.draft.isEmpty, "accepted photo upload clears attachment and caption")
        expect(server.sentPhotoIDs.count == 2 && server.sentPhotoIDs[0] == server.sentPhotoIDs[1], "photo retry keeps the same idempotency key")
        await model.refresh()
        expect(old.voice == nil && old.duration == nil, "old messages decode without voice fields")
        let voiceMessage = try JSONDecoder().decode(ChatMessage.self, from: Data("{\"seq\":8,\"sender\":\"bob\",\"clientId\":\"voice\",\"text\":\"\",\"at\":1,\"voice\":\"clip-id\",\"duration\":1500}".utf8))
        expect(voiceMessage.voice == "clip-id" && voiceMessage.duration == 1500, "voice metadata decodes")
        model.voice = Data([4,5,6]); model.draft = "Voice caption"
        model.select(nil); model.select(chat)
        expect(model.voice == Data([4,5,6]) && model.draft == "Voice caption", "voice draft stays with its conversation")
        model.recording = true; await model.send()
        expect(server.sentVoiceIDs.isEmpty, "active recording is never sent")
        model.recording = false; await model.send()
        expect(model.voice == Data([4,5,6]) && model.draft == "Voice caption", "failed voice upload preserves recording and caption")
        await model.send()
        expect(model.voice == nil && model.draft.isEmpty, "accepted voice upload clears recording and caption")
        expect(server.sentVoiceIDs.count == 2 && server.sentVoiceIDs[0] == server.sentVoiceIDs[1], "voice retry keeps the same idempotency key")
        await model.refresh()
        let denied = await model.logout(password: "wrong-password")
        expect(!denied && model.session?.token == "test-token" && model.messages.count == 5, "wrong logout password preserves account and messages")
        let accepted = await model.logout(password: "correct-password")
        expect(accepted && model.session == nil && model.messages.isEmpty, "correct logout password clears the account")
        print("Polling regression checks completed.")
    }
}
