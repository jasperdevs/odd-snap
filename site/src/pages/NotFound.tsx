import { Link } from "react-router-dom";

export default function NotFound() {
  return (
    <div className="flex flex-col items-center justify-center py-32 px-8 text-center">
      <h1 className="text-6xl font-bold text-[#e8e6e3] mb-4">404</h1>
      <p className="text-[#8a8a80] text-lg mb-8">Page not found.</p>
      <Link
        to="/"
        className="inline-flex items-center px-6 py-3 rounded-md border border-[#2a2a28] font-medium text-[#d0cec8] hover:bg-[#1c1c1a] hover:border-[#444440] transition-colors"
      >
        Back to Home
      </Link>
    </div>
  );
}
