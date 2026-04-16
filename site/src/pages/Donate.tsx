import PageIntro from "../components/PageIntro";

function GitHubIcon() {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" className="h-7 w-7 text-[var(--muted)]">
      <path d="M12 .297c-6.63 0-12 5.373-12 12 0 5.303 3.438 9.8 8.205 11.385.6.113.82-.258.82-.577 0-.285-.01-1.04-.015-2.04-3.338.724-4.042-1.61-4.042-1.61C4.422 18.07 3.633 17.7 3.633 17.7c-1.087-.744.084-.729.084-.729 1.205.084 1.838 1.236 1.838 1.236 1.07 1.835 2.809 1.305 3.495.998.108-.776.417-1.305.76-1.605-2.665-.3-5.466-1.332-5.466-5.93 0-1.31.465-2.38 1.235-3.22-.135-.303-.54-1.523.105-3.176 0 0 1.005-.322 3.3 1.23.96-.267 1.98-.399 3-.405 1.02.006 2.04.138 3 .405 2.28-1.552 3.285-1.23 3.285-1.23.645 1.653.24 2.873.12 3.176.765.84 1.23 1.91 1.23 3.22 0 4.61-2.805 5.625-5.475 5.92.42.36.81 1.096.81 2.22 0 1.606-.015 2.896-.015 3.286 0 .315.21.69.825.57C20.565 22.092 24 17.592 24 12.297c0-6.627-5.373-12-12-12" />
    </svg>
  );
}

const cards = [
  {
    title: "Ko-fi",
    description: "Buy me a coffee to support development.",
    url: "https://ko-fi.com/jasperdevs",
    buttonText: "Open Ko-fi",
    logo: (
      <img
        src={import.meta.env.BASE_URL + "kofi-logo.png"}
        alt="Ko-fi"
        className="h-7 w-7 rounded object-cover"
      />
    ),
  },
  {
    title: "PayPal",
    description: "Send a one-time donation through PayPal.",
    url: "https://www.paypal.com/paypalme/9KGFX",
    buttonText: "Open PayPal",
    logo: <span className="font-semibold">PP</span>,
  },
  {
    title: "GitHub Star",
    description: "Star the repo to help more people find Yoink.",
    url: "https://github.com/jasperdevs/yoink",
    buttonText: "Open GitHub",
    logo: <GitHubIcon />,
  },
];

export default function Donate() {
  return (
    <div className="space-y-6">
      <PageIntro
        eyebrow="Support the project"
        title="Yoink is free to use. Support helps keep releases moving."
        description="If the app saves you time, the most useful support is a star, a share, or a small donation."
      />

      <div className="support-grid">
        {cards.map((card) => (
          <a
            key={card.title}
            href={card.url}
            target="_blank"
            rel="noopener noreferrer"
            className="panel support-card flex flex-col gap-4 text-left transition-colors hover:border-[rgba(255,245,231,0.22)]"
          >
            <div className="support-mark">
              {card.logo}
            </div>
            <div>
              <h2 className="text-lg font-medium text-[var(--text)]">{card.title}</h2>
              <p className="mt-2 text-[var(--muted)]">{card.description}</p>
            </div>
            <span className="button-secondary mt-auto w-fit text-sm">
              {card.buttonText}
            </span>
          </a>
        ))}
      </div>
    </div>
  );
}
