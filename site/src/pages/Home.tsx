import { Link } from "react-router-dom";
import { useState } from "react";
import StarChart from "../components/StarChart";
import PageIntro from "../components/PageIntro";

const featureCards = [
  {
    title: "Capture without mode-hopping",
    body: "Rectangle, freeform, fullscreen, active window, scrolling capture, and quick timers stay in one flow.",
  },
  {
    title: "Annotate before momentum dies",
    body: "Arrows, text, blur, highlights, ruler, emoji, and step markers are built into the capture surface.",
  },
  {
    title: "Find screenshots by meaning",
    body: "Search by filename, OCR text, or semantic similarity when your history gets too big to browse manually.",
  },
  {
    title: "Translate without shipping your workflow out",
    body: "OCR is built in, and translation can stay local with Argos or switch to Google when needed.",
  },
  {
    title: "Publish from the same tool",
    body: "Send captures to Imgur, S3, Dropbox, GitHub, OneDrive, or your own HTTP endpoint.",
  },
  {
    title: "Keep the utility small",
    body: "No account system, no premium gates, and no extra launcher pretending to be a product.",
  },
];

const workflowStats = [
  { label: "Capture modes", value: "5 core flows" },
  { label: "Upload targets", value: "19 destinations" },
  { label: "Recording formats", value: "GIF, MP4, WebM, MKV" },
];

const showcaseSections = [
  {
    eyebrow: "Annotation surface",
    title: "Mark up the shot while the context is still fresh",
    body: "The capture overlay keeps the tools close and the canvas quiet. You can draw, label, blur, measure, or drop in emoji without jumping into a second editor.",
    image: "annotations.png",
    alt: "Yoink annotation tools on a captured image",
  },
  {
    eyebrow: "OCR and translation",
    title: "Extract text, clean it up, and translate in the same window",
    body: "The OCR view is a working surface, not a modal dead-end. Edit the text, translate it, and copy just the part you need.",
    image: "ocr-screenshot.png",
    alt: "Yoink text capture and translation window",
  },
  {
    eyebrow: "Search history",
    title: "Recover old captures by what they say or what they show",
    body: "History stops being a dumping ground once OCR, naming, and semantic matching all point at the same result set.",
    image: "search-screenshot.png",
    alt: "Yoink image history search results",
  },
  {
    eyebrow: "Recording",
    title: "Record the screen without switching into a different product",
    body: "GIF clips, video recordings, microphone input, and desktop audio all live beside the same capture and share flows.",
    image: "recording.png",
    alt: "Yoink recording interface",
  },
];

const faq = [
  {
    q: "What is Yoink?",
    a: "Yoink is a Windows capture tool for screenshots, recordings, OCR, quick annotations, uploads, and searchable history.",
  },
  {
    q: "Is it free?",
    a: "Yes. Yoink is open source under GPL-3.0 and does not hide features behind accounts or paid tiers.",
  },
  {
    q: "Does it work offline?",
    a: "Capture, annotation, OCR, history, and most editing flows work locally. Uploads and Google Translate are the main network-dependent paths.",
  },
  {
    q: "What versions of Windows are supported?",
    a: "Windows 10 (1903+) and Windows 11, with x64 and ARM64 builds.",
  },
  {
    q: "What makes it different from ShareX?",
    a: "The product leans harder into a calmer interface, built-in sticker creation, semantic history search, and native Windows OCR.",
  },
  {
    q: "Can I run it without installing?",
    a: "Yes. The Downloads page includes both an installer and portable archives.",
  },
];

function FaqItem({ q, a }: { q: string; a: string }) {
  const [open, setOpen] = useState(false);

  return (
    <div className="faq-item">
      <button type="button" onClick={() => setOpen((value) => !value)} aria-expanded={open}>
        <div className="flex items-start justify-between gap-4">
          <span className="text-sm font-medium text-[var(--text)]">{q}</span>
          <span className="tag-pill px-3 py-1 text-xs">{open ? "Hide" : "Open"}</span>
        </div>
      </button>
      {open ? <p className="faq-answer">{a}</p> : null}
    </div>
  );
}

