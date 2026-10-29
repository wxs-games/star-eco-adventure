#!/usr/bin/env node
/**
 * gen_sheet.mjs —— 用 Codex Image Router 插件批量生成「网格精灵表 raw 图」
 *
 * 为什么存在：unityMCP.generate_image 走的是 fal 槽位（当前是 nvapi- 密钥，全部 401），
 *              本项目出图统一走 codex-image-router 插件（gpt-image-2.5-sunburst + image_edit 保角色一致）。
 *
 * 用法：
 *   node gen_sheet.mjs Player                # 全部动作
 *   node gen_sheet.mjs Player --only Idle Run
 *   node gen_sheet.mjs Player --only Idle --size 1536x1024 --quality high --force
 *
 * 产物：Assets/Art/Art/<角色>/Sheets/raw/<角色>_<动作>_raw.png（PNG，白底，等大网格）
 */
import path from 'node:path';
import fs from 'node:fs';
import { fileURLToPath, pathToFileURL } from 'node:url';

const HERE = path.dirname(fileURLToPath(import.meta.url));
const PROJECT = path.resolve(HERE, '..', '..');            // 与 Assets 并列
const ART = path.join(PROJECT, 'Assets', 'Art', 'Art');

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

// 每帧都要逐条说清楚，模型才不会自己发挥
const STYLE = [
  'Same character as the reference image, identical design, identical colors, identical proportions in every single frame.',
  'Clean 2D game sprite, cartoon cel shading, bold dark outline, flat colors, no gradients on the background.',
].join(' ');

const RULES = (cols, rows, n) => [
  `Exactly ${cols} columns and ${rows} rows, ` + `${n} figures total, ordered left-to-right then top-to-bottom, one figure per cell, no cell empty.`,
  'Pure flat white background (#FFFFFF) everywhere: no shadow under the feet, no ground line, no floor, no frame border, no grid lines, no separators.',
  'No text, no numbers, no labels, no watermark, no extra props or objects.',
  'Every figure shows the FULL body from the top of the hat to the soles of both boots. Never crop the legs, never draw a close-up or a giant head, never zoom in on part of the body.',
  'Layout budget (strict): every figure must occupy at most 65% of the cell width and at most 85% of the cell height, and must leave at least 12% of the cell width completely empty on its left and on its right. Neighbouring figures must never come close to each other - leave a wide white gutter between columns.',
  'Every figure in the same pose scale: same head size, same body height, same distance from the top of the cell - the character must never grow or shrink between frames.',
  'Every frame must be a clearly different pose from the previous one, readable at 12 fps; do not duplicate the same pose twice.',
  'The camera never changes: the same three-quarter side view from the character\'s right in every single frame. Never switch to a back view, a front view, or a different angle.',
  'The animation must progress in one direction only: no frame may return to a pose that already happened earlier in the sequence.',
  'All figures face right (profile / three-quarter side view), full body visible including the top of the hat and the soles of both boots.',
].join(' ');

