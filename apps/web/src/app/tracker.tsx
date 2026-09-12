'use client';
import { FormEvent, useCallback, useEffect, useRef, useState } from 'react';
import { appMessages } from '@/lib/app-messages';
import Link from 'next/link';

type Person = { id: string; name: string };
type Medication = { id: string; personId: string; name: string; strength?: string; activeIngredient?: string; notes?: string; inventoryItemId: string; stockNumerator: number; stockDenominator: number };
type Regimen = { id: string; medicationId: string; localTime: string; validFrom: string; validTo: string | null; doseNumerator: number; doseDenominator: number };
type Entry = { id: string; inventoryItemId: string; reason: string; quantityNumerator: number; quantityDenominator: number; recordedAt: string };
type Workspace = { people: Person[]; medications: Medication[]; regimens: Regimen[]; ledger: Entry[] };
type Dose = { regimenVersionId: string; personName: string; medicationName: string; doseNumerator: number; doseDenominator: number; scheduledFor: string; status: string };
type Session = { accountId: string; households: { id: string; name: string }[] };
type Panel = 'person' | 'medication' | 'schedule' | 'refill' | 'count';
const empty: Workspace = { people: [], medications: [], regimens: [], ledger: [] };
const today = () => { const d = new Date(); return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`; };
const quantity = (n: number, d: number) => d === 1 ? String(n) : `${n}/${d}`;
function parseQuantity(value: string, zero = false) {
  if (!/^\d+(\/\d+)?$/.test(value.trim())) throw new Error('invalidQuantity');
  const [n, d = 1] = value.trim().split('/').map(Number);
  if (!Number.isSafeInteger(n) || !Number.isSafeInteger(d) || n < (zero ? 0 : 1) || n > 1000000 || d < 1 || d > 10000) throw new Error('invalidQuantity');
  return [n, d];
}
async function api<T>(path: string, body?: unknown): Promise<T> {
  const response = await fetch(`/api${path}`, { method: body === undefined ? 'GET' : 'POST', credentials: 'same-origin', cache: 'no-store', headers: { 'Content-Type': 'application/json', 'X-Medication-Client': '1' }, ...(body === undefined ? {} : { body: JSON.stringify(body) }) });
  if (!response.ok) throw new Error(response.status === 401 ? 'credentials' : response.status === 409 ? 'exists' : 'error');
  return response.status === 204 ? undefined as T : response.json();
}
export default function Tracker() {
  const [locale, setLocale] = useState<'tr' | 'en'>('tr'); const t = appMessages[locale];
  const [session, setSession] = useState<Session | null>(null);
  const [loading, setLoading] = useState(true); const [busy, setBusy] = useState(false);
  const [register, setRegister] = useState(false); const [workspace, setWorkspace] = useState<Workspace>(empty);
  const [doses, setDoses] = useState<Dose[]>([]); const [date, setDate] = useState('');
  const [tab, setTab] = useState<'today' | 'medications' | 'people' | 'history'>('today');
  const [panel, setPanel] = useState<Panel | null>(null); const [selected, setSelected] = useState(''); const [notice, setNotice] = useState('');
  const [workspaceReady, setWorkspaceReady] = useState(false);
  const editor = useRef<HTMLElement>(null);
  const householdId = session?.households[0]?.id;
  const refresh = useCallback(async () => {
    if (!householdId || !date) return;
    const data = await api<Workspace>(`/households/${householdId}/workspace`);
    setWorkspace(data); setWorkspaceReady(true);
    setDoses(await api<Dose[]>(`/households/${householdId}/today?date=${date}`));
  }, [householdId, date]);
  useEffect(() => { setDate(today()); if (localStorage.getItem('medication-locale') === 'en') setLocale('en'); api<Session>('/auth/session').then(setSession).catch(() => {}).finally(() => setLoading(false)); }, []);
  useEffect(() => { refresh().catch(() => setNotice('error')); }, [refresh]);
  useEffect(() => { document.documentElement.lang = locale; }, [locale]);
  useEffect(() => { if (panel) { editor.current?.scrollIntoView({ block: 'start', behavior: 'smooth' }); editor.current?.querySelector<HTMLInputElement>('input')?.focus({ preventScroll: true }); } }, [panel, selected]);
  async function run(action: () => Promise<void>) {
    if (busy) return; setBusy(true); setNotice('');
    try { await action(); } catch (error) { setNotice(error instanceof Error ? error.message : 'error'); } finally { setBusy(false); }
  }
  async function authenticate(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); const data = new FormData(event.currentTarget);
    if (register && data.get('password') !== data.get('confirmPassword')) { setNotice('passwordMismatch'); return; }
    await run(async () => { await api(`/auth/${register ? 'register' : 'login'}`, { email: data.get('email'), password: data.get('password'), ...(register ? { confirmPassword: data.get('confirmPassword') } : {}) }); setSession(await api<Session>('/auth/session')); });
  }
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault(); const data = new FormData(event.currentTarget); const value = (key: string) => String(data.get(key) ?? '');
    await run(async () => {
      const base = `/households/${householdId}`;
      if (panel === 'person') {
        const person = await api<{ id: string }>(`${base}/people`, { name: value('name').trim() });
        await refresh(); setTab('medications'); setSelected(person.id); setPanel('medication'); setNotice('personSaved'); return;
      }
      if (panel === 'medication') {
        const [stockNumerator, stockDenominator] = parseQuantity(value('stock'), true);
        const medication = await api<{ id: string }>(`${base}/medications`, { personId: value('personId'), name: value('name').trim(), strength: value('strength'), activeIngredient: value('activeIngredient'), notes: value('notes'), form: 'tablet', stockNumerator, stockDenominator });
        await refresh(); setTab('medications'); setSelected(medication.id); setPanel('schedule'); setNotice('medicationSaved'); return;
      }
      if (panel === 'schedule') { const [doseNumerator, doseDenominator] = parseQuantity(value('dose')); const medication = workspace.medications.find(m => m.id === value('medicationId'))!; await api(`${base}/regimens`, { personId: medication.personId, medicationId: medication.id, doseNumerator, doseDenominator, localTime: `${value('time')}:00`, timeZoneId: Intl.DateTimeFormat().resolvedOptions().timeZone, validFrom: value('start'), validTo: value('end') || null }); }
      if (panel === 'refill' || panel === 'count') { const [numerator, denominator] = parseQuantity(value('stock'), panel === 'count'); await api(`${base}/inventory/${selected}`, { idempotencyKey: crypto.randomUUID(), kind: panel, numerator, denominator }); }
      setPanel(null); await refresh(); if (panel === 'schedule') { setDate(value('start')); setTab('today'); } setNotice('saved');
    });
  }
  function open(next: Panel, id = '') {
    if (busy) return;
    if (next === 'medication' && !workspace.people.length) { setPanel('person'); setSelected(''); setNotice('personRequired'); return; }
    setPanel(next); setSelected(id); setNotice('');
  }
  const input = (key: 'name' | 'stock' | 'dose' | 'time' | 'start' | 'end', type = 'text', defaultValue = '') => <label>{t[key]}<input name={key} type={type} defaultValue={defaultValue} required={key !== 'end'} maxLength={key === 'name' ? 160 : undefined} /></label>;
  return <main className="shell">
    <header><Link href="/" className="brand"><span className="brandMark">M</span>{t.title}</Link><div className="actions"><select aria-label={t.language} value={locale} onChange={e => { const lang = e.target.value as 'tr' | 'en'; setLocale(lang); localStorage.setItem('medication-locale', lang); }}><option value="tr">Türkçe</option><option value="en">English</option></select>{session && <button className="secondary" disabled={busy} onClick={() => run(async () => { await api('/auth/logout', {}); setSession(null); setWorkspace(empty); setWorkspaceReady(false); setPanel(null); setDoses([]); })}>{t.logout}</button>}</div></header>
    {notice && <p role="status" className={`notice ${['saved', 'personSaved', 'medicationSaved'].includes(notice) ? 'success' : ''}`}>{notice in t ? t[notice as keyof typeof t] : t.error}</p>}
    {loading ? <p className="empty">{t.loading}</p> : !session ? <section className="auth"><div><p className="eyebrow">{t.title}</p><h1>{t.intro}</h1><p>{t.safety}</p></div><form className="card" onSubmit={authenticate}><h2>{register ? t.register : t.login}</h2><label>{t.email}<input name="email" type="email" autoComplete="email" required maxLength={320} /></label><label>{t.password}<input name="password" type="password" autoComplete={register ? 'new-password' : 'current-password'} required minLength={register ? 12 : 1} maxLength={128} /></label>{register && <><small>{t.passwordHint}</small><label>{t.confirmPassword}<input name="confirmPassword" type="password" autoComplete="new-password" required minLength={12} maxLength={128} /></label></>}<button disabled={busy}>{busy ? t.busy : register ? t.register : t.login}</button><button type="button" className="link" onClick={() => { setRegister(!register); setNotice(''); }}>{register ? t.login : t.register}</button></form></section> : <>
      <section className="heading"><div><p className="eyebrow">{t.intro}</p><h1>{t[tab]}</h1></div><div className="actions wrap"><button disabled={busy || !workspaceReady} onClick={() => open('medication')}>{t.medication}</button><button className="secondary" disabled={busy || !workspaceReady} onClick={() => open('person')}>{t.person}</button><button className="secondary" disabled={busy} onClick={() => run(refresh)}>{t.refresh}</button></div></section>
      {!workspaceReady && <p role="status">{t.loading}</p>}
      {workspaceReady && !workspace.regimens.length && <section className="card onboarding"><h2>{t.getStarted}</h2><p>{!workspace.people.length ? t.stepPerson : !workspace.medications.length ? t.stepMedication : t.stepSchedule}</p><div className="actions wrap"><button disabled={busy} onClick={() => open(!workspace.people.length ? 'person' : !workspace.medications.length ? 'medication' : 'schedule')}>{!workspace.people.length ? t.person : !workspace.medications.length ? t.medication : t.schedule}</button></div></section>}
      <nav aria-label={t.title}>{(['today', 'medications', 'people', 'history'] as const).map(item => <button key={item} className={tab === item ? 'active' : ''} onClick={() => setTab(item)}>{t[item]}</button>)}</nav>
      {panel && <section ref={editor} className="card editor"><form key={`${panel}:${selected}`} onSubmit={submit}><div className="sectionTitle"><h2>{t[panel]}</h2><button type="button" disabled={busy} className="secondary" onClick={() => setPanel(null)}>{t.cancel}</button></div><div className="formGrid">
        {panel === 'person' && input('name')}
        {panel === 'medication' && <><label>{t.medicationName}<input name="name" required maxLength={200} /></label><label>{t.personId}<select name="personId" defaultValue={selected || undefined} required>{workspace.people.map(p => <option key={p.id} value={p.id}>{p.name}</option>)}</select></label><label>{t.strength}<input name="strength" maxLength={100} placeholder={t.strengthExample} /></label><label>{t.activeIngredient}<input name="activeIngredient" maxLength={200} /></label><label className="wide">{t.notes}<textarea name="notes" maxLength={2000} rows={3} /></label><p className="wide">{t.tabletScope}</p></>}
        {(panel === 'medication' || panel === 'refill' || panel === 'count') && input('stock')}
        {panel === 'schedule' && <><p className="wide">{t.scheduleHelp}</p><label>{t.medicationId}<select name="medicationId" defaultValue={selected} required>{workspace.medications.map(m => <option key={m.id} value={m.id}>{m.name} — {workspace.people.find(p => p.id === m.personId)?.name}</option>)}</select></label>{input('dose')}{input('time', 'time', '09:00')}{input('start', 'date', today())}{input('end', 'date')}</>}
      </div><button disabled={busy}>{busy ? t.busy : t.save}</button></form></section>}
      {tab === 'today' && <section><div className="toolbar"><label>{t.date}<input type="date" value={date} onChange={e => setDate(e.target.value)} /></label>{workspace.medications.length > 0 && <button className="secondary" onClick={() => open('schedule')}>{t.schedule}</button>}</div>{!doses.length ? <p className="empty">{t.emptyToday}</p> : <div className="list">{doses.map(d => <article className="card dose" key={`${d.regimenVersionId}:${d.scheduledFor}`}><time>{new Date(d.scheduledFor).toLocaleTimeString(locale, { hour: '2-digit', minute: '2-digit' })}</time><div className="grow"><small>{d.personName}</small><h2>{d.medicationName}</h2><p>{quantity(d.doseNumerator, d.doseDenominator)} {t.tablets}</p></div>{d.status === 'due' ? <div className="actions">{(['taken', 'skipped'] as const).map(outcome => <button key={outcome} className={outcome === 'skipped' ? 'secondary' : ''} disabled={busy} onClick={() => run(async () => { await api(`/households/${householdId}/sync/administrations`, { idempotencyKey: `${d.regimenVersionId}:${d.scheduledFor}`, regimenVersionId: d.regimenVersionId, scheduledFor: d.scheduledFor, takenAt: new Date().toISOString(), outcome }); await refresh(); })}>{t[outcome]}</button>)}</div> : <span className="badge">{d.status === 'taken' ? t.done : t.skipDone}</span>}</article>)}</div>}</section>}
      {tab === 'medications' && (!workspace.medications.length ? <p className="empty">{t.emptyMedications}</p> : <section className="medicationGrid">{workspace.medications.map(m => <article className="card" key={m.id}><small>{workspace.people.find(p => p.id === m.personId)?.name}</small><h2>{m.name}</h2>{m.strength && <p><strong>{t.strengthLabel}:</strong> {m.strength}</p>}{m.activeIngredient && <p><strong>{t.ingredientLabel}:</strong> {m.activeIngredient}</p>}{m.notes && <p className="notes">{m.notes}</p>}<div className="stockNumber">{quantity(m.stockNumerator, m.stockDenominator)} <small>{t.tablets}</small></div><p>{t.remaining}</p>{workspace.regimens.filter(r => r.medicationId === m.id).map(r => <p key={r.id}>{t.perDay} · {r.localTime.slice(0, 5)} · {quantity(r.doseNumerator, r.doseDenominator)} {t.tablets}<br /><small>{r.validFrom}{r.validTo ? ` — ${r.validTo}` : ''}</small></p>)}<div className="actions wrap">{(['schedule', 'refill', 'count'] as const).map(action => <button key={action} className="secondary" onClick={() => open(action, m.id)}>{t[action]}</button>)}</div></article>)}</section>)}
      {tab === 'people' && <section><div className="toolbar"><button onClick={() => open('person')}>{t.person}</button></div>{!workspace.people.length ? <p className="empty">{t.emptyPeople}</p> : <div className="medicationGrid">{workspace.people.map(p => <article className="card" key={p.id}><h2>{p.name}</h2><p>{t.medications}: {workspace.medications.filter(m => m.personId === p.id).length}</p><button disabled={busy} onClick={() => open('medication', p.id)}>{t.medication}</button></article>)}</div>}</section>}
      {tab === 'history' && <section className="list">{!workspace.ledger.length ? <p className="empty">{t.emptyHistory}</p> : workspace.ledger.map(entry => <article className="card movement" key={entry.id}><div className="grow"><h3>{workspace.medications.find(m => m.inventoryItemId === entry.inventoryItemId)?.name}</h3><small>{entry.reason in t ? t[entry.reason as keyof typeof t] : entry.reason} · {new Date(entry.recordedAt).toLocaleString(locale)}</small></div><strong>{quantity(entry.quantityNumerator, entry.quantityDenominator)} {t.tablets}</strong></article>)}</section>}
      <footer>{t.safety}</footer>
    </>}
  </main>;
}
