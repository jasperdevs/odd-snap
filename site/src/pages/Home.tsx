import { Link } from "react-router-dom";
import StarChart from "../components/StarChart";
import { Button } from "@/components/ui/button";
import {
  AccordionGroup,
  AccordionItem,
  AccordionTrigger,
  AccordionContent,
} from "@/components/ui/accordion";
import { Download } from "lucide-react";

function GithubIcon({ size = 16, strokeWidth = 1.5 }: { size?: number; strokeWidth?: number }) {
  return (
    <svg width={size} height={size} viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={strokeWidth} strokeLinecap="round" strokeLinejoin="round">
      <path d="M15 22v-4a4.8 4.8 0 0 0-1-3.5c3 0 6-2 6-5.5.08-1.25-.27-2.48-1-3.5.28-1.15.28-2.35 0-3.5 0 0-1 0-3 1.5-2.64-.5-5.36-.5-8 0C6 2 5 2 5 2c-.3 1.15-.3 2.35 0 3.5A5.403 5.403 0 0 0 4 9c0 3.5 3 5.5 6 5.5-.39.49-.68 1.05-.85 1.65-.17.6-.22 1.23-.15 1.85v4" />
      <path d="M9 18c-4.51 2-5-2-7-2" />
    </svg>
  );
}

const features = [
  { name: "Region capture", desc: "Rectangle, freeform, fullscreen, active window, and scrolling capture with delay timer and window detection" },
  { name: "Annotation tools", desc: "Arrows, curved arrows, text, shapes, highlights, blur, freehand, step numbers, emoji, ruler, magnifier, and eraser with undo/redo" },
  { name: "OCR & Translate", desc: "Extract text from your screen with Windows OCR, translate with Argos (offline) or Google Translate across 35+ languages" },
  { name: "Screen recording", desc: "Record as GIF, MP4, WebM, or MKV with mic and desktop audio at 15/24/30/60 FPS" },
  { name: "Stickers", desc: "Remove backgrounds with 5 local AI models or cloud providers, add shadow and stroke effects" },
  { name: "Color picker", desc: "Pick colors from anywhere on screen with magnified preview, hex/RGB values, and color history" },
  { name: "QR/Barcode scanner", desc: "Scan QR codes, Aztec, Data Matrix, PDF-417, CODE-128, EAN, UPC, and more" },
  { name: "Search history", desc: "Find past screenshots by filename, OCR text, or AI-powered semantic similarity" },
  { name: "Upload anywhere", desc: "19 destinations including Imgur, S3, Dropbox, GitHub, OneDrive, and custom HTTP" },
  { name: "Hotkeys", desc: "Fully configurable global hotkeys for every action with modifier key support" },
  { name: "Image formats", desc: "Save as PNG, JPEG, or BMP with configurable quality and custom naming patterns" },
  { name: "After-capture actions", desc: "Auto-copy, auto-save, auto-upload, auto-pin previews, or prompt for filename" },
  { name: "Settings import/export", desc: "Save and load your settings as JSON, reset to defaults anytime" },
  { name: "Start with Windows", desc: "Auto-launch on startup, runs quietly in the system tray" },
  { name: "Multiple monitors", desc: "Full multi-monitor support for capture, recording, and color picking" },
  { name: "Auto-updates", desc: "Background update checking keeps Yoink up to date" },
];