const JOBS = [
  { a: 'Idle',        n: 10, cols: 5, rows: 2,
    desc: 'A relaxed idle breathing loop with clearly readable motion: (1) neutral standing, (2) shoulders rise and chest lifts on inhale, (3) head tilts slightly up, (4) hands swing a little forward and away from the hips, (5) weight shifts onto one leg, torso leans a few degrees, (6) exhale, shoulders drop, (7) head turns a little to the right, (8) one hand comes up and touches the backpack strap, (9) hand returns, (10) back to neutral. Feet stay planted on the ground line, but the upper body must visibly move every frame.' },
  { a: 'Idle02',      n: 8,  cols: 4, rows: 2,
    desc: 'A second idle with clear key poses: (1) neutral, (2) head turns right and looks around, (3) shoulder roll up, (4) right hand rises to the chest strap, (5) tugs the strap while the head tilts, (6) hand drops to the hip, (7) small shrug and weight shift, (8) back to neutral. Feet planted, upper body clearly moving.' },
  { a: 'Run',         n: 8,  cols: 4, rows: 2,
    desc: 'A full 8-frame run cycle facing right, one continuous cycle in this exact order: ' +
      '(1) CONTACT: front foot slams flat on the ground under the body, the other leg is fully extended BEHIND with its foot off the ground; ' +
      '(2) DOWN: weight sinks onto the front leg, that knee bends, the back leg starts to swing forward; ' +
      '(3) PASSING: the swinging leg LIFTS and CROSSES IN FRONT of the standing leg - the two legs must visibly CROSS in an X shape, the swing foot passing the standing knee; ' +
      '(4) UP: push off the standing foot, body rises, the swing leg reaches forward ready to land; ' +
      '(5) CONTACT with the OTHER leg in front, the first leg now extended behind; ' +
      '(6) DOWN: weight sinks onto that leg; ' +
      '(7) PASSING: the other leg LIFTS and CROSSES IN FRONT of the standing leg in an X shape again; ' +
      '(8) UP: push off and loop back to frame 1. ' +
      'Legs: in the passing frames the legs MUST cross over each other - never drawn side by side, never both feet on the ground, always exactly one foot on the ground. ' +
      'Arms pump opposite to the legs, fists up at chest height. Leaning forward. ' +
      'CRITICAL: the top of the hat stays at exactly the same height in all 8 frames, the body never changes size, only limbs move.' },
  { a: 'Dash',        n: 6,  cols: 3, rows: 2, forceSize: '2048x1536',
    desc: 'A quick forward dash burst: crouch loading, explosive push-off, body stretched low and forward, two fast blurred-but-clean strides, then a skid recovery. Low center of gravity, facing right.' },
  { a: 'Roll',        n: 8,  cols: 4, rows: 2,
    desc: 'ONE continuous forward somersault (dodge roll) in 8 frames, rotating clockwise toward the right, ' +
      'every frame rotated further than the frame before it: ' +
      '(1) standing ready, arms slightly forward; ' +
      '(2) deep crouch, chin tucked to the chest, both hands reaching down to the ground in front; ' +
      '(3) push off, hips rising up over the head, body tucking into a tight ball, feet leaving the ground; ' +
      '(4) rolling over the shoulders, head fully tucked, knees pulled to the chest, body upside down; ' +
      '(5) on the upper back, legs swinging over the top, still a tight ball, body rotated about 180 degrees; ' +
      '(6) legs swing down and both feet come back down to the ground behind the body; ' +
      '(7) rising out of the crouch, one knee up, hands off the ground; ' +
      '(8) standing again, ready to run. ' +
      'This is ONE rotation: the character must never stand up straight in the middle, never repeat a pose, ' +
      'and must never face backward - the spin is a forward roll, the body center stays in the middle of the cell.' },
  // 注：Attack01/02/03/AttackAir/HeavyAttack 这几条在 2026-10-26 修改 Roll 提示词时被误删，
  //     此处按当时的出图意图重建（Attack01/02/AttackAir 为原文，03/Heavy 为按现成产物重写）。
  { a: 'Attack01',    n: 8,  cols: 4, rows: 2,
    desc: 'A three-hit light combo with bare fists: wind-up, straight punch, recoil, hook punch, recoil, spinning back fist, follow-through, return to guard. Facing right, feet stay near the ground.' },
  { a: 'Attack02',    n: 8,  cols: 4, rows: 2,
    desc: 'An 8-frame second combo, facing right, standing on the ground line in every frame: (1) guard stance, both fists up at chest height, (2) step in and throw a low kick with the front leg, (3) the same leg rises into a high kick at head height, (4) retract that leg, knee lifted, body balanced and upright, (5) pivot and cock the back arm, (6) spinning elbow strike, (7) follow through, torso rotated, (8) recover to guard. The camera stays on the same side the whole time - never show the back of the character, never zoom in.' },
  { a: 'Attack03',    n: 9,  cols: 3, rows: 3, size: '1024x1536',
    desc: 'A 9-frame heavy third combo, facing right, feet on the ground: (1) wind-up crouch, back fist chambered at the hip, (2) drive forward, (3) lunging straight punch at full extension, (4) recoil the arm, (5) drop low and start a rising uppercut, (6) uppercut at its apex, chin lifting the target, (7) torso spins for a back fist, (8) back fist lands across the body, (9) recover to guard. The camera never changes side.' },
  { a: 'AttackAir',   n: 6,  cols: 3, rows: 2, forceSize: '2048x1536',
    desc: 'An air attack: airborne tuck, downward kick, spinning air slash with the fist, landing impact, absorb, recover. The figure floats in the cell (no ground line drawn), facing right.' },
  { a: 'HeavyAttack', n: 12, cols: 4, rows: 3, forceSize: '2560x2048',
    desc: 'A 12-frame heavy smash, facing right: (1) plant both feet, (2) raise both fists overhead, (3) full wind-up, body arched back, (4) step in, (5) start the overhead double-fist smash, (6) the smash lands, body crouched low, both fists at the ground, (7) hold the impact, (8) pull the arms back, (9) start to rise, (10) torso turns, (11) follow-through, (12) recover to guard. The character never leaves the ground and never turns its back.' },
  { a: 'Die',         n: 8,  cols: 4, rows: 2, forceSize: '3840x1920',
    desc: 'A death fall facing right, strictly one-way progression: (1) standing hit reaction, head snapped back, (2) knees buckle, body folding, (3) dropping to knees, (4) falling forward, arms reaching down, (5) hands hit the ground, body low, (6) chest hits the ground, face down, (7) settling flat, one leg still bent, (8) completely still, lying flat face down. Once the body goes down it must never rise back up to kneeling or standing.' },
  { a: 'Fall',        n: 4,  cols: 2, rows: 2, size: '1024x1536',
    desc: 'A falling loop in mid air: arms up, legs trailing, slight body rotation, cloth and backpack straps fluttering upward. No ground, facing right.' },
  { a: 'Jump',        n: 3,  cols: 3, rows: 1,
    desc: 'Jump takeoff: crouch compress, explosive extension, rising with legs tucked. Facing right.' },
  { a: 'Land',        n: 3,  cols: 3, rows: 1,
    desc: 'Landing: feet touch with knees bent deep, absorb with body low, recover to standing. Facing right.' },
  { a: 'Hurt',        n: 3,  cols: 3, rows: 1,
    desc: 'Damage reaction: sharp flinch backward, head snapped back, then recovering guard. Facing right, feet still planted.' },
  { a: 'Interact',    n: 6,  cols: 3, rows: 2,
    desc: 'Interaction: reach out with the right hand, press or pull something, look at it, small nod, withdraw the hand, back to neutral. Facing right.' },
  { a: 'LedgeGrab',   n: 3,  cols: 3, rows: 1,
    desc: 'Hanging on a ledge: both hands raised gripping an invisible edge above, body hanging, legs dangling, slight sway. Facing right.' },
  { a: 'Turn',        n: 1,  cols: 1, rows: 1, size: '1024x1536',
    desc: 'Single frame: mid-turn pivot pose, torso rotated, one foot pivoting, ready to face right.' },
  { a: 'Victory',     n: 8,  cols: 4, rows: 2,
    desc: 'Victory celebration loop: fist pump up, both arms raised, small hop, landing, confident pose with hands on hips, then back to the raised fist. Facing right.' },
  { a: 'WallJump',    n: 3,  cols: 3, rows: 1, forceSize: '2048x1024',
    desc: 'Wall jump: foot planted on an invisible wall behind, body coiled, then pushing off and flying to the right. Facing right.' },
  { a: 'WallSlide',   n: 3,  cols: 3, rows: 1,
    desc: 'Wall slide loop: back against an invisible wall, one hand and one boot touching it, body sliding down, cloth fluttering. Facing right.' },
  { a: 'Crouch',      n: 2,  cols: 2, rows: 1,
    desc: 'Crouch: standing neutral, then a low alert crouch with knees bent, body compressed. Facing right.' },
];

