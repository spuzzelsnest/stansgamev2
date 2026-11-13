// Generates the instructor's lines with Kokoro-82M (kokoro-js, Apache-2.0), offline, on the CPU.
//
//   node make_voice.mjs <lines.tsv> <out folder> [voice] [speed]
//
// lines.tsv: header "id<TAB>speaker<TAB>text<TAB>say"; the "say" column is what the voice reads
// (the subtitle shows "text"). Writes <out>/<id>.wav (24 kHz mono float, as Kokoro gives it); the
// period processing and the level are done afterwards by process_voice.py.
// kokoro-js must be installed next to this script or on NODE_PATH (npm install kokoro-js@1.2.1);
// the first run downloads the model (onnx-community/Kokoro-82M-v1.0-ONNX, about 330 MB, fp32).
import { readFileSync, mkdirSync } from "node:fs";
import { join } from "node:path";
import { KokoroTTS } from "kokoro-js";

const [tsv, out, voice = "bm_george", speed = "1.0"] = process.argv.slice(2);
if (!tsv || !out) { console.error("usage: node make_voice.mjs <lines.tsv> <out folder> [voice] [speed]"); process.exit(2); }
mkdirSync(out, { recursive: true });

const rows = readFileSync(tsv, "utf8").replace(/^﻿/, "").split(/\r?\n/).filter(l => l.trim());
const head = rows.shift().split("\t");
const col = n => head.indexOf(n);
if (col("id") < 0 || col("text") < 0) { console.error("lines.tsv needs id and text columns"); process.exit(2); }

const tts = await KokoroTTS.from_pretrained("onnx-community/Kokoro-82M-v1.0-ONNX", { dtype: "fp32", device: "cpu" });
for (const r of rows) {
  const c = r.split("\t");
  const id = c[col("id")];
  const say = (col("say") >= 0 && c[col("say")]) ? c[col("say")] : c[col("text")];
  const t0 = Date.now();
  const audio = await tts.generate(say, { voice, speed: Number(speed) });
  audio.save(join(out, id + ".wav"));
  console.log(id, ((Date.now() - t0) / 1000).toFixed(1) + " s", (audio.audio.length / audio.sampling_rate).toFixed(2) + " s audio");
}