const faq = [
  { q: "What is Yoink?", a: "Yoink is a free, open-source screenshot and screen recording tool for Windows. It replaces tools like ShareX with a clean, modern interface." },
  { q: "Is Yoink free?", a: "Yes, completely free and open source under the GPL-3.0 license. No ads, no tracking, no premium tiers." },
  { q: "Does Yoink work offline?", a: "Yes. All capture, annotation, OCR, and recording features work fully offline. Only uploads and Google Translate require internet." },
  { q: "What Windows versions are supported?", a: "Windows 10 (version 1903+) and Windows 11. Both x64 and ARM64 are supported." },
  { q: "How does OCR work?", a: "Yoink uses the Windows built-in OCR engine. No downloads or setup needed. It supports all languages installed in your Windows language settings." },
  { q: "Can I upload screenshots automatically?", a: "Yes. Yoink supports auto-upload to 19 destinations: Imgur, ImgBB, Catbox, Litterbox, Gyazo, file.io, Uguu, transfer.sh, Dropbox, Google Drive, OneDrive, Azure Blob, GitHub, Immich, FTP, SFTP, WebDAV, S3-compatible storage (AWS, Cloudflare R2, Backblaze B2), and custom HTTP endpoints." },
  { q: "Where are screenshots saved?", a: "By default in your Pictures/Yoink folder. You can change this in Settings along with the file format and naming pattern." },
  { q: "What recording formats are supported?", a: "GIF, MP4, WebM, and MKV. You can record with microphone audio, desktop audio, or both. Frame rate and quality are configurable." },
  { q: "What translation services are supported?", a: "Yoink supports Argos Translate (fully offline, no API key needed) and Google Translate (requires internet). Both support 35+ languages including Japanese, Chinese, Korean, Arabic, and all major European languages." },
  { q: "What models does the sticker maker use?", a: "Yoink supports local background removal with 5 AI models: BRIA RMBG (recommended, best quality), BiRefNet Lite (high quality), ISNet General Use (balanced), U2Net (older), and U2Netp (fastest). You can also use cloud providers Remove.bg and Photoroom. Local models run on CPU or GPU (CUDA)." },
  { q: "How is Yoink different from ShareX?", a: "Yoink has a modern, clean interface with built-in sticker creation, semantic image search, and uses Windows native OCR instead of Tesseract. It focuses on being simple to use while still being powerful." },
  { q: "Can I customize hotkeys?", a: "Yes. Every action has a configurable global hotkey. You can set hotkeys for screenshot, OCR, color picker, recording, stickers, and more in Settings." },
  { q: "Does Yoink have a portable version?", a: "Yes. The Downloads page includes both a Windows installer (recommended) and a portable zip if you want to run Yoink without installing it." },
  { q: "How do I update Yoink?", a: "Installed builds can update through the app. You can also download the latest installer or portable build directly from the Downloads page." },
  { q: "Does Yoink support multiple monitors?", a: "Yes. Yoink fully supports multi-monitor setups for capture, recording, and color picking. You can capture regions across monitors or target a specific screen." },
];

