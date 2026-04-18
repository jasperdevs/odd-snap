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
    <div className="mx-auto max-w-[900px] min-h-screen flex flex-col bg-white text-black">
      <header className="sticky top-0 z-50 border-b border-[#EBEBEB] bg-white">
        <div className="px-8 h-16 flex items-center justify-between">
          <div className="flex items-center gap-8">
            <Link to="/" className="flex items-center gap-2.5 font-semibold text-lg text-black">
              <img src={import.meta.env.BASE_URL + "favicon.ico"} alt="Yoink" className="w-6 h-6" />
              Yoink
            </Link>
            <nav className="hidden sm:flex items-center gap-2">
              {navLinks.map((link) => (
                <Link
                  key={link.to}
                  to={link.to}
                  className={`px-3 py-1.5 rounded-md text-[15px] transition-colors ${
                    location.pathname === link.to
                      ? "text-black bg-[#EBEBEB]"
                      : "text-black/60 hover:text-black hover:bg-[#EBEBEB]"
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
            className="flex items-center gap-2 px-4 py-2 rounded-md border border-[#EBEBEB] text-[15px] text-black/70 hover:text-black hover:bg-[#EBEBEB] transition-colors"
          >
            <StarIcon />
            {stars !== null ? stars.toLocaleString() : "..."}
          </a>
        </div>
        <nav className="sm:hidden flex items-center gap-1 px-8 pb-3">
          {navLinks.map((link) => (
            <Link
              key={link.to}
              to={link.to}
              className={`px-3 py-1.5 rounded-md text-sm transition-colors ${
                location.pathname === link.to
                  ? "text-black bg-[#EBEBEB]"
                  : "text-black/60 hover:text-black"
              }`}
            >
              {link.label}
            </Link>
          ))}
        </nav>
      </header>

      <main className="flex-1">
        <Outlet />
      </main>

      <footer className="border-t border-[#EBEBEB]">
        <div className="px-8 py-10 flex flex-col sm:flex-row items-center justify-between gap-4 text-[13px] text-black/50">
          <span>Yoink is open source under the GPL-3.0 license.</span>
          <div className="flex items-center gap-5">
            <a href="https://github.com/jasperdevs/yoink" target="_blank" rel="noopener noreferrer" className="hover:text-black transition-colors">GitHub</a>
            <a href="https://ko-fi.com/jasperdevs" target="_blank" rel="noopener noreferrer" className="hover:text-black transition-colors">Ko-fi</a>
            <Link to="/changelog" className="hover:text-black transition-colors">Changelog</Link>
          </div>
        </div>
      </footer>
    </div>
  );
}
