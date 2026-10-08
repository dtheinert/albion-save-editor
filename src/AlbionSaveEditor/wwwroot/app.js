// Albion Save Editor - UI.
// Plain ES module, no build step. All server access goes through ./api.js.

import { connect, limitFor, textLimitFor, isDecimalField, validateNumbers, READ_ONLY_PATHS,
  itemDef, stackOverLimit, validateItemStacks, validatePairs, validateReadOnly } from './api.js';

// ===========================================================================
// Small helpers
// ===========================================================================

const $ = (sel, root = document) => root.querySelector(sel);
const $$ = (sel, root = document) => [...root.querySelectorAll(sel)];
const clone = v => (v === undefined ? undefined : structuredClone(v));

const PROPS = new Set(['value', 'checked', 'disabled', 'hidden', 'selected', 'indeterminate']);
function h(tag, props, ...kids) {
  const el = document.createElement(tag);
  if (props) {
    for (const [k, v] of Object.entries(props)) {
      if (v == null || v === false) continue;
      if (k === 'class') el.className = v;
      else if (k === 'text') el.textContent = v;
      else if (k.startsWith('on') && typeof v === 'function') el.addEventListener(k.slice(2), v);
      else if (PROPS.has(k)) el[k] = v;
      else el.setAttribute(k, v === true ? '' : String(v));
    }
  }
  appendKids(el, kids);
  return el;
}
function appendKids(el, kids) {
  for (const k of kids.flat(Infinity)) {
    if (k == null || k === false) continue;
    el.append(k instanceof Node ? k : document.createTextNode(String(k)));
  }
}
function svg(pathD, cls = 'icon') {
  const s = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  s.setAttribute('viewBox', '0 0 24 24');
  s.setAttribute('aria-hidden', 'true');
  s.setAttribute('class', cls);
  const p = document.createElementNS('http://www.w3.org/2000/svg', 'path');
  p.setAttribute('d', pathD);
  s.append(p);
  return s;
}
const ICON = {
  upload: 'M12 16V4m0 0L7 9m5-5 5 5M4 20h16',
  heart: 'M12 20s-7-4.4-7-10a4 4 0 0 1 7-2.6A4 4 0 0 1 19 10c0 5.6-7 10-7 10z',
  sparkle: 'M12 3v4m0 10v4M3 12h4m10 0h4M6 6l2.5 2.5M15.5 15.5 18 18M6 18l2.5-2.5M15.5 8.5 18 6',
  undo: 'M9 14 4 9l5-5M4 9h10a6 6 0 0 1 0 12h-3',
  check: 'M5 12l5 5L20 7',
  x: 'M6 6l12 12M18 6 6 18',
};

function deepEqual(a, b) {
  if (a === b) return true;
  if (typeof a !== typeof b || a === null || b === null || typeof a !== 'object') {
    return typeof a === 'number' && typeof b === 'number' && Number.isNaN(a) && Number.isNaN(b);
  }
  if (Array.isArray(a) !== Array.isArray(b)) return false;
  const ka = Object.keys(a), kb = Object.keys(b);
  if (ka.length !== kb.length) return false;
  return ka.every(k => Object.prototype.hasOwnProperty.call(b, k) && deepEqual(a[k], b[k]));
}
const segs = path => (path === '' ? [] : String(path).split('.'));
function getAt(obj, path) {
  let o = obj;
  for (const s of segs(path)) { if (o == null) return undefined; o = o[s]; }
  return o;
}
function setAt(obj, path, value) {
  const p = segs(path);
  let o = obj;
  for (let i = 0; i < p.length - 1; i++) o = o[p[i]];
  o[p[p.length - 1]] = value;
}
/** Lists every path (containers and leaves) inside obj. */
function allPaths(obj, prefix = '', out = []) {
  if (obj && typeof obj === 'object') {
    for (const k of Object.keys(obj)) {
      const p = prefix ? `${prefix}.${k}` : k;
      out.push(p);
      allPaths(obj[k], p, out);
    }
  }
  return out;
}
const leafKey = path => segs(path).filter(s => !/^\d+$/.test(s)).pop() || '';
const fieldId = path => `f-${String(path).replace(/[^a-zA-Z0-9]+/g, '-')}`;
const isPair = v => v && typeof v === 'object' && !Array.isArray(v) && 'current' in v && 'max' in v;
const isStack = v => v && typeof v === 'object' && !Array.isArray(v) && 'itemId' in v;

function storage(op, key, value) {
  try {
    if (op === 'get') return sessionStorage.getItem(key);
    if (op === 'set') sessionStorage.setItem(key, value);
    if (op === 'del') sessionStorage.removeItem(key);
  } catch { /* storage unavailable */ }
  return null;
}

// ===========================================================================
// Labels, help texts
// ===========================================================================

const LABELS = {
  name: 'Name', type: 'Character type', gender: 'Gender', race: 'Race', class: 'Class',
  level: 'Level', actionPoints: 'Action points', experience: 'Experience', trainingPoints: 'Training points',
  gold: 'Gold', rations: 'Rations', appearance: 'Map sprite', face: 'Portrait', inventoryPicture: 'Inventory picture',
  lifePoints: 'Life points (LP)', spellPoints: 'Spell points (SP)',
  strength: 'Strength', intelligence: 'Intelligence', dexterity: 'Dexterity', speed: 'Speed', stamina: 'Stamina',
  luck: 'Luck', magicResistance: 'Magic resistance', magicTalent: 'Magic talent',
  closeCombat: 'Close combat', rangedCombat: 'Ranged combat', criticalChance: 'Critical hit chance', lockpicking: 'Lockpicking',
  conditions: 'Conditions', language: 'Languages', magic: 'Magic schools',
  version: 'Game version', days: 'Days', hours: 'Hour', minutes: 'Minute', mapId: 'Map number', partyX: 'Party X', partyY: 'Party Y',
  itemId: 'Item number', count: 'Count', charges: 'Charges', enchantment: 'Enchantment', flags: 'Item flags',
  nameSlot2: 'Alternate name slot 2', nameSlot3: 'Alternate name slot 3',
  age: 'Age', morale: 'Morale',
  eventSetId: 'Event set', wordSetId: 'Word set',
  baseDefense: 'Base defence', bonusDefense: 'Defence bonus', baseAttack: 'Base attack', bonusAttack: 'Attack bonus',
  levelsPerActionPoint: 'Levels per action point', lifePointsPerLevel: 'Life points per level',
  spellPointsPerLevel: 'Spell points per level', trainingPointsPerLevel: 'Training points per level',
};
const HELP = {
  name: 'The in-game name, shown in the party bar and in conversations.',
  morale: 'Mood of the character, 0 to 100.',
  age: 'Current age / natural limit (up to 100).',
  eventSetId: 'Which script events belong to this character.',
  wordSetId: 'Which conversation words this character knows.',
  bonusDefense: 'Added by worn equipment.', bonusAttack: 'Added by worn equipment.',
  nameSlot2: 'Unused by the game in most saves.',
  nameSlot3: 'Unused by the game in most saves.',
  type: 'Leave this as it is for party members.',
  level: 'Raising the level alone does not add LP or skills.',
  experience: 'Total experience points collected.',
  trainingPoints: 'Spent at trainers to improve skills.',
  actionPoints: 'How far the character can act in a combat round.',
  gold: 'Albion stores gold in tenths, so one decimal place is allowed.',
  rations: 'Food. Resting uses one ration per party member.',
  appearance: 'Number of the sprite drawn on the map.',
  face: 'Number of the portrait in the party bar.',
  inventoryPicture: 'Number of the full-body picture in the inventory screen.',
  lifePoints: 'Health. At 0 the character falls unconscious.',
  spellPoints: 'Energy used for casting spells.',
  strength: 'Carrying capacity and close-combat damage.',
  intelligence: 'Helps learning and magic.',
  dexterity: 'Helps hitting in combat.',
  speed: 'Order and movement in combat.',
  stamina: 'Endurance and resistance to exhaustion.',
  luck: 'Helps in many small ways.',
  magicResistance: 'Chance to shrug off hostile magic.',
  magicTalent: 'Talent for casting magic.',
  closeCombat: 'Hit chance with melee weapons.',
  rangedCombat: 'Hit chance with bows, slings and guns.',
  criticalChance: 'Chance of a critical hit.',
  lockpicking: 'Chance to open locks and disarm traps.',
  days: 'Days passed since the game started.',
  hours: '0 to 23.', minutes: '0 to 59.',
  mapId: 'Number of the map the party stands on. Wrong values can make the save unplayable.',
  partyX: 'Column of the party on the map.', partyY: 'Row of the party on the map.',
  itemId: 'Number of the item type. 0 means the slot is empty.',
  count: 'How many items are stacked in this slot.',
  charges: 'Remaining uses (wands, guns, magic items).',
  enchantment: 'How often the item was recharged or enchanted.',
};
const ENUM_LABELS = {
  Man: 'Male', Woman: 'Female', DjiKas: 'Dji-Kas', DjiKasMage: 'Dji-Kas mage', DjiCantos: 'Dji-Cantos',
  KengetKamulos: 'Kenget Kamulos', OquloKamulos: 'Oqulo Kamulos', EnlightenedOne: 'Enlightened One',
  IskaiWarrior: 'Iskai warrior', HumanWarrior: 'Warrior', ShowMoreInfo: 'Show more info', NPC: 'NPC', Druidic: 'Druidic',
};
const CONDITION_HELP = {
  Unconscious: 'Knocked out; cannot act.', Poisoned: 'Loses LP over time.', Ill: 'Weakened by disease.',
  Exhausted: 'Needs rest; attributes reduced.', Paralyzed: 'Cannot move or act.', Fleeing: 'Runs from combat.',
  Intoxicated: 'Drunk; clumsy.', Blind: 'Cannot see; badly hampered in combat.', Panicking: 'Acts erratically.',
  Asleep: 'Sleeping; cannot act.', Insane: 'Out of control.', Irritated: 'Distracted.',
};
const EQUIP_SLOTS = ['neck', 'head', 'tail', 'rightHand', 'chest', 'leftHand', 'rightFinger', 'feet', 'leftFinger'];
const EQUIP_LABELS = {
  neck: 'Neck', head: 'Head', tail: 'Tail', rightHand: 'Right hand', chest: 'Body',
  leftHand: 'Left hand', rightFinger: 'Right finger', feet: 'Feet', leftFinger: 'Left finger',
};
const KNOWN_CHAR_KEYS = new Set(['index', 'empty', 'age', 'morale', 'name', 'type', 'gender', 'race', 'class', 'magic', 'language', 'level',
  'actionPoints', 'appearance', 'face', 'inventoryPicture', 'trainingPoints', 'gold', 'rations', 'conditions',
  'experience', 'lifePoints', 'spellPoints', 'attributes', 'skills', 'spells', 'equipment', 'backpack', 'other']);
const KNOWN_STACK_KEYS = new Set(['itemId', 'itemName', 'count', 'charges', 'enchantment', 'flags']);

function splitCamel(s) {
  return String(s).replace(/([a-z0-9])([A-Z])/g, '$1 $2').replace(/([A-Z])([A-Z][a-z])/g, '$1 $2');
}
function labelFor(key) {
  if (LABELS[key]) return LABELS[key];
  if (/^\d+$/.test(key)) return `#${Number(key) + 1}`;
  const words = splitCamel(key).replace(/[_-]+/g, ' ').trim();
  return words.charAt(0).toUpperCase() + words.slice(1).toLowerCase();
}
const enumLabel = name => (/^-?\d+$/.test(String(name)) ? `Unknown (${name})` : ENUM_LABELS[name] || splitCamel(name));

// ===========================================================================
// State
// ===========================================================================

const S = {
  api: null, meta: null, mode: 'real', reason: null,
  session: null,                 // { sessionId, fileName, characters: [summary] }
  header: null,                  // { server, draft }
  chars: new Map(),              // index -> { server, draft }
  selected: 'info',              // 'info' | character index (number)
  tab: 'basics',
  errors: new Map(),             // scope -> Map(path -> message)
  bannerErrors: new Map(),       // scope -> message (errors not tied to a visible field)
  slot: null,                    // { path, label } while the item editor is open
  busy: false,
  appliedNotDownloaded: false,
};

