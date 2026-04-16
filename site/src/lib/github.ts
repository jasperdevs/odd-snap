const REPOSITORY_API = "https://api.github.com/repos/jasperdevs/yoink";

export async function fetchGitHubRepoJson<T>(path = "", init?: RequestInit): Promise<T> {
  const response = await fetch(`${REPOSITORY_API}${path}`, init);
  if (!response.ok) {
    throw new Error(`GitHub request failed (${response.status})`);
  }

  return response.json() as Promise<T>;
}

export function logGitHubFetchError(context: string, error: unknown) {
  console.error(`Failed to fetch ${context}`, error);
}
