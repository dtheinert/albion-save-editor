// API layer for the Albion Save Editor.
// Everything that talks to the server lives here. The rest of the UI only sees `api.*` methods.
// If the real backend is not reachable (or ?mock=1 is set), an in-browser mock with the
// same contract is used instead (see mock/mock-api.js).

export class ApiError extends Error {
  constructor(message, { status = 0, field = null } = {}) {
    super(message);
    this.name = 'ApiError';
    this.status = status;
    this.field = field;
  }
}

/**
 * Finds the limits entry for a value path like "attributes.strength.current" or "backpack.3.count".
 * Lookup order: the full path (array indexes removed), then ever-shorter suffixes
 * ("strength.current", "current"), then the containers ("attributes.strength", "attributes").
 * `accept` decides which entries count (numeric {min,max} by default).
 */
export function limitFor(limits, path, accept = l => l.min != null || l.max != null) {
  if (!limits || !path) return null;
  const segs = String(path).split('.').filter(s => s !== '' && !/^\d+$/.test(s));
  const cands = [segs.join('.')];
  // path keys like "attributes.max" / "age.max" (container + leaf) beat plain leaf keys
  if (segs.length > 2) cands.push(`${segs[0]}.${segs[segs.length - 1]}`);
  for (let i = 1; i < segs.length; i++) cands.push(segs.slice(i).join('.'));
  for (let i = segs.length - 1; i > 0; i--) cands.push(segs.slice(0, i).join('.'));
  for (const c of cands) {
    const l = limits[c];
    if (l && typeof l === 'object' && accept(l)) return l;
  }
  return null;
}
/** {maxLength} for text fields (e.g. name, other.nameGerman, header.name). */
export const textLimitFor = (limits, path) => limitFor(limits, path, l => l.maxLength != null);

/** True for fields that hold a fractional number (gold is stored in tenths). */
export function isDecimalField(path, value, limit) {
  const leaf = String(path).split('.').pop();
  if (leaf === 'gold') return true;
  if (typeof value === 'number' && !Number.isInteger(value)) return true;
  if (limit && limit.step != null && !Number.isInteger(Number(limit.step))) return true;
  if (limit && ((limit.max != null && !Number.isInteger(limit.max)) || (limit.min != null && !Number.isInteger(limit.min)))) return true;
  return false;
}

/** Fields that are shown read-only and never validated as user input. */
export const READ_ONLY_PATHS = /(^|\.)(index|version|sessionId|itemName|school|id)$|^spells\.\d+\.name$/;

/**
 * Walks every number in `obj` and checks it against `limits`.
 * Returns { field, error } for the first problem, or null if everything is fine.
 * `field` uses the contract's style: dotted names with [n] for array indexes.
 * With `baseline` (the last server state, same shape) unchanged values are skipped: the backend
 * accepts existing out-of-range values as long as they are not edited.
 */
export function validateNumbers(obj, limits, prefix = '', baseline = undefined) {
  const strip = p => (prefix && p.startsWith(`${prefix}.`) ? p.slice(prefix.length + 1) : p);
  const toField = p => strip(p).replace(/\.(\d+)(?=\.|$)/g, '[$1]');
  const hasBase = baseline !== undefined;
  let problem = null;
  (function walk(v, path, base) {
    if (problem) return;
    const sub = k => (base != null && typeof base === 'object' ? base[k] : undefined);
    if (Array.isArray(v)) { v.forEach((x, i) => walk(x, path ? `${path}.${i}` : String(i), sub(i))); return; }
    if (v && typeof v === 'object') { for (const k of Object.keys(v)) walk(v[k], path ? `${path}.${k}` : k, sub(k)); return; }
    if (READ_ONLY_PATHS.test(path)) return;
    if (hasBase && v === base) return;
    if (v === null || (typeof v === 'number' && Number.isNaN(v))) {
      problem = { field: toField(path), error: 'Please enter a number here.' };
      return;
    }
    if (typeof v === 'string') {
      const tl = textLimitFor(limits, path);
      if (tl && v.length > tl.maxLength) problem = { field: toField(path), error: `At most ${tl.maxLength} characters.` };
      return;
    }
    if (typeof v !== 'number') return;
    const lim = limitFor(limits, path);
    if (!isDecimalField(path, v, lim) && !Number.isInteger(v)) {
      problem = { field: toField(path), error: 'Whole numbers only.' };
    } else if (isDecimalField(path, v, lim) && Math.round(v * 10) / 10 !== v) {
      problem = { field: toField(path), error: 'At most one decimal place.' };
    } else if (lim && lim.min != null && v < lim.min) {
      problem = { field: toField(path), error: `Must be at least ${lim.min}.` };
    } else if (lim && lim.max != null && v > lim.max) {
      problem = { field: toField(path), error: `Must be at most ${lim.max}.` };
    }
  })(obj, prefix, baseline);
  return problem;
}

// ---------------------------------------------------------------------------
// Character rules (API_CONTRACT.md "Character field limits & corrections")
// ---------------------------------------------------------------------------

