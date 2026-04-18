import { useState, useMemo } from "react";
import { useReleases } from "../hooks/useReleases";
import type { Release, ReleaseAsset } from "../hooks/useReleases";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";

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
    <Button asChild size="md" variant="primary">
      <a href={href}>{children}</a>
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
      className="transition-transform duration-150"
      style={{ transform: open ? "rotate(180deg)" : "rotate(0deg)" }}
    >
      <path d="M6 9l6 6 6-6" />
    </svg>
  );
}

function OutlineBtn({ href, external, children }: { href: string; external?: boolean; children: React.ReactNode }) {
  return (
    <Button asChild size="md" variant="tertiary">
      <a href={href} {...(external ? { target: "_blank", rel: "noopener noreferrer" } : {})}>
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
    <div className="border-t border-[#EBEBEB] py-6">
      <div className="flex items-center gap-2 mb-4 flex-wrap">
        <h2 className="text-[16px] text-black">
          {release.tag_name}
          <span className="text-black/40 mx-2">//</span>
          <span className="text-black/60 text-[14px]">{formatDate(release.published_at)}</span>
        </h2>
        {isLatest && (
          <Badge variant="dot" size="sm" color="green">
            latest
          </Badge>
        )}
      </div>

      <div className="flex flex-col gap-2">
        {sortedExeAssets.map((asset) => {
          const assetArch = getAssetArch(asset.name);
          const isRecommended = assetArch === userArch;

          return (
            <div key={asset.name} className="flex items-center gap-3 flex-wrap">
              <span className="text-[14px] text-black flex-1 min-w-0">
                {getArchLabel(asset.name)}
                {isRecommended && <span className="ml-2 text-black/60 text-[12px]">(recommended)</span>}
              </span>
              <PrimaryBtn href={asset.browser_download_url}>
                download {formatSize(asset.size)}
              </PrimaryBtn>
            </div>
          );
        })}
      </div>

      {hasExtras && (
        <div className="mt-3">
          <button
            onClick={() => setExtrasOpen((v) => !v)}
            aria-label={extrasOpen ? "hide more downloads" : "show more downloads"}
            aria-expanded={extrasOpen}
            className="inline-flex items-center justify-center h-7 w-7 text-black/60 hover:text-black transition-colors"
          >
            <ChevronDown open={extrasOpen} />
          </button>
          {extrasOpen && (
            <div className="flex flex-col gap-2 mt-2">
              {sortedZipAssets.map((asset) => {
                const assetArch = getAssetArch(asset.name);
                const isRecommended = assetArch === userArch;
                return (
                  <div key={asset.name} className="flex items-center gap-3 flex-wrap">
                    <span className="text-[14px] text-black flex-1 min-w-0">
                      {getArchLabel(asset.name)} (.zip)
                      {isRecommended && <span className="ml-2 text-black/60 text-[12px]">(recommended)</span>}
                    </span>
                    <OutlineBtn href={asset.browser_download_url}>
                      download {formatSize(asset.size)}
                    </OutlineBtn>
                  </div>
                );
              })}
              <div className="flex items-center gap-3 flex-wrap">
                <span className="text-[14px] text-black flex-1 min-w-0">source code</span>
                <OutlineBtn href={release.html_url} external>
                  view on github
                </OutlineBtn>
              </div>
            </div>
          )}
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
                  className="px-4 py-3 font-mono changelog-body [&>*:first-child]:mt-0 [&>*:last-child]:mb-0 overflow-hidden"
                  style={{
                    fontFamily: "Consolas, 'Cascadia Mono', 'Fira Code', monospace",
                    maxHeight: collapsed ? 260 : "none",
                  }}
                  dangerouslySetInnerHTML={{ __html: renderMarkdown(release.body) }}
                />
                {collapsed && (
                  <div
                    className="pointer-events-none absolute inset-x-0 bottom-0 h-16"
                    style={{
                      background:
                        "linear-gradient(to bottom, rgba(255,255,255,0) 0%, rgba(255,255,255,0.9) 55%, rgba(255,255,255,1) 100%)",
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
                className="inline-flex items-center justify-center h-7 w-7 text-black/60 hover:text-black transition-colors mt-2"
              >
                <ChevronDown open={changelogExpanded} />
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