const CHARS = {
  Player: { lockup: 'Player/Lockup/Player_Lockup_A.png', out: 'Player/Sheets/raw', prefix: 'Player' },
};

function parseArgs(argv) {
  const o = { char: argv[0], only: null, size: null, quality: 'high', force: false, dry: false, layout: 'single', cols: null };
  for (let i = 1; i < argv.length; i++) {
    const k = argv[i];
    if (k === '--only') { o.only = []; while (argv[i + 1] && !argv[i + 1].startsWith('--')) o.only.push(argv[++i]); }
    else if (k === '--size') o.size = argv[++i];
    else if (k === '--quality') o.quality = argv[++i];
    else if (k === '--force') o.force = true;
    else if (k === '--dry') o.dry = true;
    else if (k === '--layout') o.layout = argv[++i];   // single = 一图一动作 / multi = 一图多动作
    else if (k === '--cols') o.cols = parseInt(argv[++i], 10);   // multi 模式：每行放几帧（默认=整行一个动作）
  }
  return o;
}

const opt = parseArgs(process.argv.slice(2));
if (!opt.char || !CHARS[opt.char]) {
  console.error('用法: node gen_sheet.mjs <角色> [--only 动作...] [--layout single|multi] [--size WxH] [--quality low|medium|high] [--force] [--dry]');
  console.error('已登记角色: ' + Object.keys(CHARS).join(', '));
  process.exit(2);
}
const cfg = CHARS[opt.char];
const lockup = path.join(ART, cfg.lockup);
if (!fs.existsSync(lockup)) throw new Error('定妆图不存在: ' + lockup);

