import { useState, useMemo } from "react";
import { useReleases } from "../hooks/useReleases";
import type { Release, ReleaseAsset } from "../hooks/useReleases";

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
    <a
      href={href}
      className="inline-flex items-center justify-center px-4 py-2 rounded-md bg-black text-white text-[13px] hover:bg-black/85 transition-colors"
    >
      {children}
    </a>
  );
}

function OutlineBtn({ href, external, children }: { href: string; external?: boolean; children: React.ReactNode }) {
  return (
    <a
      href={href}
      {...(external ? { target: "_blank", rel: "noopener noreferrer" } : {})}
      className="inline-flex items-center justify-center px-4 py-2 rounded-md border border-black text-black text-[13px] hover:bg-[#EBEBEB] transition-colors"
    >
      {children}
    </a>
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
      <div className="flex items-center gap-3 mb-4 flex-wrap">
        <h2 className="text-[16px] text-black">{release.tag_name}</h2>
        {isLatest && (
          <span className="px-2 py-0.5 rounded-full border border-black text-[11px] text-black">
            latest
          </span>
        )}
        <span className="text-[13px] text-black/60 ml-auto">
          {formatDate(release.published_at)}
        </span>
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
              <span className="text-[12px] text-black/60">{formatSize(asset.size)}</span>
              <PrimaryBtn href={asset.browser_download_url}>download</PrimaryBtn>
            </div>
          );
        })}
      </div>

      {hasExtras && (
        <div className="mt-3">
          {!extrasOpen ? (
            <button
              onClick={() => setExtrasOpen(true)}
              className="text-[13px] text-black/60 hover:text-black transition-colors underline underline-offset-4"
            >
              more downloads
            </button>
          ) : (
            <div className="flex flex-col gap-2">
              {sortedZipAssets.map((asset) => {
                const assetArch = getAssetArch(asset.name);
                const isRecommended = assetArch === userArch;
                return (
                  <div key={asset.name} className="flex items-center gap-3 flex-wrap">
                    <span className="text-[14px] text-black flex-1 min-w-0">
                      {getArchLabel(asset.name)} (.zip)
                      {isRecommended && <span className="ml-2 text-black/60 text-[12px]">(recommended)</span>}
                    </span>
                    <span className="text-[12px] text-black/60">{formatSize(asset.size)}</span>
                    <OutlineBtn href={asset.browser_download_url}>download</OutlineBtn>
                  </div>
                );
              })}
              <div className="flex items-center gap-3 flex-wrap">
                <span className="text-[14px] text-black flex-1 min-w-0">source code</span>
                <OutlineBtn href={release.html_url} external>
                  view on github
                </OutlineBtn>
              </div>
              <button
                onClick={() => setExtrasOpen(false)}
                className="text-[13px] text-black/60 hover:text-black transition-colors underline underline-offset-4 text-left mt-1"
              >
                show less
              </button>
            </div>
          )}
        </div>
      )}

      {hasBody && (
        <div className="mt-5">
          <h3 className="text-[14px] text-black/70 mb-2">changelog</h3>
          <div className="rounded-md border border-[#EBEBEB] bg-white overflow-hidden">
            <div
              className="px-4 py-3 font-mono changelog-body [&>*:first-child]:mt-0 [&>*:last-child]:mb-0"
              style={{ fontFamily: "Consolas, 'Cascadia Mono', 'Fira Code', monospace" }}
              dangerouslySetInnerHTML={{ __html: renderMarkdown(release.body) }}
            />
          </div>
        </div>
      )}
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
            <div className="flex items-center justify-between pt-6 mt-2 border-t border-[#EBEBEB]">
              <button
                onClick={() => setPage((p) => Math.max(0, p - 1))}
                disabled={safePage === 0}
                className="text-[13px] text-black/70 hover:text-black transition-colors disabled:text-black/25 disabled:cursor-not-allowed"
              >
                ← previous
              </button>
              <div className="flex items-center gap-1">
                {Array.from({ length: pageCount }, (_, i) => (
                  <button
                    key={i}
                    onClick={() => setPage(i)}
                    className={`w-7 h-7 rounded-md text-[12px] transition-colors ${
                      i === safePage
                        ? "bg-black text-white"
                        : "text-black/60 hover:text-black hover:bg-[#EBEBEB]"
                    }`}
                  >
                    {i + 1}
                  </button>
                ))}
              </div>
              <button
                onClick={() => setPage((p) => Math.min(pageCount - 1, p + 1))}
                disabled={safePage === pageCount - 1}
                className="text-[13px] text-black/70 hover:text-black transition-colors disabled:text-black/25 disabled:cursor-not-allowed"
              >
                next →
              </button>
            </div>
          )}
        </>
      )}
    </div>
  );
}
