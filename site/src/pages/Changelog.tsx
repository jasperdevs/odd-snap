import { useReleases } from "../hooks/useReleases";

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

  html = html.replace(/^### (.+)$/gm, '<h4 class="text-sm font-semibold mt-4 mb-1 text-black">$1</h4>');
  html = html.replace(/^## (.+)$/gm, '<h3 class="text-base font-semibold mt-5 mb-2 text-black">$1</h3>');
  html = html.replace(/^# (.+)$/gm, '<h2 class="text-lg font-semibold mt-6 mb-2 text-black">$1</h2>');

  html = html.replace(/\*\*(.+?)\*\*/g, "<strong>$1</strong>");

  html = html.replace(/`([^`]+)`/g, '<code class="px-1 py-0.5 rounded bg-[#EBEBEB] text-black text-sm font-mono">$1</code>');

  html = html.replace(/^[*-] (.+)$/gm, '<li class="ml-4 list-disc text-sm text-black">$1</li>');

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
      return `<p class="text-sm text-black/70 my-1">${trimmed}</p>`;
    })
    .join("\n");

  return html;
}

function ChangelogSkeleton() {
  return (
    <div className="px-6 py-10 space-y-6">
      <div>
        <h1 className="text-2xl font-bold tracking-tight text-black">Changelog</h1>
        <p className="text-black/60 mt-2">Release notes for every version of Yoink.</p>
      </div>
      <div className="space-y-4">
        {[1, 2, 3].map((i) => (
          <div key={i} className="rounded-lg border border-[#EBEBEB] bg-white p-5 space-y-3 animate-pulse">
            <div className="flex items-center gap-3">
              <div className="h-5 w-16 bg-[#EBEBEB] rounded" />
              <div className="h-4 w-24 bg-[#EBEBEB] rounded" />
            </div>
            <div className="space-y-2">
              <div className="h-3 w-full bg-[#EBEBEB] rounded" />
              <div className="h-3 w-4/5 bg-[#EBEBEB] rounded" />
              <div className="h-3 w-3/5 bg-[#EBEBEB] rounded" />
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
    <div className="px-6 py-10 space-y-6">
      <div>
        <h1 className="text-2xl font-bold tracking-tight text-black">Changelog</h1>
        <p className="text-black/60 mt-2">
          Release notes for every version of Yoink.
        </p>
      </div>

      <div className="space-y-4">
        {releases.map((release) => (
          <div
            key={release.id}
            className="rounded-lg border border-[#EBEBEB] bg-white p-5 space-y-3"
          >
            <div className="flex items-center gap-3">
              <h2 className="text-base font-semibold text-black">{release.tag_name}</h2>
              <span className="text-sm text-black/60">
                {formatDate(release.published_at)}
              </span>
            </div>
            {release.body && (
              <div
                className="max-w-none"
                dangerouslySetInnerHTML={{
                  __html: renderMarkdown(release.body),
                }}
              />
            )}
          </div>
        ))}
      </div>
    </div>
  );
}