const scopeOf = sel => (sel === 'info' ? 'header' : `c${sel}`);
const currentScope = () => scopeOf(S.selected);
const docFor = scope => (scope === 'header' ? S.header : S.chars.get(Number(scope.slice(1))));
const isDirty = scope => { const d = docFor(scope); return !!d && !deepEqual(d.draft, d.server); };
const dirtyScopes = () => ['header', ...[...S.chars.keys()].map(i => `c${i}`)].filter(isDirty);
const anyDirty = () => dirtyScopes().length > 0;
const errorsFor = scope => { if (!S.errors.has(scope)) S.errors.set(scope, new Map()); return S.errors.get(scope); };

function enumFor(path) {
  const key = leafKey(path);
  const enums = S.meta.enums || {};
  if (key === 'flags') return enums.itemFlags || enums.flags || null;
  return enums[key] || null;
}
const zeroName = en => (en && en.flags ? (en.values.find(v => v.value === 0) || {}).name : undefined);

/** Removes "None"-style zero flags so that [] means "nothing set". */
function normalizeDoc(obj) {
  (function walk(v, path) {
    if (Array.isArray(v)) {
      const en = enumFor(path);
      if (en && en.flags && v.every(x => typeof x === 'string')) {
        const z = zeroName(en);
        const order = en.values.map(x => x.name);
        const kept = v.filter(x => x !== z);
        kept.sort((a, b) => (order.indexOf(a) + 1 || 999) - (order.indexOf(b) + 1 || 999));
        v.splice(0, v.length, ...kept);
        return;
      }
      v.forEach((x, i) => walk(x, `${path}.${i}`));
    } else if (v && typeof v === 'object') {
      for (const k of Object.keys(v)) walk(v[k], path ? `${path}.${k}` : k);
    }
  })(obj, '');
  return obj;
}
/** Puts the zero flag ("None") back into empty flag lists before sending. */
function denormalizeDoc(obj) {
  (function walk(v, path) {
    if (Array.isArray(v)) {
      const en = enumFor(path);
      if (en && en.flags && v.length === 0) { const z = zeroName(en); if (z) v.push(z); return; }
      v.forEach((x, i) => walk(x, `${path}.${i}`));
    } else if (v && typeof v === 'object') {
      for (const k of Object.keys(v)) walk(v[k], path ? `${path}.${k}` : k);
    }
  })(obj, '');
  return obj;
}
const makeDoc = data => { const n = normalizeDoc(clone(data)); return { server: n, draft: clone(n) }; };

const isEmptyChar = c => !!(c && c.empty);
const summaryOf = index => (S.session && S.session.characters.find(c => c.index === index)) || {};
/** Sidebar/title label: real name, else "Unused slot #i" for empty records, else "Character #i". */
function charLabel(c, index) {
  if (c && c.name) return c.name;
  return isEmptyChar(c) ? `Unused slot #${index}` : `Character #${index}`;
}
const avatarLetter = c => (c && c.name ? c.name.charAt(0).toUpperCase() : isEmptyChar(c) ? '–' : '?');

function itemNameById(id) {
  const it = (S.meta.items || []).find(i => i.id === id);
  return it ? it.name : '';
}
function stackLabel(st) {
  if (!st || !st.itemId || !st.count) return '';
  return st.itemName || itemNameById(st.itemId) || `Item #${st.itemId}`;
}

// ===========================================================================
// Startup
// ===========================================================================

const app = $('#app');

let globalEventsWired = false;
async function init() {
  if (!globalEventsWired) { wireGlobalEvents(); globalEventsWired = true; }
  app.replaceChildren(h('p', { class: 'loading', id: 'startup-status', role: 'status' }, 'Loading\u2026'));
  let conn;
  try {
    conn = await connect({
      onWait: () => { const st = $('#startup-status'); if (st) st.textContent = 'Waiting for the editor to start\u2026'; },
    });
  } catch (e) {
    app.replaceChildren(h('section', { class: 'landing' },
      h('h1', null, 'The editor could not start'),
      h('p', { class: 'form-error', role: 'alert' }, e.message || String(e)),
      h('p', { class: 'center' }, h('button', { type: 'button', class: 'btn primary', onclick: () => init() }, 'Try again'))));
    return;
  }
  S.api = conn.api;
  S.mode = conn.mode;
  S.reason = conn.reason;
  S.meta = normalizeMeta(conn.meta);
  showModeBadge();

  const saved = S.mode === 'real' ? storage('get', 'albionSaveEditor.sessionId') : null;
  if (saved) {
    try { openSession(await S.api.getSave(saved)); return; } catch { storage('del', 'albionSaveEditor.sessionId'); }
  }
  renderLanding();
}

function normalizeMeta(m) {
  m = m || {};
  const items = Array.isArray(m.items) ? m.items : [];
  return { enums: m.enums || {}, limits: m.limits || {}, items, gameDataConfigured: m.gameDataConfigured ?? items.length > 0,
    readOnly: Array.isArray(m.readOnly) ? m.readOnly : [], rules: Array.isArray(m.rules) ? m.rules : [] };
}

function showModeBadge() {
  const b = $('#mode-badge');
  if (S.mode !== 'mock') { b.hidden = true; return; }
  b.hidden = false;
  b.textContent = 'Demo mode';
  b.title = S.reason === 'unreachable'
    ? 'The editor server did not answer, so sample data is used. Nothing you do here changes a real save.'
    : S.reason === 'noapi'
      ? 'No editor server at this address, so sample data is used. Nothing you do here changes a real save.'
      : 'Running with sample data (?mock=1). Nothing you do here changes a real save.';
}

// ===========================================================================
// Landing screen
// ===========================================================================

function renderLanding(error) {
  S.session = null; S.header = null; S.chars.clear(); S.errors.clear(); S.bannerErrors.clear();
  S.appliedNotDownloaded = false; S.selected = 'info'; S.tab = 'basics';
  $('#top-actions').hidden = true;
  $('#backup-hint').hidden = true;
  document.title = 'Albion Save Editor';

  const input = h('input', {
    type: 'file', id: 'file-input', class: 'visually-hidden', tabindex: '-1', 'aria-hidden': 'true',
    onchange: e => { const f = e.target.files[0]; if (f) loadFile(f); e.target.value = ''; },
  });
  const status = h('p', { class: 'drop-status', role: 'status', 'aria-live': 'polite' });
  const err = h('p', { class: 'form-error', role: 'alert', hidden: !error }, error || '');
  const chooseBtn = h('button', { type: 'button', class: 'btn primary big', onclick: () => input.click() },
    svg(ICON.upload), 'Choose save file…');

  const zone = h('div', { class: 'dropzone', id: 'dropzone' },
    h('div', { class: 'drop-art', 'aria-hidden': 'true' }, svg('M6 3h9l4 4v14H6zM14 3v5h5M9 13h7M9 17h5', 'icon big')),
    h('p', { class: 'drop-title' }, 'Drop your save file here'),
    h('p', { class: 'drop-or' }, 'or'),
    chooseBtn, input, status);

  zone.addEventListener('dragenter', e => { e.preventDefault(); zone.classList.add('over'); });
  zone.addEventListener('dragover', e => { e.preventDefault(); e.dataTransfer.dropEffect = 'copy'; zone.classList.add('over'); });
  zone.addEventListener('dragleave', e => { if (!zone.contains(e.relatedTarget)) zone.classList.remove('over'); });
  zone.addEventListener('drop', e => {
    e.preventDefault(); zone.classList.remove('over');
    const f = e.dataTransfer.files && e.dataTransfer.files[0];
    if (f) loadFile(f);
  });

  const sample = S.mode === 'mock'
    ? h('p', { class: 'sample-row' },
      h('button', { type: 'button', class: 'btn ghost', onclick: () => loadFile(new File([new Uint8Array(8192)], 'SAVE.001')) },
        'Try it with the sample party'),
      h('span', { class: 'muted' }, ' (Tom, Rainer and Drirr)'))
    : null;

  app.replaceChildren(h('section', { class: 'landing', 'aria-labelledby': 'landing-title' },
    h('h1', { id: 'landing-title' }, 'Edit your Albion savegame'),
    h('p', { class: 'lead' }, 'Open a save, change your party\u2019s stats, items and more, then download the modified file.'),
    zone, err,
    h('p', { class: 'hint center' }, 'Albion keeps its saves in the ', h('code', null, 'ALBION\\SAVES\\'),
      ' folder. They are named ', h('code', null, 'SAVE.001'), ', ', h('code', null, 'SAVE.002'), ' and so on.'),
    h('p', { class: 'hint center' }, 'Tip: copy the original file somewhere safe before you start.'),
    h('p', { class: 'hint center' }, h('button', { type: 'button', class: 'linklike', onclick: showAbout }, 'About this editor')),
    sample));
}

async function loadFile(file) {
  const zone = $('#dropzone');
  const status = zone && $('.drop-status', zone);
  const err = $('.landing .form-error');
  if (err) { err.hidden = true; err.textContent = ''; }
  if (zone) zone.classList.add('busy');
  if (status) status.textContent = `Reading ${file.name}…`;
  $$('.landing button').forEach(b => { b.disabled = true; });
  try {
    const res = await S.api.upload(file);
    openSession(res);
  } catch (e) {
    renderLanding(`Could not open \u201c${file.name}\u201d: ${e.message}`);
  }
}

// ===========================================================================
// Editor shell
// ===========================================================================

function openSession(res) {
  S.session = { sessionId: res.sessionId, fileName: res.fileName || 'SAVE.001', characters: res.characters || [] };
  S.header = makeDoc(res.header || {});
  S.chars.clear(); S.errors.clear(); S.bannerErrors.clear();
  S.appliedNotDownloaded = false;
  S.tab = 'basics';
  if (S.mode === 'real') storage('set', 'albionSaveEditor.sessionId', res.sessionId);

  $('#top-actions').hidden = false;
  $('#backup-hint').hidden = false;
  $('#file-label').replaceChildren(h('span', { class: 'muted' }, 'Editing '), h('strong', null, S.session.fileName));
  document.title = `${S.session.fileName} \u2013 Albion Save Editor`;

  app.replaceChildren(h('div', { class: 'editor' },
    h('aside', { class: 'sidebar', 'aria-label': 'Save contents' }, h('nav', { id: 'sidebar-nav' })),
    h('section', { class: 'panel', id: 'panel', 'aria-labelledby': 'panel-title' })));
  const first = S.session.characters.find(c => !isEmptyChar(c)) || S.session.characters[0];
  select(first ? first.index : 'info');
}

function renderSidebar() {
  const nav = $('#sidebar-nav');
  if (!nav) return;
  const item = (sel, main, sub, extra) => {
    const scope = scopeOf(sel);
    const active = S.selected === sel;
    return h('button', {
      type: 'button', class: `nav-item${active ? ' active' : ''}`, 'data-sel': String(sel),
      'aria-current': active ? 'page' : null, onclick: () => select(sel),
    },
    extra,
    h('span', { class: 'nav-text' }, h('span', { class: 'nav-main' }, main), sub && h('span', { class: 'nav-sub' }, sub)),
    h('span', { class: 'dirty-dot', hidden: !isDirty(scope), title: 'Unapplied changes' },
      h('span', { class: 'visually-hidden' }, 'Unapplied changes')));
  };
  const h0 = S.header.draft;
  const infoSub = [h0.days != null ? `Day ${h0.days}` : null, h0.mapId != null ? `Map ${h0.mapId}` : null].filter(Boolean).join(' \u00b7 ');
  const entry = sum => {
    const d = S.chars.get(sum.index);
    const c = d ? { ...d.draft, empty: d.draft.empty ?? sum.empty } : sum;
    const empty = isEmptyChar(c);
    const sub = empty ? 'Not in use' : [c.race && enumLabel(c.race), c.class && enumLabel(c.class)].filter(Boolean).join(' \u00b7 ');
    return h('li', null, item(sum.index, charLabel(c, sum.index), sub,
      h('span', { class: `avatar${empty ? ' unused' : ''}`, 'aria-hidden': 'true' }, avatarLetter(c),
        !empty && c.level != null && h('span', { class: 'lvl' }, c.level))));
  };
  const used = S.session.characters.filter(c => !isEmptyChar(c));
  const unused = S.session.characters.filter(c => isEmptyChar(c));
  const party = used.map(entry);
  let unusedBlock = null;
  if (unused.length) {
    const open = S.unusedOpen || unused.some(c => c.index === S.selected);
    unusedBlock = h('details', { class: 'unused-slots', open },
      h('summary', null, `Unused slots (${unused.length})`),
      h('ul', { class: 'nav-list', 'aria-label': 'Unused party slots' }, unused.map(entry)));
    unusedBlock.addEventListener('toggle', () => { S.unusedOpen = unusedBlock.open; });
  }
  nav.replaceChildren(
    item('info', 'Save info', infoSub, h('span', { class: 'avatar info', 'aria-hidden': 'true' }, svg('M12 8h.01M11 12h1v5h1M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18z'))),
    h('h2', { class: 'nav-heading', id: 'party-heading' }, 'Party'),
    party.length ? h('ul', { class: 'nav-list', 'aria-labelledby': 'party-heading' }, party)
      : h('p', { class: 'muted small' }, 'No party members found in this save.'),
    unusedBlock,
    h('p', { class: 'sidebar-note' },
      'Edits stay here until you press ', h('strong', null, 'Apply'), '. Then use ',
      h('strong', null, 'Download modified save'), ' and copy the file into ', h('code', null, 'ALBION\\SAVES\\'), '.'),
    h('p', { class: 'sidebar-footer' }, h('button', { type: 'button', class: 'linklike', onclick: showAbout }, 'About this editor')));
}

