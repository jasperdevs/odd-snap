import { Link } from "react-router-dom";
import { useState } from "react";
import StarChart from "../components/StarChart";
import { DitheringShader } from "../components/DitheringShader";

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
  { q: "What recording formats are supported?", a: "GIF, MP4, WebM, and MKV. You can record with microphone audio, desktop audio, or both. Frame rate and quality are configurable." },
  { q: "How is Yoink different from ShareX?", a: "Yoink has a modern, clean interface with built-in sticker creation, semantic image search, and uses Windows native OCR instead of Tesseract. It focuses on being simple to use while still being powerful." },
  { q: "Can I customize hotkeys?", a: "Yes. Every action has a configurable global hotkey. You can set hotkeys for screenshot, OCR, color picker, recording, stickers, and more in Settings." },
  { q: "Does Yoink have a portable version?", a: "Yes. The standalone .exe from the Downloads page works without installation. Just run it from any folder." },
];

function FaqItem({ q, a }: { q: string; a: string }) {
  const [open, setOpen] = useState(false);
  const id = q.replace(/\s+/g, "-").toLowerCase();
  return (
    <div className="border-t border-[#2a2a28]">
      <button
        onClick={() => setOpen(!open)}
        aria-expanded={open}
        aria-controls={`faq-${id}`}
        className="w-full text-left py-5 cursor-pointer"
      >
        <div className="flex items-start gap-4">
          <span className="text-[#555550] shrink-0 text-lg" aria-hidden="true">{open ? "\u2212" : "+"}</span>
          <span className="font-medium text-[#e8e6e3]">{q}</span>
        </div>
      </button>
      <div
        id={`faq-${id}`}
        role="region"
        aria-labelledby={`faq-${id}`}
        hidden={!open}
      >
        {open && <p className="text-[#8a8a80] pb-5 pl-8 leading-relaxed">{a}</p>}
      </div>
    </div>
  );
}

