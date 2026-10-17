import fs from "node:fs";
import crypto from "node:crypto";

const [inputPath, outputPath, ...mappingArgs] = process.argv.slice(2);
if (!inputPath || !outputPath || mappingArgs.length === 0) {
  console.error("Usage: node patch_amitse_diy_colors.mjs <input PE> <output PE> <old-argb=new-argb> [...]");
  process.exit(2);
}

const input = fs.readFileSync(inputPath);
const output = Buffer.from(input);
if (input.readUInt16LE(0) !== 0x5a4d) throw new Error("Input is not a PE image");
const peOffset = input.readUInt32LE(0x3c);
if (input.readUInt32LE(peOffset) !== 0x00004550) throw new Error("Invalid PE signature");
const coff = peOffset + 4;
const sectionCount = input.readUInt16LE(coff + 2);
const optionalHeaderSize = input.readUInt16LE(coff + 16);
const sectionTable = coff + 20 + optionalHeaderSize;
let section;
for (let index = 0; index < sectionCount; index += 1) {
  const offset = sectionTable + index * 40;
  const name = input.subarray(offset, offset + 8).toString("ascii").replace(/\0+$/, "");
  if (name === ".data") {
    const start = input.readUInt32LE(offset + 20);
    section = { start, end: start + input.readUInt32LE(offset + 16) };
  }
}
if (!section) throw new Error("PE .data section was not found");

const parseValue = (text) => {
  const cleaned = text.toLowerCase().replace(/^0x/, "");
  if (!/^[0-9a-f]{8}$/.test(cleaned)) throw new Error(`Invalid ARGB value: ${text}`);
  return Number.parseInt(cleaned, 16) >>> 0;
};

const mappings = new Map();
for (const arg of mappingArgs) {
  const separator = arg.indexOf("=");
  if (separator < 1) throw new Error(`Invalid mapping: ${arg}`);
  const oldValue = parseValue(arg.slice(0, separator));
  const newValue = parseValue(arg.slice(separator + 1));
  if ((oldValue >>> 24) !== (newValue >>> 24)) {
    throw new Error(`Alpha must remain unchanged: ${arg}`);
  }
  mappings.set(oldValue, newValue);
}

const counts = new Map([...mappings.keys()].map((value) => [value, 0]));
for (let offset = section.start; offset + 24 <= section.end; offset += 8) {
  const owner = input.readUInt32LE(offset);
  const property = input.readUInt32LE(offset + 4);
  const value = input.readUInt32LE(offset + 8);
  if (
    owner < 1 || owner > 0xffffff ||
    property < 0x100 || property > 0x500 ||
    !input.subarray(offset + 12, offset + 24).every((byte) => byte === 0) ||
    !mappings.has(value)
  ) continue;
  output.writeUInt32LE(mappings.get(value), offset + 8);
  counts.set(value, counts.get(value) + 1);
}

for (const [oldValue, newValue] of mappings) {
  const count = counts.get(oldValue);
  if (count === 0) throw new Error(`No structured records matched 0x${oldValue.toString(16).padStart(8, "0")}`);
  console.log(`MAPPED=0x${oldValue.toString(16).padStart(8, "0")} TO=0x${newValue.toString(16).padStart(8, "0")} COUNT=${count}`);
}

fs.writeFileSync(outputPath, output);
const sha256 = (bytes) => crypto.createHash("sha256").update(bytes).digest("hex").toUpperCase();
console.log(`OUTPUT_SHA256=${sha256(output)}`);
