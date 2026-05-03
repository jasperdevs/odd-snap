import { useEffect, useState } from "react";
import StarChart from "../components/StarChart";
import { useReleases } from "../hooks/useReleases";
import {
  AccordionGroup,
  AccordionItem,
  AccordionTrigger,
  AccordionContent,
} from "@/components/ui/accordion";
import { Tabs, TabsList, TabItem, TabPanel } from "@/components/ui/tabs";
import { Button } from "@/components/ui/button";

const features = [
  "region capture",
  "scrolling capture",
  "active window capture",
  "delay timer",
  "annotation tools",
  "step numbers",
  "emoji & ruler",
  "blur & highlight",
  "magnifier",
  "100+ ocr languages",
  "argos offline translate",
  "qr/barcode scanner",
  "tray menu",
  "global hotkeys",
  "multi-monitor",
  "png, jpeg, bmp",
  "15/24/30/60 fps recording",
  "mic + desktop audio",
  "start with windows",
  "auto-updates",
  "gpl-3.0 licensed",
];

const showcase = [
  { title: "annotate", desc: "arrows, text, shapes, blur, highlights, freehand, step numbers, emoji, and ruler with undo/redo.", img: "annotations.png" },
  { title: "ocr & translate", desc: "extract text from any region with 100+ ocr languages. translate offline with argos or online with google.", img: "ocr-screenshot.png" },
  { title: "ai redirects", desc: "open chatgpt, claude, gemini, or google lens right after capture. image stays pinned and ready to drop in.", img: "ai-redirects.png" },
  { title: "upscale", desc: "upscale any capture locally with swinir x4 or real-esrgan x4plus. compare before and after side by side.", img: "upscale.png" },
  { title: "stickers", desc: "remove backgrounds locally with 5 ai models. add stroke and shadow finishing, save as transparent png.", img: "sticker-showcase.png" },
  { title: "record", desc: "save as gif, mp4, webm, or mkv. microphone and desktop audio at 15/24/30/60 fps.", img: "recording.png" },
  { title: "search", desc: "find past screenshots by filename, ocr text, or ai-powered semantic similarity.", img: "search-screenshot.png" },
  { title: "color picker", desc: "pick any color on screen with a magnified preview. hex and rgb to clipboard.", img: "color-picker.png" },
  { title: "uploads", desc: "19 destinations: imgur, s3/r2/b2, dropbox, github, onedrive, immich, webdav, and more.", img: "uploads.png" },
];

const faq = [
  { q: "what is oddsnap?", a: "oddsnap is a free, open-source screenshot and screen recording tool for windows. it replaces tools like sharex with a clean, modern interface." },
  { q: "is oddsnap free?", a: "yes, completely free and open source under the gpl-3.0 license. no ads, no tracking, no premium tiers." },
  { q: "does oddsnap work offline?", a: "yes. all capture, annotation, ocr, and recording features work fully offline. only uploads and google translate require internet." },
  { q: "what windows versions are supported?", a: "windows 10 (version 1903+) and windows 11. both x64 and arm64 are supported." },
  { q: "how does ocr work?", a: "oddsnap uses the windows built-in ocr engine. no downloads or setup needed. it supports all languages installed in your windows language settings." },
  { q: "can i upload screenshots automatically?", a: "yes. oddsnap supports auto-upload to 19 destinations: imgur, imgbb, catbox, litterbox, gyazo, file.io, uguu, tmpfiles, transfer.sh, dropbox, google drive, onedrive, azure blob, github, immich, ftp, sftp, webdav, s3-compatible storage (aws, cloudflare r2, backblaze b2), and custom http endpoints." },
  { q: "where are screenshots saved?", a: "by default in your pictures/oddsnap folder. you can change this in settings along with the file format and naming pattern." },
  { q: "what recording formats are supported?", a: "gif, mp4, webm, and mkv. you can record with microphone audio, desktop audio, or both. frame rate and quality are configurable." },
  { q: "what translation services are supported?", a: "oddsnap supports argos translate (fully offline, no api key needed) and google translate (requires internet). both support 35+ languages." },
  { q: "how is oddsnap different from sharex?", a: "oddsnap has a modern, clean interface with built-in sticker creation, ai redirects, image upscaling, and semantic image search. it focuses on being simple to use while still being powerful." },
  { q: "can i customize hotkeys?", a: "yes. every action has a configurable global hotkey. you can set hotkeys for screenshot, ocr, color picker, recording, stickers, and more in settings." },
  { q: "does oddsnap have a portable version?", a: "yes. the downloads page includes both a windows installer (recommended) and a portable zip." },
  { q: "how do i update oddsnap?", a: "installed builds can update through the app. you can also download the latest installer or portable build directly from the downloads page." },
  { q: "does oddsnap support multiple monitors?", a: "yes. oddsnap fully supports multi-monitor setups for capture, recording, and color picking. you can capture regions across monitors or target a specific screen." },
];

