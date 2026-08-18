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

/** @type {{src: string, out: string, width: number, height?: number, format?: "webp"|"png", quality?: number, position?: string}[]} */
// #UpdateLink — every served image on the site is produced here. To swap a photo, replace
// the master in assets/ under the same filename and re-run `npm run build:images`; only edit
// a job when you are adding a new image or changing the size it is rendered at.
const jobs = [
    // Full-bleed page background (MainLayout). Three widths behind a srcset.
    { src: "banner.jpg", out: "images/banner-640.webp", width: 640, quality: 72 },
    { src: "banner.jpg", out: "images/banner-1280.webp", width: 1280, quality: 72 },
    { src: "banner.jpg", out: "images/banner-1920.webp", width: 1920, quality: 70 },

    // Header logo. Rendered up to h-24 (96 px tall, ~190 px wide), so a phone at 3x DPR wants
    // ~570 px of real pixels — the single 384 px file it used to get was visibly soft there.
    // Two widths behind a srcset instead.
    { src: "spe-logo.png", out: "images/spe-logo-384.webp", width: 384, quality: 90 },
    { src: "spe-logo.png", out: "images/spe-logo-768.webp", width: 768, quality: 90 },

    // Home "About Us" photo, rendered in an aspect-4/3 box.
    { src: "student-chapter.jpeg", out: "images/student-chapter.webp", width: 800, quality: 78 },

    // Banner behind the "Why Join Us" heading (.spe-photo-wash). Kept at its native 1588 px so
    // the band is never upscaled, and re-encoded from a 1.5 MB PNG — PNG is the wrong format
    // for a photograph, which is most of that size.
    { src: "background.png", out: "images/background-1588.webp", width: 1588, quality: 80 },

    // Photo beside the "Technical & Academic Development" copy (.spe-academic-photo). Rendered
    // in a half-width aspect-4/3 box, so 1280 covers it on a high-DPI screen.
    { src: "academic.jpg", out: "images/academic-1280.webp", width: 1280, quality: 78 },

    // Photo behind the "Competitions & Academic Recognition" band (.spe-competition-wash).
    { src: "competition.jpg", out: "images/competition-1280.webp", width: 1280, quality: 76 },

    // Group photo under the "Financial Support" intro on the Scholarships page. A ~4.2:1 strip
    // spanning the full band (max 1200 px), so two widths behind a srcset — a single 1280 file
    // would be barely above 1x there and visibly soft on a high-DPI screen.
    { src: "bursaries.jpg", out: "images/bursaries-1280.webp", width: 1280, quality: 78 },
    { src: "bursaries.jpg", out: "images/bursaries-1920.webp", width: 1920, quality: 76 },

    // Photo at the top of the Careers card on the Opportunities page. A ~2.2:1 strip in a
    // ~370 px column, so 800 covers it on a high-DPI screen. Cropped rather than letterboxed;
    // "attention" keeps the busiest region.
    { src: "careers.jpg", out: "images/careers-800.webp", width: 800, height: 360, quality: 78 },

    // Favicons must stay PNG — WebP favicons are still unevenly supported.
    { src: "favicon.png", out: "favicon.png", width: 32, format: "png" },
    { src: "favicon.png", out: "apple-touch-icon.png", width: 180, format: "png" },
];

async function isStale(srcPath, outPath) {
    if (!existsSync(outPath)) return true;
    const [srcStat, outStat] = await Promise.all([stat(srcPath), stat(outPath)]);
    return srcStat.mtimeMs > outStat.mtimeMs;
}

async function run({ src, out, width, height, format = "webp", quality = 80, position = "attention" }) {
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

    // With a height given the resize becomes a crop. "attention" keeps the busiest region,
    // which usually means faces — but on a wide strip out of a group shot it can lock onto a
    // foreground jacket instead and cut every head off, so those jobs pass a fixed position.
    const pipeline = sharp(srcPath).resize({
        width,
        height,
        fit: height ? "cover" : undefined,
        position,
        withoutEnlargement: true
    });
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
