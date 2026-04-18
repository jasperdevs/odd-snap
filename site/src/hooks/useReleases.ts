import { useState, useEffect } from "react";

export interface ReleaseAsset {
  name: string;
  browser_download_url: string;
  size: number;
  content_type: string;
}

export interface Release {
  id: number;
  tag_name: string;
  name: string;
  published_at: string;
  body: string;
  html_url: string;
  assets: ReleaseAsset[];
  tarball_url: string;
  zipball_url: string;
}

export function useReleases() {
  const [releases, setReleases] = useState<Release[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    fetch(
      "https://api.github.com/repos/jasperdevs/odd-snap/releases?per_page=20"
    )
      .then((res) => res.json())
      .then((data) => {
        if (Array.isArray(data)) {
          setReleases(data);
        }
      })
      .catch(() => {})
      .finally(() => setLoading(false));
  }, []);

  return { releases, loading };
}
