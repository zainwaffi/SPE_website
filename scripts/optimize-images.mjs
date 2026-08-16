// Image optimiser: assets/ (masters, never served) -> wwwroot/ (resized WebP, served).
//
// Run with `npm run build:images`. Idempotent — skips an output whose source has not
// changed since it was written, so re-running is cheap.
//
// Why this exists: wwwroot previously shipped a 10.25 MB banner.jpg as the background of
// every page, a 468 KB favicon, a 536 KB logo rendered at 96 px, and eight ~180 KB PNG
// headshots rendered as ~56 px thumbnails — roughly 12.5 MB of images to draw well under
// 400 KB worth of actual pixels.

import { mkdir, readdir, stat, writeFile } from "node:fs/promises";
import { existsSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import sharp from "sharp";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const assets = path.join(root, "assets");
const wwwroot = path.join(root, "wwwroot");

/** @type {{src: string, out: string, width: number, format?: "webp"|"png", quality?: number}[]} */
const jobs = [
    // Full-bleed page background (MainLayout). Three widths behind a srcset.
    { src: "banner.jpg", out: "images/banner-640.webp", width: 640, quality: 72 },
    { src: "banner.jpg", out: "images/banner-1280.webp", width: 1280, quality: 72 },
    { src: "banner.jpg", out: "images/banner-1920.webp", width: 1920, quality: 70 },

    // Header logo, rendered at h-24 (96 px tall); 2x for high-DPI.
    { src: "spe-logo.png", out: "images/spe-logo.webp", width: 384, quality: 90 },

    // Home "About Us" photo, rendered in an aspect-4/3 box.
    { src: "student-chapter.jpeg", out: "images/student-chapter.webp", width: 800, quality: 78 },

    // Favicons must stay PNG — WebP favicons are still unevenly supported.
    { src: "favicon.png", out: "favicon.png", width: 32, format: "png" },
    { src: "favicon.png", out: "apple-touch-icon.png", width: 180, format: "png" },
];

async function isStale(srcPath, outPath) {
    if (!existsSync(outPath)) return true;
    const [srcStat, outStat] = await Promise.all([stat(srcPath), stat(outPath)]);
    return srcStat.mtimeMs > outStat.mtimeMs;
}

async function run({ src, out, width, format = "webp", quality = 80 }) {
    const srcPath = path.join(assets, src);
    const outPath = path.join(wwwroot, out);

    if (!existsSync(srcPath)) {
        console.warn(`  SKIP ${out} — missing master assets/${src}`);
        return 0;
    }

    await mkdir(path.dirname(outPath), { recursive: true });

    if (!(await isStale(srcPath, outPath))) {
        console.log(`  skip ${out} (up to date)`);
        return 0;
    }

    const pipeline = sharp(srcPath).resize({ width, withoutEnlargement: true });
    const buffer = await (format === "png"
        ? pipeline.png({ compressionLevel: 9, palette: true }).toBuffer()
        : pipeline.webp({ quality, effort: 6 }).toBuffer());

    await writeFile(outPath, buffer);

    const before = (await stat(srcPath)).size;
    console.log(`  ${out.padEnd(34)} ${fmt(before)} -> ${fmt(buffer.length)}`);
    return buffer.length;
}

// Committee headshots render as ~56-224 px square avatars; 256 px covers high-DPI.
async function runMembers() {
    const dir = path.join(assets, "members");
    if (!existsSync(dir)) {
        console.warn("  SKIP members — missing assets/members/");
        return 0;
    }

    let total = 0;
    for (const file of await readdir(dir)) {
        if (!/\.(png|jpe?g|webp)$/i.test(file)) continue;

        const name = path.parse(file).name.toLowerCase();
        const outPath = path.join(wwwroot, "members", `${name}.webp`);
        const srcPath = path.join(dir, file);

        await mkdir(path.dirname(outPath), { recursive: true });

        if (!(await isStale(srcPath, outPath))) {
            console.log(`  skip members/${name}.webp (up to date)`);
            continue;
        }

        const buffer = await sharp(srcPath)
            .resize({ width: 256, height: 256, fit: "cover", position: "attention" })
            .webp({ quality: 82, effort: 6 })
            .toBuffer();

        await writeFile(outPath, buffer);
        total += buffer.length;

        const before = (await stat(srcPath)).size;
        console.log(`  ${`members/${name}.webp`.padEnd(34)} ${fmt(before)} -> ${fmt(buffer.length)}`);
    }
    return total;
}

function fmt(bytes) {
    return bytes >= 1024 * 1024
        ? `${(bytes / 1024 / 1024).toFixed(2)} MB`
        : `${(bytes / 1024).toFixed(1)} KB`;
}

console.log("Optimising images (assets/ -> wwwroot/)\n");
let written = 0;
for (const job of jobs) written += await run(job);
written += await runMembers();
console.log(`\nDone. ${fmt(written)} written this run.`);
