import { Outlet, Link, useLocation } from "react-router-dom";
import { useStarCount } from "../hooks/useStarCount";

function StarIcon() {
  return (
    <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="currentColor" className="w-4 h-4">
      <path fillRule="evenodd" d="M10.788 3.21c.448-1.077 1.976-1.077 2.424 0l2.082 5.006 5.404.434c1.164.093 1.636 1.545.749 2.305l-4.117 3.527 1.257 5.273c.271 1.136-.964 2.033-1.96 1.425L12 18.354 7.373 21.18c-.996.608-2.231-.29-1.96-1.425l1.257-5.273-4.117-3.527c-.887-.76-.415-2.212.749-2.305l5.404-.434 2.082-5.005Z" clipRule="evenodd" />
    </svg>
  );
}

const navLinks = [
  { to: "/", label: "Home" },
  { to: "/downloads", label: "Downloads" },
  { to: "/changelog", label: "Changelog" },
  { to: "/hotkeys", label: "Hotkeys" },
  { to: "/donate", label: "Donate" },
];

export default function Layout() {
  const stars = useStarCount();
  const location = useLocation();

  return (
    <div className="site-shell flex flex-col">
      <header className="site-header">
        <div className="flex min-h-18 flex-col gap-4 px-6 py-4 sm:px-8">
          <div className="flex flex-wrap items-center justify-between gap-4">
            <div className="flex items-center gap-8">
              <Link to="/" className="flex items-center gap-3">
                <span className="support-mark h-10 w-10 rounded-[10px]">
                  <img src={import.meta.env.BASE_URL + "favicon.ico"} alt="Yoink" className="h-5 w-5" />
                </span>
                <div className="leading-tight">
                  <div className="font-semibold text-[1.02rem] tracking-[-0.015em] text-[var(--text)]">Yoink</div>
                  <div className="text-[0.78rem] text-[var(--muted)]">
                    Capture, annotate, OCR, and ship.
                  </div>
                </div>
              </Link>
              <nav className="hidden sm:flex items-center gap-2">
                {navLinks.map((link) => (
                  <Link
                    key={link.to}
                    to={link.to}
                    className={`tag-pill ${
                      location.pathname === link.to
                        ? "border-[var(--line-strong)] bg-[rgba(255,255,255,0.06)] text-[var(--text)]"
                        : "hover:border-[var(--line-strong)] hover:bg-[rgba(255,255,255,0.04)] hover:text-[var(--text)]"
                    }`}
                  >
                    {link.label}
                  </Link>
                ))}
              </nav>
            </div>
            <a
              href="https://github.com/jasperdevs/yoink"
              target="_blank"
              rel="noopener noreferrer"
              className="button-secondary text-sm"
            >
              <StarIcon />
              <span>{stars !== null ? stars.toLocaleString() : "..."}</span>
            </a>
          </div>
          <nav className="flex flex-wrap items-center gap-2 sm:hidden">
            {navLinks.map((link) => (
              <Link
                key={link.to}
                to={link.to}
                className={`tag-pill ${
                  location.pathname === link.to
                    ? "border-[var(--line-strong)] bg-[rgba(255,255,255,0.06)] text-[var(--text)]"
                    : "hover:border-[var(--line-strong)] hover:bg-[rgba(255,255,255,0.04)] hover:text-[var(--text)]"
                }`}
              >
                {link.label}
              </Link>
            ))}
          </nav>
        </div>
      </header>

      <main className="site-main flex-1">
        <Outlet />
      </main>

      <footer className="site-footer">
        <div className="flex flex-col gap-4 px-6 py-8 text-sm sm:flex-row sm:items-center sm:justify-between sm:px-8">
          <div>
            <div className="font-medium text-[var(--text)]">Yoink is open source under GPL-3.0.</div>
            <div className="text-[var(--subtle)]">
              A focused Windows toolkit for capture, OCR, recording, and sharing.
            </div>
          </div>
          <div className="flex flex-wrap items-center gap-4">
            <a href="https://github.com/jasperdevs/yoink" target="_blank" rel="noopener noreferrer" className="hover:text-[var(--text)] transition-colors">GitHub</a>
            <a href="https://ko-fi.com/jasperdevs" target="_blank" rel="noopener noreferrer" className="hover:text-[var(--text)] transition-colors">Ko-fi</a>
            <Link to="/downloads" className="hover:text-[var(--text)] transition-colors">Downloads</Link>
            <Link to="/changelog" className="hover:text-[var(--text)] transition-colors">Changelog</Link>
          </div>
        </div>
      </footer>
    </div>
  );
}