const mod = await import(pathToFileURL(findRouter()).href);
const t0 = Date.now();

// 两种模式共用的一套「多动作合图」补充规则：一张图里塞多个动作时用
const RULES_MULTI = (cols, rows, count, names, rowsPer) => [
  `Exactly ${cols} columns and ${rows} rows, split into ${count} horizontal BANDS of ${rowsPer} row(s) each. ` +
  `Band 1 = ${names[0]}${count > 1 ? `, band 2 = ${names[1]}` : ''}${count > 2 ? ', ...' : ''}; ` +
  `within a band the frames run left to right, then the next row of the same band.`,
  'Do not blend actions: no frame of one action may show a pose belonging to another action.',
  'All bands use the SAME character, the SAME scale, the SAME head size and the SAME distance from the top of the cell.',
  RULES(cols, rows, cols * rows),
].join(' ');

// 画布自动排布：每格基准 384x768（与既有 Player 素材同密度），超出 3840 上限就压缩并告警
// 每格目标 768x768（实测 384 宽会把跑步的交叉腿切掉），单边上限 3840。
// 先按 768 要，超上限就压窄，但只在必要时压 —— 保证「每行几帧」能真正换来更宽的格子。
const CELL_W = 768, CELL_H = 768, MAX_SIDE = 3840;
function planCanvas(cols, rows) {
  const cw = Math.max(192, Math.min(CELL_W, Math.floor(MAX_SIDE / cols / 16) * 16));
  const ch = Math.max(192, Math.min(CELL_H, Math.floor(MAX_SIDE / rows / 16) * 16));
  const w = cw * cols, h = ch * rows;
  const squeezed = (cw < CELL_W || ch < CELL_H);
  return { size: `${w}x${h}`, cellW: cw, cellH: ch, squeezed };
}

const jobs = JOBS.filter((j) => !opt.only || opt.only.includes(j.a));

