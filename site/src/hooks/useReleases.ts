import { useState, useEffect } from "react";
import { fetchGitHubRepoJson, logGitHubFetchError } from "../lib/github";
import type { GitHubRelease, GitHubReleaseAsset } from "../types/github";

export type ReleaseAsset = GitHubReleaseAsset;
export type Release = GitHubRelease;

export function useReleases() {
  const [releases, setReleases] = useState<Release[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    fetchGitHubRepoJson<Release[]>("/releases?per_page=20")
      .then((data) => {
        if (Array.isArray(data)) {
          setReleases(data);
        }
      })
      .catch((error) => logGitHubFetchError("releases", error))
      .finally(() => setLoading(false));
  }, []);

  return { releases, loading };
}
