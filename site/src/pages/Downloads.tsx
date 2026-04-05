import { useState, useMemo } from "react";
import { useReleases } from "../hooks/useReleases";
import type { Release, ReleaseAsset } from "../hooks/useReleases";

function WindowsIcon() {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" className="w-5 h-5">
      <path d="M0 3.449L9.75 2.1v9.451H0m10.949-9.602L24 0v11.4H10.949M0 12.6h9.75v9.451L0 20.699M10.949 12.6H24V24l-12.9-1.801" />
    </svg>
  );
}

function DownloadIcon() {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="w-4 h-4">
      <path d="M10.75 2.75a.75.75 0 0 0-1.5 0v8.614L6.295 8.235a.75.75 0 1 0-1.09 1.03l4.25 4.5a.75.75 0 0 0 1.09 0l4.25-4.5a.75.75 0 0 0-1.09-1.03l-2.955 3.129V2.75Z" />
      <path d="M3.5 12.75a.75.75 0 0 0-1.5 0v2.5A2.75 2.75 0 0 0 4.75 18h10.5A2.75 2.75 0 0 0 18 15.25v-2.5a.75.75 0 0 0-1.5 0v2.5c0 .69-.56 1.25-1.25 1.25H4.75c-.69 0-1.25-.56-1.25-1.25v-2.5Z" />
    </svg>
  );
}

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

type Arch = "x64" | "arm64" | "x86" | "unknown";

