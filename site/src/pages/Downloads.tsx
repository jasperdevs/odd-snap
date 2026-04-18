import { useState, useMemo } from "react";
import { useReleases } from "../hooks/useReleases";
import type { Release, ReleaseAsset } from "../hooks/useReleases";
import { Button } from "@/components/ui/button";
import { Badge } from "@/components/ui/badge";
import { Download, Monitor } from "lucide-react";

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
  const flavor = lower.includes("setup") ? "Installer" : lower.includes("portable") ? "Portable" : "";
  const suffix = flavor ? ` ${flavor}` : "";
  if (arch === "arm64") return "Windows (arm64)" + suffix;
  if (arch === "x64") return "Windows (x64)" + suffix;
  if (arch === "x86") return "Windows (x86)" + suffix;
  return "Windows" + suffix;
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
    <div className="rounded-lg border border-[#EBEBEB] bg-white overflow-hidden">
      <div className="flex items-center gap-3 px-5 py-4 border-b border-[#EBEBEB]">
        <h2 className="text-base font-semibold text-black">{release.tag_name}</h2>
        {isLatest && <Badge size="sm">Latest</Badge>}
        <span className="text-sm text-black/60 ml-auto">
          {formatDate(release.published_at)}
        </span>
      </div>

      <div className="divide-y divide-[#EBEBEB]">
        {sortedExeAssets.map((asset) => {
          const assetArch = getAssetArch(asset.name);
          const isRecommended = assetArch === userArch;

          return (
            <div key={asset.name} className="flex items-center gap-4 px-5 py-3">
              <Monitor className="w-5 h-5 text-black" />
              <span className="text-sm font-medium flex-1 text-black">
                {getArchLabel(asset.name)}
              </span>
              {isRecommended && <Badge size="sm">Recommended</Badge>}
              <span className="text-sm text-black/60">{formatSize(asset.size)}</span>
              <Button asChild variant="primary" size="sm" leadingIcon={Download}>
                <a href={asset.browser_download_url}>Download</a>
              </Button>
            </div>
          );
        })}

        {(zipAssets.length > 0 || release.zipball_url) && (
          <>
            {!showMore && (
              <div className="px-5 py-3">
                <Button variant="ghost" size="sm" onClick={() => setShowMore(true)}>
                  Show more
                </Button>
              </div>
            )}
            {showMore && (
              <>
                {sortedZipAssets.map((asset) => {
                  const assetArch = getAssetArch(asset.name);
                  const isRecommended = assetArch === userArch;

                  return (
                    <div key={asset.name} className="flex items-center gap-4 px-5 py-3">
                      <Monitor className="w-5 h-5 text-black" />
                      <span className="text-sm font-medium flex-1 text-black">
                        {getArchLabel(asset.name)} (.zip)
                      </span>
                      {isRecommended && <Badge size="sm">Recommended</Badge>}
                      <span className="text-sm text-black/60">{formatSize(asset.size)}</span>
                      <Button asChild variant="tertiary" size="sm" leadingIcon={Download}>
                        <a href={asset.browser_download_url}>Download</a>
                      </Button>
                    </div>
                  );
                })}
                <div className="flex items-center gap-4 px-5 py-3">
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="w-5 h-5 text-black">
                    <path fillRule="evenodd" d="M4.25 2A2.25 2.25 0 0 0 2 4.25v11.5A2.25 2.25 0 0 0 4.25 18h11.5A2.25 2.25 0 0 0 18 15.75V4.25A2.25 2.25 0 0 0 15.75 2H4.25Zm4.03 6.28a.75.75 0 0 0-1.06-1.06L4.97 9.47a.75.75 0 0 0 0 1.06l2.25 2.25a.75.75 0 0 0 1.06-1.06L6.56 10l1.72-1.72Zm2.38-1.06a.75.75 0 1 0-1.06 1.06L11.44 10l-1.72 1.72a.75.75 0 1 0 1.06 1.06l2.25-2.25a.75.75 0 0 0 0-1.06l-2.25-2.25Z" clipRule="evenodd" />
                  </svg>
                  <span className="text-sm font-medium flex-1 text-black">Source code</span>
                  <Button asChild variant="tertiary" size="sm">
                    <a href={release.html_url} target="_blank" rel="noopener noreferrer">
                      View on GitHub
                    </a>
                  </Button>
                </div>
                <div className="px-5 py-3">
                  <Button variant="ghost" size="sm" onClick={() => setShowMore(false)}>
                    Show less
                  </Button>
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

  const windowsReleases = releases.filter((r) => r.assets.some(isWindowsAsset));

  if (loading) {
    return (
      <div className="px-6 py-10 space-y-6">
        <div>
          <h1 className="text-2xl font-bold tracking-tight text-black">Downloads</h1>
          <p className="text-black/60 mt-2">Loading releases...</p>
        </div>
        <div className="space-y-4">
          {[1, 2].map((i) => (
            <div key={i} className="rounded-lg border border-[#EBEBEB] bg-white overflow-hidden animate-pulse">
              <div className="flex items-center gap-3 px-5 py-4 border-b border-[#EBEBEB]">
                <div className="h-5 w-16 bg-[#EBEBEB] rounded" />
                <div className="h-4 w-24 bg-[#EBEBEB] rounded ml-auto" />
              </div>
              <div className="px-5 py-3 flex items-center gap-4">
                <div className="h-5 w-5 bg-[#EBEBEB] rounded" />
                <div className="h-4 w-32 bg-[#EBEBEB] rounded" />
                <div className="h-4 w-16 bg-[#EBEBEB] rounded ml-auto" />
                <div className="h-8 w-24 bg-[#EBEBEB] rounded" />
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
        <h1 className="text-2xl font-bold tracking-tight text-black">Downloads</h1>
        <p className="text-black/60 mt-2">
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