function detectArch(): "arm64" | "x64" {
  const ua = navigator.userAgent.toLowerCase();
  if (ua.includes("arm64") || ua.includes("aarch64")) return "arm64";
  return "x64";
}

function pickInstaller(release: { assets: { name: string; browser_download_url: string }[] }, arch: "arm64" | "x64") {
  const exes = release.assets.filter((a) => a.name.toLowerCase().endsWith(".exe"));
  const match = exes.find((a) => a.name.toLowerCase().includes(arch));
  const installer = exes.find((a) => a.name.toLowerCase().includes("setup"));
  return (match ?? installer ?? exes[0])?.browser_download_url ?? null;
}

function WindowsIcon() {
  return (
    <svg viewBox="0 0 88 88" width="14" height="14" fill="currentColor" aria-hidden="true">
      <path d="m0 12.402 35.687-4.86.016 34.423-35.67.203zm35.67 33.529.028 34.453L.028 75.48.026 45.7zm4.326-39.025L87.314 0v41.527l-47.318.376zm47.329 39.349-.011 41.34-47.318-6.678-.066-34.739z" />
    </svg>
  );
}

const AUTO_MS = 5000;
const TRANSITION_MS = 600;

function Showcase() {
  const base = import.meta.env.BASE_URL;
  const N = showcase.length;
  const [idx, setIdx] = useState(0); // 0..N (N = clone of 0 for seamless wrap)
  const [animate, setAnimate] = useState(true);
  const visible = idx % N;

  useEffect(() => {
    const timer = setInterval(() => {
      setAnimate(true);
      setIdx((i) => i + 1);
    }, AUTO_MS);
    return () => clearInterval(timer);
  }, []);

  useEffect(() => {
    if (idx !== N) return;
    const t = setTimeout(() => {
      setAnimate(false);
      setIdx(0);
    }, TRANSITION_MS + 20);
    return () => clearTimeout(t);
  }, [idx, N]);

  useEffect(() => {
    if (animate) return;
    const r = requestAnimationFrame(() =>
      requestAnimationFrame(() => setAnimate(true))
    );
    return () => cancelAnimationFrame(r);
  }, [animate]);

  const clickTab = (i: number) => {
    setAnimate(true);
    setIdx(i);
  };

  const slotPct = 100 / (N + 1);

  return (
    <div>
      <div className="mb-4 overflow-x-auto no-scrollbar">
        <Tabs selectedIndex={visible} onSelect={clickTab}>
          <TabsList className="bg-[#E7DFD1]/80">
            {showcase.map((s) => (
              <TabItem key={s.title} value={s.title} label={s.title} />
            ))}
          </TabsList>
          {showcase.map((s) => (
            <TabPanel key={s.title} value={s.title} className="sr-only" />
          ))}
        </Tabs>
      </div>

      <div className="overflow-hidden rounded-[1.35rem] border border-[#DDD5C7] bg-[#FFFDF8] shadow-[0_28px_90px_rgba(72,57,34,0.14)]">
        <div className="aspect-[16/10] w-full overflow-hidden relative">
          <div className="pointer-events-none absolute inset-0 z-10 shadow-[inset_0_0_0_1px_rgba(255,255,255,0.44),inset_0_-80px_90px_rgba(23,21,18,0.05)]" />
          <div
            className="absolute inset-0 flex"
            style={{
              width: `${(N + 1) * 100}%`,
              transform: `translateX(-${idx * slotPct}%)`,
              transition: animate ? `transform ${TRANSITION_MS}ms cubic-bezier(0.22, 1, 0.36, 1)` : "none",
            }}
          >
            {[...showcase, showcase[0]].map((s, i) => (
              <div
                key={`${s.title}-${i}`}
                className="h-full shrink-0"
                style={{ width: `${slotPct}%` }}
              >
                <img
                  src={base + s.img}
                  alt={s.title}
                  className="w-full h-full object-cover object-top"
                  loading={i <= 1 ? "eager" : "lazy"}
                />
              </div>
            ))}
          </div>
        </div>
      </div>

      <p className="mt-5 text-[15px] text-[#171512]/68 leading-relaxed max-w-[70ch]">
        {showcase[visible].desc}
      </p>
    </div>
  );
}

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="pt-14 pb-5">
      <h2 className="mb-5 text-[22px] font-semibold leading-tight tracking-[-0.01em] text-[#171512] text-balance">{title}</h2>
      {children}
    </section>
  );
}

