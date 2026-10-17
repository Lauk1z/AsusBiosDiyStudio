import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";

const [inputPath, outputDirectory, manifestPath] = process.argv.slice(2);
if (!inputPath || !outputDirectory) {
  console.error("Usage: node unpack_asus_ui_bundle.mjs <bundle> <output-directory>");
  process.exit(2);
}

const data = fs.readFileSync(inputPath);
const signature = Buffer.from("89504e470d0a1a0a", "hex");
const align4 = (value) => (value + 3) & ~3;
const pngLength = (offset) => {
  if (!data.subarray(offset, offset + 8).equals(signature)) return null;
  let cursor = offset + 8;
  while (cursor + 12 <= data.length) {
    const size = data.readUInt32BE(cursor);
    const type = data.subarray(cursor + 4, cursor + 8).toString("ascii");
    cursor += 12 + size;
    if (cursor > data.length) return null;
    if (type === "IEND") return cursor - offset;
  }
  return null;
};

fs.mkdirSync(outputDirectory, { recursive: true });
let cursor = 0x20;
let index = 0;
const manifest = [];
while (cursor < data.length) {
  if (cursor + 0x20 > data.length) throw new Error(`Truncated record ${index}`);
  const declared = data.readUInt32LE(cursor);
  const pngOffset = cursor + 0x20;
  const size = pngLength(pngOffset);
  if (size === null || size !== declared) throw new Error(`Invalid PNG record ${index}`);
  const png = data.subarray(pngOffset, pngOffset + size);
  const width = png.readUInt32BE(16);
  const height = png.readUInt32BE(20);
  const hash = crypto.createHash("sha256").update(png).digest("hex").toUpperCase();
  const name = `${String(index).padStart(4, "0")}_${width}x${height}.png`;
  fs.writeFileSync(path.join(outputDirectory, name), png);
  const line = `IMAGE=${index} WIDTH=${width} HEIGHT=${height} SIZE=${size} SHA256=${hash} FILE=${name}`;
  manifest.push(line);
  if (!manifestPath) console.log(line);
  cursor = align4(pngOffset + size);
  index++;
}
if (cursor !== data.length) throw new Error("Invalid trailing bundle data");
if (manifestPath) fs.writeFileSync(manifestPath, manifest.join("\n") + "\n", "utf8");
console.log(`RECORDS=${index}`);
