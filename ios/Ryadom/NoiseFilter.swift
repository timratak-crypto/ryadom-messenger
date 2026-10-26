import Foundation

// Level-based expander, not speaker recognition. One instance per PCM16/16 kHz stream.
final class NoiseFilter {
    private var mode = -1, hold = 0
    private var previous = 0.0, high = 0.0, power = 0.0, gain = 0.0
    func sample(_ input: Int16, mode selected: Int) -> Int16 {
        let selected = (0...2).contains(selected) ? selected : 2
        if mode != selected { mode = selected; previous = 0; high = 0; power = 0; hold = 0; gain = selected == 1 ? 0.1 : 0.025 }
        if mode == 0 { return input }
        let x = Double(input); high = 0.969 * (high + x - previous); previous = x
        let square = high * high; power += (square > power ? 0.03 : 0.00078) * (square - power)
        let threshold = mode == 1 ? 130.0 : 520.0, floor = mode == 1 ? 0.1 : 0.025
        if power >= threshold * threshold { hold = 2560 } else if hold > 0 { hold -= 1 }
        let target = hold > 0 || power >= threshold * threshold * 0.36 && gain > 0.5 ? 1.0 : floor
        gain += (target > gain ? 0.02 : 0.00052) * (target - gain)
        return Int16(max(-32768, min(32767, high * gain)))
    }
    func process(_ input: Data, mode: Int) -> Data {
        precondition(input.count % 2 == 0)
        var bytes = [UInt8](input)
        for i in stride(from: 0, to: bytes.count, by: 2) {
            let value = UInt16(bitPattern: sample(Int16(bitPattern: UInt16(bytes[i]) | UInt16(bytes[i+1]) << 8), mode: mode))
            bytes[i] = UInt8(value & 255); bytes[i+1] = UInt8(value >> 8)
        }
        return Data(bytes)
    }
    // AVAudioRecorder may insert extra WAV chunks. Preserve headers and padding verbatim.
    static func wav(_ input: Data, mode: Int) throws -> Data {
        let bytes = [UInt8](input)
        func fail() -> NSError { NSError(domain: "RyadomAudio", code: 1, userInfo: [NSLocalizedDescriptionKey: "Не удалось обработать запись. Повторите запись."]) }
        func u16(_ i: Int) -> Int { Int(bytes[i]) | Int(bytes[i+1]) << 8 }
        func u32(_ i: Int) -> Int { u16(i) | u16(i+2) << 16 }
        func tag(_ i: Int, _ s: String) -> Bool { Array(bytes[i..<i+4]) == Array(s.utf8) }
        guard bytes.count >= 44, tag(0,"RIFF"), tag(8,"WAVE"), u32(4) + 8 == bytes.count else { throw fail() }
        var at = 12, valid = false, ranges: [Range<Int>] = []
        while at <= bytes.count - 8 {
            let n = u32(at+4), start = at+8
            guard n <= bytes.count-start else { throw fail() }
            if tag(at,"fmt ") {
                guard n >= 16 else { throw fail() }
                let pcm = u16(start) == 1 || (n >= 40 && u16(start) == 65534 && u16(start+16) >= 22 && u16(start+18) == 16 && Array(bytes[start+24..<start+40]) == [1,0,0,0,0,0,16,0,128,0,0,170,0,56,155,113])
                valid = pcm && u16(start+2) == 1 && u32(start+4) == 16000 && u32(start+8) == 32000 && u16(start+12) == 2 && u16(start+14) == 16
                guard valid else { throw fail() }
            }
            if tag(at,"data") { guard n % 2 == 0 else { throw fail() }; ranges.append(start..<start+n) }
            at = start+n+n%2
        }
        guard valid, ranges.count == 1, at == bytes.count else { throw fail() }
        var result = input; let filter = NoiseFilter()
        for range in ranges { result.replaceSubrange(range, with: filter.process(input.subdata(in: range), mode: mode)) }
        return result
    }
}
enum NoisePreferences {
    static let names = ["Выключено", "Мягкое", "Сильное"]
    static let hint = "Приглушает тихий фон в звонках и новых голосовых. Говорите ближе к микрофону. Если речь обрезается — выберите мягкое. Громкие и одновременные голоса могут оставаться слышны."
    static var mode: Int { let n = UserDefaults.standard.object(forKey: "ryadom.noise") as? Int ?? 2; return (0...2).contains(n) ? n : 2 }
}
