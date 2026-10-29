#!/usr/bin/env node
/**
 * gen_from_ref.mjs —— 用「指定参考图」+「提示词文件」出一张网格 raw 图（走 codex-image-router）。
 *
 * 和 gen_sheet.mjs 的区别：gen_sheet 的参考图固定是角色定妆图；
 * 本脚本允许把任意一张图（比如外部生成的半成品、旧素材、别人的图）当参考图重绘，
 * 用来做「沿用某张图的构图/姿势，但提高分辨率 + 修正错误」。
 *
 * 用法：
 *   node gen_from_ref.mjs --ref <参考图> --prompt <提示词txt> --out <输出png> --size 3072x3072
 *   可选：--quality low|medium|high   --force 覆盖   --dry 只打印
 */
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));

function findRouter() {
  if (process.env.IMAGE_ROUTER_MJS && fs.existsSync(process.env.IMAGE_ROUTER_MJS)) return process.env.IMAGE_ROUTER_MJS;
  const cache = path.join(process.env.USERPROFILE || '', '.codex', 'plugins', 'cache', 'codex-image-router');
  if (fs.existsSync(cache)) {
    const hits = [];
    const walk = (d) => {
      for (const e of fs.readdirSync(d, { withFileTypes: true })) {
        const p = path.join(d, e.name);
        if (e.isDirectory()) walk(p);
        else if (e.name === 'image-mcp-server.mjs') hits.push(p);
      }
    };
    walk(cache);
    if (hits.length) return hits.sort().pop();
  }
  throw new Error('找不到 codex-image-router 的 image-mcp-server.mjs，请设置 IMAGE_ROUTER_MJS');
}

const a = { ref: null, prompt: null, out: null, size: '3072x3072', quality: 'high', force: false, dry: false };
for (let i = 2; i < process.argv.length; i++) {
  const k = process.argv[i];
  if (k === '--ref') a.ref = process.argv[++i];
  else if (k === '--prompt') a.prompt = process.argv[++i];
  else if (k === '--out') a.out = process.argv[++i];
  else if (k === '--size') a.size = process.argv[++i];
  else if (k === '--quality') a.quality = process.argv[++i];
  else if (k === '--force') a.force = true;
  else if (k === '--dry') a.dry = true;
}
if (!a.ref || !a.prompt || !a.out) {
  console.error('用法: node gen_from_ref.mjs --ref <参考图> --prompt <提示词txt> --out <输出png> [--size WxH] [--quality low|medium|high] [--force] [--dry]');
  process.exit(2);
}
for (const p of [a.ref, a.prompt]) if (!fs.existsSync(p)) { console.error('[ERR] 文件不存在: ' + p); process.exit(2); }

const refPath = path.resolve(a.ref);
const prompt = fs.readFileSync(a.prompt, 'utf8').trim();
const out = path.resolve(a.out);
fs.mkdirSync(path.dirname(out), { recursive: true });

if (a.dry) {
  console.log(`[dry] ref=${a.ref}\n[dry] out=${out}\n[dry] size=${a.size}\n---- prompt (${prompt.length} chars) ----\n${prompt}\n----------------`);
  process.exit(0);
}
if (fs.existsSync(out) && !a.force) { console.log('[skip] 已存在（--force 覆盖）: ' + out); process.exit(0); }

const mod = await import(pathToFileURL(findRouter()).href);
const t0 = Date.now();
let done = false;
for (let attempt = 1; attempt <= 3 && !done; attempt++) {
  try {
    process.stdout.write(`[gen ] attempt ${attempt} ${a.size} ... `);
    const r = await mod.runImageEdit({
      image_path: refPath, prompt, output_path: out, size: a.size,
      quality: a.quality, output_format: 'png', overwrite: true,
    });
    console.log(`ok ${r.actual_size || r.size || ''} ${Math.round((Date.now() - t0) / 1000)}s -> ${out}`);
    done = true;
  } catch (e) {
    const msg = e && e.message ? e.message : String(e);
    console.log('FAIL ' + msg.slice(0, 200));
    if (attempt === 3) process.exitCode = 1;
    else await new Promise((r2) => setTimeout(r2, 5000));
  }
}