async function select(sel) {
  if (S.slot) $('#slot-dialog').close();
  S.selected = sel;
  renderSidebar();
  const panel = $('#panel');
  if (sel !== 'info' && !S.chars.has(sel)) {
    panel.replaceChildren(h('p', { class: 'loading' }, 'Loading character…'));
    try {
      const c = await S.api.getCharacter(S.session.sessionId, sel);
      S.chars.set(sel, makeDoc(c));
    } catch (e) {
      if (e.status === 404) { sessionLost(); return; }
      if (S.selected !== sel) return;
      panel.replaceChildren(h('div', { class: 'panel-error', role: 'alert' }, `Could not load this character: ${e.message}`),
        h('button', { type: 'button', class: 'btn', onclick: () => select(sel) }, 'Try again'));
      return;
    }
    if (S.selected !== sel) return;
    renderSidebar();
  }
  renderPanel();
}

// ===========================================================================
// Panel (title, action bar, tabs)
// ===========================================================================

const CHAR_TABS = [
  { id: 'basics', label: 'Basics' }, { id: 'stats', label: 'Stats' }, { id: 'skills', label: 'Skills' },
  { id: 'conditions', label: 'Conditions' }, { id: 'langmagic', label: 'Languages & magic' },
  { id: 'spells', label: 'Spells' }, { id: 'inventory', label: 'Inventory' }, { id: 'other', label: 'Other' },
];
const TAB_OF_KEY = {
  name: 'basics', gender: 'basics', race: 'basics', class: 'basics', level: 'basics', actionPoints: 'basics',
  appearance: 'basics', face: 'basics', inventoryPicture: 'basics', trainingPoints: 'basics', gold: 'basics',
  rations: 'basics', experience: 'basics', morale: 'basics', age: 'stats',
  lifePoints: 'stats', spellPoints: 'stats', attributes: 'stats', skills: 'skills', conditions: 'conditions',
  magic: 'langmagic', language: 'langmagic', spells: 'spells', equipment: 'inventory', backpack: 'inventory',
};
const tabForPath = path => TAB_OF_KEY[segs(path)[0]] || 'other';

function renderPanel() {
  const panel = $('#panel');
  const scope = currentScope();
  const isInfo = S.selected === 'info';

  const head = h('div', { class: 'panel-head' },
    h('div', { class: 'panel-title-wrap' },
      h('h1', { id: 'panel-title' }), h('p', { class: 'panel-sub', id: 'panel-sub' })),
    h('div', { class: 'actionbar' },
      h('span', { class: 'dirty-status', id: 'dirty-status', 'aria-live': 'polite' }),
      h('button', { type: 'button', class: 'btn ghost', id: 'btn-revert', onclick: () => revert(scope),
        title: 'Throw away edits on this page and go back to the last applied values' }, svg(ICON.undo), 'Revert'),
      h('button', { type: 'button', class: 'btn primary', id: 'btn-apply', onclick: () => apply(scope),
        title: 'Store these edits in the loaded save (Ctrl+S)' }, svg(ICON.check), 'Apply changes')));
  const banner = h('div', { class: 'panel-error', id: 'panel-error', role: 'alert', hidden: true });

  if (isInfo) {
    panel.replaceChildren(head, banner, h('div', { class: 'panel-body', id: 'panel-body' }));
  } else {
    const tablist = h('div', { class: 'tabs', role: 'tablist', 'aria-label': 'Character sections', onkeydown: onTabKey },
      CHAR_TABS.map(t => h('button', {
        type: 'button', role: 'tab', id: `tab-${t.id}`, class: 'tab', 'data-tab': t.id,
        'aria-selected': String(S.tab === t.id), 'aria-controls': 'panel-body', tabindex: S.tab === t.id ? '0' : '-1',
        onclick: () => switchTab(t.id),
      }, t.label, h('span', { class: 'tab-dot', hidden: true, 'aria-hidden': 'true' }))));
    panel.replaceChildren(head, banner, tablist,
      h('div', { class: 'panel-body', id: 'panel-body', role: 'tabpanel', 'aria-labelledby': `tab-${S.tab}`, tabindex: '-1' }));
  }
  renderBody();
}

function onTabKey(e) {
  const tabs = $$('[role=tab]', e.currentTarget);
  const i = tabs.indexOf(document.activeElement);
  if (i < 0) return;
  let n = null;
  if (e.key === 'ArrowRight') n = (i + 1) % tabs.length;
  else if (e.key === 'ArrowLeft') n = (i - 1 + tabs.length) % tabs.length;
  else if (e.key === 'Home') n = 0;
  else if (e.key === 'End') n = tabs.length - 1;
  if (n == null) return;
  e.preventDefault();
  switchTab(tabs[n].dataset.tab);
  $(`#tab-${tabs[n].dataset.tab}`).focus();
}

function switchTab(id) {
  if (S.slot) $('#slot-dialog').close();
  S.tab = id;
  $$('[role=tab]').forEach(t => {
    const on = t.dataset.tab === id;
    t.setAttribute('aria-selected', String(on));
    t.tabIndex = on ? 0 : -1;
  });
  const body = $('#panel-body');
  if (body) body.setAttribute('aria-labelledby', `tab-${id}`);
  renderBody();
}

function renderBody() {
  const body = $('#panel-body');
  if (!body) return;
  const doc = docFor(currentScope());
  let content;
  if (S.selected === 'info') content = viewHeader(doc.draft);
  else {
    const c = doc.draft;
    content = ({
      basics: viewBasics, stats: viewStats, skills: viewSkills, conditions: viewConditions,
      langmagic: viewLangMagic, spells: viewSpells, inventory: viewInventory, other: viewOther,
    }[S.tab] || viewBasics)(c);
  }
  body.replaceChildren(...[content].flat(Infinity).filter(Boolean));
  refresh();
}

// ===========================================================================
// Controls
// ===========================================================================

function numberInput(path, value, { ariaLabel, wide, id } = {}) {
  const lim = limitFor(S.meta.limits, path);
  const dec = isDecimalField(path, value, lim);
  return h('input', {
    type: 'number', id: id || fieldId(path), class: `num${wide ? ' wide' : ''}`,
    'data-path': path, 'data-kind': dec ? 'decimal' : 'int',
    step: lim && lim.step != null ? String(lim.step) : dec ? '0.1' : '1', min: lim ? lim.min : null, max: lim ? lim.max : null,
    inputmode: dec ? 'decimal' : 'numeric', value: value == null ? '' : String(value), 'aria-label': ariaLabel,
  });
}

function enumSelect(path, value, en, id) {
  const opts = en.values.map(v => h('option', { value: v.name, selected: v.name === value }, enumLabel(v.name)));
  if (value != null && !en.values.some(v => v.name === value)) opts.push(h('option', { value, selected: true }, /^-?\d+$/.test(value) ? `Unknown (${value})` : `${value} (unknown)`));
  return h('select', { id: id || fieldId(path), 'data-path': path, 'data-kind': 'enum' }, opts);
}

/** Label + control + hint for one scalar value; chooses the control from the value and metadata. */
function field(path, value, opts = {}) {
  const key = leafKey(path);
  const id = fieldId(path);
  const label = opts.label || labelFor(key);
  const help = opts.help !== undefined ? opts.help : HELP[key];
  const en = enumFor(path);
  let control;
  if (opts.readOnly || READ_ONLY_PATHS.test(path) || (S.meta.readOnly || []).includes(key)) {
    control = h('output', { id, class: 'readonly' }, value == null || value === '' ? '\u2014' : String(value));
  } else if (en && !en.flags && typeof value === 'string') {
    control = enumSelect(path, value, en, id);
  } else if (typeof value === 'number' || value === null) {
    control = numberInput(path, value, { wide: opts.wide });
  } else if (typeof value === 'boolean') {
    return h('div', { class: 'field', 'data-field': path },
      h('label', { class: 'check' }, h('input', { type: 'checkbox', id, 'data-path': path, 'data-kind': 'bool', checked: value }), label),
      help && h('p', { class: 'hint' }, help));
  } else {
    const tl = textLimitFor(S.meta.limits, currentScope() === 'header' ? `header.${path}` : path);
    control = h('input', { type: 'text', id, 'data-path': path, 'data-kind': 'text', value: value ?? '', autocomplete: 'off', spellcheck: 'false', maxlength: tl ? tl.maxLength : opts.maxLength });
  }
  if (help) control.setAttribute('aria-describedby', `${id}-hint`);
  return h('div', { class: `field${opts.wide ? ' wide' : ''}`, 'data-field': path },
    h('label', { for: id }, label), control, help && h('p', { class: 'hint', id: `${id}-hint` }, help));
}

function flagGroup(path, values, en, { legend, help, chipHelp } = {}) {
  const list = Array.isArray(values) ? values : [];
  const known = en.values.filter(v => v.value !== 0);
  const unknown = list.filter(n => !en.values.some(v => v.name === n));
  const chip = (name, unknownName) => h('label', { class: 'chip', title: (chipHelp && chipHelp[name]) || null },
    h('input', { type: 'checkbox', 'data-path': path, 'data-kind': 'flag', 'data-flag': name, checked: list.includes(name) }),
    h('span', null, unknownName ? `${name} (unknown)` : enumLabel(name)));
  return h('fieldset', { class: 'flag-group', 'data-group-path': path, 'data-field': path },
    h('legend', null, legend || labelFor(leafKey(path))),
    help && h('p', { class: 'hint' }, help),
    h('div', { class: 'chips' }, known.map(v => chip(v.name)), unknown.map(n => chip(n, true))));
}

const MAX_HINT_ID = 'hint-pair-max';
const MAX_HINT = 'Maximum: the highest this character can ever train to (game data never exceeds 99).';

function pairRow(path, pair, label, help, { maxLabel = 'maximum' } = {}) {
  const id = fieldId(`${path}.current`);
  const cur = numberInput(`${path}.current`, pair.current, { ariaLabel: `${label}: current`, id });
  cur.dataset.pairCurrent = path; // its max follows the pair's max (refresh keeps it in sync)
  const max = numberInput(`${path}.max`, pair.max, { ariaLabel: `${label}: ${maxLabel}` });
  if ($('#' + MAX_HINT_ID) || /^(attributes|skills)\./.test(path)) max.setAttribute('aria-describedby', MAX_HINT_ID);
  max.title = /^(attributes|skills)\./.test(path) ? MAX_HINT : '';
  return h('div', { class: 'pair-row', 'data-field': path },
    h('label', { for: id, class: 'pair-label', title: help || null }, label,
      help && h('span', { class: 'pair-help' }, help)),
    h('div', { class: 'pair-inputs' },
      cur,
      h('span', { class: 'slash', 'aria-hidden': 'true' }, '/'),
      max),
    h('div', { class: 'bar', 'data-bar': path, 'aria-hidden': 'true' }, h('span', { class: 'fill' })));
}

function card(title, ...kids) {
  return h('section', { class: 'card' }, title && h('h2', { class: 'card-title' }, title), kids);
}
const grid = (...kids) => h('div', { class: 'grid' }, kids);

