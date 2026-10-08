// In-browser mock of the Albion Save Editor API (see API_CONTRACT.md).
// Used with ?mock=1, or automatically when /api/meta does not answer.
// Mirrors the contract: same method names/shapes as realApi in ../api.js,
// 400 { error, field } validation errors, and a fake file download.

import { ApiError, validateNumbers, validateItemStacks, validatePairs, validateReadOnly } from '../api.js';

const clone = v => structuredClone(v);
const delay = (ms = 120) => new Promise(r => setTimeout(r, ms));

export async function createMockApi({ gameData = false } = {}) {
  const base = new URL('./', import.meta.url);
  const load = async p => {
    const res = await fetch(new URL(p, base));
    if (!res.ok) throw new Error(`Mock data missing: ${p}`);
    return res.json();
  };
  const meta = await load('meta.json');
  const sample = await load('save.json');
  const sampleChars = await Promise.all(sample.characters.map(c => load(`characters/${c.index}.json`)));
  // Game data (item & spell names) starts unconfigured, like a fresh backend; POST /api/gamedata enables it.
  const allItems = meta.items;
  let configured = !!gameData;

  const sessions = new Map();
  let counter = 0;

  const itemName = id => (configured ? (allItems.find(i => i.id === id) || { name: '' }).name : '');
  // what the server would return: names only when game data is configured
  const present = c => {
    const out = clone(c);
    for (const st of [...Object.values(out.equipment || {}), ...(out.backpack || [])]) st.itemName = itemName(st.itemId);
    if (!configured) for (const sp of out.spells || []) sp.name = '';
    return out;
  };
  const summary = s => ({
    sessionId: s.id,
    fileName: s.fileName,
    header: clone(s.header),
    characters: s.characters.map(c => ({ index: c.index, empty: !!c.empty, name: c.name, race: c.race, class: c.class, level: c.level })),
  });
  const session = id => {
    const s = sessions.get(id);
    if (!s) throw new ApiError('The save is no longer loaded (demo session expired). Please open the file again.', { status: 404 });
    return s;
  };
  const bad = (error, field) => { throw new ApiError(error, { status: 400, field }); };

  function checkEnum(enumName, value, field) {
    const e = meta.enums[enumName];
    if (!e) return;
    const names = e.values.map(v => v.name);
    const list = e.flags ? value : [value];
    if (e.flags && !Array.isArray(value)) bad(`"${field}" must be a list of names.`, field);
    // unnamed values come as decimal strings ("12") and are accepted, like the backend
    for (const n of list) if (!names.includes(n) && !/^\d+$/.test(String(n))) bad(`"${n}" is not a valid value for ${field}.`, field);
  }

  function validateItem(stack, field) {
    if (!stack || typeof stack !== 'object') bad('Item slot is missing.', field);
    checkEnum('itemFlags', stack.flags || [], `${field}.flags`);
  }

  function validateCharacter(c, old) {
    if (typeof c.name !== 'string' || (!c.empty && !c.name.trim())) bad('The name cannot be empty.', 'name');
    if (/[^\x20-\x7e\xa0-\xff]/.test(c.name)) bad('The name contains characters Albion cannot display.', 'name');
    const allowedOther = ['nameSlot2', 'nameSlot3', ...(meta.readOnly || [])];
    for (const k of Object.keys(c.other || {})) if (!allowedOther.includes(k)) bad(`Unknown field "${k}".`, `other.${k}`);
    for (const k of ['type', 'gender', 'race', 'class']) if (k in c) checkEnum(k, c[k], k);
    for (const k of ['magic', 'language', 'conditions']) if (k in c) checkEnum(k, c[k], k);
    if (!Array.isArray(c.backpack) || c.backpack.length !== 24) bad('The backpack must have exactly 24 slots.', 'backpack');
    c.backpack.forEach((s, i) => validateItem(s, `backpack[${i}]`));
    for (const [slot, s] of Object.entries(c.equipment || {})) validateItem(s, `equipment.${slot}`);
    const ro = validateReadOnly(c, old, meta.readOnly);
    if (ro) bad(ro.error, ro.field);
    const pr = validatePairs(c, old);
    if (pr) bad(pr.error, pr.field);
    const p = validateNumbers(c, meta.limits, '', old);
    if (p) bad(p.error, p.field);
    // per-item limits: only with game data, only for stacks that changed
    if (configured) {
      const q = validateItemStacks(c, old, allItems);
      if (q) bad(q.error, q.field);
    }
  }

  return {
    mode: 'mock',
    async getMeta() { await delay(50); return { ...clone(meta), items: configured ? clone(allItems) : [], gameDataConfigured: configured }; },

    async upload(file) {
      await delay(300);
      if (!file || file.size === 0) bad('This file is empty, so it cannot be an Albion save.');
      if (file.size > 4 * 1024 * 1024) bad('This file is far too large to be an Albion save.');
      if (/\.(png|jpe?g|gif|txt|pdf|zip|exe)$/i.test(file.name)) bad(`"${file.name}" does not look like an Albion save. Saves are named like SAVE.001.`);
      const id = `mock-${++counter}`;
      const s = { id, fileName: file.name || 'SAVE.001', header: clone(sample.header), characters: clone(sampleChars) };
      sessions.set(id, s);
      return summary(s);
    },

    async getSave(id) { await delay(); return summary(session(id)); },

    async putHeader(id, header) {
      await delay();
      const s = session(id);
      if (typeof header.name !== 'string') bad('The save name is missing.', 'name');
      const h = clone(header);
      h.version = s.header.version; // read-only
      const p = validateNumbers({ header: h }, meta.limits, 'header', { header: s.header });
      if (p) bad(p.error, p.field);
      s.header = h;
      return clone(h);
    },

    async getCharacter(id, index) {
      await delay();
      const c = session(id).characters.find(x => x.index === Number(index));
      if (!c) throw new ApiError(`There is no party member #${index}.`, { status: 404 });
      return present(c);
    },

    async putCharacter(id, index, character) {
      await delay(200);
      const s = session(id);
      const i = s.characters.findIndex(x => x.index === Number(index));
      if (i < 0) throw new ApiError(`There is no party member #${index}.`, { status: 404 });
      const c = clone(character);
      c.index = Number(index);
      validateCharacter(c, s.characters[i]);
      for (const st of [...Object.values(c.equipment || {}), ...(c.backpack || [])]) {
        if (!st.itemId) Object.assign(st, { count: 0, charges: 0, enchantment: 0, flags: [] });
        st.itemName = itemName(st.itemId);
      }
      // keep the full spell names internally, only strip them in responses
      (c.spells || []).forEach((sp, k) => { const o = s.characters[i].spells && s.characters[i].spells[k]; if (o) sp.name = o.name; });
      s.characters[i] = c;
      return present(c);
    },

    async download(id) {
      await delay(250);
      const s = session(id);
      const text = `ALBION SAVE EDITOR - DEMO FILE\r\nThis is not a real savegame. It was produced by the in-browser mock.\r\n\r\n${JSON.stringify({ header: s.header, characters: s.characters }, null, 2)}`;
      return { blob: new Blob([text], { type: 'application/octet-stream' }), fileName: s.fileName };
    },

    async getAbout() {
      await delay(50);
      // like an older backend without /api/about: the UI shows its built-in credits
      throw new ApiError('Not available in demo mode.', { status: 404 });
    },

    async setGameData(path) {
      await delay(400);
      const err = msg => { throw new ApiError(msg, { status: 400, field: 'path' }); };
      if (!path || !String(path).trim()) err('Please enter the folder where Albion is installed.');
      if (!/albion/i.test(path)) err(`No Albion game data found in "${path}". Choose the folder that contains XLDLIBS or game.gog.`);
      configured = true;
      const gog = /gog/i.test(path);
      return { ok: true, itemCount: allItems.length, spellCount: 20, source: gog ? 'game.gog (CD image)' : 'XLDLIBS' };
    },
  };
}
