import { useState, useMemo } from "react";
import { useReleases } from "../hooks/useReleases";
import type { Release, ReleaseAsset } from "../hooks/useReleases";
import PageIntro from "../components/PageIntro";

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
  if (ua.includes("x64") || ua.includes("x86_64") || ua.includes("amd64") || ua.includes("wow64") || ua.includes("win64")) {
    return "x64";
  }
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

function AssetRow({
  asset,
  userArch,
  buttonKind,
  suffix = "",
}: {
  asset: ReleaseAsset;
  userArch: Arch;
  buttonKind: "primary" | "secondary";
  suffix?: string;
}) {
  const assetArch = getAssetArch(asset.name);
  const isRecommended = assetArch === userArch;

  return (
    <div className="asset-row">
      <WindowsIcon />
      <div className="min-w-0 flex-1">
        <div className="font-medium text-[var(--text)]">
          {getArchLabel(asset.name)}
          {suffix}
        </div>
        <div className="mt-1 text-sm text-[var(--muted)]">{formatSize(asset.size)}</div>
      </div>
      {isRecommended ? (
        <span className="tag-pill border-[var(--line-strong)] bg-[rgba(255,255,255,0.06)] text-[var(--text)]">
          Recommended
        </span>
      ) : null}
      <a
        href={asset.browser_download_url}
        className={buttonKind === "primary" ? "button-primary text-sm" : "button-secondary text-sm"}
      >
        <DownloadIcon />
        Download
      </a>
    </div>
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
    <div className="panel release-card">
      <div className="release-card-header">
        <h2 className="text-lg font-semibold">{release.tag_name}</h2>
        {isLatest ? (
          <span className="tag-pill border-[var(--line-strong)] bg-[rgba(255,255,255,0.08)] text-[var(--text)]">
            Latest
          </span>
        ) : null}
        <span className="ml-auto text-sm text-[var(--muted)]">
          {formatDate(release.published_at)}
        </span>
      </div>

      <div className="release-card-body">
        {sortedExeAssets.map((asset) => (
          <AssetRow
            key={asset.name}
            asset={asset}
            userArch={userArch}
            buttonKind="primary"
          />
        ))}

        {zipAssets.length > 0 || release.zipball_url ? (
          <>
            {!showMore ? (
              <div className="asset-row">
                <button
                  onClick={() => setShowMore(true)}
                  className="text-sm font-medium text-[var(--muted)] transition-colors hover:text-[var(--text)]"
                >
                  Show portable builds and source
                </button>
              </div>
            ) : (
              <>
                {sortedZipAssets.map((asset) => (
                  <AssetRow
                    key={asset.name}
                    asset={asset}
                    userArch={userArch}
                    buttonKind="secondary"
                    suffix=" (.zip)"
                  />
                ))}
                <div className="asset-row">
                  <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20" fill="currentColor" className="h-5 w-5 text-[var(--muted)]">
                    <path fillRule="evenodd" d="M4.25 2A2.25 2.25 0 0 0 2 4.25v11.5A2.25 2.25 0 0 0 4.25 18h11.5A2.25 2.25 0 0 0 18 15.75V4.25A2.25 2.25 0 0 0 15.75 2H4.25Zm4.03 6.28a.75.75 0 0 0-1.06-1.06L4.97 9.47a.75.75 0 0 0 0 1.06l2.25 2.25a.75.75 0 0 0 1.06-1.06L6.56 10l1.72-1.72Zm2.38-1.06a.75.75 0 1 0-1.06 1.06L11.44 10l-1.72 1.72a.75.75 0 1 0 1.06 1.06l2.25-2.25a.75.75 0 0 0 0-1.06l-2.25-2.25Z" clipRule="evenodd" />
                  </svg>
                  <div className="min-w-0 flex-1">
                    <div className="font-medium text-[var(--text)]">Source code</div>
                    <div className="mt-1 text-sm text-[var(--muted)]">Browse tags, notes, and assets on GitHub.</div>
                  </div>
                  <a
                    href={release.html_url}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="button-secondary text-sm"
                  >
                    View on GitHub
                  </a>
                </div>
                <div className="asset-row">
                  <button
                    onClick={() => setShowMore(false)}
                    className="text-sm font-medium text-[var(--muted)] transition-colors hover:text-[var(--text)]"
                  >
                    Show less
                  </button>
                </div>
              </>
            )}
          </>
        ) : null}
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
      <div className="space-y-6">
        <PageIntro
          eyebrow="Release builds"
          title="Choose a build that matches your machine."
          description="Release metadata is loading from GitHub."
        />
        <div className="release-stack">
          {[1, 2].map((i) => (
            <div key={i} className="panel release-card animate-pulse">
              <div className="release-card-header">
                <div className="h-5 w-16 rounded bg-[rgba(255,255,255,0.07)]" />
                <div className="ml-auto h-4 w-24 rounded bg-[rgba(255,255,255,0.07)]" />
              </div>
              <div className="asset-row">
                <div className="h-5 w-5 rounded bg-[rgba(255,255,255,0.07)]" />
                <div className="h-4 w-32 rounded bg-[rgba(255,255,255,0.07)]" />
                <div className="ml-auto h-8 w-24 rounded-full bg-[rgba(255,255,255,0.07)]" />
              </div>
              <div className="asset-row">
                <div className="h-5 w-5 rounded bg-[rgba(255,255,255,0.07)]" />
                <div className="h-4 w-32 rounded bg-[rgba(255,255,255,0.07)]" />
                <div className="ml-auto h-8 w-24 rounded-full bg-[rgba(255,255,255,0.07)]" />
              </div>
            </div>
          ))}
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <PageIntro
        eyebrow="Release builds"
        title="Choose a build that matches your machine."
        description={`Download Yoink for Windows. Your architecture (${userArch}) is detected automatically so the recommended installer stays on top.`}
        actions={<span className="tag-pill">{userArch} detected</span>}
      />

      <div className="panel info-card">
        <div className="grid gap-4 md:grid-cols-3">
          <div>
            <div className="font-medium text-[var(--text)]">Installer first</div>
            <p className="mt-2">Use the setup build if you want updates and the cleanest Windows install path.</p>
          </div>
          <div>
            <div className="font-medium text-[var(--text)]">Portable available</div>
            <p className="mt-2">ZIP builds are still available when you need a no-install workflow.</p>
          </div>
          <div>
            <div className="font-medium text-[var(--text)]">Source stays public</div>
            <p className="mt-2">Every release links back to the corresponding GitHub notes and tag.</p>
          </div>
        </div>
      </div>

      <div className="release-stack">
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
