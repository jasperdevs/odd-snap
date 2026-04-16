import { useReleases } from "../hooks/useReleases";
import PageIntro from "../components/PageIntro";

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

  html = html.replace(/^### (.+)$/gm, '<h4 class="text-sm font-semibold mt-4 mb-1">$1</h4>');
  html = html.replace(/^## (.+)$/gm, '<h3 class="text-base font-semibold mt-5 mb-2">$1</h3>');
  html = html.replace(/^# (.+)$/gm, '<h2 class="text-lg font-semibold mt-6 mb-2">$1</h2>');
  html = html.replace(/\*\*(.+?)\*\*/g, "<strong>$1</strong>");
  html = html.replace(/`([^`]+)`/g, '<code class="px-2 py-1 text-sm">$1</code>');
  html = html.replace(/^[*-] (.+)$/gm, '<li class="ml-4 list-disc text-sm">$1</li>');

  html = html.replace(
    /(<li[^>]*>.*<\/li>\n?)+/g,
    (match) => `<ul class="space-y-1 my-2">${match}</ul>`
  );

  html = html.replace(
    /\[([^\]]+)\]\((https?:\/\/[^)]+)\)/g,
    '<a href="$2" target="_blank" rel="noopener noreferrer">$1</a>'
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
      return `<p class="my-1 text-sm">${trimmed}</p>`;
    })
    .join("\n");

  return html;
}

function ChangelogSkeleton() {
  return (
    <div className="space-y-6">
      <PageIntro
        eyebrow="Release notes"
        title="Every shipped change, without the noise."
        description="Release notes are loading from GitHub."
      />
      <div className="release-stack">
        {[1, 2, 3].map((i) => (
          <div key={i} className="panel info-card space-y-3 animate-pulse">
            <div className="flex items-center gap-3">
              <div className="h-5 w-16 rounded bg-[rgba(255,255,255,0.07)]" />
              <div className="h-4 w-24 rounded bg-[rgba(255,255,255,0.07)]" />
            </div>
            <div className="space-y-2">
              <div className="h-3 w-full rounded bg-[rgba(255,255,255,0.07)]" />
              <div className="h-3 w-4/5 rounded bg-[rgba(255,255,255,0.07)]" />
              <div className="h-3 w-3/5 rounded bg-[rgba(255,255,255,0.07)]" />
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

export default function Changelog() {
  const { releases, loading } = useReleases();

  if (loading) {
    return <ChangelogSkeleton />;
  }

  return (
    <div className="space-y-6">
      <PageIntro
        eyebrow="Release notes"
        title="Every shipped change, without the noise."
        description="Release notes are pulled directly from GitHub releases."
      />

      <div className="release-stack">
        {releases.map((release) => (
          <div key={release.id} className="panel info-card space-y-4">
            <div className="flex items-center gap-3">
              <h2 className="text-lg font-semibold">{release.tag_name}</h2>
              <span className="text-sm text-[var(--muted)]">
                {formatDate(release.published_at)}
              </span>
            </div>
            {release.body ? (
              <div
                className="prose-block max-w-none"
                dangerouslySetInnerHTML={{
                  __html: renderMarkdown(release.body),
                }}
              />
            ) : null}
          </div>
        ))}
      </div>
    </div>
  );
}
