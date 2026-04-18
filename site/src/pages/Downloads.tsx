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

        {(zipAssets.length > 0 || release.zipball_url) && (
          <>
            {!showMore && (
              <button
                onClick={() => setShowMore(true)}
                className="text-[13px] text-black/60 hover:text-black transition-colors text-left mt-2 underline underline-offset-4"
              >
                show more
              </button>
            )}
            {showMore && (
              <>
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
                  onClick={() => setShowMore(false)}
                  className="text-[13px] text-black/60 hover:text-black transition-colors text-left mt-2 underline underline-offset-4"
                >
                  show less
                </button>
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
        <div>
          {windowsReleases.map((release, i) => (
            <ReleaseCard
              key={release.id}
              release={release}
              isLatest={i === 0}
              userArch={userArch}
            />
          ))}
        </div>
      )}
    </div>
  );
}
