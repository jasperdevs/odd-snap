import { Link } from "react-router-dom";

export default function NotFound() {
  return (
    <div className="flex flex-col items-center justify-center py-32 text-center">
      <h1 className="text-[56px] text-black mb-3">404</h1>
      <p className="text-black/70 text-[16px] mb-8">page not found.</p>
      <Link
        to="/"
        className="inline-flex items-center justify-center px-5 py-2.5 rounded-md bg-black text-white text-[14px] hover:bg-black/85 transition-colors"
      >
        back to home
      </Link>
    </div>
  );
}