function detectArch(): Arch {
  const ua = navigator.userAgent.toLowerCase();
  if (ua.includes("arm64") || ua.includes("aarch64")) return "arm64";
  if (ua.includes("x64") || ua.includes("x86_64") || ua.includes("amd64") || ua.includes("wow64") || ua.includes("win64"))
    return "x64";
  return "x64"; // default to x64
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
  if (arch === "arm64") return "Windows (arm64)";
  if (arch === "x64") return "Windows (x64)";
  if (arch === "x86") return "Windows (x86)";
  return "Windows";
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
  const [showMore, setShowMore] = useState(false);

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

  return (
    <div className="rounded-lg border border-[#2a2a28] bg-[#1a1a18] overflow-hidden">
      <div className="flex items-center gap-3 px-5 py-4 border-b border-[#2a2a28]">
        <h2 className="text-base font-semibold">{release.tag_name}</h2>
        {isLatest && (
          <span className="px-2 py-0.5 rounded-full bg-emerald-500/10 text-emerald-400 text-xs font-medium border border-emerald-500/20">
            Latest
          </span>
        )}
        <span className="text-sm text-[#8a8a80] ml-auto">
          {formatDate(release.published_at)}
        </span>
      </div>

      <div className="divide-y divide-[#2a2a28]">
        {sortedExeAssets.map((asset) => {
          const assetArch = getAssetArch(asset.name);
          const isRecommended = assetArch === userArch;

          return (
            <div
              key={asset.name}
              className="flex items-center gap-4 px-5 py-3"
            >
              <WindowsIcon />
              <span className="text-sm font-medium flex-1">
                {getArchLabel(asset.name)}
              </span>
              {isRecommended && (
                <span className="px-2 py-0.5 rounded-full bg-blue-500/10 text-blue-400 text-xs font-medium border border-blue-500/20">
                  Recommended
                </span>
              )}
              <span className="text-sm text-[#8a8a80]">
                {formatSize(asset.size)}
              </span>
              <a
                href={asset.browser_download_url}
                className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md bg-[#e8e6e3] text-[#111110] text-sm font-medium hover:bg-[#d0cec8] transition-colors"
              >
                <DownloadIcon />
                Download
              </a>
            </div>
          );
        })}

        {(zipAssets.length > 0 || release.zipball_url) && (
          <>
            {!showMore && (
              <div className="px-5 py-3">
                <button
                  onClick={() => setShowMore(true)}
                  className="text-sm text-[#8a8a80] hover:text-[#e8e6e3] transition-colors"
                >
                  Show more
                </button>
              </div>
            )}
            {showMore && (
              <>
                {sortedZipAssets.map((asset) => {
                  const assetArch = getAssetArch(asset.name);
                  const isRecommended = assetArch === userArch;

                  return (
                    <div
                      key={asset.name}
                      className="flex items-center gap-4 px-5 py-3"
                    >
                      <WindowsIcon />
                      <span className="text-sm font-medium flex-1">
                        {getArchLabel(asset.name)} (.zip)
                      </span>
                      {isRecommended && (
                        <span className="px-2 py-0.5 rounded-full bg-blue-500/10 text-blue-400 text-xs font-medium border border-blue-500/20">
                          Recommended
                        </span>
                      )}
                      <span className="text-sm text-[#8a8a80]">
                        {formatSize(asset.size)}
                      </span>
                      <a
                        href={asset.browser_download_url}
                        className="inline-flex items-center gap-1.5 px-3 py-1.5 rounded-md border border-[#444440] text-sm font-medium text-[#d0cec8] hover:bg-[#222220] transition-colors"
                      >
                        <DownloadIcon />
                        Download
                      </a>
                    </div>
                  );
                })}
                <div className="flex items-center gap-4 px-5 py-3">
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="w-5 h-5 text-[#8a8a80]">
                    <path fillRule="evenodd" d="M4.25 2A2.25 2.25 0 0 0 2 4.25v11.5A2.25 2.25 0 0 0 4.25 18h11.5A2.25 2.25 0 0 0 18 15.75V4.25A2.25 2.25 0 0 0 15.75 2H4.25Zm4.03 6.28a.75.75 0 0 0-1.06-1.06L4.97 9.47a.75.75 0 0 0 0 1.06l2.25 2.25a.75.75 0 0 0 1.06-1.06L6.56 10l1.72-1.72Zm2.38-1.06a.75.75 0 1 0-1.06 1.06L11.44 10l-1.72 1.72a.75.75 0 1 0 1.06 1.06l2.25-2.25a.75.75 0 0 0 0-1.06l-2.25-2.25Z" clipRule="evenodd" />
                  </svg>
                  <span className="text-sm font-medium flex-1">Source code</span>
                  <a
                    href={release.html_url}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="inline-flex items-center px-3 py-1.5 rounded-md border border-[#444440] text-sm font-medium text-[#d0cec8] hover:bg-[#222220] transition-colors"
                  >
                    View on GitHub
                  </a>
                </div>
                <div className="px-5 py-3">
                  <button
                    onClick={() => setShowMore(false)}
                    className="text-sm text-[#8a8a80] hover:text-[#e8e6e3] transition-colors"
                  >
                    Show less
                  </button>
                </div>
              </>
            )}
          </>
        )}
      </div>
    </div>
  );
}

export default function Downloads() {
  const { releases, loading } = useReleases();
  const userArch = useMemo(() => detectArch(), []);

  const windowsReleases = releases.filter((r) =>
    r.assets.some(isWindowsAsset)
  );

  if (loading) {
    return (
      <div className="px-6 py-10 space-y-6">
        <div>
          <h1 className="text-2xl font-bold tracking-tight">Downloads</h1>
          <p className="text-[#8a8a80] mt-2">Loading releases...</p>
        </div>
        <div className="space-y-4">
          {[1, 2].map((i) => (
            <div key={i} className="rounded-lg border border-[#2a2a28] bg-[#1a1a18] overflow-hidden animate-pulse">
              <div className="flex items-center gap-3 px-5 py-4 border-b border-[#2a2a28]">
                <div className="h-5 w-16 bg-[#222220] rounded" />
                <div className="h-4 w-24 bg-[#222220] rounded ml-auto" />
              </div>
              <div className="px-5 py-3 flex items-center gap-4">
                <div className="h-5 w-5 bg-[#222220] rounded" />
                <div className="h-4 w-32 bg-[#222220] rounded" />
                <div className="h-4 w-16 bg-[#222220] rounded ml-auto" />
                <div className="h-8 w-24 bg-[#222220] rounded" />
              </div>
              <div className="px-5 py-3 flex items-center gap-4 border-t border-[#2a2a28]">
                <div className="h-5 w-5 bg-[#222220] rounded" />
                <div className="h-4 w-32 bg-[#222220] rounded" />
                <div className="h-4 w-16 bg-[#222220] rounded ml-auto" />
                <div className="h-8 w-24 bg-[#222220] rounded" />
              </div>
            </div>
          ))}
        </div>
      </div>
    );
  }

  return (
    <div className="px-6 py-10 space-y-6">
      <div>
        <h1 className="text-2xl font-bold tracking-tight">Downloads</h1>
        <p className="text-[#8a8a80] mt-2">
          Download Yoink for Windows. Your architecture ({userArch}) is detected automatically.
        </p>
      </div>

      <div className="space-y-4">
        {windowsReleases.map((release, i) => (
          <ReleaseCard
            key={release.id}
            release={release}
            isLatest={i === 0}
            userArch={userArch}
          />
        ))}
      </div>
    </div>
  );
}
