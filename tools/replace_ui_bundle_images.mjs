import fs from "node:fs";
import crypto from "node:crypto";

const [inputPath, outputPath, ...replacementArgs] = process.argv.slice(2);
if (!inputPath || !outputPath || replacementArgs.length === 0) {
  console.error(
    "Usage: node replace_ui_bundle_images.mjs <input-bundle> <output-bundle> <index=png> [...]",
  );
  process.exit(2);
}

const PNG_SIGNATURE = Buffer.from("89504e470d0a1a0a", "hex");
const GLOBAL_HEADER_SIZE = 0x20;
const RECORD_HEADER_SIZE = 0x20;
const align4 = (value) => (value + 3) & ~3;

function pngLength(data, offset) {
  if (!data.subarray(offset, offset + 8).equals(PNG_SIGNATURE)) return null;
  let cursor = offset + 8;
  while (cursor + 12 <= data.length) {
    const length = data.readUInt32BE(cursor);
    const type = data.subarray(cursor + 4, cursor + 8).toString("ascii");
    cursor += 12 + length;
    if (cursor > data.length) return null;
    if (type === "IEND") return cursor - offset;
  }
  return null;
}

function parseBundle(data) {
  const records = [];
  let cursor = GLOBAL_HEADER_SIZE;
  while (cursor < data.length) {
    const header = Buffer.from(data.subarray(cursor, cursor + RECORD_HEADER_SIZE));
    if (header.length !== RECORD_HEADER_SIZE) throw new Error("Truncated record header");
    const pngOffset = cursor + RECORD_HEADER_SIZE;
    const size = pngLength(data, pngOffset);
    if (size === null || header.readUInt32LE(0) !== size) {
      throw new Error(`Invalid PNG record ${records.length}`);
    }
    records.push({
      header,
      png: Buffer.from(data.subarray(pngOffset, pngOffset + size)),
    });
    cursor = align4(pngOffset + size);
  }
  if (cursor !== data.length) throw new Error("Invalid trailing data");
  return {
    globalHeader: Buffer.from(data.subarray(0, GLOBAL_HEADER_SIZE)),
    records,
  };
}

const replacements = new Map();
for (const arg of replacementArgs) {
  const separator = arg.indexOf("=");
  if (separator < 1) throw new Error(`Invalid replacement argument: ${arg}`);
  const index = Number.parseInt(arg.slice(0, separator), 10);
  const pngPath = arg.slice(separator + 1);
  const png = fs.readFileSync(pngPath);
  const size = pngLength(png, 0);
  if (size !== png.length) throw new Error(`${pngPath} is not a standalone valid PNG`);
  replacements.set(index, png);
}

const input = fs.readFileSync(inputPath);
const bundle = parseBundle(input);
const parts = [bundle.globalHeader];

for (let index = 0; index < bundle.records.length; index += 1) {
  const record = bundle.records[index];
  const png = replacements.get(index) ?? record.png;
  const header = Buffer.from(record.header);
  header.writeUInt32LE(png.length, 0);
  parts.push(header, png);
  const padding = align4(png.length) - png.length;
  if (padding) parts.push(Buffer.alloc(padding));
}

for (const index of replacements.keys()) {
  if (index < 0 || index >= bundle.records.length) {
    throw new Error(`Replacement index ${index} is outside the bundle`);
  }
}

const output = Buffer.concat(parts);
const verified = parseBundle(output);
fs.writeFileSync(outputPath, output);

const sha256 = (data) => crypto.createHash("sha256").update(data).digest("hex").toUpperCase();
console.log(`RECORDS=${verified.records.length}`);
for (const [index, png] of replacements) {
  console.log(
    `REPLACED_INDEX=${index} WIDTH=${png.readUInt32BE(16)} HEIGHT=${png.readUInt32BE(20)} ` +
      `PNG_SIZE=${png.length} PNG_SHA256=${sha256(png)}`,
  );
}
console.log(`OUTPUT_SIZE=${output.length}`);
console.log(`OUTPUT_SHA256=${sha256(output)}`);
