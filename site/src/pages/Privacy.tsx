const updatedAt = "April 30, 2026";

const thirdPartyServices = [
  "GitHub, for release checks, downloads, repository information, and optional GitHub uploads.",
  "Imgur, ImgBB, Catbox, Litterbox, Gyazo, file.io, Uguu, tmpfiles.org, Gofile, ImgPile, Dropbox, Google Drive, OneDrive, Azure Blob, Immich, FTP, SFTP, WebDAV, S3-compatible storage, and custom HTTP endpoints when you choose one of those upload destinations.",
  "ChatGPT, Claude, Gemini, and Google Lens when you use AI redirect features.",
  "Google Translate when you choose Google translation and provide an API key.",
  "Remove.bg, Photoroom, and DeepAI when you choose those cloud providers for sticker or upscale processing.",
  "Hugging Face and Python package repositories when you install optional local translation, sticker, or upscale models.",
];

function Section({ title, children }: { title: string; children: React.ReactNode }) {
  return (
    <section className="border-t border-[#EBEBEB] pt-8">
      <h2 className="text-[17px] text-black mb-3">{title}</h2>
      <div className="space-y-3 text-[14px] leading-relaxed text-black/70">
        {children}
      </div>
    </section>
  );
}

export default function Privacy() {
  return (
    <div className="py-12 space-y-8">
      <div className="space-y-3">
        <h1 className="text-[28px] text-black">privacy policy</h1>
        <p className="text-[14px] text-black/50">last updated: {updatedAt}</p>
        <p className="text-[15px] leading-relaxed text-black/70 max-w-[72ch]">
          OddSnap is a local-first screenshot, OCR, annotation, upload, sticker,
          and recording tool for Windows. OddSnap does not run an OddSnap account
          service, does not include advertising, and does not collect analytics
          or telemetry from the desktop app.
        </p>
      </div>

      <Section title="what oddsnap collects">
        <p>
          The OddSnap desktop app does not collect personal information for us.
          Captures, recordings, OCR text, color history, settings, logs, and
          search indexes are stored locally on your device when the related
          features are enabled.
        </p>
        <p>
          The website does not use its own analytics. It loads release and star
          information from GitHub so the downloads page and repository count can
          stay current. GitHub may receive normal request information such as
          your IP address and browser details when those requests are made.
        </p>
      </Section>

      <Section title="local files and history">
        <p>
          OddSnap can save screenshots, stickers, GIFs, videos, OCR history,
          color history, thumbnails, and image-search indexes on your computer.
          By default, saved captures and history are kept in local user folders
          such as Pictures/OddSnap and Pictures/OddSnap History.
        </p>
        <p>
          You control local retention in the app. Deleting history in OddSnap is
          intended to remove the local entries and their managed files.
        </p>
      </Section>

      <Section title="settings and secrets">
        <p>
          OddSnap stores app settings locally. If you add API keys, access
          tokens, passwords, or upload credentials, OddSnap stores them in the
          local settings file and protects supported secrets with Windows DPAPI
          for the current Windows user. Exported settings are redacted.
        </p>
        <p>
          Diagnostic logs are written locally and attempt to redact common
          secrets such as API keys, passwords, tokens, and authorization headers.
        </p>
      </Section>

      <Section title="uploads and cloud features">
        <p>
          Screenshots, recordings, stickers, or other files leave your device
          only when you choose an upload destination, enable auto-upload, use a
          cloud processing provider, or use a feature that opens a third-party
          service with your content.
        </p>
        <p>
          When you use an upload destination, the selected file and any required
          credentials are sent to that provider. The provider's own privacy
          policy and retention rules apply. Public or temporary hosting services
          may make uploaded files accessible to anyone with the resulting link.
        </p>
      </Section>

      <Section title="ocr, translation, stickers, and upscaling">
        <p>
          OCR uses the Windows OCR engine locally. Local translation, local
          sticker removal, and local upscaling run on your device after their
          optional runtimes or models are installed.
        </p>
        <p>
          If you choose Google Translate, Remove.bg, Photoroom, or DeepAI,
          OddSnap sends the text or image needed for that operation to the
          selected service. If you install optional local models or runtimes,
          OddSnap may download model files or Python packages from their
          upstream hosts.
        </p>
      </Section>

      <Section title="updates and downloads">
        <p>
          OddSnap can check GitHub for new releases and can download updates
          from GitHub when you choose to install them. Installed builds may also
          use the app's update system to retrieve release metadata and update
          packages from the configured GitHub release channel.
        </p>
      </Section>

      <Section title="third-party services">
        <p>
          Depending on what you choose to use, OddSnap may interact with these
          third-party services:
        </p>
        <ul className="space-y-2 list-disc pl-5">
          {thirdPartyServices.map((service) => (
            <li key={service}>{service}</li>
          ))}
        </ul>
        <p>
          Review the privacy policy of any service you configure or open from
          OddSnap. We do not control how those services process uploaded files,
          text, account tokens, or request metadata.
        </p>
      </Section>

      <Section title="children">
        <p>
          OddSnap is not directed to children, and we do not knowingly collect
          personal information from children through the desktop app or website.
        </p>
      </Section>

      <Section title="changes">
        <p>
          We may update this policy as OddSnap changes. The current version will
          be posted on this page with the latest update date.
        </p>
      </Section>

      <Section title="contact">
        <p>
          For privacy questions, open an issue or discussion on the OddSnap
          GitHub repository at{" "}
          <a
            href="https://github.com/jasperdevs/odd-snap"
            target="_blank"
            rel="noopener noreferrer"
            className="text-black underline hover:no-underline"
          >
            github.com/jasperdevs/odd-snap
          </a>
          .
        </p>
      </Section>
    </div>
  );
}
