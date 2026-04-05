import { Link } from "react-router-dom";
import { useStarCount } from "../hooks/useStarCount";
import { useState } from "react";

const features = [
  { name: "Region capture", desc: "Rectangle, freeform, fullscreen, active window, and scrolling capture" },
  { name: "Annotation tools", desc: "Arrows, text, shapes, blur, freehand, step numbers, emoji, and ruler" },
  { name: "OCR & Translate", desc: "Extract text from your screen with Windows OCR, translate with Argos or Google" },
  { name: "Screen recording", desc: "Record as GIF, MP4, WebM, or MKV with mic and desktop audio" },
  { name: "Stickers", desc: "Remove backgrounds from captures with local or cloud providers" },
  { name: "Color picker", desc: "Pick colors from anywhere on screen with hex/RGB values" },
  { name: "QR/Barcode scanner", desc: "Scan QR codes and barcodes from screen regions" },
  { name: "Search history", desc: "Find past screenshots by filename, OCR text, or semantic similarity" },
  { name: "Upload anywhere", desc: "15+ services including Imgur, S3, Dropbox, GitHub, and custom HTTP" },
  { name: "Hotkeys", desc: "Fully configurable global hotkeys for every action" },
];

const faq = [
  { q: "What is Yoink?", a: "Yoink is a free, open-source screenshot and screen recording tool for Windows. It replaces tools like ShareX with a clean, modern interface." },
  { q: "Is Yoink free?", a: "Yes, completely free and open source under the GPL-3.0 license. No ads, no tracking, no premium tiers." },
  { q: "Does Yoink work offline?", a: "Yes. All capture, annotation, OCR, and recording features work fully offline. Only uploads and Google Translate require internet." },
  { q: "What Windows versions are supported?", a: "Windows 10 (version 1903+) and Windows 11. Both x64 and ARM64 are supported." },
  { q: "How does OCR work?", a: "Yoink uses the Windows built-in OCR engine. No downloads or setup needed. It supports all languages installed in your Windows language settings." },
  { q: "Can I upload screenshots automatically?", a: "Yes. Yoink supports auto-upload to 15+ services including Imgur, S3, Dropbox, Google Drive, and custom HTTP endpoints." },
  { q: "Where are screenshots saved?", a: "By default in your Pictures/Yoink folder. You can change this in Settings along with the file format and naming pattern." },
];

function FaqItem({ q, a }: { q: string; a: string }) {
  const [open, setOpen] = useState(false);
  return (
    <button onClick={() => setOpen(!open)} className="w-full text-left border-t border-zinc-800 py-4 cursor-pointer">
      <div className="flex items-start gap-3">
        <span className="text-zinc-600 text-sm mt-0.5 shrink-0">{open ? "−" : "+"}</span>
        <div>
          <span className="text-sm font-semibold text-zinc-200">{q}</span>
          {open && <p className="text-sm text-zinc-500 mt-2 leading-relaxed">{a}</p>}
        </div>
      </div>
    </button>
  );
}