export default function Home() {
  const base = import.meta.env.BASE_URL;
  const { releases } = useReleases();
  const [downloadUrl, setDownloadUrl] = useState<string | null>(null);

  useEffect(() => {
    if (releases.length === 0) return;
    const arch = detectArch();
    const latest = releases[0];
    setDownloadUrl(pickInstaller(latest, arch));
  }, [releases]);

  return (
    <div className="space-y-2">
      <section className="relative pt-20 pb-24 sm:pt-24 sm:pb-28">
        <div className="absolute left-1/2 top-8 -z-10 h-[34rem] w-[min(58rem,100vw)] -translate-x-1/2 rounded-full bg-[radial-gradient(circle,rgba(255,253,248,0.94)_0%,rgba(231,220,200,0.58)_42%,rgba(244,241,234,0)_72%)] blur-sm" />
        <div className="mx-auto flex max-w-[760px] flex-col items-center text-center">
          <div className="mb-9 rounded-[2rem] border border-[#DDD5C7] bg-[#FFFDF8]/72 p-4 shadow-[0_24px_70px_rgba(72,57,34,0.13)] backdrop-blur">
            <img
              src={base + "oddsnap-square.png"}
              alt=""
              className="h-28 w-28 sm:h-32 sm:w-32"
            />
          </div>
          <div className="brand-wordmark mb-9 px-3">
            <img
              src={base + "oddsnap.png"}
              alt="OddSnap"
              className="w-full"
              style={{
                filter: "brightness(0) saturate(100%) invert(8%) sepia(8%) saturate(879%) hue-rotate(353deg) brightness(91%) contrast(94%)",
              }}
            />
          </div>
          <p className="mb-10 max-w-[20rem] text-[19px] leading-[1.65] text-[#171512]/68 text-pretty sm:max-w-[54ch] sm:text-[22px]">
            your new favorite, free, open source screenshot and recording tool for windows.
          </p>
        </div>
        <div className="flex flex-wrap items-center justify-center gap-3">
          {downloadUrl ? (
            <Button asChild size="lg" variant="primary" className="h-12 px-8 text-[17px] gap-2 shadow-[0_16px_40px_rgba(23,21,18,0.2)] hover:-translate-y-0.5 active:translate-y-0">
              <a href={downloadUrl}>
                <WindowsIcon />
                download for windows
              </a>
            </Button>
          ) : (
            <Button size="lg" variant="primary" className="h-12 px-8 text-[17px]" loading>
              loading
            </Button>
          )}
        </div>
      </section>

      <Section title="everything in one tool">
        <Showcase />
      </Section>

      <Section title="also included">
        <div className="grid grid-cols-2 gap-2 sm:grid-cols-3 lg:grid-cols-4">
          {features.map((f) => (
            <span key={f} className="rounded-lg border border-[#DDD5C7] bg-[#FFFDF8]/68 px-3 py-2 text-[14px] leading-snug text-[#171512]/70 shadow-[0_8px_24px_rgba(72,57,34,0.05)]">
              {f}
            </span>
          ))}
        </div>
      </Section>

      <Section title="built for privacy">
        <p className="max-w-[68ch] rounded-[1.15rem] border border-[#DDD5C7] bg-[#FFFDF8]/70 p-5 text-[15px] leading-relaxed text-[#171512]/70 shadow-[0_18px_55px_rgba(72,57,34,0.09)]">
          oddsnap runs entirely on your machine. no accounts, no telemetry, no cloud dependencies. your screenshots never leave your computer unless you choose to upload them.
        </p>
      </Section>

      <Section title="faq">
        <AccordionGroup type="single" collapsible className="w-full max-w-full rounded-[1.15rem] border border-[#DDD5C7] bg-[#FFFDF8]/62 p-2 shadow-[0_18px_55px_rgba(72,57,34,0.08)]">
          {faq.map((item, i) => (
            <AccordionItem key={item.q} value={item.q} index={i}>
              <AccordionTrigger>{item.q}</AccordionTrigger>
              <AccordionContent>{item.a}</AccordionContent>
            </AccordionItem>
          ))}
        </AccordionGroup>
      </Section>

      <Section title="open source">
        <p className="text-[#171512]/68 leading-relaxed mb-5 max-w-[70ch]">
          free and open source, licensed under gpl-3.0.
        </p>
        <div className="rounded-[1.15rem] border border-[#DDD5C7] bg-[#FFFDF8]/68 p-4 shadow-[0_18px_55px_rgba(72,57,34,0.08)]">
          <StarChart />
        </div>
      </Section>
    </div>
  );
}