const isPairObj = v => v && typeof v === 'object' && !Array.isArray(v) && typeof v.current === 'number' && typeof v.max === 'number';
const toBracket = p => p.replace(/\.(\d+)(?=\.|$)/g, '[$1]');

/**
 * current <= max for every {current, max} pair, checked when current or max changed
 * (pairs already broken in the save stay accepted while untouched). Returns { field, error } or null.
 */
export function validatePairs(draft, server) {
  let problem = null;
  (function walk(v, base, path) {
    if (problem || !v || typeof v !== 'object') return;
    if (isPairObj(v)) {
      const changed = !base || v.current !== base.current || v.max !== base.max;
      if (changed && v.current > v.max) problem = { field: toBracket(`${path}.current`), error: `Can't be higher than the maximum (${v.max}).` };
      return;
    }
    for (const k of Object.keys(v)) walk(v[k], base && typeof base === 'object' ? base[k] : undefined, path ? `${path}.${k}` : k);
  })(draft, server, '');
  return problem;
}

/** Leaf keys listed in meta.readOnly must not change. Returns { field, error } or null. */
export function validateReadOnly(draft, server, readOnly) {
  if (!Array.isArray(readOnly) || !readOnly.length) return null;
  let problem = null;
  (function walk(v, base, path) {
    if (problem || !v || typeof v !== 'object') return;
    for (const k of Object.keys(v)) {
      const p = path ? `${path}.${k}` : k;
      const b = base && typeof base === 'object' ? base[k] : undefined;
      if (readOnly.includes(k) && (typeof v[k] !== 'object' || v[k] === null)) {
        if (v[k] !== b) { problem = { field: toBracket(p), error: `'${k}' is read-only` }; return; }
      } else walk(v[k], b, p);
    }
  })(draft, server, '');
  return problem;
}

// ---------------------------------------------------------------------------
// Per-item limits (API_CONTRACT.md "Item limits")
// ---------------------------------------------------------------------------

/** Item definition from meta.items, or null (unknown id or no game data). */
export const itemDef = (items, id) => (id && Array.isArray(items) ? items.find(i => i.id === id) || null : null);

/** Fields of a stack that exceed its item's limits: [{ key, value, max }]. */
export function stackOverLimit(st, def) {
  if (!st || !def || !st.itemId) return [];
  const out = [];
  const maxCount = def.maxCount ?? (def.stackable ? 99 : 1);
  if (typeof st.count === 'number' && maxCount < 255 && st.count > maxCount) out.push({ key: 'count', value: st.count, max: maxCount });
  if (typeof st.charges === 'number' && def.maxCharges != null && st.charges > def.maxCharges) out.push({ key: 'charges', value: st.charges, max: def.maxCharges });
  if (typeof st.enchantment === 'number' && def.maxEnchantments != null && st.enchantment > def.maxEnchantments) out.push({ key: 'enchantment', value: st.enchantment, max: def.maxEnchantments });
  return out;
}

const stackContent = st => (st && typeof st === 'object' ? { ...st, itemName: undefined } : st);

/** Lists every item stack in a character: [{ path: 'backpack.3', field: 'backpack[3]', stack }]. */
export function stacksOf(ch) {
  const out = [];
  for (const [k, st] of Object.entries((ch && ch.equipment) || {})) out.push({ path: `equipment.${k}`, field: `equipment.${k}`, stack: st });
  ((ch && ch.backpack) || []).forEach((st, i) => out.push({ path: `backpack.${i}`, field: `backpack[${i}]`, stack: st }));
  return out;
}

/**
 * Validates stacks whose content changed vs `server` (untouched stacks are never rejected).
 * Only runs when item data is available. Returns { field, error } or null.
 */
export function validateItemStacks(draft, server, items) {
  if (!Array.isArray(items) || !items.length) return null;
  for (const { path, field, stack: st } of stacksOf(draft)) {
    const old = path.split('.').reduce((o, k) => (o == null ? o : o[k]), server) || {};
    if (!st || JSON.stringify(stackContent(st)) === JSON.stringify(stackContent(old))) continue;
    if (!st.itemId) continue; // clearing a slot: the server zeroes the rest
    const def = itemDef(items, st.itemId);
    if (!def) return { field: `${field}.itemId`, error: `There is no item with number ${st.itemId}.` };
    // like the backend: check a field only if it or the item itself changed
    const itemChanged = st.itemId !== old.itemId;
    const touched = k => itemChanged || st[k] !== old[k];
    const maxCount = def.maxCount ?? (def.stackable ? 99 : 1);
    const countKnown = maxCount < 255; // 255 = only names loaded, limit unknown
    if (touched('count') && !(st.count >= 1)) return { field: `${field}.count`, error: 'Count must be at least 1.' };
    if (touched('count') && countKnown && st.count > maxCount) {
      return { field: `${field}.count`, error: maxCount === 1 ? `${def.name} doesn't stack: count must be 1.` : `${def.name} stacks up to ${maxCount}.` };
    }
    if (touched('charges') && def.maxCharges != null && st.charges > def.maxCharges) {
      return { field: `${field}.charges`, error: def.maxCharges ? `${def.name} holds at most ${def.maxCharges} charges.` : `${def.name} has no charges.` };
    }
    if (touched('enchantment') && def.maxEnchantments != null && st.enchantment > def.maxEnchantments) {
      return { field: `${field}.enchantment`, error: def.maxEnchantments ? `${def.name} can be enchanted at most ${def.maxEnchantments} times.` : `${def.name} can't be enchanted.` };
    }
  }
  return null;
}