export default function Home() {
  const stars = useStarCount();
  const base = import.meta.env.BASE_URL;

  return (
    <div>
      {/* Hero */}
      <section className="text-center py-16 px-6">
        <img src={base + "banner.svg"} alt="Yoink" className="w-48 mx-auto mb-6 opacity-80" />
        <h1 className="text-4xl font-bold tracking-tight mb-4">Yoink</h1>
        <p className="text-sm text-zinc-500 max-w-md mx-auto leading-relaxed mb-8">
          Capture, annotate, OCR, translate, make stickers, record video, and upload. All in one open-source tool for Windows.
        </p>
        <div className="flex items-center justify-center gap-3">
          <Link
            to="/downloads"
            className="inline-flex items-center px-5 py-2 rounded-md bg-zinc-50 text-zinc-950 text-xs font-medium hover:bg-zinc-200 transition-colors"
          >
            Download for Windows
          </Link>
          <a
            href="https://github.com/jasperdevs/yoink"
            target="_blank"
            rel="noopener noreferrer"
            className="inline-flex items-center px-5 py-2 rounded-md border border-zinc-800 text-xs font-medium text-zinc-400 hover:bg-zinc-800/50 transition-colors"
          >
            Source Code
          </a>
        </div>
      </section>

      {/* What is Yoink */}
      <section className="border-t border-zinc-800 py-14 px-6">
        <h2 className="text-base font-bold mb-3">What is Yoink?</h2>
        <p className="text-sm text-zinc-500 leading-relaxed mb-6">
          Yoink is an open-source screen capture and productivity toolkit for Windows. It handles everything from quick screenshots to annotated recordings, OCR, translation, and one-click uploads.
        </p>
        <div className="space-y-2.5">
          {features.map((f) => (
            <div key={f.name} className="flex gap-3 text-xs leading-relaxed">
              <span className="text-zinc-600 shrink-0">[*]</span>
              <span>
                <strong className="text-zinc-200">{f.name}</strong>
                <span className="text-zinc-500">&nbsp;&nbsp;{f.desc}</span>
              </span>
            </div>
          ))}
        </div>
        <div className="mt-6">
          <Link
            to="/downloads"
            className="inline-flex items-center gap-2 px-5 py-2 rounded-md border border-zinc-800 text-xs font-medium text-zinc-300 hover:bg-zinc-800/50 transition-colors"
          >
            Download &rarr;
          </Link>
        </div>
      </section>

      {/* Stickers showcase */}
      <section className="border-t border-zinc-800 py-14 px-6">
        <h2 className="text-base font-bold mb-3">Built-in sticker maker</h2>
        <p className="text-sm text-zinc-500 leading-relaxed mb-6">
          [*] Turn any screenshot into a sticker by removing the background, then save, copy, or upload it like a normal image.
        </p>
        <div className="rounded-lg border border-zinc-800 overflow-hidden">
          <img src={base + "sticker-showcase.png"} alt="Sticker showcase" className="w-full" />
        </div>
      </section>

      {/* OCR screenshot */}
      <section className="border-t border-zinc-800 py-14 px-6">
        <h2 className="text-base font-bold mb-3">OCR and translate</h2>
        <p className="text-sm text-zinc-500 leading-relaxed mb-6">
          [*] Extract text from any region of your screen. Results open in a dedicated window where you can edit, copy, or translate the text instantly.
        </p>
        <div className="rounded-lg border border-zinc-800 overflow-hidden">
          <img src={base + "ocr-screenshot.png"} alt="OCR result" className="w-full" />
        </div>
      </section>

      {/* Search */}
      <section className="border-t border-zinc-800 py-14 px-6">
        <h2 className="text-base font-bold mb-3">Search your history</h2>
        <p className="text-sm text-zinc-500 leading-relaxed mb-6">
          [*] Search your image history by filename, OCR text, and semantic matching, so you can find screenshots by what they say or by what they show.
        </p>
        <div className="rounded-lg border border-zinc-800 overflow-hidden">
          <img src={base + "search-screenshot.png"} alt="Search history" className="w-full" />
        </div>
      </section>

      {/* Privacy */}
      <section className="border-t border-zinc-800 py-14 px-6">
        <h2 className="text-base font-bold mb-3">Built for privacy</h2>
        <p className="text-sm text-zinc-500 leading-relaxed">
          [*] Yoink runs entirely on your machine. No accounts, no telemetry, no cloud dependencies. Your screenshots never leave your computer unless you choose to upload them.
        </p>
      </section>

      {/* Star history */}
      <section className="border-t border-zinc-800 py-14 px-6">
        <h2 className="text-base font-bold mb-3">Open source</h2>
        <p className="text-sm text-zinc-500 mb-6">
          Free and open source with{" "}
          <span className="text-zinc-200 font-medium">
            {stars !== null ? stars.toLocaleString() : "..."}
          </span>{" "}
          GitHub stars. Licensed under GPL-3.0.
        </p>
        <div className="rounded-lg border border-zinc-800 overflow-hidden">
          <img
            src="https://api.star-history.com/image?repos=jasperdevs/yoink&type=timeline&theme=dark&legend=top-left"
            alt="Star History Chart"
            className="w-full"
          />
        </div>
      </section>

      {/* FAQ */}
      <section className="border-t border-zinc-800 py-14 px-6">
        <h2 className="text-base font-bold mb-4">FAQ</h2>
        <div>
          {faq.map((item) => (
            <FaqItem key={item.q} q={item.q} a={item.a} />
          ))}
        </div>
      </section>
    </div>
  );
}
