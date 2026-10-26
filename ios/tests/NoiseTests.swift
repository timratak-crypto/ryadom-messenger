import Foundation
@main struct NoiseTests {
    static var checks = 0
    static func check(_ ok: Bool, _ name: String) { checks += 1; precondition(ok, name) }
    static func tone(_ amp: Int, _ count: Int) -> [Int16] { (0..<count).map { i in let p = i % 40; return Int16((p < 20 ? p-10 : 30-p)*amp/10) } }
    static func rms(_ x: [Int16], _ from: Int, _ to: Int) -> Double { var sum = 0.0; for i in from..<to { sum += Double(x[i])*Double(x[i]) }; return sqrt(sum/Double(to-from)) }
    static func filter(_ x: [Int16], _ mode: Int) -> [Int16] { let f = NoiseFilter(); return x.map { f.sample($0, mode: mode) } }
    static func hash(_ x: [Int16]) -> UInt32 { var h: UInt32 = 2166136261; for s in x { let n = UInt16(bitPattern: s); h = (h ^ UInt32(n & 255)) &* 16777619; h = (h ^ UInt32(n >> 8)) &* 16777619 }; return h }
    static func encode(_ x: [Int16]) -> Data { var b = Data(); for s in x { let n = UInt16(bitPattern: s); b.append(UInt8(n & 255)); b.append(UInt8(n >> 8)) }; return b }
    static func le32(_ n: Int) -> Data { Data([UInt8(n & 255),UInt8((n >> 8) & 255),UInt8((n >> 16) & 255),UInt8((n >> 24) & 255)]) }
    static func main() throws {
        let quiet = tone(100,32000), near = tone(5000,32000), medium = tone(350,32000)
        check(filter(near,0) == near,"off bit-exact")
        check(rms(filter(quiet,2),16000,32000) < rms(quiet,16000,32000)*0.04,"strong >28 dB quiet reduction")
        check(rms(filter(quiet,1),16000,32000) < rms(quiet,16000,32000)*0.12,"soft >18 dB quiet reduction")
        check(rms(filter(near,2),16000,32000) > rms(near,16000,32000)*0.85,"near voice preserved")
        check(rms(filter(medium,1),16000,32000) > rms(filter(medium,2),16000,32000)*10,"soft preserves quieter voice")
        check(rms(filter(Array(repeating:2000,count:32000),2),16000,32000) < 1,"DC removed")
        var mixed = [Int16](repeating:0,count:64000); mixed.replaceSubrange(16000..<32000,with:near[0..<16000]); mixed.replaceSubrange(32000..<64000,with:quiet)
        let y = filter(mixed,2)
        check(rms(y,16160,16480) > rms(mixed,16160,16480)*0.8,"speech onset")
        check(rms(y,32000,32320) > rms(quiet,0,320)*0.8,"hold protects endings")
        check(rms(y,48000,64000) < rms(quiet,0,16000)*0.04,"background closes after speech")
        check(hash(y) == 878819357,"cross-platform golden output")
        let pcm = encode(mixed), whole = NoiseFilter().process(pcm,mode:2), streaming = NoiseFilter()
        var at = 0, chunked = Data()
        while at < pcm.count { let n = min(((at%97)+1)*2,pcm.count-at); chunked.append(streaming.process(pcm.subdata(in:at..<at+n),mode:2)); at += n }
        check(whole == chunked && whole == encode(y),"arbitrary chunks and PCM endian parity")
        let reset = NoiseFilter(); for _ in 0..<1000 { _ = reset.sample(32767,mode:2) }; check(reset.sample(-32768,mode:0) == -32768,"switch off immediate"); check(reset.sample(100,mode:2) == NoiseFilter().sample(100,mode:2),"mode clears state")
        // Odd-sized ancillary chunk and padding before fmt/data must survive processing.
        var body = Data("WAVEJUNK".utf8); body.append(le32(3)); body.append(contentsOf:[7,8,9,0]); body.append(Data("fmt ".utf8)); body.append(le32(16)); body.append(contentsOf:[1,0,1,0]); body.append(le32(16000)); body.append(le32(32000)); body.append(contentsOf:[2,0,16,0]); body.append(Data("data".utf8)); body.append(le32(pcm.count)); body.append(pcm)
        var wav = Data("RIFF".utf8); wav.append(le32(body.count)); wav.append(body)
        let result = try NoiseFilter.wav(wav,mode:2), start = wav.count-pcm.count
        check(result.count == wav.count && result.prefix(start) == wav.prefix(start),"WAV headers and padding unchanged")
        check(result.suffix(pcm.count) == whole,"WAV payload processed once")
        check(try NoiseFilter.wav(wav,mode:0) == wav,"WAV off bit-exact")
        var bad = wav; bad[bad.count-pcm.count-4] = 255
        do { _ = try NoiseFilter.wav(bad,mode:2); preconditionFailure("invalid data length accepted") } catch { checks += 1 }
        do { _ = try NoiseFilter.wav(wav.dropLast(),mode:2); preconditionFailure("truncated WAV accepted") } catch { checks += 1 }
        print("Noise tests passed: \(checks)")
    }
}