export default function Home() {
  const base = import.meta.env.BASE_URL;

  return (
    <div>
      {/* Hero with shader background */}
      <section className="relative text-center py-24 px-8 overflow-hidden">
        <div className="absolute inset-0 opacity-[0.12]">
          <DitheringShader
            colorBack="#000000"
            colorFront="#ffffff"
            pxSize={4}
            speed={0.9}
          />
        </div>
        <div className="relative z-10">
          <img src={base + "banner.svg"} alt="Yoink" className="w-96 mx-auto mb-10 opacity-70" />
          <h1 className="text-4xl font-bold tracking-tight mb-5 text-[#e8e6e3]">Yoink</h1>
          <p className="text-[#8a8a80] max-w-lg mx-auto leading-relaxed mb-10 text-[17px]">
            Capture, annotate, OCR, translate, make stickers, record video, and upload. All in one open-source tool for Windows.
          </p>
          <div className="flex items-center justify-center gap-4">
            <Link
              to="/downloads"
              className="inline-flex items-center px-7 py-3 rounded-md bg-[#e8e6e3] text-[#111110] text-[15px] font-medium hover:bg-[#d0cec8] transition-colors"
            >
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="w-4 h-4"><path d="M10.75 2.75a.75.75 0 0 0-1.5 0v8.614L6.295 8.235a.75.75 0 1 0-1.09 1.03l4.25 4.5a.75.75 0 0 0 1.09 0l4.25-4.5a.75.75 0 0 0-1.09-1.03l-2.955 3.129V2.75Z" /><path d="M3.5 12.75a.75.75 0 0 0-1.5 0v2.5A2.75 2.75 0 0 0 4.75 18h10.5A2.75 2.75 0 0 0 18 15.25v-2.5a.75.75 0 0 0-1.5 0v2.5c0 .69-.56 1.25-1.25 1.25H4.75c-.69 0-1.25-.56-1.25-1.25v-2.5Z" /></svg>
              Download for Windows
            </Link>
            <a
              href="https://github.com/jasperdevs/yoink"
              target="_blank"
              rel="noopener noreferrer"
              className="inline-flex items-center gap-2 px-7 py-3 rounded-md border border-[#2a2a28] text-[15px] font-medium text-[#8a8a80] hover:text-[#e8e6e3] hover:border-[#444440] transition-colors"
            >
              <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" className="w-4 h-4"><path d="M12 .297c-6.63 0-12 5.373-12 12 0 5.303 3.438 9.8 8.205 11.385.6.113.82-.258.82-.577 0-.285-.01-1.04-.015-2.04-3.338.724-4.042-1.61-4.042-1.61C4.422 18.07 3.633 17.7 3.633 17.7c-1.087-.744.084-.729.084-.729 1.205.084 1.838 1.236 1.838 1.236 1.07 1.835 2.809 1.305 3.495.998.108-.776.417-1.305.76-1.605-2.665-.3-5.466-1.332-5.466-5.93 0-1.31.465-2.38 1.235-3.22-.135-.303-.54-1.523.105-3.176 0 0 1.005-.322 3.3 1.23.96-.267 1.98-.399 3-.405 1.02.006 2.04.138 3 .405 2.28-1.552 3.285-1.23 3.285-1.23.645 1.653.24 2.873.12 3.176.765.84 1.23 1.91 1.23 3.22 0 4.61-2.805 5.625-5.475 5.92.42.36.81 1.096.81 2.22 0 1.606-.015 2.896-.015 3.286 0 .315.21.69.825.57C20.565 22.092 24 17.592 24 12.297c0-6.627-5.373-12-12-12"/></svg>
              Source Code
            </a>
          </div>
        </div>
      </section>

      {/* What is Yoink */}
      <section className="border-t border-[#2a2a28] py-16 px-8">
        <h2 className="font-bold text-lg mb-4 text-[#e8e6e3]">What is Yoink?</h2>
        <p className="text-[#8a8a80] leading-relaxed mb-8">
          Yoink is an open-source screen capture and productivity toolkit for Windows. It handles everything from quick screenshots to annotated recordings, OCR, translation, and one-click uploads.
        </p>
        <div className="space-y-3.5">
          {features.map((f) => (
            <div key={f.name} className="flex gap-4 leading-relaxed">
              <span className="text-[#555550] shrink-0">[&#x2605;]</span>
              <span>
                <strong className="text-[#e8e6e3]">{f.name}</strong>
                <span className="text-[#8a8a80]">&nbsp;&nbsp;{f.desc}</span>
              </span>
            </div>
          ))}
        </div>
        <div className="mt-8">
          <Link
            to="/downloads"
            className="inline-flex items-center gap-2 px-6 py-3 rounded-md border border-[#2a2a28] font-medium text-[#d0cec8] hover:bg-[#1c1c1a] hover:border-[#444440] transition-colors"
          >
            Download &rarr;
          </Link>
        </div>
      </section>

      {/* Stickers */}
      <section className="border-t border-[#2a2a28] py-16 px-8">
        <h2 className="font-bold text-lg mb-4 text-[#e8e6e3]">Built-in sticker maker</h2>
        <p className="text-[#8a8a80] leading-relaxed mb-6">
          [&#x2605;] Turn any screenshot into a sticker by removing the background, then save, copy, or upload it like a normal image.
        </p>
        <div className="rounded-lg border border-[#2a2a28] overflow-hidden">
          <img loading="lazy" src={base + "sticker-showcase.png"} alt="Sticker showcase showing background removal on a screenshot" className="w-full" />
        </div>
      </section>

      {/* OCR */}
      <section className="border-t border-[#2a2a28] py-16 px-8">
        <h2 className="font-bold text-lg mb-4 text-[#e8e6e3]">OCR and translate</h2>
        <p className="text-[#8a8a80] leading-relaxed mb-6">
          [&#x2605;] Extract text from any region of your screen. Results open in a dedicated window where you can edit, copy, or translate the text instantly.
        </p>
        <div className="rounded-lg border border-[#2a2a28] overflow-hidden">
          <img loading="lazy" src={base + "ocr-screenshot.png"} alt="OCR result window showing extracted text" className="w-full" />
        </div>
      </section>

      {/* Search */}
      <section className="border-t border-[#2a2a28] py-16 px-8">
        <h2 className="font-bold text-lg mb-4 text-[#e8e6e3]">Search your history</h2>
        <p className="text-[#8a8a80] leading-relaxed mb-6">
          [&#x2605;] Search your image history by filename, OCR text, and semantic matching, so you can find screenshots by what they say or by what they show.
        </p>
        <div className="rounded-lg border border-[#2a2a28] overflow-hidden">
          <img loading="lazy" src={base + "search-screenshot.png"} alt="Search history interface with results" className="w-full" style={{ marginBottom: "-20%", clipPath: "inset(0 0 20% 0)" }} />
        </div>
      </section>

      {/* Color picker */}
      <section className="border-t border-[#2a2a28] py-16 px-8">
        <h2 className="font-bold text-lg mb-4 text-[#e8e6e3]">Color picker</h2>
        <p className="text-[#8a8a80] leading-relaxed mb-6">
          [&#x2605;] Pick any color from your screen with a magnified preview. Copies hex and RGB values to your clipboard instantly.
        </p>
        <div className="rounded-lg border border-[#2a2a28] overflow-hidden flex justify-center" style={{ background: "#0a0a09" }}>
          <img loading="lazy" src={base + "color-picker.png"} alt="Color picker with magnified preview" className="max-h-96 object-contain" />
        </div>
      </section>

      {/* Recording */}
      <section className="border-t border-[#2a2a28] py-16 px-8">
        <h2 className="font-bold text-lg mb-4 text-[#e8e6e3]">Screen recording</h2>
        <p className="text-[#8a8a80] leading-relaxed mb-6">
          [&#x2605;] Record your screen as GIF, MP4, WebM, or MKV. Capture microphone and desktop audio simultaneously with configurable frame rate and quality.
        </p>
        <div className="rounded-lg border border-[#2a2a28] overflow-hidden flex justify-center" style={{ background: "#0a0a09" }}>
          <img loading="lazy" src={base + "recording.png"} alt="Screen recording interface" className="max-h-96 object-contain" />
        </div>
      </section>

      {/* Privacy */}
      <section className="border-t border-[#2a2a28] py-16 px-8">
        <h2 className="font-bold text-lg mb-4 text-[#e8e6e3]">Built for privacy</h2>
        <p className="text-[#8a8a80] leading-relaxed">
          [&#x2605;] Yoink runs entirely on your machine. No accounts, no telemetry, no cloud dependencies. Your screenshots never leave your computer unless you choose to upload them.
        </p>
      </section>

      {/* FAQ */}
      <section className="border-t border-[#2a2a28] py-16 px-8">
        <h2 className="font-bold text-lg mb-4 text-[#e8e6e3]">FAQ</h2>
        <div role="list">
          {faq.map((item) => (
            <FaqItem key={item.q} q={item.q} a={item.a} />
          ))}
        </div>
      </section>

      {/* Star chart */}
      <section className="border-t border-[#2a2a28] py-16 px-8">
        <h2 className="font-bold text-lg mb-4 text-[#e8e6e3]">Open source</h2>
        <p className="text-[#8a8a80] mb-6">
          Free and open source, licensed under GPL-3.0.
        </p>
        <StarChart />
      </section>
    </div>
  );
}
