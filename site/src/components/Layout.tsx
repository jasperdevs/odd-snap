import { Outlet, Link, useLocation } from "react-router-dom";
import { useStarCount } from "../hooks/useStarCount";

const navLinks = [
  { to: "/", label: "home" },
  { to: "/downloads", label: "downloads" },
  { to: "/donate", label: "donate" },
];

export default function Layout() {
  const stars = useStarCount();
  const location = useLocation();

  return (
    <div className="relative min-h-screen flex flex-col bg-[#F6F6F6] text-black">
      <header
        className="sticky top-0 z-50 bg-[#F6F6F6]"
        style={{ paddingTop: "env(safe-area-inset-top)" }}
      >
        <div className="mx-auto max-w-[800px] px-6 sm:px-8 h-14 flex items-center justify-between">
          <Link to="/" className="flex items-center gap-2 text-[15px] text-black">
            <img src={import.meta.env.BASE_URL + "favicon.ico"} alt="yoink" className="w-5 h-5" />
            yoink
          </Link>
          <nav className="hidden sm:flex items-center gap-5 text-[14px]">
            {navLinks.map((link) => (
              <Link
                key={link.to}
                to={link.to}
                className={`transition-colors ${
                  location.pathname === link.to
                    ? "text-black underline underline-offset-4"
                    : "text-black/60 hover:text-black"
                }`}
              >
                {link.label}
              </Link>
            ))}
          </nav>
          <a
            href="https://github.com/jasperdevs/yoink"
            target="_blank"
            rel="noopener noreferrer"
            className="hidden sm:inline-flex items-center gap-1.5 text-[14px] text-black/60 hover:text-black transition-colors"
          >
            <svg width="14" height="14" viewBox="0 0 24 24" fill="currentColor"><path d="M12 .297c-6.63 0-12 5.373-12 12 0 5.303 3.438 9.8 8.205 11.385.6.113.82-.258.82-.577 0-.285-.01-1.04-.015-2.04-3.338.724-4.042-1.61-4.042-1.61C4.422 18.07 3.633 17.7 3.633 17.7c-1.087-.744.084-.729.084-.729 1.205.084 1.838 1.236 1.838 1.236 1.07 1.835 2.809 1.305 3.495.998.108-.776.417-1.305.76-1.605-2.665-.3-5.466-1.332-5.466-5.93 0-1.31.465-2.38 1.235-3.22-.135-.303-.54-1.523.105-3.176 0 0 1.005-.322 3.3 1.23.96-.267 1.98-.399 3-.405 1.02.006 2.04.138 3 .405 2.28-1.552 3.285-1.23 3.285-1.23.645 1.653.24 2.873.12 3.176.765.84 1.23 1.91 1.23 3.22 0 4.61-2.805 5.625-5.475 5.92.42.36.81 1.096.81 2.22 0 1.606-.015 2.896-.015 3.286 0 .315.21.69.825.57C20.565 22.092 24 17.592 24 12.297c0-6.627-5.373-12-12-12" /></svg>
            <span>{stars !== null ? stars.toLocaleString() : "..."}</span>
          </a>
        </div>
        <nav className="sm:hidden flex items-center gap-4 px-6 pb-3 text-[13px] overflow-x-auto">
          {navLinks.map((link) => (
            <Link
              key={link.to}
              to={link.to}
              className={`shrink-0 transition-colors ${
                location.pathname === link.to
                  ? "text-black underline underline-offset-4"
                  : "text-black/60"
              }`}
            >
              {link.label}
            </Link>
          ))}
        </nav>
      </header>

      {/* Top fade, sits directly under the sticky header */}
      <div
        className="sticky z-40 h-8 pointer-events-none"
        style={{
          top: "calc(3.5rem + env(safe-area-inset-top))",
          background: "linear-gradient(to bottom, #F6F6F6, rgba(246,246,246,0))",
          marginBottom: "-2rem",
        }}
      />

      <main
        className="flex-1 mx-auto w-full max-w-[800px] px-6 sm:px-8"
        style={{
          paddingLeft: "max(1.5rem, env(safe-area-inset-left))",
          paddingRight: "max(1.5rem, env(safe-area-inset-right))",
        }}
      >
        <Outlet />
      </main>

      {/* Bottom fade, fixed above the viewport bottom */}
      <div
        className="fixed bottom-0 left-0 right-0 z-40 h-12 pointer-events-none"
        style={{
          background: "linear-gradient(to top, #F6F6F6, rgba(246,246,246,0))",
          paddingBottom: "env(safe-area-inset-bottom)",
        }}
      />

      <footer
        className="mt-20"
        style={{ paddingBottom: "env(safe-area-inset-bottom)" }}
      >
        <div className="mx-auto max-w-[800px] px-6 sm:px-8 py-8 flex flex-col sm:flex-row items-start sm:items-center justify-between gap-3 text-[13px] text-black/50">
          <span>yoink is open source under gpl-3.0.</span>
          <div className="flex items-center gap-4">
            <a href="https://github.com/jasperdevs/yoink" target="_blank" rel="noopener noreferrer" className="hover:text-black transition-colors">github</a>
            <a href="https://ko-fi.com/jasperdevs" target="_blank" rel="noopener noreferrer" className="hover:text-black transition-colors">ko-fi</a>
            <Link to="/downloads" className="hover:text-black transition-colors">downloads</Link>
          </div>
        </div>
      </footer>
    </div>
  );
}