/** Generic renderer used for unknown fields and the "Other" tab. */
function generic(path, value) {
  const key = leafKey(path);
  if (isPair(value)) return h('div', { class: 'pairs' }, pairRow(path, value, labelFor(key), HELP[key]));
  if (Array.isArray(value)) {
    const en = enumFor(path);
    if (en && en.flags && value.every(x => typeof x === 'string')) return flagGroup(path, value, en);
    return h('fieldset', { class: 'nested', 'data-field': path }, h('legend', null, labelFor(key)),
      value.length ? grid(value.map((v, i) => generic(`${path}.${i}`, v))) : h('p', { class: 'muted small' }, 'Empty list'));
  }
  if (value && typeof value === 'object') {
    return h('fieldset', { class: 'nested', 'data-field': path }, h('legend', null, labelFor(key)),
      grid(Object.keys(value).map(k => generic(`${path}.${k}`, value[k]))));
  }
  return field(path, value);
}

// ===========================================================================
// Views
// ===========================================================================

function viewHeader(hd) {
  const known = ['name', 'version', 'days', 'hours', 'minutes', 'mapId', 'partyX', 'partyY'];
  const pick = keys => keys.filter(k => k in hd).map(k => field(k, hd[k], k === 'name' ? { wide: true, help: 'The name shown in the game\u2019s load menu.' } : {}));
  const extra = Object.keys(hd).filter(k => !known.includes(k));

  const configured = S.meta.gameDataConfigured;
  const gameDataCard = configured
    ? card('Albion folder',
      h('p', { class: 'hint' }, 'Item and spell names are loaded. You can point to a different Albion folder if needed. Game files are only read, never changed.'),
      gameDataForm('info', `${S.meta.items.length} item names are available.`))
    : h('section', { class: 'card callout' },
      h('h2', { class: 'card-title' }, 'Show item and spell names'),
      h('p', null, 'Right now items are shown by number only and spells have no names. Tell the editor where Albion is installed to fix that. ',
        'A GOG installation works too; the game files are only read, never changed.'),
      gameDataForm('info', 'Not set up yet.'));

  return [
    configured ? null : gameDataCard,
    card('Save', grid(pick(['name', 'version']))),
    card('In-game time', grid(pick(['days', 'hours', 'minutes']))),
    card('Party position', grid(pick(['mapId', 'partyX', 'partyY'])),
      h('p', { class: 'hint warn' }, 'Only change the position if you know the map. A party stuck inside a wall cannot move.')),
    extra.length ? card('More', grid(extra.map(k => generic(k, hd[k])))) : null,
    card('Download',
      h('p', null, 'When you are done, download the modified save and copy it into ', h('code', null, 'ALBION\\SAVES\\'),
        '. ', h('strong', null, 'Keep a backup of the original file'), ' in case something goes wrong.'),
      h('button', { type: 'button', class: 'btn primary', onclick: download }, 'Download modified save')),
    configured ? gameDataCard : null,
  ];
}

const DEFAULT_ALBION_PATH = 'C:\\Program Files (x86)\\GOG Galaxy\\Games\\Albion';

/** Path input + "Load item names" button; POSTs /api/gamedata. `key` keeps ids unique per screen. */
function gameDataForm(key, statusText) {
  const id = `gamedata-path-${key}`;
  const input = h('input', { type: 'text', id, value: S.gameDataPath || DEFAULT_ALBION_PATH, autocomplete: 'off', spellcheck: 'false',
    'aria-describedby': `${id}-msg`, onkeydown: e => { if (e.key === 'Enter') { e.preventDefault(); btn.click(); } } });
  const msg = h('p', { class: 'hint', id: `${id}-msg`, 'aria-live': 'polite' }, statusText || '');
  const btn = h('button', { type: 'button', class: 'btn primary', onclick: () => setGameData(input, msg, btn) }, 'Load item names');
  return h('div', { class: 'gamedata-form' },
    h('div', { class: 'inline-form' }, h('label', { for: id }, 'Albion folder'), input, btn), msg);
}

function viewBasics(c) {
  const sec = (title, keys, opts = {}) => {
    const present = keys.filter(k => k in c);
    return present.length ? card(title, grid(present.map(k => field(k, c[k], (opts[k] || {}))))) : null;
  };
  return [
    sec('Identity', ['name', 'gender', 'race', 'class'], { name: { wide: true } }),
    sec('Progress', ['level', 'experience', 'trainingPoints', 'actionPoints', 'morale'], { experience: { wide: true } }),
    sec('Supplies', ['gold', 'rations']),
    sec('Portrait & sprites', ['face', 'inventoryPicture', 'appearance']),
  ];
}

function pairList(c, paths) {
  return h('div', { class: 'pairs' }, paths.filter(p => isPair(getAt(c, p))).map(p => {
    const key = leafKey(p);
    return pairRow(p, getAt(c, p), labelFor(key), HELP[key]);
  }));
}
function maxButton(paths, label) {
  return h('button', { type: 'button', class: 'btn', onclick: () => {
    const d = docFor(currentScope()).draft;
    for (const p of paths) { const v = getAt(d, p); if (isPair(v)) v.current = v.max; }
    refresh({ sync: true });
    toast('Current values set to their maximum. Press Apply to keep them.');
  } }, svg(ICON.sparkle), label);
}

function viewStats(c) {
  const vital = ['lifePoints', 'spellPoints'].filter(k => isPair(c[k]));
  const attrs = c.attributes ? Object.keys(c.attributes).map(k => `attributes.${k}`) : [];
  return [
    h('div', { class: 'toolbar' },
      h('p', { class: 'hint' }, 'Each value is current / maximum. The bar shows how full it is.'),
      maxButton([...vital, ...attrs], 'Max all (heal & restore)')),
    vital.length ? card('Vitality', pairList(c, vital)) : null,
    attrs.length ? card('Attributes', pairList(c, attrs), h('p', { class: 'hint', id: MAX_HINT_ID }, MAX_HINT)) : null,
    isPair(c.age) ? card('Age', h('div', { class: 'pairs' },
      pairRow('age', c.age, 'Age', 'Current age / natural limit (up to 100).', { maxLabel: 'natural limit' }))) : null,
  ];
}

function viewSkills(c) {
  const skills = c.skills ? Object.keys(c.skills).map(k => `skills.${k}`) : [];
  if (!skills.length) return card('Skills', h('p', { class: 'muted' }, 'This save does not list any skills for this character.'));
  return [
    h('div', { class: 'toolbar' },
      h('p', { class: 'hint' }, 'Skills are percentages: current / maximum the character can reach.'),
      maxButton(skills, 'Max all skills')),
    card('Skills', pairList(c, skills), h('p', { class: 'hint', id: MAX_HINT_ID }, MAX_HINT)),
  ];
}

function viewConditions(c) {
  const en = S.meta.enums.conditions;
  if (!en) return card('Conditions', generic('conditions', c.conditions));
  const status = h('p', { class: 'condition-status', id: 'condition-status' });
  return card('Conditions',
    h('p', { class: 'hint' }, 'Tick a condition to give it, untick to cure it. Hover a condition for what it does.'),
    flagGroup('conditions', c.conditions, en, { legend: 'Active conditions', chipHelp: CONDITION_HELP }),
    h('div', { class: 'toolbar' }, status,
      h('button', { type: 'button', class: 'btn', id: 'btn-cure', onclick: () => {
        docFor(currentScope()).draft.conditions = [];
        refresh({ sync: true });
        toast('All conditions cured. Press Apply to keep it.');
      } }, svg(ICON.heart), 'Cure all')));
}

function viewLangMagic(c) {
  const out = [];
  const lang = S.meta.enums.language, magic = S.meta.enums.magic;
  if ('language' in c) {
    out.push(card('Languages', lang
      ? flagGroup('language', c.language, lang, { legend: 'Languages spoken', help: 'Characters can only talk to people whose language they know.' })
      : generic('language', c.language)));
  }
  if ('magic' in c) {
    out.push(card('Magic', magic
      ? flagGroup('magic', c.magic, magic, { legend: 'Magic schools', help: 'Which kinds of magic the character can use. Spells are on the Spells tab.' })
      : generic('magic', c.magic)));
  }
  return out.length ? out : card('Languages & magic', h('p', { class: 'muted' }, 'Not available in this save.'));
}

function schoolName(school) {
  const magic = S.meta.enums.magic;
  const v = magic && magic.values.find(x => x.value === (1 << school));
  return v ? `${enumLabel(v.name)} magic` : `School ${school}`;
}

const spellUi = { open: new Map(), knownOnly: false, q: '' }; // per character: Set of open schools

function applySpellFilter() {
  const q = spellUi.q.trim().toLowerCase();
  const doc = docFor(currentScope());
  for (const tr of $$('#panel-body tr[data-spell-index]')) {
    const sp = doc.draft.spells[Number(tr.dataset.spellIndex)];
    tr.hidden = (!!q && !tr.dataset.spellName.includes(q)) || (spellUi.knownOnly && !sp.known);
  }
  for (const det of $$('#panel-body details.school')) {
    const visible = $$('tr[data-spell-index]', det).some(tr => !tr.hidden);
    det.hidden = (!!q || spellUi.knownOnly) && !visible;
    if ((q || spellUi.knownOnly) && visible) det.open = true;
  }
  const none = $('#spell-none');
  if (none) none.hidden = $$('#panel-body details.school').some(d => !d.hidden);
}

function viewSpells(c) {
  if (!Array.isArray(c.spells) || !c.spells.length) {
    return card('Spells', h('p', { class: 'muted' }, 'No spell list is available for this character.'));
  }
  const groups = new Map();
  c.spells.forEach((sp, i) => {
    const k = sp.school ?? 0;
    if (!groups.has(k)) groups.set(k, []);
    groups.get(k).push(i);
  });
  if (!spellUi.open.has(S.selected)) {
    // first visit: open schools with known spells or that the character can use
    const open = new Set();
    const magicEnum = S.meta.enums.magic;
    for (const [school, idxs] of groups) {
      const flag = magicEnum && magicEnum.values.find(v => v.value === (1 << school));
      if (idxs.some(i => c.spells[i].known) || (flag && (c.magic || []).includes(flag.name))) open.add(school);
    }
    spellUi.open.set(S.selected, open);
  }
  const openSet = spellUi.open.get(S.selected);
  const search = h('input', { type: 'search', id: 'spell-search', placeholder: 'Filter by name…', autocomplete: 'off', value: spellUi.q,
    oninput: e => { spellUi.q = e.target.value; applySpellFilter(); } });
  const knownOnly = h('label', { class: 'check' },
    h('input', { type: 'checkbox', id: 'spell-known-only', checked: spellUi.knownOnly,
      onchange: e => { spellUi.knownOnly = e.target.checked; applySpellFilter(); } }), 'Show known spells only');
  const setAll = (idxs, known) => {
    const d = docFor(currentScope()).draft;
    idxs.forEach(i => { d.spells[i].known = known; });
    refresh({ sync: true });
  };
  const hasStrength = c.spells.some(sp => 'strength' in sp);
  const out = [
    h('div', { class: 'toolbar' },
      h('div', { class: 'inline-form' }, h('label', { for: 'spell-search' }, 'Find a spell'), search),
      knownOnly),
    h('p', { class: 'hint' }, 'A known spell can only be cast if the character also has that magic school (Languages & magic tab). Strength is how well the spell has been practised.'),
    [...groups.entries()].sort((x, y) => x[0] - y[0]).map(([school, idxs]) => {
      const name = schoolName(school);
      const det = h('details', { class: 'card school', 'data-school': String(school), open: openSet.has(school) },
        h('summary', null, h('span', { class: 'card-title' }, name),
          h('span', { class: 'school-count', 'data-school-count': idxs.join(',') })),
        h('div', { class: 'toolbar end' },
          h('button', { type: 'button', class: 'btn small', onclick: () => setAll(idxs, true) }, 'Learn all'),
          h('button', { type: 'button', class: 'btn small ghost', onclick: () => setAll(idxs, false) }, 'Forget all')),
        h('div', { class: 'table-wrap' }, h('table', { class: 'spells' },
          h('caption', { class: 'visually-hidden' }, name + ' spells'),
          h('thead', null, h('tr', null, h('th', { scope: 'col' }, 'Known'), h('th', { scope: 'col' }, '#'),
            h('th', { scope: 'col' }, 'Spell'), hasStrength ? h('th', { scope: 'col' }, 'Strength') : null)),
          h('tbody', null, idxs.map(i => {
            const sp = c.spells[i];
            const num = sp.id != null ? sp.id + 1 : idxs.indexOf(i) + 1;
            const nm = sp.name || ('Spell ' + num);
            return h('tr', { 'data-spell-name': nm.toLowerCase(), 'data-spell-index': String(i) },
              h('td', null, 'known' in sp ? h('label', { class: 'check solo' },
                h('input', { type: 'checkbox', 'data-path': 'spells.' + i + '.known', 'data-kind': 'bool', checked: !!sp.known }),
                h('span', { class: 'visually-hidden' }, name + ': knows ' + nm)) : null),
              h('td', { class: 'muted num-cell' }, num),
              h('th', { scope: 'row', class: 'spell-name' + (sp.name ? '' : ' muted') }, nm),
              hasStrength ? h('td', null, 'strength' in sp ? numberInput('spells.' + i + '.strength', sp.strength, { ariaLabel: name + ', ' + nm + ': strength' }) : null) : null);
          })))));
      det.addEventListener('toggle', () => { if (det.open) openSet.add(school); else openSet.delete(school); });
      return det;
    }),
    h('p', { class: 'muted', id: 'spell-none', hidden: true }, 'No spells match the filter.'),
  ];
  queueMicrotask(applySpellFilter);
  return out;
}

