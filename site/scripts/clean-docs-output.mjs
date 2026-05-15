import { readdir, rm } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const docsDir = path.resolve(scriptDir, "../../docs");
const keepNames = new Set([".nojekyll", "CNAME", "CHANGELOG.md"]);

for (const entry of await readdir(docsDir, { withFileTypes: true })) {
  if (entry.isFile() && keepNames.has(entry.name)) {
    continue;
  }

  await rm(path.join(docsDir, entry.name), { recursive: true, force: true });
}
