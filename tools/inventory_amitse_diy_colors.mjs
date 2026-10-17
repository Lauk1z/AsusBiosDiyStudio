import fs from "node:fs";

const file = process.argv[2];
if (!file) {
  console.error("Usage: node inventory_amitse_diy_colors.mjs <AMITSE PE>");
  process.exit(2);
}

const data = fs.readFileSync(file);
if (data.readUInt16LE(0) !== 0x5a4d) throw new Error("Input is not a PE image");
const peOffset = data.readUInt32LE(0x3c);
if (data.readUInt32LE(peOffset) !== 0x00004550) throw new Error("Invalid PE signature");
const coff = peOffset + 4;
const sectionCount = data.readUInt16LE(coff + 2);
const optionalHeaderSize = data.readUInt16LE(coff + 16);
const sectionTable = coff + 20 + optionalHeaderSize;
let section;
for (let index = 0; index < sectionCount; index += 1) {
  const offset = sectionTable + index * 40;
  const name = data.subarray(offset, offset + 8).toString("ascii").replace(/\0+$/, "");
  if (name === ".data") {
    const start = data.readUInt32LE(offset + 20);
    section = { start, end: start + data.readUInt32LE(offset + 16) };
  }
}
if (!section) throw new Error("PE .data section was not found");

const allowedAlpha = new Set([0x50, 0x66, 0x80, 0x99, 0xcc, 0xff]);
const groups = new Map();
for (let offset = section.start; offset + 24 <= section.end; offset += 8) {
  const owner = data.readUInt32LE(offset);
  const property = data.readUInt32LE(offset + 4);
  const value = data.readUInt32LE(offset + 8);
  const alpha = value >>> 24;
  const blue = value & 0xff;
  const green = (value >>> 8) & 0xff;
  const red = (value >>> 16) & 0xff;
  const maximum = Math.max(red, green, blue);
  const minimum = Math.min(red, green, blue);
  if (
    owner < 1 || owner > 0xffffff ||
    property < 0x100 || property > 0x500 ||
    !allowedAlpha.has(alpha) ||
    maximum - minimum < 24 ||
    !data.subarray(offset + 12, offset + 24).every((byte) => byte === 0)
  ) continue;

  const group = groups.get(value) ?? { value, count: 0, properties: new Map() };
  group.count++;
  group.properties.set(property, (group.properties.get(property) ?? 0) + 1);
  groups.set(value, group);
}

console.log(`FILE=${file}`);
console.log(`DATA_START=0x${section.start.toString(16)} DATA_END=0x${section.end.toString(16)}`);
for (const group of [...groups.values()].sort((a, b) => b.count - a.count)) {
  const value = group.value;
  const alpha = value >>> 24;
  const blue = value & 0xff;
  const green = (value >>> 8) & 0xff;
  const red = (value >>> 16) & 0xff;
  const argb = `0x${value.toString(16).padStart(8, "0")}`;
  const rgb = `#${red.toString(16).padStart(2, "0")}${green.toString(16).padStart(2, "0")}${blue.toString(16).padStart(2, "0")}`.toUpperCase();
  const properties = [...group.properties]
    .sort((a, b) => a[0] - b[0])
    .map(([property, count]) => `0x${property.toString(16)}:${count}`)
    .join(",");
  console.log(`COLOR=${argb} RGB=${rgb} ALPHA=${alpha} COUNT=${group.count} PROPERTIES=${properties}`);
}
