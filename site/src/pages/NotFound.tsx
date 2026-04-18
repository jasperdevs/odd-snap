import { Link } from "react-router-dom";
import { Button } from "@/components/ui/button";

export default function NotFound() {
  return (
    <div className="flex flex-col items-center justify-center py-32 text-center">
      <h1 className="text-[56px] text-black mb-3">404</h1>
      <p className="text-black/70 text-[16px] mb-8">page not found.</p>
      <Button asChild size="lg" variant="primary">
        <Link to="/">back to home</Link>
      </Button>
    </div>
  );
}