// ---------------------------------------------------------------------------
// Real backend
// ---------------------------------------------------------------------------

async function request(method, url, { json, form, timeoutMs = 30000, raw = false } = {}) {
  const ctrl = new AbortController();
  const timer = setTimeout(() => ctrl.abort(), timeoutMs);
  let res;
  try {
    const init = { method, signal: ctrl.signal, headers: {} };
    if (json !== undefined) { init.body = JSON.stringify(json); init.headers['Content-Type'] = 'application/json'; }
    if (form) init.body = form;
    res = await fetch(url, init);
  } catch (e) {
    throw new ApiError(e.name === 'AbortError'
      ? 'The editor server took too long to answer.'
      : 'Cannot reach the editor server. Is it still running?', { status: 0 });
  } finally {
    clearTimeout(timer);
  }
  if (!res.ok) {
    let body = null;
    try { body = await res.json(); } catch { /* not JSON */ }
    let msg = body && (body.error || body.title || body.detail);
    if (!msg) {
      if (res.status === 404) msg = 'The save is no longer loaded on the server (maybe it was restarted). Please open the file again.';
      else if (res.status === 413) msg = 'That file is too large to be an Albion save.';
      else msg = `The server answered with an error (${res.status}).`;
    }
    throw new ApiError(msg, { status: res.status, field: body && body.field ? body.field : null });
  }
  if (raw) return res;
  if (res.status === 204) return null;
  const text = await res.text();
  return text ? JSON.parse(text) : null;
}

function fileNameFromDisposition(header, fallback) {
  if (!header) return fallback;
  const star = /filename\*\s*=\s*(?:UTF-8'')?([^;]+)/i.exec(header);
  if (star) { try { return decodeURIComponent(star[1].trim().replace(/^"|"$/g, '')); } catch { /* ignore */ } }
  const plain = /filename\s*=\s*"?([^";]+)"?/i.exec(header);
  return plain ? plain[1].trim() : fallback;
}

const enc = encodeURIComponent;

export const realApi = {
  mode: 'real',
  getMeta: () => request('GET', '/api/meta', { timeoutMs: 5000 }),
  upload(file) {
    const form = new FormData();
    form.append('file', file, file.name);
    return request('POST', '/api/saves', { form });
  },
  getSave: id => request('GET', `/api/saves/${enc(id)}`),
  putHeader: (id, header) => request('PUT', `/api/saves/${enc(id)}/header`, { json: header }),
  getCharacter: (id, index) => request('GET', `/api/saves/${enc(id)}/characters/${index}`),
  putCharacter: (id, index, character) => request('PUT', `/api/saves/${enc(id)}/characters/${index}`, { json: character }),
  async download(id, fallbackName) {
    const res = await request('GET', `/api/saves/${enc(id)}/download`, { raw: true, timeoutMs: 60000 });
    const blob = await res.blob();
    return { blob, fileName: fileNameFromDisposition(res.headers.get('Content-Disposition'), fallbackName || 'SAVE.001') };
  },
  setGameData: path => request('POST', '/api/gamedata', { json: { path } }),
  getAbout: () => request('GET', '/api/about', { timeoutMs: 5000 }),
};

/**
 * Picks the API implementation and loads metadata.
 * Returns { api, meta, mode: 'real' | 'mock', reason }.
 */
export async function connect({ onWait } = {}) {
  const params = new URLSearchParams(location.search);
  const useMock = async reason => {
    const { createMockApi } = await import('./mock/mock-api.js');
    const api = await createMockApi({ gameData: params.get('gamedata') === '1' });
    return { api, meta: await api.getMeta(), mode: 'mock', reason };
  };
  if (params.get('mock') === '1') return useMock('requested');

  // The desktop app's server may still be starting: retry for ~25 s before giving up.
  // Only "no server at all" (network errors the whole time) or "no API here" (404, e.g. a plain
  // static file server during development) fall back to the demo. Other server errors are reported.
  const delays = [0, 300, 700, 1000, 1500, 2000, 2500, 3000, 3000, 4000, 4000, 4000];
  let lastError = null;
  for (let i = 0; i < delays.length; i++) {
    if (delays[i]) {
      if (onWait) onWait(i, delays.length);
      await new Promise(r => setTimeout(r, delays[i]));
    }
    try {
      const meta = await request('GET', '/api/meta', { timeoutMs: 8000 });
      if (!meta || typeof meta !== 'object' || !meta.enums) throw new ApiError('The editor server sent unexpected data.', { status: 502 });
      return { api: realApi, meta, mode: 'real', reason: null };
    } catch (e) {
      lastError = e;
      if (e.status === 404) return useMock('noapi'); // reachable, but no editor API at this address
    }
  }
  if (lastError && lastError.status === 0) return useMock('unreachable');
  throw lastError || new ApiError('The editor server could not be reached.');
}
