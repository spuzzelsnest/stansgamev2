"""Period processing for the instructor's lines (numpy only).

    python process_voice.py <raw folder> <out folder> <lines.tsv> [--fx period|clean] [--voice NAME]

For every <id>.wav in the raw folder (Kokoro output, 24 kHz float): cuts the silence before and after
(keeps 40 ms), then with --fx period (the default)
  - a slight period / field-telephone EQ: high-pass at 170 Hz, low-pass at 6 kHz, +3 dB presence at
    2.2 kHz, and a little soft saturation, so the voice sits in 1944 rather than in a studio;
  - a parade-ground reverb: open air with barrack walls, so two slap echoes (75 and 140 ms, about -14
    and -19 dB) and a short, dark, diffuse tail (RT60 about 0.9 s), mixed at about 20 %;
with --fx clean neither. Then sets the peak to -1 dBFS and writes 16-bit PCM mono at the input rate.
Also writes manifest.tsv (id, file, seconds, speaker, text) in the out folder, from lines.tsv.
"""
import struct
import sys
import zlib
from pathlib import Path

import numpy as np


def read_wav(path):
    b = Path(path).read_bytes()
    if b[:4] != b"RIFF" or b[8:12] != b"WAVE":
        raise ValueError(path + ": not a WAVE file")
    pos, fmt, data = 12, None, None
    while pos + 8 <= len(b):
        cid, size = b[pos:pos + 4], struct.unpack("<I", b[pos + 4:pos + 8])[0]
        body = b[pos + 8:pos + 8 + size]
        if cid == b"fmt ":
            fmt = struct.unpack("<HHIIHH", body[:16])
        elif cid == b"data":
            data = body
        pos += 8 + size + (size & 1)
    tag, ch, rate, _, _, bits = fmt
    if tag == 3 and bits == 32:
        x = np.frombuffer(data, "<f4").astype(np.float64)
    elif tag == 1 and bits == 16:
        x = np.frombuffer(data, "<i2").astype(np.float64) / 32768.0
    else:
        raise ValueError("%s: unsupported format tag %d, %d bit" % (path, tag, bits))
    if ch > 1:
        x = x.reshape(-1, ch).mean(axis=1)
    return x, rate


def write_wav16(path, x, rate):
    pcm = np.clip(np.round(x * 32767.0), -32768, 32767).astype("<i2").tobytes()
    head = b"RIFF" + struct.pack("<I", 36 + len(pcm)) + b"WAVE"
    head += b"fmt " + struct.pack("<IHHIIHH", 16, 1, 1, rate, rate * 2, 2, 16)
    head += b"data" + struct.pack("<I", len(pcm))
    Path(path).write_bytes(head + pcm)


def trim(x, rate, keep=0.04, floor_db=-45.0):
    env = np.abs(x)
    thr = env.max() * 10 ** (floor_db / 20.0)
    idx = np.nonzero(env > thr)[0]
    if len(idx) == 0:
        return x
    k = int(keep * rate)
    return x[max(0, idx[0] - k):min(len(x), idx[-1] + k)]


def fft_filter(x, rate, gain_of_freq):
    n = 1 << int(np.ceil(np.log2(len(x) * 2)))
    spec = np.fft.rfft(x, n)
    f = np.fft.rfftfreq(n, 1.0 / rate)
    return np.fft.irfft(spec * gain_of_freq(f), n)[:len(x)]


def period_eq(f):
    f = np.maximum(f, 1.0)
    hp = 1.0 / np.sqrt(1.0 + (170.0 / f) ** 4)       # 2nd-order-ish high-pass
    lp = 1.0 / np.sqrt(1.0 + (f / 6000.0) ** 6)      # 3rd-order-ish low-pass
    bump = 1.0 + (10 ** (3.0 / 20.0) - 1.0) * np.exp(-0.5 * (np.log2(f / 2200.0) / 0.6) ** 2)
    return hp * lp * bump


def parade_ir(rate, seed):
    rng = np.random.default_rng(seed)
    n = int(1.3 * rate)
    t = np.arange(n) / rate
    tail = rng.standard_normal(n) * np.exp(-6.91 * t / 0.9)            # RT60 0.9 s
    tail[: int(0.03 * rate)] *= np.linspace(0, 1, int(0.03 * rate))    # tail builds up after 30 ms
    tail = fft_filter(tail, rate, lambda f: 1.0 / np.sqrt(1.0 + (np.maximum(f, 1) / 2500.0) ** 2)
                      / np.sqrt(1.0 + (250.0 / np.maximum(f, 1)) ** 2))
    tail /= np.sqrt(np.sum(tail ** 2)) + 1e-12
    ir = 0.35 * tail
    for delay, db in ((0.075, -14.0), (0.140, -19.0), (0.215, -26.0)):
        i = int(delay * rate)
        ir[i] += 10 ** (db / 20.0)
    return ir


def soft_sat(x, drive=1.6):
    peak = np.max(np.abs(x)) + 1e-12
    y = np.tanh(drive * x / peak) / np.tanh(drive)
    return y * peak


def process(x, rate, fx, seed):
    x = trim(x, rate)
    if fx == "period":
        x = fft_filter(x, rate, period_eq)
        x = soft_sat(x)
        x = np.concatenate([x, np.zeros(int(0.9 * rate))])
        ir = parade_ir(rate, seed)
        n = 1 << int(np.ceil(np.log2(len(x) + len(ir))))
        wet = np.fft.irfft(np.fft.rfft(x, n) * np.fft.rfft(ir, n), n)[:len(x)]
        wet *= (np.sqrt(np.mean(x ** 2)) / (np.sqrt(np.mean(wet ** 2)) + 1e-12)) * 0.45
        x = x + wet
        # cut the tail once it has died away, with a 60 ms fade
        x = trim(x, rate, keep=0.06, floor_db=-50.0)
    fade = int(0.008 * rate)
    x[:fade] *= np.linspace(0, 1, fade)
    x[-fade:] *= np.linspace(1, 0, fade)
    return x / (np.max(np.abs(x)) + 1e-12) * 10 ** (-1.0 / 20.0)


def main(argv):
    if len(argv) < 3:
        print(__doc__)
        return 2
    raw, out, tsv = Path(argv[0]), Path(argv[1]), Path(argv[2])
    fx = argv[argv.index("--fx") + 1] if "--fx" in argv else "period"
    voice = argv[argv.index("--voice") + 1] if "--voice" in argv else "bm_george"
    out.mkdir(parents=True, exist_ok=True)
    rows = [r.split("\t") for r in tsv.read_text("utf-8-sig").splitlines() if r.strip()]
    head, rows = rows[0], rows[1:]
    ci = {n: head.index(n) for n in head}
    man = ["# voice: Kokoro-82M %s, fx: %s. Generated by tools/voice; edit lines.tsv, not this file." % (voice, fx),
           "id\tfile\tseconds\tspeaker\ttext"]
    for r in rows:
        rid = r[ci["id"]]
        src = raw / (rid + ".wav")
        if not src.exists():
            print("missing", src)
            return 1
        x, rate = read_wav(src)
        y = process(x, rate, fx, zlib.crc32(rid.encode()))
        write_wav16(out / (rid + ".wav"), y, rate)
        man.append("%s\t%s.wav\t%.2f\t%s\t%s" % (rid, rid, len(y) / rate, r[ci["speaker"]], r[ci["text"]]))
        print("%-10s %5.2f s" % (rid, len(y) / rate))
    (out / "manifest.tsv").write_text("\n".join(man) + "\n", "utf-8", newline="\n")
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