// ---- Inventory --------------------------------------------------------------

function slotTile(path, label, hint) {
  return h('button', { type: 'button', class: 'slot', 'data-slot-path': path, 'data-slot-label': label, title: hint || null,
    onclick: () => openSlot(path, label) },
  h('span', { class: 'slot-label' }, label),
  h('span', { class: 'slot-icon', 'aria-hidden': 'true' }, h('span', { class: 'slot-glyph' }), h('span', { class: 'slot-qty' })),
  h('span', { class: 'slot-item' }),
  h('span', { class: 'slot-badges' }));
}

const defOf = st => (st && st.itemId ? itemDef(S.meta.items, st.itemId) : null);
const isCursed = (st, def) => !!((def && def.cursed) || (st && Array.isArray(st.flags) && st.flags.includes('Cursed')));
const maxCountOf = def => def.maxCount ?? (def.stackable ? (S.meta.limits.stackMax ?? 99) : 1);
/** Short tags for an item definition (picker list, tiles, dialog). */
function itemTags(def) {
  if (!def) return [];
  const t = [];
  if (def.stackable && maxCountOf(def) < 255) t.push({ cls: 'stack', text: 'x' + maxCountOf(def), title: 'Stacks up to ' + maxCountOf(def) });
  if (def.plotItem) t.push({ cls: 'quest', text: 'Quest', title: 'Quest item: needed for the story' });
  return t;
}
const overText = list => list.map(o => labelFor(o.key).toLowerCase() + ' ' + o.value + ' > ' + o.max).join(', ');

function updateTile(btn, doc) {
  const path = btn.dataset.slotPath;
  const st = getAt(doc.draft, path) || {};
  const label = btn.dataset.slotLabel;
  const name = stackLabel(st);
  const empty = !name;
  const def = defOf(st);
  const over = empty ? [] : stackOverLimit(st, def);
  btn.classList.toggle('empty', empty);
  btn.classList.toggle('modified', !deepEqual(st, getAt(doc.server, path)));
  btn.classList.toggle('over-limit', over.length > 0);
  $('.slot-item', btn).textContent = empty ? 'Empty' : name;
  const realName = st.itemName || itemNameById(st.itemId);
  $('.slot-glyph', btn).textContent = empty ? '' : realName ? realName.charAt(0).toUpperCase() : '#';
  $('.slot-qty', btn).textContent = !empty && st.count > 1 ? String(st.count) : '';
  const flags = Array.isArray(st.flags) ? st.flags.filter(f => f !== zeroName(S.meta.enums.itemFlags)) : [];
  const badges = [];
  if (!empty) {
    for (const t of itemTags(def)) badges.push(h('span', { class: 'badge tag-' + t.cls, title: t.title }, t.text));
    if (isCursed(st, def)) badges.push(h('span', { class: 'badge flag-cursed', title: 'Cursed' }, 'Cursed'));
    if (flags.includes('Broken')) badges.push(h('span', { class: 'badge flag-broken', title: 'Broken' }, 'broken'));
    if (over.length) badges.push(h('span', { class: 'badge warn', title: "Over the game's limit: " + overText(over) }, '\u26a0 limit'));
  }
  $('.slot-badges', btn).replaceChildren(...badges);
  const bits = [];
  if (!empty) {
    if (st.count > 1) bits.push(st.count + ' pieces');
    if (def && def.plotItem) bits.push('quest item');
    if (isCursed(st, def)) bits.push('cursed');
    if (flags.includes('Broken')) bits.push('broken');
    if (over.length) bits.push("over the game's limit: " + overText(over));
  }
  btn.setAttribute('aria-label', label + ': ' + (empty ? 'empty' : [name, ...bits].join(', ')) + '. Edit slot.');
}

function viewInventory(c) {
  const eqKeys = c.equipment ? [...EQUIP_SLOTS.filter(k => k in c.equipment), ...Object.keys(c.equipment).filter(k => !EQUIP_SLOTS.includes(k))] : [];
  const doll = h('div', { class: 'doll' }, eqKeys.map(k => slotTile(`equipment.${k}`, EQUIP_LABELS[k] || labelFor(k),
    k === 'tail' ? 'Tail slot: only Iskai can use it.' : null)));
  const pack = Array.isArray(c.backpack)
    ? h('div', { class: 'backpack' }, c.backpack.map((_, i) => slotTile(`backpack.${i}`, `Slot ${i + 1}`)))
    : null;
  const gold = 'gold' in c || 'rations' in c
    ? h('p', { class: 'hint' }, 'Gold and rations are on the Basics tab.') : null;
  return [
    h('p', { class: 'hint' }, 'Click a slot to change its item. Highlighted slots have unapplied changes.'),
    S.meta.gameDataConfigured ? null : h('section', { class: 'card callout compact', 'aria-labelledby': 'cta-items-title' },
      h('h2', { class: 'card-title', id: 'cta-items-title' }, 'Items shown by number'),
      h('p', null, 'Load item names from your Albion folder:'),
      gameDataForm('inv')),
    h('div', { class: 'inventory' },
      c.equipment ? card('Equipment', doll) : null,
      pack ? card('Backpack', pack, gold) : null),
  ];
}

function openSlot(path, label) {
  S.slot = { path, label };
  const dlg = $('#slot-dialog');
  renderSlotDialog();
  if (!dlg.open) dlg.showModal();
  refresh();
  const first = $('#item-search', dlg) || $('[data-path$=".itemId"]', dlg);
  if (first) first.focus();
}

function renderSlotDialog() {
  const dlg = $('#slot-dialog');
  const { path, label } = S.slot;
  const doc = docFor(currentScope());
  const st = getAt(doc.draft, path);
  const items = S.meta.items || [];
  const kids = [];

  kids.push(h('div', { class: 'dialog-head' },
    h('h2', { id: 'slot-dialog-title' }, label),
    h('button', { type: 'button', class: 'icon-btn', 'aria-label': 'Close', onclick: () => dlg.close() }, svg(ICON.x))));
  kids.push(h('div', { class: 'slot-summary' }, h('p', { class: 'slot-current', id: 'slot-current' }), h('span', { class: 'slot-tags', id: 'slot-tags' })));

  if (items.length) {
    const list = h('select', { id: 'item-list', size: '8', 'aria-label': 'Matching items', class: 'item-list' });
    const fill = q => {
      q = q.trim().toLowerCase();
      const cur = getAt(doc.draft, path).itemId;
      const matches = items.filter(i => !q || i.name.toLowerCase().includes(q) || String(i.id) === q).slice(0, 300);
      list.replaceChildren(
        h('option', { value: '0', selected: !cur }, '\u2014 Empty slot \u2014'),
        ...matches.map(i => h('option', { value: String(i.id), selected: i.id === cur },
          [`${i.name}  (#${i.id})`, ...itemTags(i).map(t => t.text), i.cursed ? 'Cursed' : null].filter(Boolean).join('  \u00b7  '))));
    };
    const search = h('input', { type: 'search', id: 'item-search', placeholder: 'Type to search, e.g. sword', autocomplete: 'off',
      'aria-controls': 'item-list', oninput: e => fill(e.target.value),
      onkeydown: e => { if (e.key === 'ArrowDown') { e.preventDefault(); list.focus(); if (list.selectedIndex < 0) list.selectedIndex = 0; } } });
    list.addEventListener('change', async () => {
      const id = Number(list.value);
      const s = getAt(doc.draft, path);
      if (id === s.itemId) return;
      if (!(await confirmQuestRemoval(s))) { list.value = String(s.itemId || 0); return; }
      s.itemId = id;
      clearError(currentScope(), `${path}.itemId`);
      afterChange(`${path}.itemId`);
      refresh({ sync: true });
    });
    fill('');
    kids.push(h('div', { class: 'field wide' }, h('label', { for: 'item-search' }, 'Item'), search, list));
  }

  const nums = ['itemId', 'count', 'charges', 'enchantment'].filter(k => k in st);
  const numGrid = grid(nums.map(k => field(`${path}.${k}`, st[k], k === 'itemId' && items.length ? { help: 'Or type the item number directly.' } : {})));
  const idInput = $('[data-path$=".itemId"]', numGrid);
  if (idInput) idInput.dataset.commit = 'change'; // applied on change: may need the quest-item confirm
  kids.push(numGrid);
  if ('flags' in st) {
    const en = enumFor(`${path}.flags`);
    kids.push(en ? flagGroup(`${path}.flags`, st.flags, en, { legend: 'Flags' }) : generic(`${path}.flags`, st.flags));
  }
  const extra = Object.keys(st).filter(k => !KNOWN_STACK_KEYS.has(k));
  if (extra.length) kids.push(h('div', { class: 'dialog-extra' }, extra.map(k => generic(`${path}.${k}`, st[k]))));

  kids.push(h('p', { class: 'hint' }, 'Changes show up right away; press Apply to store them in the save.'));
  kids.push(h('div', { class: 'dialog-actions' },
    h('button', { type: 'button', class: 'btn ghost danger', onclick: async () => {
      const s = getAt(doc.draft, path);
      if (!(await confirmQuestRemoval(s))) return;
      for (const k of Object.keys(s)) {
        if (Array.isArray(s[k])) s[k] = [];
        else if (typeof s[k] === 'number') s[k] = 0;
        else if (typeof s[k] === 'string') s[k] = '';
      }
      renderSlotDialog();
      refresh({ sync: true });
    } }, 'Empty this slot'),
    h('button', { type: 'button', class: 'btn primary', onclick: () => dlg.close() }, 'Done')));
  dlg.replaceChildren(...kids);
}

// ---- Other --------------------------------------------------------------------

const READ_ONLY_GROUPS = [
  { title: 'Combat values', note: 'Calculated by the game from equipment.', keys: ['baseDefense', 'bonusDefense', 'baseAttack', 'bonusAttack'] },
  { title: 'Script links', note: 'Do not change: editing could break conversations.', keys: ['eventSetId', 'wordSetId'] },
  { title: 'Growth per level', note: 'How much the character gains when levelling up.', keys: ['levelsPerActionPoint', 'lifePointsPerLevel', 'spellPointsPerLevel', 'trainingPointsPerLevel'] },
];

function viewOther(c) {
  const out = [];
  const ro = S.meta.readOnly || [];
  // read-only values may live in "other" or at the top level
  const where = k => (c.other && k in c.other ? `other.${k}` : k in c ? k : null);
  const roPaths = ro.map(where).filter(Boolean);
  const other = c.other && typeof c.other === 'object' ? Object.keys(c.other).filter(k => !ro.includes(k)) : [];
  if (other.length) {
    out.push(card('Other values', h('p', { class: 'hint' }, 'Extra values found in the save. Change them only if you know what they do.'),
      grid(other.map(k => generic(`other.${k}`, c.other[k])))));
  }
  const shown = new Set();
  for (const g of READ_ONLY_GROUPS) {
    const paths = g.keys.map(where).filter(Boolean);
    if (!paths.length) continue;
    paths.forEach(x => shown.add(x));
    out.push(h('section', { class: 'card readonly-card' },
      h('h2', { class: 'card-title' }, g.title, h('span', { class: 'ro-badge' }, 'read-only')),
      h('p', { class: 'hint' }, g.note),
      grid(paths.map(x => field(x, getAt(c, x), { readOnly: true, help: HELP[leafKey(x)] || null })))));
  }
  const restRo = roPaths.filter(x => !shown.has(x));
  if (restRo.length) {
    out.push(h('section', { class: 'card readonly-card' }, h('h2', { class: 'card-title' }, 'Other read-only values', h('span', { class: 'ro-badge' }, 'read-only')),
      grid(restRo.map(x => field(x, getAt(c, x), { readOnly: true })))));
  }
  const unknown = Object.keys(c).filter(k => !KNOWN_CHAR_KEYS.has(k) && !ro.includes(k));
  if (unknown.length) out.push(card('Additional fields', grid(unknown.map(k => generic(k, c[k])))));
  if ('type' in c) out.push(card('Advanced', grid(field('type', c.type))));
  return out.length ? out : card('Other', h('p', { class: 'muted' }, 'Nothing else to edit for this character.'));
}

