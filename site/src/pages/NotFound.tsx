import { Link } from "react-router-dom";
import PageIntro from "../components/PageIntro";

export default function NotFound() {
  return (
    <div className="flex min-h-[60vh] items-center justify-center">
      <div className="site-panel-strong max-w-xl p-8 text-center">
        <PageIntro
          eyebrow="404"
          title="That page does not exist."
          description="The route is missing or the link is stale. Head back to the homepage and keep moving."
        />
        <Link to="/" className="button-secondary">
          Back to home
        </Link>
      </div>
    </div>
  );
}