export default function Home() {
  const base = import.meta.env.BASE_URL;

  return (
    <div>
      <section className="text-center py-24 px-8">
        <img src={base + "banner.svg"} alt="Yoink" className="w-96 mx-auto mb-10" />
        <h1 className="text-4xl font-bold tracking-tight mb-5 text-black">Yoink</h1>
        <p className="text-black/60 max-w-lg mx-auto leading-relaxed mb-10 text-[17px]">
          Capture, annotate, OCR, translate, make stickers, record video, and upload. All in one open-source tool for Windows.
        </p>
        <div className="flex items-center justify-center gap-4">
          <Button asChild variant="primary" size="lg" leadingIcon={Download}>
            <Link to="/downloads">Download for Windows</Link>
          </Button>
          <Button asChild variant="tertiary" size="lg" leadingIcon={GithubIcon}>
            <a href="https://github.com/jasperdevs/yoink" target="_blank" rel="noopener noreferrer">
              Source Code
            </a>
          </Button>
        </div>
      </section>

      <section className="border-t border-[#EBEBEB] py-16 px-8">
        <h2 className="font-bold text-lg mb-4 text-black">What is Yoink?</h2>
        <p className="text-black/60 leading-relaxed mb-8">
          Yoink is an open-source screen capture and productivity toolkit for Windows. It handles everything from quick screenshots to annotated recordings, OCR, translation, and one-click uploads.
        </p>
        <div className="space-y-3.5">
          {features.map((f) => (
            <div key={f.name} className="flex gap-4 leading-relaxed">
              <span className="text-black/40 shrink-0">[&#x2605;]</span>
              <span>
                <strong className="text-black">{f.name}</strong>
                <span className="text-black/60">&nbsp;&nbsp;{f.desc}</span>
              </span>
            </div>
          ))}
        </div>
        <div className="mt-8">
          <Button asChild variant="tertiary" size="lg">
            <Link to="/downloads">Download &rarr;</Link>
          </Button>
        </div>
      </section>

      <section className="border-t border-[#EBEBEB] py-16 px-8">
        <h2 className="font-bold text-lg mb-4 text-black">Powerful annotation tools</h2>
        <p className="text-black/60 leading-relaxed mb-6">
          [&#x2605;] Arrows, text, shapes, blur, highlights, freehand drawing, step numbers, emoji, ruler, and more. Everything you need to mark up screenshots before sharing.
        </p>
        <div className="rounded-lg border border-[#EBEBEB] overflow-hidden">
          <img loading="lazy" src={base + "annotations.png"} alt="Annotation tools showing arrows, shapes, text, blur, and more" className="w-full" />
        </div>
      </section>

      <section className="border-t border-[#EBEBEB] py-16 px-8">
        <h2 className="font-bold text-lg mb-4 text-black">Built-in sticker maker</h2>
        <p className="text-black/60 leading-relaxed mb-6">
          [&#x2605;] Turn any screenshot into a sticker by removing the background, then save, copy, or upload it like a normal image.
        </p>
        <div className="rounded-lg border border-[#EBEBEB] overflow-hidden">
          <img loading="lazy" src={base + "sticker-showcase.png"} alt="Sticker showcase showing background removal on a screenshot" className="w-full" />
        </div>
      </section>

      <section className="border-t border-[#EBEBEB] py-16 px-8">
        <h2 className="font-bold text-lg mb-4 text-black">OCR and translate</h2>
        <p className="text-black/60 leading-relaxed mb-6">
          [&#x2605;] Extract text from any region of your screen. Results open in a dedicated window where you can edit, copy, or translate the text instantly.
        </p>
        <div className="rounded-lg border border-[#EBEBEB] overflow-hidden">
          <img loading="lazy" src={base + "ocr-screenshot.png"} alt="OCR result window showing extracted text" className="w-full" />
        </div>
      </section>

      <section className="border-t border-[#EBEBEB] py-16 px-8">
        <h2 className="font-bold text-lg mb-4 text-black">Search your history</h2>
        <p className="text-black/60 leading-relaxed mb-6">
          [&#x2605;] Search your image history by filename, OCR text, and semantic matching, so you can find screenshots by what they say or by what they show.
        </p>
        <div className="rounded-lg border border-[#EBEBEB] overflow-hidden">
          <img loading="lazy" src={base + "search-screenshot.png"} alt="Search history interface with results" className="w-full" style={{ marginBottom: "-20%", clipPath: "inset(0 0 20% 0)" }} />
        </div>
      </section>

      <section className="border-t border-[#EBEBEB] py-16 px-8">
        <h2 className="font-bold text-lg mb-4 text-black">Color picker</h2>
        <p className="text-black/60 leading-relaxed mb-6">
          [&#x2605;] Pick any color from your screen with a magnified preview. Copies hex and RGB values to your clipboard instantly.
        </p>
        <div className="rounded-lg border border-[#EBEBEB] overflow-hidden">
          <img loading="lazy" src={base + "color-picker.png"} alt="Color picker with magnified preview" className="w-full" />
        </div>
      </section>

      <section className="border-t border-[#EBEBEB] py-16 px-8">
        <h2 className="font-bold text-lg mb-4 text-black">Screen recording</h2>
        <p className="text-black/60 leading-relaxed mb-6">
          [&#x2605;] Record your screen as GIF, MP4, WebM, or MKV. Capture microphone and desktop audio simultaneously with configurable frame rate and quality.
        </p>
        <div className="rounded-lg border border-[#EBEBEB] overflow-hidden">
          <img loading="lazy" src={base + "recording.png"} alt="Screen recording interface" className="w-full" />
        </div>
      </section>

      <section className="border-t border-[#EBEBEB] py-16 px-8">
        <h2 className="font-bold text-lg mb-4 text-black">Built for privacy</h2>
        <p className="text-black/60 leading-relaxed">
          [&#x2605;] Yoink runs entirely on your machine. No accounts, no telemetry, no cloud dependencies. Your screenshots never leave your computer unless you choose to upload them.
        </p>
      </section>

      <section className="border-t border-[#EBEBEB] py-16 px-8">
        <h2 className="font-bold text-lg mb-6 text-black">FAQ</h2>
        <AccordionGroup type="single" collapsible className="w-full max-w-full">
          {faq.map((item, i) => (
            <AccordionItem key={item.q} value={item.q} index={i}>
              <AccordionTrigger>{item.q}</AccordionTrigger>
              <AccordionContent>{item.a}</AccordionContent>
            </AccordionItem>
          ))}
        </AccordionGroup>
      </section>

      <section className="border-t border-[#EBEBEB] py-16 px-8">
        <h2 className="font-bold text-lg mb-4 text-black">Open source</h2>
        <p className="text-black/60 mb-6">
          Free and open source, licensed under GPL-3.0.
        </p>
        <StarChart />
      </section>
    </div>
  );
}