// ===========================================================================
// Change tracking
// ===========================================================================

function readControl(el, current) {
  switch (el.dataset.kind) {
    case 'int': case 'decimal': {
      if (el.value === '') return el.validity && el.validity.badInput ? NaN : null;
      return Number(el.value);
    }
    case 'bool': return el.checked;
    case 'flag': {
      const list = Array.isArray(current) ? current.slice() : [];
      const name = el.dataset.flag;
      const i = list.indexOf(name);
      if (el.checked && i < 0) list.push(name);
      if (!el.checked && i >= 0) list.splice(i, 1);
      const en = enumFor(el.dataset.path);
      if (en) { const order = en.values.map(v => v.name); list.sort((a, b) => (order.indexOf(a) + 1 || 999) - (order.indexOf(b) + 1 || 999)); }
      return list;
    }
    default: return el.value;
  }
}

function writeControl(el, v) {
  switch (el.dataset.kind) {
    case 'int': case 'decimal': if (Number(el.value) !== v || el.value === '') el.value = v == null || Number.isNaN(v) ? '' : String(v); break;
    case 'bool': el.checked = !!v; break;
    case 'flag': el.checked = Array.isArray(v) && v.includes(el.dataset.flag); break;
    default: el.value = v ?? '';
  }
}

function onControlInput(e) {
  const el = e.target;
  if (!el.dataset || !el.dataset.kind || !el.dataset.path) return;
  if (!el.closest('#panel, #slot-dialog')) return;
  const scope = currentScope();
  const doc = docFor(scope);
  if (!doc) return;
  const path = el.dataset.path;
  const isNum = el.dataset.kind === 'int' || el.dataset.kind === 'decimal';
  if (el.dataset.commit === 'change') {
    if (e.type === 'change') commitItemId(el, path);
    return;
  }
  // text and number inputs fire both input and change; handle once via input
  if (e.type === 'change' && (isNum || el.dataset.kind === 'text')) {
    if (isNum && /\.(count|charges|enchantment)$/.test(path) && el.closest('#slot-dialog')) clampStackField(el, path);
    else if (isNum) clampNumber(el, path);
    return;
  }
  setAt(doc.draft, path, readControl(el, getAt(doc.draft, path)));
  clearError(scope, path);
  afterChange(path);
  refresh({ sync: el.dataset.kind === 'flag' || path.endsWith('.itemId') });
}

/** Side effects of particular edits (keeps items consistent). */
function afterChange(path) {
  if (!path.endsWith('.itemId')) return;
  const stackPath = path.slice(0, -'.itemId'.length);
  const doc = docFor(currentScope());
  const st = getAt(doc.draft, stackPath);
  if (!isStack(st)) return;
  if (!st.itemId) {
    // empty slot: everything back to zero (the backend normalises the same way)
    st.count = 0;
    if ('charges' in st) st.charges = 0;
    if ('enchantment' in st) st.enchantment = 0;
    if (Array.isArray(st.flags)) st.flags = [];
    if ('itemName' in st) st.itemName = '';
    return;
  }
  if ('itemName' in st) {
    const orig = getAt(doc.server, stackPath);
    st.itemName = itemNameById(st.itemId) || (orig && orig.itemId === st.itemId ? orig.itemName : '');
  }
  if (!st.count) st.count = 1;
  // clamp to the new item's limits (e.g. 19 canisters -> sword: count 1, charges clamped)
  const def = defOf(st);
  if (def) {
    st.count = Math.max(1, Math.min(st.count, maxCountOf(def)));
    if (typeof st.charges === 'number' && def.maxCharges != null) st.charges = Math.min(st.charges, def.maxCharges);
    if (typeof st.enchantment === 'number' && def.maxEnchantments != null) st.enchantment = Math.min(st.enchantment, def.maxEnchantments);
  }
}

/** Number box for the item number: applied on change, with the quest-item confirm. */
async function commitItemId(el, path) {
  const doc = docFor(currentScope());
  const stackPath = path.slice(0, -'.itemId'.length);
  const st = getAt(doc.draft, stackPath);
  const v = readControl(el);
  if (v === st.itemId) return;
  if (typeof v !== 'number' || !Number.isInteger(v)) { st.itemId = v; refresh(); return; } // validation reports it
  if (!(await confirmQuestRemoval(st))) { el.value = String(st.itemId); return; }
  st.itemId = v;
  clearError(currentScope(), path);
  afterChange(path);
  refresh({ sync: true });
}

/** Count/charges/enchantment: pull an edited value back into the item's limits when the box is left. */
function clampStackField(el, path) {
  const doc = docFor(currentScope());
  const key = path.split('.').pop();
  const st = getAt(doc.draft, path.slice(0, -(key.length + 1)));
  const def = defOf(st);
  const v = getAt(doc.draft, path);
  if (!def || typeof v !== 'number' || Number.isNaN(v)) return;
  if (v === getAt(doc.server, path)) return; // untouched (even if over the limit): leave it
  const max = key === 'count' ? maxCountOf(def) : key === 'charges' ? def.maxCharges : def.maxEnchantments;
  if (max == null) return;
  const min = key === 'count' ? 1 : 0;
  const clamped = Math.max(min, Math.min(v, max));
  if (clamped === v) return;
  setAt(doc.draft, path, clamped);
  el.value = String(clamped);
  toast(`${labelFor(key)} set to ${clamped}: ${def.name} allows ${min} to ${max}.`);
  refresh();
}

/** Range for a numeric input: min/max from limits; a pair's current is capped by the pair's max. */
function rangeOf(el, doc) {
  const lim = limitFor(S.meta.limits, el.dataset.path);
  let min = lim && lim.min != null ? lim.min : null, max = lim && lim.max != null ? lim.max : null;
  if (el.dataset.pairCurrent) {
    const pr = getAt(doc.draft, el.dataset.pairCurrent);
    if (pr && typeof pr.max === 'number') max = max == null ? pr.max : Math.min(max, pr.max);
    if (min == null) min = 0;
  }
  return { min, max };
}

/** Pulls an edited value back into range when the box is left (untouched values are left alone). */
function clampNumber(el, path) {
  const doc = docFor(currentScope());
  const v = getAt(doc.draft, path);
  if (typeof v !== 'number' || Number.isNaN(v) || v === getAt(doc.server, path)) return;
  const { min, max } = rangeOf(el, doc);
  let c = v;
  if (max != null && c > max) c = max;
  if (min != null && c < min) c = min;
  if (c !== v) {
    setAt(doc.draft, path, c);
    el.value = String(c);
    const m = /^(.*)\.(current|max)$/.exec(path);
    const what = m && isPair(getAt(doc.draft, m[1]))
      ? `${labelFor(leafKey(m[1]))} ${m[2] === 'max' ? (m[1] === 'age' ? 'natural limit' : 'maximum') : 'current value'}`
      : labelFor(leafKey(path));
    toast(`${what} set to ${c}: allowed ${min ?? '…'} to ${max ?? '…'}.`);
  }
  // lowering a maximum pulls the current value down with it
  if (path.endsWith('.max')) {
    const pairPath = path.slice(0, -4);
    const pr = getAt(doc.draft, pairPath);
    if (isPair(pr) && pr.current > pr.max) {
      pr.current = pr.max;
      toast(`${labelFor(leafKey(pairPath))}: current lowered to ${pr.max} to fit the new maximum.`);
    }
  }
  refresh({ sync: true });
}

/** Marks untouched values that are already outside the game's limits (warning only; Apply is not blocked). */
function renderLimitNotes(doc) {
  for (const root of controlRoots()) {
    $$('.limit-note', root).forEach(n => n.remove());
    $$('.over-game-limit', root).forEach(n => n.classList.remove('over-game-limit'));
    for (const el of $$('input[data-kind="int"], input[data-kind="decimal"]', root)) {
      const path = el.dataset.path;
      if (el.closest('#slot-dialog') && /\.(count|charges|enchantment)$/.test(path)) continue; // item limits handle these
      if (el.dataset.pairCurrent) {
        const pr = getAt(doc.draft, el.dataset.pairCurrent);
        if (pr && typeof pr.max === 'number') el.max = String(rangeOf(el, doc).max);
      }
      const v = getAt(doc.draft, path);
      if (typeof v !== 'number' || v !== getAt(doc.server, path)) continue;
      const { min, max } = rangeOf(el, doc);
      const over = (max != null && v > max) || (min != null && v < min);
      if (!over) continue;
      const holder = el.closest('.pair-row, .field') || el.parentElement;
      holder.classList.add('over-game-limit');
      el.classList.add('over-game-limit');
      if ($('.limit-note', holder)) continue; // one note per row
      const id = `lim-${fieldId(path)}`;
      const why = el.dataset.pairCurrent && max != null && v > max ? `current is above the maximum (${max})`
        : max != null && v > max ? `max ${max}` : `min ${min}`;
      holder.append(h('p', { class: 'limit-note', id }, `\u26a0 Over the game's limit (${why}). Fine to keep; edits must fit.`));
      if (!(el.getAttribute('aria-describedby') || '').includes(id)) el.setAttribute('aria-describedby', `${el.getAttribute('aria-describedby') || ''} ${id}`.trim());
    }
  }
}

/** Asks before a quest item is removed or replaced. Resolves true to go ahead. */
async function confirmQuestRemoval(st) {
  const def = defOf(st);
  if (!def || !def.plotItem) return true;
  const v = await choose({
    title: 'Remove a quest item?',
    message: `"${def.name}" is a quest item. Removing it may make the story impossible to finish. Remove anyway?`,
    buttons: [{ label: 'Remove anyway', value: 'yes', danger: true }, { label: 'Keep it', value: null, primary: true }],
  });
  return v === 'yes';
}

/** Slot dialog: limits, disabled states and hints that depend on the chosen item. */
function applySlotLimits(doc) {
  const dlg = $('#slot-dialog');
  const path = S.slot.path;
  const st = getAt(doc.draft, path);
  if (!st) return;
  const items = S.meta.items || [];
  const def = defOf(st);
  const over = stackOverLimit(st, def);
  for (const key of ['count', 'charges', 'enchantment']) {
    const el = $(`[data-path="${path}.${key}"]`, dlg);
    if (!el) continue;
    const wrap = el.closest('.field');
    let hint = $('.limit-hint', wrap);
    if (!hint) {
      hint = h('p', { class: 'limit-hint', id: `${el.id}-limit` });
      el.after(hint);
      el.setAttribute('aria-describedby', `${el.getAttribute('aria-describedby') || ''} ${hint.id}`.trim());
    }
    const byte = limitFor(S.meta.limits, `${path}.${key}`);
    const o = over.find(x => x.key === key);
    let max = byte && byte.max != null ? byte.max : 255, disabled = false, text = '';
    if (!items.length) {
      if (key === 'count') text = 'Limits per item appear after loading item names.';
    } else if (!st.itemId) {
      disabled = true;
      if (key === 'count') text = 'Choose an item first.';
    } else if (!def) {
      if (key === 'count') text = 'Unknown item number: no limits known.';
    } else if (key === 'count') {
      max = maxCountOf(def);
      if (max >= 255) text = 'Stack limit unknown (only item names are loaded).';
      else if (max <= 1) { text = "Doesn't stack"; disabled = !o && st.count === 1; } else text = `Stacks up to ${max}`;
    } else {
      const m = key === 'charges' ? def.maxCharges : def.maxEnchantments;
      if (m != null) {
        max = m;
        if (m === 0) { text = key === 'charges' ? 'This item has no charges' : "This item can't be enchanted"; disabled = !o && !st[key]; }
        else text = `Up to ${m}`;
      }
    }
    el.max = String(max);
    el.disabled = disabled;
    wrap.classList.toggle('is-disabled', disabled);
    wrap.classList.toggle('over-limit', !!o);
    hint.replaceChildren(
      o ? h('span', { class: 'warn-text' }, `\u26a0 Over the game's limit (max ${o.max})`) : '',
      o && text ? ' \u00b7 ' : '', text);
  }
  const tags = $('#slot-tags', dlg);
  if (tags) {
    const t = st.itemId ? itemTags(def).map(x => h('span', { class: 'badge tag-' + x.cls, title: x.title }, x.text)) : [];
    if (st.itemId && isCursed(st, def)) t.push(h('span', { class: 'badge flag-cursed' }, 'Cursed'));
    if (st.itemId && def && def.class) t.push(h('span', { class: 'badge' }, splitCamel(def.class)));
    tags.replaceChildren(...t);
  }
}

