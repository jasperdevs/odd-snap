import { readFile, writeFile } from "node:fs/promises";
import path from "node:path";
import { fileURLToPath } from "node:url";

const scriptDir = path.dirname(fileURLToPath(import.meta.url));
const indexPath = path.resolve(scriptDir, "../../docs/index.html");
const html = await readFile(indexPath, "utf8");

await writeFile(indexPath, html.replace(/\r\n/g, "\n"));
