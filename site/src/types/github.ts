export interface GitHubReleaseAsset {
  name: string;
  browser_download_url: string;
  size: number;
  content_type: string;
}

export interface GitHubRelease {
  id: number;
  tag_name: string;
  name: string;
  published_at: string;
  body: string;
  html_url: string;
  assets: GitHubReleaseAsset[];
  tarball_url: string;
  zipball_url: string;
}

export interface GitHubRepository {
  stargazers_count: number;
}

export interface GitHubStarEvent {
  starred_at: string;
}
