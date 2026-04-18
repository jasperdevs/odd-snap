import { useState, useMemo, useRef, useLayoutEffect, type RefObject } from "react";
import { useReleases } from "../hooks/useReleases";
import type { Release, ReleaseAsset } from "../hooks/useReleases";
import { Button } from "@/components/ui/button";
import { InputCopy } from "@/components/ui/input-copy";

function formatSize(bytes: number): string {
  if (bytes < 1024) return bytes + " B";
  if (bytes < 1024 * 1024) return (bytes / 1024).toFixed(1) + " KB";
  return (bytes / (1024 * 1024)).toFixed(1) + " MB";
}

function formatDate(iso: string): string {
  return new Date(iso).toLocaleDateString("en-US", {
    year: "numeric",
    month: "short",
    day: "numeric",
  });
}

function escapeHtml(str: string): string {
  return str
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#039;");
}

function renderMarkdown(body: string): string {
  let html = escapeHtml(body);

  html = html.replace(/^### (.+)$/gm, '<h4 class="text-[14px] mt-4 mb-1 text-black">$1</h4>');
  html = html.replace(/^## (.+)$/gm, '<h3 class="text-[15px] mt-5 mb-2 text-black">$1</h3>');
  html = html.replace(/^# (.+)$/gm, '<h2 class="text-[16px] mt-6 mb-2 text-black">$1</h2>');

  html = html.replace(/\*\*(.+?)\*\*/g, "<strong>$1</strong>");

  html = html.replace(/`([^`]+)`/g, '<code class="px-1 py-0.5 rounded bg-[#EBEBEB] text-black text-[13px] font-mono">$1</code>');

  html = html.replace(/^[*-] (.+)$/gm, '<li class="ml-4 list-disc text-[14px] text-black/70">$1</li>');

  html = html.replace(
    /(<li[^>]*>.*<\/li>\n?)+/g,
    (match) => `<ul class="space-y-1 my-2">${match}</ul>`
  );

  html = html.replace(
    /\[([^\]]+)\]\((https?:\/\/[^)]+)\)/g,
    '<a href="$2" target="_blank" rel="noopener noreferrer" class="text-black underline hover:no-underline">$1</a>'
  );

  html = html
    .split("\n")
    .map((line) => {
      const trimmed = line.trim();
      if (
        !trimmed ||
        trimmed.startsWith("<h") ||
        trimmed.startsWith("<ul") ||
        trimmed.startsWith("</ul") ||
        trimmed.startsWith("<li") ||
        trimmed.startsWith("<a")
      ) {
        return line;
      }
      return `<p class="text-[14px] text-black/70 my-1">${trimmed}</p>`;
    })
    .join("\n");

  return html;
}

type Arch = "x64" | "arm64" | "x86" | "unknown";

function detectArch(): Arch {
  const ua = navigator.userAgent.toLowerCase();
  if (ua.includes("arm64") || ua.includes("aarch64")) return "arm64";
  if (ua.includes("x64") || ua.includes("x86_64") || ua.includes("amd64") || ua.includes("wow64") || ua.includes("win64"))
    return "x64";
  return "x64";
}

function getAssetArch(name: string): Arch {
  const lower = name.toLowerCase();
  if (lower.includes("arm64") || lower.includes("aarch64")) return "arm64";
  if (lower.includes("x64") || lower.includes("x86_64") || lower.includes("amd64")) return "x64";
  if (lower.includes("x86") && !lower.includes("x86_64") && !lower.includes("x86-64")) return "x86";
  return "unknown";
}

function isExe(asset: ReleaseAsset): boolean {
  return asset.name.toLowerCase().endsWith(".exe");
}

function isZip(asset: ReleaseAsset): boolean {
  return asset.name.toLowerCase().endsWith(".zip");
}

function isWindowsAsset(asset: ReleaseAsset): boolean {
  return isExe(asset) || isZip(asset);
}

function getArchLabel(name: string): string {
  const arch = getAssetArch(name);
  const lower = name.toLowerCase();
  const flavor = lower.includes("setup") ? "installer" : lower.includes("portable") ? "portable" : "";
  const suffix = flavor ? ` ${flavor}` : "";
  if (arch === "arm64") return "windows (arm64)" + suffix;
  if (arch === "x64") return "windows (x64)" + suffix;
  if (arch === "x86") return "windows (x86)" + suffix;
  return "windows" + suffix;
}

function PrimaryBtn({ href, children }: { href: string; children: React.ReactNode }) {
  return (
    <Button asChild size="md" variant="primary" className="pl-[10px]">
      <a href={href}>
        <DownloadIcon />
        {children}
      </a>
    </Button>
  );
}

function ChevronLeft() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M15 6l-6 6 6 6" />
    </svg>
  );
}

function ChevronRight() {
  return (
    <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="1.75" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
      <path d="M9 6l6 6-6 6" />
    </svg>
  );
}

function ChevronDown({ open }: { open: boolean }) {
  return (
    <svg
      width="14"
      height="14"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.75"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
      className="transition-transform duration-300 ease-out"
      style={{ transform: open ? "rotate(180deg)" : "rotate(0deg)" }}
    >
      <path d="M6 9l6 6 6-6" />
    </svg>
  );
}

function DownloadIcon() {
  return (
    <svg
      width="14"
      height="14"
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.75"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d="M12 3v12m0 0l-4-4m4 4l4-4M5 21h14" />
    </svg>
  );
}

function GitHubIcon() {
  return (
    <svg
      width="14"
      height="14"
      viewBox="0 0 24 24"
      fill="currentColor"
      aria-hidden="true"
    >
      <path d="M12 .5C5.65.5.5 5.65.5 12c0 5.08 3.29 9.39 7.86 10.91.58.1.79-.25.79-.56 0-.28-.01-1.02-.02-2-3.2.69-3.87-1.54-3.87-1.54-.52-1.33-1.28-1.68-1.28-1.68-1.05-.72.08-.7.08-.7 1.16.08 1.77 1.19 1.77 1.19 1.03 1.76 2.7 1.25 3.35.95.1-.74.4-1.25.73-1.54-2.55-.29-5.23-1.28-5.23-5.69 0-1.26.45-2.28 1.19-3.09-.12-.29-.52-1.46.11-3.04 0 0 .97-.31 3.18 1.18a11 11 0 0 1 5.79 0c2.21-1.49 3.18-1.18 3.18-1.18.63 1.58.23 2.75.11 3.04.74.81 1.19 1.83 1.19 3.09 0 4.42-2.69 5.4-5.25 5.68.41.35.78 1.05.78 2.12 0 1.53-.01 2.76-.01 3.14 0 .31.21.67.8.56A11.5 11.5 0 0 0 23.5 12C23.5 5.65 18.35.5 12 .5Z" />
    </svg>
  );
}

function useMeasuredHeight(ref: RefObject<HTMLElement | null>, deps: unknown[]) {
  const [height, setHeight] = useState(0);
  useLayoutEffect(() => {
    const el = ref.current;
    if (!el) return;
    const measure = () => setHeight(el.scrollHeight);
    measure();
    const ro = new ResizeObserver(measure);
    ro.observe(el);
    return () => ro.disconnect();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, deps);
  return height;
}

function OutlineDownloadBtn({ href, children }: { href: string; children: React.ReactNode }) {
  return (
    <Button asChild size="md" variant="tertiary" className="pl-[10px]">
      <a href={href}>
        <DownloadIcon />
        {children}
      </a>
    </Button>
  );
}

function GitHubBtn({ href, children }: { href: string; children: React.ReactNode }) {
  return (
    <Button asChild size="md" variant="tertiary" className="pl-[10px]">
      <a href={href} target="_blank" rel="noopener noreferrer">
        <GitHubIcon />
        {children}
      </a>
    </Button>
  );
}

function ReleaseCard({
  release,
  isLatest,
  userArch,
}: {
  release: Release;
  isLatest: boolean;
  userArch: Arch;
}) {
  const [extrasOpen, setExtrasOpen] = useState(false);
  const [changelogExpanded, setChangelogExpanded] = useState(false);
  const extrasRef = useRef<HTMLDivElement>(null);
  const changelogRef = useRef<HTMLDivElement>(null);
  const extrasHeight = useMeasuredHeight(extrasRef, []);
  const changelogHeight = useMeasuredHeight(changelogRef, [release.body]);

  const exeAssets = release.assets.filter(isExe);
  const zipAssets = release.assets.filter(isZip);

  const sortedExeAssets = useMemo(() => {
    return [...exeAssets].sort((a, b) => {
      const aMatch = getAssetArch(a.name) === userArch ? 0 : 1;
      const bMatch = getAssetArch(b.name) === userArch ? 0 : 1;
      return aMatch - bMatch;
    });
  }, [exeAssets, userArch]);

  const sortedZipAssets = useMemo(() => {
    return [...zipAssets].sort((a, b) => {
      const aMatch = getAssetArch(a.name) === userArch ? 0 : 1;
      const bMatch = getAssetArch(b.name) === userArch ? 0 : 1;
      return aMatch - bMatch;
    });
  }, [zipAssets, userArch]);

  const hasBody = !!release.body?.trim();
  const hasExtras = zipAssets.length > 0 || !!release.zipball_url;

  return (
    <div className="border-t border-[#EBEBEB] pt-8 pb-10">
      <h2 className="text-[16px] text-black mb-4">
        {release.tag_name}
        <span className="text-black/40 mx-2">//</span>
        <span className="text-black/60 text-[14px]">{formatDate(release.published_at)}</span>
        {isLatest && (
          <>
            <span className="text-black/40 mx-2">//</span>
            <span className="text-[14px]" style={{ color: "#22c55e" }}>latest</span>
          </>
        )}
      </h2>

      <div className="flex flex-col gap-2">
        {sortedExeAssets.map((asset) => {
          const assetArch = getAssetArch(asset.name);
          const isRecommended = assetArch === userArch;

          return (
            <div key={asset.name} className="flex items-center gap-3 flex-wrap">
              <span className="text-[14px] text-black flex-1 min-w-0">
                {getArchLabel(asset.name)}
                <span className="ml-2 text-black/60 text-[12px]">{formatSize(asset.size)}</span>
                {isRecommended && <span className="ml-2 text-black/60 text-[12px]">(recommended)</span>}
              </span>
              <PrimaryBtn href={asset.browser_download_url}>download</PrimaryBtn>
            </div>
          );
        })}
      </div>

      {hasExtras && (
        <div className="mt-3">
          <div
            className="overflow-hidden transition-[max-height,opacity] duration-300 ease-out"
            style={{
              maxHeight: extrasOpen ? extrasHeight : 0,
              opacity: extrasOpen ? 1 : 0,
            }}
            aria-hidden={!extrasOpen}
          >
            <div ref={extrasRef} className="flex flex-col gap-2 pt-1 pb-3">
              {sortedZipAssets.map((asset) => {
                const assetArch = getAssetArch(asset.name);
                const isRecommended = assetArch === userArch;
                return (
                  <div key={asset.name} className="flex items-center gap-3 flex-wrap">
                    <span className="text-[14px] text-black flex-1 min-w-0">
                      {getArchLabel(asset.name)} (.zip)
                      <span className="ml-2 text-black/60 text-[12px]">{formatSize(asset.size)}</span>
                      {isRecommended && <span className="ml-2 text-black/60 text-[12px]">(recommended)</span>}
                    </span>
                    <OutlineDownloadBtn href={asset.browser_download_url}>download</OutlineDownloadBtn>
                  </div>
                );
              })}
              <div className="flex items-center gap-3 flex-wrap">
                <span className="text-[14px] text-black flex-1 min-w-0">source code</span>
                <GitHubBtn href={release.html_url}>view on github</GitHubBtn>
              </div>
            </div>
          </div>
          <button
            onClick={() => setExtrasOpen((v) => !v)}
            aria-label={extrasOpen ? "hide more downloads" : "show more downloads"}
            aria-expanded={extrasOpen}
            className="group flex items-center gap-3 w-full text-black/50 hover:text-black transition-colors"
          >
            <span className="h-px flex-1 bg-[#EBEBEB] group-hover:bg-black/20 transition-colors" />
            <ChevronDown open={extrasOpen} />
            <span className="h-px flex-1 bg-[#EBEBEB] group-hover:bg-black/20 transition-colors" />
          </button>
        </div>
      )}

      {hasBody && (() => {
        const isLong = release.body.length > 900;
        const collapsed = isLong && !changelogExpanded;
        return (
          <div className="mt-5">
            <h3 className="text-[14px] text-black/70 mb-2">changelog</h3>
            <div className="rounded-md border border-[#EBEBEB] bg-white overflow-hidden">
              <div className="relative">
                <div
                  className="overflow-hidden transition-[max-height] duration-300 ease-out"
                  style={{
                    maxHeight: isLong ? (changelogExpanded ? changelogHeight : 260) : changelogHeight || undefined,
                  }}
                >
                  <div
                    ref={changelogRef}
                    className="px-4 py-3 font-mono changelog-body [&>*:first-child]:mt-0 [&>*:last-child]:mb-0"
                    style={{ fontFamily: "Consolas, 'Cascadia Mono', 'Fira Code', monospace" }}
                    dangerouslySetInnerHTML={{ __html: renderMarkdown(release.body) }}
                  />
                </div>
                {isLong && (
                  <div
                    className="pointer-events-none absolute inset-x-0 bottom-0 h-32 transition-opacity duration-300 ease-out"
                    style={{
                      opacity: collapsed ? 1 : 0,
                      background:
                        "linear-gradient(to bottom, rgba(255,255,255,0) 0%, rgba(255,255,255,0.4) 30%, rgba(255,255,255,0.85) 65%, rgba(255,255,255,1) 100%)",
                    }}
                  />
                )}
              </div>
            </div>
            {isLong && (
              <button
                onClick={() => setChangelogExpanded((v) => !v)}
                aria-label={changelogExpanded ? "show less changelog" : "show more changelog"}
                aria-expanded={changelogExpanded}
                className="group flex items-center gap-3 w-full text-black/50 hover:text-black transition-colors mt-3"
              >
                <span className="h-px flex-1 bg-[#EBEBEB] group-hover:bg-black/20 transition-colors" />
                <ChevronDown open={changelogExpanded} />
                <span className="h-px flex-1 bg-[#EBEBEB] group-hover:bg-black/20 transition-colors" />
              </button>
            )}
          </div>
        );
      })()}
    </div>
  );
}

const PAGE_SIZE = 5;

export default function Downloads() {
  const { releases, loading } = useReleases();
  const userArch = useMemo(() => detectArch(), []);
  const [page, setPage] = useState(0);

  const windowsReleases = releases.filter((r) => r.assets.some(isWindowsAsset));
  const pageCount = Math.max(1, Math.ceil(windowsReleases.length / PAGE_SIZE));
  const safePage = Math.min(page, pageCount - 1);
  const start = safePage * PAGE_SIZE;
  const pageReleases = windowsReleases.slice(start, start + PAGE_SIZE);

  return (
    <div className="py-12">
      <div className="mb-8">
        <h1 className="text-[28px] text-black">downloads</h1>
      </div>

      <div className="grid gap-3 sm:grid-cols-2 mb-10">
        <InputCopy label="winget install" value="winget install JasperDevs.Yoink" />
        <InputCopy label="winget upgrade" value="winget upgrade JasperDevs.Yoink" />
      </div>

      {loading ? (
        <div className="space-y-4">
          {[1, 2].map((i) => (
            <div key={i} className="border-t border-[#EBEBEB] py-6 animate-pulse">
              <div className="flex items-center gap-3 mb-4">
                <div className="h-5 w-20 bg-[#EBEBEB] rounded" />
                <div className="h-4 w-24 bg-[#EBEBEB] rounded ml-auto" />
              </div>
              <div className="flex items-center gap-3">
                <div className="h-4 w-40 bg-[#EBEBEB] rounded flex-1" />
                <div className="h-8 w-24 bg-[#EBEBEB] rounded" />
              </div>
            </div>
          ))}
        </div>
      ) : (
        <>
          <div>
            {pageReleases.map((release, i) => (
              <ReleaseCard
                key={release.id}
                release={release}
                isLatest={safePage === 0 && i === 0}
                userArch={userArch}
              />
            ))}
          </div>

          {pageCount > 1 && (
            <div className="flex items-center justify-center gap-1 pt-6 mt-2 border-t border-[#EBEBEB]">
              <Button
                onClick={() => setPage((p) => Math.max(0, p - 1))}
                disabled={safePage === 0}
                aria-label="previous page"
                size="icon-sm"
                variant="ghost"
              >
                <ChevronLeft />
              </Button>
              {Array.from({ length: pageCount }, (_, i) => (
                <Button
                  key={i}
                  onClick={() => setPage(i)}
                  size="sm"
                  variant={i === safePage ? "primary" : "ghost"}
                  className="min-w-8"
                >
                  {i + 1}
                </Button>
              ))}
              <Button
                onClick={() => setPage((p) => Math.min(pageCount - 1, p + 1))}
                disabled={safePage === pageCount - 1}
                aria-label="next page"
                size="icon-sm"
                variant="ghost"
              >
                <ChevronRight />
              </Button>
            </div>
          )}
        </>
      )}
    </div>
  );
}