function controlRoots() {
  return [$('#panel'), S.slot ? $('#slot-dialog') : null].filter(Boolean);
}

/** Re-evaluates highlights, bars, tiles, dots and buttons. With sync=true also pushes draft values into inputs. */
function refresh({ sync = false } = {}) {
  const scope = currentScope();
  const doc = docFor(scope);
  if (!doc) return;
  const roots = controlRoots();

  for (const root of roots) {
    for (const el of $$('[data-path][data-kind]', root)) {
      const p = el.dataset.path;
      const dv = getAt(doc.draft, p), sv = getAt(doc.server, p);
      let changed;
      if (el.dataset.kind === 'flag') {
        const f = el.dataset.flag;
        changed = (Array.isArray(dv) && dv.includes(f)) !== (Array.isArray(sv) && sv.includes(f));
      } else changed = !deepEqual(dv, sv);
      if (sync && el !== document.activeElement) writeControl(el, dv);
      if (sync && el.dataset.kind === 'flag') writeControl(el, dv);
      const target = el.closest('.chip, .check') || el;
      target.classList.toggle('modified', changed);
    }
    for (const bar of $$('.bar[data-bar]', root)) {
      const pr = getAt(doc.draft, bar.dataset.bar) || {};
      const max = Number(pr.max) || 0, cur = Number(pr.current) || 0;
      const pct = max > 0 ? Math.max(0, Math.min(100, (cur / max) * 100)) : 0;
      const fill = $('.fill', bar);
      fill.style.width = `${pct}%`;
      bar.classList.toggle('low', max > 0 && pct < 34);
      bar.classList.toggle('over', max > 0 && cur > max);
    }
    for (const tile of $$('.slot[data-slot-path]', root)) updateTile(tile, doc);
  }

  // slot dialog: per-item limits, then the subtitle
  if (S.slot) applySlotLimits(doc);
  if (S.slot) {
    const cur = $('#slot-current');
    const st = getAt(doc.draft, S.slot.path);
    if (cur) cur.textContent = stackLabel(st) ? `Contains: ${stackLabel(st)}${st.count > 1 ? ` \u00d7${st.count}` : ''}` : 'This slot is empty.';
  }

  for (const el of $$('[data-school-count]')) {
    const idxs = el.dataset.schoolCount.split(',').map(Number);
    const known = idxs.filter(i => doc.draft.spells && doc.draft.spells[i] && doc.draft.spells[i].known).length;
    el.textContent = `${known} of ${idxs.length} known`;
    el.classList.toggle('has-known', known > 0);
  }

  // conditions status
  const cs = $('#condition-status');
  if (cs) {
    const list = doc.draft.conditions || [];
    cs.textContent = list.length ? `${list.length} active: ${list.map(enumLabel).join(', ')}` : 'Healthy \u2013 no conditions.';
    const cure = $('#btn-cure');
    if (cure) cure.disabled = !list.length;
  }

  // title
  const t = $('#panel-title'), sub = $('#panel-sub');
  if (t) {
    if (scope === 'header') {
      t.textContent = 'Save info';
      sub.textContent = doc.draft.name ? `\u201c${doc.draft.name}\u201d` : '';
    } else {
      const c = doc.draft;
      t.textContent = charLabel({ ...c, empty: c.empty ?? summaryOf(S.selected).empty }, S.selected);
      sub.textContent = [c.race && enumLabel(c.race), c.class && enumLabel(c.class), c.level != null ? `Level ${c.level}` : null].filter(Boolean).join(' \u00b7 ');
    }
  }

  // action bar
  const dirty = isDirty(scope);
  const st = $('#dirty-status');
  if (st) {
    st.textContent = S.busy ? 'Applying…' : dirty ? 'Unapplied changes' : 'No unapplied changes';
    st.classList.toggle('on', dirty);
  }
  const ab = $('#btn-apply'), rb = $('#btn-revert');
  if (ab) ab.disabled = !dirty || S.busy;
  if (rb) rb.disabled = !dirty || S.busy;

  // tab dots
  if (scope !== 'header') {
    const changedTabs = new Set();
    for (const k of new Set([...Object.keys(doc.draft), ...Object.keys(doc.server)])) {
      if (!deepEqual(doc.draft[k], doc.server[k])) changedTabs.add(TAB_OF_KEY[k] || 'other');
    }
    for (const tab of $$('[role=tab]')) {
      const dot = $('.tab-dot', tab);
      if (dot) dot.hidden = !changedTabs.has(tab.dataset.tab);
    }
  }

  // sidebar
  for (const btn of $$('#sidebar-nav .nav-item')) {
    const sel = btn.dataset.sel === 'info' ? 'info' : Number(btn.dataset.sel);
    const dot = $('.dirty-dot', btn);
    if (dot) dot.hidden = !isDirty(scopeOf(sel));
  }
  if (scope !== 'header') {
    const btn = $(`#sidebar-nav .nav-item[data-sel="${S.selected}"]`);
    const c = doc.draft;
    if (btn) {
      const cc = { ...c, empty: c.empty ?? summaryOf(S.selected).empty };
      $('.nav-main', btn).textContent = charLabel(cc, S.selected);
      const avatar = $('.avatar', btn);
      if (avatar && avatar.firstChild) avatar.firstChild.textContent = avatarLetter(cc);
      const lvl = $('.lvl', btn);
      if (lvl) lvl.textContent = c.level ?? '';
      const subEl = $('.nav-sub', btn);
      if (subEl && !isEmptyChar(cc)) subEl.textContent = [c.race && enumLabel(c.race), c.class && enumLabel(c.class)].filter(Boolean).join(' \u00b7 ');
    }
  }

  renderLimitNotes(doc);
  renderErrors();
}

// ===========================================================================
// Errors
// ===========================================================================

function clearError(scope, path) {
  const m = errorsFor(scope);
  for (const k of [...m.keys()]) if (k === path || path.startsWith(`${k}.`) || k.startsWith(`${path}.`)) m.delete(k);
  S.bannerErrors.delete(scope);
}

function resolveField(obj, field) {
  if (!field) return null;
  const norm = String(field).replace(/\[(\d+)\]/g, '.$1').replace(/^[$.]+/, '').toLowerCase();
  const paths = allPaths(obj);
  return paths.find(p => p.toLowerCase() === norm) || paths.find(p => p.toLowerCase().endsWith(`.${norm}`)) || null;
}

function findControl(path) {
  for (const root of controlRoots()) {
    const exact = root.querySelector(`[data-path="${CSS.escape(path)}"]`);
    if (exact) return exact;
    const group = root.querySelector(`[data-group-path="${CSS.escape(path)}"]`);
    if (group) return group.querySelector('input') || group;
  }
  for (const root of controlRoots()) {
    const tile = $$('.slot[data-slot-path]', root).find(t => path === t.dataset.slotPath || path.startsWith(`${t.dataset.slotPath}.`));
    if (tile) return tile;
    const child = $$('[data-path]', root).find(el => el.dataset.path.startsWith(`${path}.`));
    if (child) return child;
  }
  return null;
}

function renderErrors() {
  const scope = currentScope();
  for (const root of controlRoots()) {
    $$('.field-error', root).forEach(n => n.remove());
    $$('[aria-invalid]', root).forEach(n => {
      n.removeAttribute('aria-invalid');
      const db = (n.getAttribute('aria-describedby') || '').split(' ').filter(x => x && !x.startsWith('err-')).join(' ');
      if (db) n.setAttribute('aria-describedby', db); else n.removeAttribute('aria-describedby');
    });
    $$('.has-error', root).forEach(n => n.classList.remove('has-error'));
  }
  const unplaced = [];
  for (const [path, msg] of errorsFor(scope)) {
    const el = findControl(path);
    if (!el) { unplaced.push(`${labelFor(leafKey(path))}: ${msg}`); continue; }
    const id = `err-${fieldId(path)}`;
    el.setAttribute('aria-invalid', 'true');
    el.setAttribute('aria-describedby', `${el.getAttribute('aria-describedby') || ''} ${id}`.trim());
    const holder = el.closest('.slot') ? el : (el.closest('.pair-row, .field, .flag-group, td') || el.parentElement);
    holder.classList.add('has-error');
    const p = h('p', { class: 'field-error', id }, svg('M12 8v5m0 3h.01M10.3 3.9 2.4 18a2 2 0 0 0 1.7 3h15.8a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z'), msg);
    if (el.closest('.slot')) el.after(p); else holder.append(p);
  }
  const banner = $('#panel-error');
  if (banner) {
    const msgs = [S.bannerErrors.get(scope), ...unplaced].filter(Boolean);
    banner.hidden = !msgs.length;
    banner.replaceChildren(...msgs.map(m => h('p', null, m)));
  }
}

/** Shows a field error: switches to the right tab / opens the item slot and focuses the input. */
function showFieldError(scope, field, message) {
  const doc = docFor(scope);
  const path = resolveField(doc.draft, field);
  if (!path) {
    S.bannerErrors.set(scope, field ? `${message} (field: ${field})` : message);
    refresh();
    return;
  }
  errorsFor(scope).set(path, message);
  if (scope !== 'header') {
    const tab = tabForPath(path);
    if (S.tab !== tab) switchTab(tab);
    const m = /^(equipment\.[^.]+|backpack\.\d+)(\.|$)/.exec(path);
    if (m && m[1] !== path) {
      const tile = $(`.slot[data-slot-path="${m[1]}"]`);
      openSlot(m[1], tile ? tile.dataset.slotLabel : labelFor(leafKey(m[1])));
    }
  }
  refresh();
  const el = findControl(path);
  if (el) {
    el.scrollIntoView({ block: 'center', behavior: 'smooth' });
    el.focus({ preventScroll: true });
  }
}

// ===========================================================================
// Apply / revert / download
// ===========================================================================

function setBusy(on) {
  S.busy = on;
  document.body.classList.toggle('busy', on);
  const dl = $('#btn-download');
  if (dl) dl.disabled = on;
  refresh();
}

async function apply(scope = currentScope()) {
  const doc = docFor(scope);
  if (!doc || S.busy) return false;
  if (!isDirty(scope)) return true;
  if (scope !== currentScope()) await select(scope === 'header' ? 'info' : Number(scope.slice(1)));
  errorsFor(scope).clear();
  S.bannerErrors.delete(scope);

  const local = scope === 'header'
    ? validateNumbers({ header: doc.draft }, S.meta.limits, 'header', { header: doc.server })
    : validateReadOnly(doc.draft, doc.server, S.meta.readOnly) || validatePairs(doc.draft, doc.server)
      || validateNumbers(doc.draft, S.meta.limits, '', doc.server) || validateItemStacks(doc.draft, doc.server, S.meta.items);
  if (local) { showFieldError(scope, local.field, local.error); return false; }

  setBusy(true);
  try {
    const body = denormalizeDoc(clone(doc.draft));
    if (scope === 'header') {
      const res = await S.api.putHeader(S.session.sessionId, body);
      S.header = makeDoc(res && typeof res === 'object' ? res : body);
    } else {
      const idx = Number(scope.slice(1));
      const res = await S.api.putCharacter(S.session.sessionId, idx, body);
      const data = res && typeof res === 'object' ? res : body;
      S.chars.set(idx, makeDoc(data));
      const sum = S.session.characters.find(c => c.index === idx);
      if (sum) Object.assign(sum, { name: data.name, race: data.race, class: data.class, level: data.level }, 'empty' in data ? { empty: data.empty } : {});
    }
    S.appliedNotDownloaded = true;
    S.busy = false;
    document.body.classList.remove('busy');
    if ($('#btn-download')) $('#btn-download').disabled = false;
    if (S.slot) $('#slot-dialog').close();
    renderSidebar();
    renderPanel();
    toast('Changes applied. Download the save when you are done.');
    return true;
  } catch (e) {
    setBusy(false);
    if (e.status === 404) { sessionLost(); return false; }
    if (e.status === 400) showFieldError(scope, e.field, e.message);
    else { S.bannerErrors.set(scope, `Could not apply the changes: ${e.message}`); refresh(); }
    return false;
  }
}

