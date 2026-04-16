import { useState, useEffect } from "react";
import { fetchGitHubRepoJson, logGitHubFetchError } from "../lib/github";
import type { GitHubRepository } from "../types/github";

export function useStarCount() {
  const [stars, setStars] = useState<number | null>(null);

  useEffect(() => {
    fetchGitHubRepoJson<GitHubRepository>()
      .then((data) => {
        if (typeof data.stargazers_count === "number") {
          setStars(data.stargazers_count);
        }
      })
      .catch((error) => logGitHubFetchError("star count", error));
  }, []);

  return stars;
}
