import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";

export default function NotFound() {
  return (
    <div className="flex flex-col items-center justify-center py-32 px-8 text-center">
      <h1 className="text-6xl font-bold text-black mb-4">404</h1>
      <p className="text-black/60 text-lg mb-8">Page not found.</p>
      <Button asChild variant="tertiary" size="lg">
        <Link to="/">Back to Home</Link>
      </Button>
    </div>
  );
}