export default function Home() {
  const base = import.meta.env.BASE_URL;

  return (
    <div>
      <section className="panel hero-grid p-6 sm:p-8">
        <div className="space-y-6">
          <PageIntro
            eyebrow="Capture, annotate, record, and ship"
            title="A Windows screenshot tool that still feels like a utility."
            description="Yoink bundles capture, OCR, translation, sticker making, recording, and uploads into one calm interface instead of scattering them across five popups and a browser tab."
            actions={
              <>
                <Link to="/downloads" className="button-primary">
                  Download for Windows
                </Link>
                <a
                  href="https://github.com/jasperdevs/yoink"
                  target="_blank"
                  rel="noopener noreferrer"
                  className="button-secondary"
                >
                  Read the source
                </a>
              </>
            }
          />

          <div className="support-grid md:grid-cols-3">
            {workflowStats.map((item) => (
              <div key={item.label} className="feature-card">
                <div className="eyebrow">{item.label}</div>
                <p className="mt-3 text-lg font-semibold text-[var(--text)]">{item.value}</p>
              </div>
            ))}
          </div>
        </div>

        <div className="hero-visual">
          <div className="hero-visual-copy">
            <div className="hero-shot">
              <img src={base + "banner.svg"} alt="Yoink logotype" className="mx-auto max-w-[21rem] opacity-85" />
            </div>
            <div className="hero-shot hero-shot-secondary">
              <img src={base + "annotations.png"} alt="Annotated screenshot captured with Yoink" />
            </div>
          </div>
        </div>
      </section>

      <section className="panel p-6 sm:p-8">
        <div className="page-header">
          <div className="page-header-copy">
            <div className="eyebrow">What the product is good at</div>
            <h2 className="section-title">The feature set is broad, but the workflow stays short.</h2>
            <p className="section-copy">
              The point is not to be a kitchen-sink dashboard. The point is to keep related capture tasks in one place so you do not keep context-switching out of the shot you just took.
            </p>
          </div>
          <div className="page-header-actions">
            <Link to="/hotkeys" className="button-secondary">
              See the default hotkeys
            </Link>
          </div>
        </div>

        <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
          {featureCards.map((feature) => (
            <article key={feature.title} className="feature-card">
              <h3>{feature.title}</h3>
              <p>{feature.body}</p>
            </article>
          ))}
        </div>
      </section>

      {showcaseSections.map((section, index) => (
        <section
          key={section.title}
          className={`panel media-section ${index % 2 === 1 ? "media-section-reverse" : ""}`}
        >
          <div className="space-y-4">
            <div className="eyebrow">{section.eyebrow}</div>
            <h2 className="section-title">{section.title}</h2>
            <p className="section-copy">{section.body}</p>
          </div>
          <div className="media-frame">
            <img loading="lazy" src={base + section.image} alt={section.alt} />
          </div>
        </section>
      ))}

      <div className="grid gap-6 xl:grid-cols-[1.05fr_0.95fr]">
        <section className="panel chart-frame">
          <div className="space-y-4">
            <div className="eyebrow">Open source health</div>
            <h2 className="section-title">A utility with an active public trail.</h2>
            <p className="section-copy">
              Releases, issue history, and star growth are all visible. If you care where the project is heading, the changelog and repo activity are part of the product story.
            </p>
          </div>
          <div className="mt-6">
            <StarChart />
          </div>
        </section>

        <section className="panel p-6 sm:p-8">
          <div className="space-y-4">
            <div className="eyebrow">FAQ</div>
            <h2 className="section-title">Questions people usually ask before installing.</h2>
          </div>
          <div className="mt-6">
            {faq.map((item) => (
              <FaqItem key={item.q} q={item.q} a={item.a} />
            ))}
          </div>
        </section>
      </div>
    </div>
  );
}