if (opt.layout === 'multi') {
  // ---- 一图多动作：选中的动作合成一张，每个动作占一行 ----
  if (jobs.length < 2) {
    console.error('[ERR] --layout multi 至少要选 2 个动作（--only A B）');
    process.exit(2);
  }
  const counts = [...new Set(jobs.map((j) => j.n))];
  if (counts.length !== 1) {
    console.error(`[ERR] 多动作合图要求所选动作帧数一致，当前: ` + jobs.map((j) => `${j.a}=${j.n}`).join(' '));
    console.error('      帧数不同的动作请用 --layout single（一图一动作）。');
    process.exit(2);
  }
  const n = counts[0];
  // --cols：每行放几帧。默认 = 一个动作占一整行（cols = n）。帧数多时会折成多行，
  // 这样每个动作占 cols x (n/cols) 的「行带」，格子不会被压得太窄（跑/翻滚这类大动作尤其需要）。
  const cols = opt.cols ? Math.max(1, Math.min(opt.cols, n)) : n;
  const rowsPer = Math.ceil(n / cols);
  const rows = rowsPer * jobs.length;
  const plan = planCanvas(cols, rows);
  const size = opt.size || plan.size;
  const out = path.join(ART, cfg.out, `${cfg.prefix}_${jobs.map((j) => j.a).join('+')}_raw.png`);
  const bandLabel = (k) => {
    const r0 = k * rowsPer + 1, r1 = (k + 1) * rowsPer;
    return rowsPer === 1 ? `Row ${r0}` : `Rows ${r0}-${r1}`;
  };
  const prompt = [
    `Sprite sheet of one single character: ${cfg.prefix}. THIS SHEET CONTAINS ${jobs.length} DIFFERENT ACTIONS.`,
    `Grid: ${cols} columns x ${rows} rows, ${cols * rows} figures total. Row index counts from the top.`,
    ...jobs.map((j, k) => `${bandLabel(k)} (${j.n} frames, ${cols} per row, left to right then the next row) = action "${j.a}": ${j.desc}`),
    STYLE,
    RULES_MULTI(cols, rows, jobs.length, jobs.map((j) => j.a), rowsPer),
  ].join('\n');
  if (plan.squeezed) {
    console.log(`[warn] ${cols}x${rows} 格按每格 ${CELL_W}x${CELL_H} 需要 ${cols * CELL_W}x${rows * CELL_H}，` +
      `超过 ${MAX_SIDE} 上限已压缩：每格降到 ${plan.cellW}x${plan.cellH}`);
  }
  if (opt.dry) {
    console.log(`[dry] multi ${size} ${cols}x${rows} -> ${out}`);
    console.log('---- prompt ----\n' + prompt + '\n----------------');
  } else {
    if (fs.existsSync(out) && !opt.force) {
      console.log(`[skip] ${path.basename(out)} 已存在（--force 覆盖）`);
    } else {
      process.stdout.write(`[gen ] multi ${size} ${cols}x${rows} ... `);
      let done = false;
      for (let attempt = 1; attempt <= 3 && !done; attempt++) {
        try {
          const r = await mod.runImageEdit({
            image_path: lockup, prompt, output_path: out, size,
            quality: opt.quality, output_format: 'png', overwrite: true,
          });
          console.log(`ok ${r.actual_size || r.size || ''} ${Math.round((r.elapsed_ms || 0) / 1000)}s` + (attempt > 1 ? ` (第${attempt}次)` : ''));
          done = true;
        } catch (e) {
          const msg = e && e.message ? e.message : String(e);
          if (attempt === 3) { console.log('FAIL ' + msg); process.exitCode = 1; }
          else { process.stdout.write(`retry(${msg.slice(0, 40)}) `); await new Promise((r2) => setTimeout(r2, 5000)); }
        }
      }
    }
  }
} else {
  // ---- 一图一动作（默认，现状）----
  for (const j of jobs) {
    const out = path.join(ART, cfg.out, `${cfg.prefix}_${j.a}_raw.png`);
    const size = opt.size || j.forceSize || (j.rows === 1 ? '1536x1024' : '1536x1536');
    if (fs.existsSync(out) && !opt.force && !opt.dry) {
      console.log(`[skip] ${j.a} 已存在（--force 覆盖）`);
      continue;
    }
    const prompt = [
      `Sprite sheet of one single character: ${cfg.prefix}.`,
      `Grid: ${j.cols} columns x ${j.rows} rows, ${j.n} frames.`,
      `Animation (${j.n} frames): ${j.desc}`,
      STYLE,
      RULES(j.cols, j.rows, j.n),
    ].join('\n');
    if (opt.dry) {
      console.log(`[dry] ${j.a} ${size} ${j.cols}x${j.rows} -> ${out}`);
      console.log('---- prompt ----\n' + prompt + '\n----------------');
      continue;
    }
    process.stdout.write(`[gen ] ${j.a} ${size} ${j.cols}x${j.rows} ... `);
    let done = false;
    for (let attempt = 1; attempt <= 3 && !done; attempt++) {
      try {
        const r = await mod.runImageEdit({
          image_path: lockup,
          prompt,
          output_path: out,
          size,
          quality: opt.quality,
          output_format: 'png',
          overwrite: true,
        });
        console.log(`ok ${r.actual_size || r.size || ''} ${Math.round((r.elapsed_ms || 0) / 1000)}s` + (attempt > 1 ? ` (第${attempt}次)` : ''));
        done = true;
      } catch (e) {
        const msg = e && e.message ? e.message : String(e);
        if (attempt === 3) { console.log('FAIL ' + msg); process.exitCode = 1; }
        else { process.stdout.write(`retry(${msg.slice(0, 40)}) `); await new Promise((r2) => setTimeout(r2, 5000)); }
      }
    }
  }
}
console.log(`\n完成，用时 ${Math.round((Date.now() - t0) / 1000)}s`);