function revert(scope = currentScope()) {
  const doc = docFor(scope);
  if (!doc) return;
  doc.draft = clone(doc.server);
  errorsFor(scope).clear();
  S.bannerErrors.delete(scope);
  if (S.slot) $('#slot-dialog').close();
  renderSidebar();
  renderPanel();
  toast('Edits reverted to the last applied values.');
}

async function applyAll() {
  for (const scope of dirtyScopes()) {
    if (!(await apply(scope))) return false;
  }
  return true;
}

async function download() {
  if (!S.session || S.busy) return;
  if (anyDirty()) {
    const n = dirtyScopes().length;
    const choice = await choose({
      title: 'Some edits are not applied yet',
      message: `${n === 1 ? 'One page has' : `${n} pages have`} changes that are not in the save yet. Apply them before downloading?`,
      buttons: [
        { label: 'Apply and download', value: 'apply', primary: true },
        { label: 'Download without them', value: 'skip' },
        { label: 'Cancel', value: null },
      ],
    });
    if (!choice) return;
    if (choice === 'apply' && !(await applyAll())) {
      toast('Download cancelled: please fix the highlighted problem first.', true);
      return;
    }
  }
  setBusy(true);
  try {
    const { blob, fileName } = await S.api.download(S.session.sessionId, S.session.fileName);
    const url = URL.createObjectURL(blob);
    const a = h('a', { href: url, download: fileName || S.session.fileName });
    document.body.append(a);
    a.click();
    a.remove();
    setTimeout(() => URL.revokeObjectURL(url), 10000);
    S.appliedNotDownloaded = false;
    toast(`Downloaded ${fileName}. Copy it into ALBION\\SAVES\\ (keep your backup of the original!).`);
  } catch (e) {
    if (e.status === 404) { setBusy(false); sessionLost(); return; }
    toast(`Download failed: ${e.message}`, true);
  } finally {
    setBusy(false);
  }
}

async function setGameData(input, msg, btn) {
  const path = input.value.trim();
  input.removeAttribute('aria-invalid');
  msg.classList.remove('error-text');
  if (!path) { input.setAttribute('aria-invalid', 'true'); msg.classList.add('error-text'); msg.textContent = 'Please enter the folder where Albion is installed.'; input.focus(); return; }
  btn.disabled = true;
  S.gameDataPath = path;
  msg.textContent = 'Looking for Albion game data…';
  let res;
  try {
    res = await S.api.setGameData(path);
    if (res && res.ok === false) throw new Error(res.error || 'No Albion game data found in that folder.');
  } catch (e) {
    input.setAttribute('aria-invalid', 'true');
    msg.classList.add('error-text');
    msg.textContent = e.message;
    btn.disabled = false;
    input.focus();
    return;
  }
  msg.textContent = 'Loading names…';
  try {
    S.meta = normalizeMeta(await S.api.getMeta());
    await refreshNamesAfterGameData();
  } catch (e) {
    if (e.status === 404) { sessionLost(); return; }
    toast(`Names were loaded, but refreshing the page failed: ${e.message}`, true);
  }
  const parts = [];
  if (res && res.itemCount != null) parts.push(`${res.itemCount} item names`);
  if (res && res.spellCount != null) parts.push(`${res.spellCount} spell names`);
  toast(`Loaded ${parts.join(' and ') || 'game data'}${res && res.source ? ` from ${res.source}` : ''}.`);
  renderBody(); // the call-to-action disappears, tiles show names
}

/** After /api/gamedata: re-fetch the shown character so names appear; keep unapplied edits. */
async function refreshNamesAfterGameData() {
  const fill = draft => {
    for (const st of [...Object.values(draft.equipment || {}), ...(draft.backpack || [])]) {
      if (isStack(st) && 'itemName' in st) st.itemName = itemNameById(st.itemId);
    }
  };
  for (const i of [...S.chars.keys()]) {
    const doc = S.chars.get(i);
    const isCurrent = i === S.selected;
    if (!isCurrent && !isDirty(`c${i}`)) { S.chars.delete(i); continue; } // re-fetched on next visit
    if (!isCurrent) { fill(doc.draft); fill(doc.server); continue; }
    const fresh = makeDoc(await S.api.getCharacter(S.session.sessionId, i));
    if (!isDirty(`c${i}`)) { S.chars.set(i, fresh); continue; }
    // keep the user's edits, take names from the server copy
    fill(doc.draft);
    (doc.draft.spells || []).forEach((sp, k) => { const f = fresh.server.spells && fresh.server.spells[k]; if (f && f.id === sp.id && f.school === sp.school) sp.name = f.name; });
    doc.server = fresh.server;
  }
}

let sessionLostShown = false;
async function sessionLost() {
  if (sessionLostShown) return;
  sessionLostShown = true;
  storage('del', 'albionSaveEditor.sessionId');
  if (S.slot) $('#slot-dialog').close();
  await choose({
    title: 'Session expired',
    message: 'The editor no longer has this save loaded (the session expired or the server was restarted). Please open the save file again. Edits that were not downloaded could not be kept.',
    buttons: [{ label: 'Open the save again', value: 'ok', primary: true }],
  });
  sessionLostShown = false;
  renderLanding('Session expired – please load the save again.');
}

/** Drop a save while editing: confirm leaving, then load it directly (no browser navigation involved). */
async function openDroppedFile(file) {
  if (!(await confirmLeave())) return;
  if (S.slot) $('#slot-dialog').close();
  storage('del', 'albionSaveEditor.sessionId');
  renderLanding();
  loadFile(file);
}

async function confirmLeave() {
  if (!(anyDirty() || S.appliedNotDownloaded)) return true;
  const ok = await choose({
    title: 'Leave this save?',
    message: anyDirty()
      ? 'You have edits that are not applied or downloaded. They will be lost.'
      : 'You applied changes but have not downloaded the modified save yet. They will be lost.',
    buttons: [{ label: 'Leave without saving', value: 'leave', danger: true }, { label: 'Stay here', value: null, primary: true }],
  });
  return !!ok;
}

const BUILTIN_CREDITS = [
  { name: 'AlbLib', author: 'IS4Code', url: 'https://github.com/IS4Code/AlbLib', license: 'MIT', note: 'Albion file format library this editor is built on.' },
  { name: 'UAlbion', url: 'https://github.com/csinkers/ualbion', license: '', note: 'Format research: character sheet layout, item data.' },
];

/** About dialog: version + credits from /api/about, with built-in credits as fallback. */
async function showAbout() {
  const dlg = $('#about-dialog');
  const body = h('div', { class: 'about-body' }, h('p', { class: 'muted' }, 'Loading\u2026'));
  dlg.replaceChildren(
    h('div', { class: 'dialog-head' }, h('h2', { id: 'about-title' }, 'Albion Save Editor'),
      h('button', { type: 'button', class: 'icon-btn', 'aria-label': 'Close', onclick: () => dlg.close() }, svg(ICON.x))),
    body,
    h('div', { class: 'dialog-actions' }, h('button', { type: 'button', class: 'btn primary', onclick: () => dlg.close() }, 'Close')));
  if (!dlg.open) dlg.showModal();
  let about = null;
  try { if (S.api && S.api.getAbout) about = await S.api.getAbout(); } catch { /* endpoint missing: built-in text */ }
  const credits = about && Array.isArray(about.credits) && about.credits.length ? about.credits : BUILTIN_CREDITS;
  const version = about && about.version ? about.version : S.mode === 'mock' ? 'demo mode' : 'unknown';
  body.replaceChildren(
    h('p', null, 'Version ', h('strong', null, version)),
    h('p', null, 'Edit savegames of the 1995 role-playing game Albion. Always keep a backup of your original save.'),
    h('h3', { class: 'about-h' }, 'Credits'),
    h('ul', { class: 'credits' }, credits.map(c => h('li', null,
      c.url ? h('a', { href: c.url, target: '_blank', rel: 'noopener noreferrer' }, c.name) : h('strong', null, c.name),
      c.author ? ` by ${c.author}` : '',
      c.license ? h('span', { class: 'badge' }, c.license) : null,
      c.note ? h('span', { class: 'credit-note' }, c.note) : null))),
    about ? null : h('p', { class: 'hint' }, 'Version details are not available here.'));
}

async function openAnother() {
  if (anyDirty() || S.appliedNotDownloaded) {
    const ok = await choose({
      title: 'Leave this save?',
      message: anyDirty()
        ? 'You have edits that are not applied or downloaded. They will be lost.'
        : 'You applied changes but have not downloaded the modified save yet. They will be lost.',
      buttons: [{ label: 'Leave without saving', value: 'leave', danger: true }, { label: 'Stay here', value: null, primary: true }],
    });
    if (!ok) return;
  }
  if (S.slot) $('#slot-dialog').close();
  storage('del', 'albionSaveEditor.sessionId');
  renderLanding();
  app.focus();
}

// ===========================================================================
// Dialog + toast
// ===========================================================================

function choose({ title, message, buttons }) {
  return new Promise(resolve => {
    const dlg = $('#confirm-dialog');
    let done = false;
    const finish = v => { if (done) return; done = true; if (dlg.open) dlg.close(); resolve(v); };
    const btns = buttons.map(b => h('button', {
      type: 'button', class: `btn ${b.primary ? 'primary' : b.danger ? 'danger' : 'ghost'}`, onclick: () => finish(b.value),
    }, b.label));
    dlg.replaceChildren(h('h2', { id: 'confirm-title' }, title), h('p', null, message), h('div', { class: 'dialog-actions' }, btns));
    dlg.addEventListener('close', () => finish(null), { once: true });
    dlg.showModal();
    const primary = btns[buttons.findIndex(b => b.primary)];
    if (primary) primary.focus();
  });
}

let toastTimer = null;
function toast(text, isError = false) {
  const t = $('#toast');
  t.textContent = text;
  t.classList.toggle('error', isError);
  t.classList.add('show');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => t.classList.remove('show'), isError ? 7000 : 4000);
}

// ===========================================================================
// Global wiring
// ===========================================================================

function wireGlobalEvents() {
  document.addEventListener('input', onControlInput);
  document.addEventListener('change', onControlInput);
  $('#btn-download').addEventListener('click', download);
  $('#btn-open-other').addEventListener('click', openAnother);

  const slotDlg = $('#slot-dialog');
  slotDlg.addEventListener('close', () => {
    // 'close' is dispatched asynchronously: if the dialog was reopened meanwhile (e.g. an error
    // jumped to a slot right after closing), keep the new contents.
    if (slotDlg.open) return;
    const prev = S.slot;
    S.slot = null;
    slotDlg.replaceChildren();
    refresh();
    if (prev) { const tile = $(`.slot[data-slot-path="${prev.path}"]`); if (tile) tile.focus(); }
  });
  slotDlg.addEventListener('click', e => { if (e.target === slotDlg) slotDlg.close(); }); // backdrop click

  // dropping a file anywhere should not navigate away
  window.addEventListener('dragover', e => e.preventDefault());
  window.addEventListener('drop', e => {
    e.preventDefault();
    const f = e.dataTransfer && e.dataTransfer.files && e.dataTransfer.files[0];
    if (!f || e.target.closest?.('#dropzone')) return;
    if (!S.session) loadFile(f); else openDroppedFile(f);
  });

  window.addEventListener('beforeunload', e => {
    if (S.session && (anyDirty() || S.appliedNotDownloaded)) { e.preventDefault(); e.returnValue = ''; }
  });

  document.addEventListener('keydown', e => {
    if ((e.ctrlKey || e.metaKey) && (e.key === 's' || e.key === 'S') && S.session) {
      e.preventDefault();
      apply(currentScope());
    }
  });
}

init();
