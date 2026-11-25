import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { api, ApiError, errText, formatDate, nzDateTime, Perm } from '../api';
import { useSession } from '../App';

const errs = (e: unknown) => (e instanceof ApiError && e.body?.errors ? e.body.errors as string[] : [errText(e)]);
const num = (v: string) => (v.trim() === '' ? null : Number(v));
const ROUTES = ['', 'Oral', 'IV', 'IM', 'SC', 'PR', 'SL', 'Buccal', 'Topical', 'Inhaled', 'Nasal', 'Eye', 'Infiltration', 'Epidural', 'Intrathecal', 'Nerve block'];
const UNITS = ['mg', 'microgram', 'g', 'unit', 'mmol'];
const SEV = ['Stop', 'Warn', 'Info'];
export const sevClass = (s: string) => (s === 'Stop' ? 'red' : s === 'Warn' ? 'amber' : 'grey');
const RESULT: Record<string, [string, string]> = { Clear: ['green', 'checked – clear'], Warnings: ['amber', 'checked – warnings'], Overridden: ['red', 'given despite a stop'], Stopped: ['red', 'stopped'], NotChecked: ['grey', 'not checked'] };
export const ResultPill = ({ result, reason }: { result: string | null | undefined; reason?: string | null }) =>
  result ? <span className={`pill ${RESULT[result]?.[0] ?? ''}`} title={reason ?? undefined}>{RESULT[result]?.[1] ?? result}</span> : null;

/** What a medicine check found – shown while charting and after. */
export function Findings({ findings }: { findings: { severity: string; text: string; kind?: string }[] }) {
  if (findings.length === 0) return <p className="notice" role="status">No problems found.</p>;
  return (
    <ul className="plain med-findings">{findings.map((f, i) => (
      <li key={i} className={`med-finding sev-${f.severity.toLowerCase()}`}><span className={`pill ${sevClass(f.severity)}`}>{f.severity === 'Stop' ? 'Stop' : f.severity === 'Warn' ? 'Check' : 'Note'}</span> {f.text}</li>))}
    </ul>
  );
}

// ------------------------------------------------------------------ charting helper for the clinical record
export interface MedForm { medicine: string; medicineId: string | null; dose: string; route: string; barcode: string; weightKg: string; overrideReason: string }

/** Medicine name with suggestions from the medicines list, barcode lookup, and the live check while charting. */
export function MedicinePicker({ value, onChange, patientId, episodeId, givenAt }: { value: MedForm; onChange: (m: MedForm) => void; patientId: string; episodeId: string; givenAt: string | null }) {
  const [options, setOptions] = useState<any[]>([]);
  const [check, setCheck] = useState<any>(null);
  const [checking, setChecking] = useState(false);
  const [note, setNote] = useState<string | null>(null);
  const timer = useRef<number | undefined>(undefined);
  useEffect(() => {
    if (value.medicine.trim().length < 2) { setOptions([]); return; }
    const t = window.setTimeout(() => { api<any[]>(`/api/medicines/search?q=${encodeURIComponent(value.medicine.trim())}`).then(setOptions).catch(() => setOptions([])); }, 250);
    return () => window.clearTimeout(t);
  }, [value.medicine]);
  useEffect(() => {
    window.clearTimeout(timer.current);
    if (!value.medicine.trim()) { setCheck(null); return; }
    timer.current = window.setTimeout(() => {
      setChecking(true);
      api<any>('/api/medicines/check', { body: { patientId, episodeId, medicine: value.medicine, dose: value.dose, route: value.route, at: givenAt, barcode: value.barcode || null, medicineId: value.medicineId, weightKg: num(value.weightKg) } })
        .then(setCheck).catch(() => setCheck(null)).finally(() => setChecking(false));
    }, 600);
    return () => window.clearTimeout(timer.current);
  }, [value.medicine, value.dose, value.route, value.barcode, value.medicineId, value.weightKg, givenAt, patientId, episodeId]);
  const pick = (name: string) => {
    const o = options.find((x) => x.name === name);
    onChange({ ...value, medicine: name, medicineId: o?.id ?? null, route: o?.defaultRoute && !value.route ? o.defaultRoute : (o?.defaultRoute ?? value.route) });
  };
  const scan = async () => {
    if (!value.barcode.trim()) return;
    try { const m = await api<any>(`/api/medicines/barcode/${encodeURIComponent(value.barcode.trim())}`); onChange({ ...value, medicine: m.name, medicineId: m.id, route: m.defaultRoute ?? value.route }); setNote(`Barcode: ${m.name}`); }
    catch { setNote('This barcode is not in the medicines list – type the medicine.'); }
  };
  const stop = check?.findings?.some((f: any) => f.severity === 'Stop');
  return (
    <>
      <label>Scan barcode (optional)<input value={value.barcode} onChange={(e) => onChange({ ...value, barcode: e.target.value })} onBlur={() => void scan()} onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); void scan(); } }} autoComplete="off" /></label>
      <label>Medicine<input list="med-options" value={value.medicine} onChange={(e) => { const o = options.find((x) => x.name === e.target.value); if (o) pick(o.name); else onChange({ ...value, medicine: e.target.value, medicineId: null }); }} autoComplete="off" />
        <datalist id="med-options">{options.map((o) => <option key={o.id} value={o.name}>{o.ingredients}</option>)}</datalist></label>
      {note && <p className="hint">{note}</p>}
      {check && (
        <div className="med-check-box" aria-live="polite">
          <div className="row-between"><strong>Medicine check{checking ? ' …' : ''}</strong>
            <span className="small muted">{check.weightKg ? `Weight ${check.weightKg} kg (${check.weightSource})` : 'Weight not known'}{check.ruleSetVersion ? ` · rules v${check.ruleSetVersion}` : ''}{check.mode === 'Advise' ? ' · advice only' : ''}</span></div>
          {check.mode === 'Off' ? <p className="muted small">Medicine checks are switched off.</p> : <Findings findings={check.findings} />}
          {check.ingredients?.length > 0 && <p className="small">NZ Formulary: {check.ingredients.map((i: string, n: number) => <span key={i}>{n > 0 && ' · '}<a href={`/medicines?tab=nzf&q=${encodeURIComponent(i)}`} target="_blank" rel="noreferrer">{i}</a></span>)}</p>}
          <div className="row">
            <label className="small">Weight (kg) if different<input type="number" step="0.1" min="0" value={value.weightKg} onChange={(e) => onChange({ ...value, weightKg: e.target.value })} /></label>
            {stop && <label className="grow">Reason to give it anyway{check.mode === 'Enforce' ? ' (needed)' : ''}<input value={value.overrideReason} onChange={(e) => onChange({ ...value, overrideReason: e.target.value })} placeholder="e.g. tolerated before – allergy letter on file" /></label>}
          </div>
        </div>
      )}
    </>
  );
}

// ------------------------------------------------------------------ patient page panel
export function PatientMedicines({ patientId }: { patientId: string }) {
  const { can } = useSession();
  const [allergies, setAllergies] = useState<any[] | null>(null);
  const [checks, setChecks] = useState<any[]>([]);
  const [term, setTerm] = useState<Record<string, string>>({});
  const [options, setOptions] = useState<Record<string, any[]>>({});
  const [errors, setErrors] = useState<string[]>([]);
  const load = useCallback(async () => {
    setAllergies(await api<any[]>(`/api/medicines/patients/${patientId}/allergies`));
    setChecks(await api<any[]>(`/api/medicines/patients/${patientId}/checks`));
  }, [patientId]);
  useEffect(() => { load().catch((e) => setErrors(errs(e))); }, [load]);
  const find = async (alertId: string, q: string) => { setTerm({ ...term, [alertId]: q }); if (q.trim().length >= 2) setOptions({ ...options, [alertId]: await api<any[]>(`/api/medicines/allergens?q=${encodeURIComponent(q)}`) }); };
  const link = async (alertId: string, t: string) => { setErrors([]); try { await api(`/api/medicines/patients/${patientId}/allergies/${alertId}`, { method: 'PUT', body: { term: t } }); setTerm({ ...term, [alertId]: '' }); await load(); } catch (e) { setErrors(errs(e)); } };
  const unlink = async (alertId: string) => { try { await api(`/api/medicines/patients/${patientId}/allergies/${alertId}`, { method: 'DELETE' }); await load(); } catch (e) { setErrors(errs(e)); } };
  if (!allergies) return null;
  const notable = checks.filter((c) => c.result === 'Overridden' || c.result === 'Stopped').slice(0, 5);
  return (
    <section className="card stack">
      <h2>Medicine safety</h2>
      {errors.length > 0 && <div className="error" role="alert"><ul>{errors.map((e) => <li key={e}>{e}</li>)}</ul></div>}
      {allergies.length === 0 ? <p className="muted">No allergy alerts.</p> : (
        <table className="results compact"><thead><tr><th>Allergy alert</th><th>Checked as</th>{can(Perm.PatientAlerts) && <th>Link to a medicine or group</th>}</tr></thead>
          <tbody>{allergies.map((a) => (
            <tr key={a.alertId}>
              <td>{a.description}</td>
              <td>{a.term ? <><span className="pill blue">{a.term.startsWith('group:') ? a.term.slice(6) + ' group' : a.term}</span>{can(Perm.PatientAlerts) && <button className="btn btn-link" onClick={() => void unlink(a.alertId)}>Unlink</button>}</> : <span className="small muted">by the words in the alert</span>}</td>
              {can(Perm.PatientAlerts) && <td>
                <input aria-label={`Link ${a.description}`} list={`allergen-${a.alertId}`} value={term[a.alertId] ?? ''} placeholder="e.g. penicillins, amoxicillin" onChange={(e) => {
                  const o = (options[a.alertId] ?? []).find((x) => x.label === e.target.value);
                  if (o) void link(a.alertId, o.term); else void find(a.alertId, e.target.value);
                }} />
                <datalist id={`allergen-${a.alertId}`}>{(options[a.alertId] ?? []).map((o) => <option key={o.term} value={o.label} />)}</datalist>
              </td>}
            </tr>))}</tbody></table>
      )}
      <p className="hint">Linking an allergy means the medicine checks don't depend on how the alert is worded. Allergy groups (e.g. penicillins) also catch the rest of the group.</p>
      {notable.length > 0 && <>
        <h3>Recent stops and overrides</h3>
        <ul className="plain">{notable.map((c) => <li key={c.id}><ResultPill result={c.result} /> {c.medicine} {c.dose} {c.route} · {nzDateTime(c.checkedAt)} · {c.checkedBy}{c.overrideReason && <> – reason: {c.overrideReason}</>}</li>)}</ul>
      </>}
    </section>
  );
}

// ------------------------------------------------------------------ the Medicines page
const TABS: [string, string][] = [['list', 'Medicines list'], ['nzf', 'NZ Formulary'], ['import', 'Import'], ['names', 'Other names'], ['rules', 'Rules'], ['settings', 'Settings'], ['report', 'Report']];

export default function Medicines() {
  const { can } = useSession();
  const [params, setParams] = useSearchParams();
  const tab = params.get('tab') ?? 'list';
  const tabs = TABS.filter(([k]) => (k === 'import' ? can(Perm.MedicinesCatalogue) : k === 'report' ? can(Perm.MedicinesRules) || can(Perm.MedicinesApprove) : true));
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Medicines</h1>
        <p className="muted">TDS's medicines list and the medicine rules every charted medicine is checked against: allergies, dose limits, what has already been given and interactions. BrightPAS suggests; the clinician decides.</p></div>
      <div className="tabs" role="tablist">{tabs.map(([k, t]) => <button key={k} role="tab" aria-selected={tab === k} className={tab === k ? 'on' : ''} onClick={() => setParams({ tab: k })}>{t}</button>)}</div>
      {tab === 'list' && <MedicineList />}
      {tab === 'nzf' && <NzfTab initial={params.get('q') ?? ''} />}
      {tab === 'import' && can(Perm.MedicinesCatalogue) && <MedicineImport />}
      {tab === 'names' && <OtherNames />}
      {tab === 'rules' && <MedicineRules />}
      {tab === 'settings' && <MedicineSettings />}
      {tab === 'report' && <MedicineReport />}
    </div>
  );
}

const blankMed = { id: null as string | null, name: '', nzmtId: '', form: '', defaultRoute: '', onFormulary: true, isActive: true, ingredients: [{ ingredient: '', amount: '', unit: 'mg', perMl: '' }], gtins: '' };

function MedicineList() {
  const { can } = useSession();
  const [q, setQ] = useState('');
  const [formulary, setFormulary] = useState(true);
  const [inactive, setInactive] = useState(false);
  const [rows, setRows] = useState<any[] | null>(null);
  const [edit, setEdit] = useState<typeof blankMed | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const load = useCallback(() => api<any[]>(`/api/medicines/search?q=${encodeURIComponent(q)}&formulary=${formulary}&inactive=${inactive}`).then(setRows).catch((e) => setErrors(errs(e))), [q, formulary, inactive]);
  useEffect(() => { const t = window.setTimeout(() => void load(), 250); return () => window.clearTimeout(t); }, [load]);
  const open = async (id: string) => {
    const m = await api<any>(`/api/medicines/catalogue/${id}`);
    setEdit({ id: m.id, name: m.name, nzmtId: m.nzmtId ?? '', form: m.form ?? '', defaultRoute: m.defaultRoute ?? '', onFormulary: m.onFormulary, isActive: m.isActive,
      ingredients: m.ingredients.length ? m.ingredients.map((i: any) => ({ ingredient: i.ingredient, amount: i.amount ?? '', unit: i.unit ?? 'mg', perMl: i.perMl ?? '' })) : blankMed.ingredients, gtins: m.gtins.join(', ') });
  };
  const save = async () => {
    if (!edit) return;
    setErrors([]); setMsg(null);
    const body = { name: edit.name, nzmtId: edit.nzmtId || null, form: edit.form || null, defaultRoute: edit.defaultRoute || null, onFormulary: edit.onFormulary, isActive: edit.isActive,
      ingredients: edit.ingredients.filter((i) => i.ingredient.trim()).map((i) => ({ ingredient: i.ingredient, amount: num(String(i.amount)), unit: String(i.amount).trim() ? i.unit : null, perMl: num(String(i.perMl)) })),
      gtins: edit.gtins.split(/[\s,;]+/).filter(Boolean) };
    try { if (edit.id) await api(`/api/medicines/catalogue/${edit.id}`, { method: 'PUT', body }); else await api('/api/medicines/catalogue', { body }); setMsg('Saved.'); setEdit(null); await load(); }
    catch (e) { setErrors(errs(e)); }
  };
  const starter = async () => { setErrors([]); try { const r = await api<any>('/api/medicines/catalogue/starter', { body: {} }); setMsg(`${r.added} medicine(s) added from the starter list. Check the strengths match TDS's stock.`); await load(); } catch (e) { setErrors(errs(e)); } };
  const setIng = (i: number, p: any) => edit && setEdit({ ...edit, ingredients: edit.ingredients.map((x, j) => (j === i ? { ...x, ...p } : x)) });
  return (
    <div className="stack">
      {errors.length > 0 && <div className="error" role="alert"><ul>{errors.map((e) => <li key={e}>{e}</li>)}</ul></div>}
      {msg && <p className="notice" role="status">{msg}</p>}
      <section className="card stack">
        <div className="row">
          <label className="grow">Find<input value={q} onChange={(e) => setQ(e.target.value)} placeholder="name, ingredient, brand, NZMT id or barcode" /></label>
          <label className="check"><input type="checkbox" checked={formulary} onChange={(e) => setFormulary(e.target.checked)} /> TDS list only</label>
          <label className="check"><input type="checkbox" checked={inactive} onChange={(e) => setInactive(e.target.checked)} /> Include inactive</label>
          {can(Perm.MedicinesCatalogue) && <button className="btn btn-light" onClick={() => { setEdit({ ...blankMed, ingredients: [...blankMed.ingredients] }); setMsg(null); }}>Add a medicine</button>}
        </div>
        {!rows ? <p>Loading…</p> : rows.length === 0 ? (
          <div className="stack"><p className="muted">No medicines found.</p>{can(Perm.MedicinesCatalogue) && !q && <div><button className="btn btn-primary" onClick={() => void starter()}>Load the starter list</button> <span className="hint">About 45 common day-surgery medicines with strengths, plus brand and older names.</span></div>}</div>
        ) : (
          <table className="results compact"><thead><tr><th>Medicine</th><th>Ingredients</th><th>Route</th><th>From</th><th></th></tr></thead>
            <tbody>{rows.map((m) => (
              <tr key={m.id}><td><strong>{m.name}</strong>{!m.isActive && <span className="pill grey">inactive</span>}{m.onFormulary && <span className="pill blue">TDS list</span>}</td><td className="small">{m.ingredients}</td><td>{m.defaultRoute ?? '—'}</td><td className="small">{m.source}{m.nzmtId && ` · ${m.nzmtId}`}</td>
                <td>{can(Perm.MedicinesCatalogue) && <button className="btn btn-link" onClick={() => void open(m.id)}>Edit</button>}</td></tr>))}</tbody></table>
        )}
      </section>
      {edit && (
        <section className="card stack">
          <h2>{edit.id ? 'Edit medicine' : 'Add a medicine'}</h2>
          <div className="form-grid">
            <label>Name, strength and form<input value={edit.name} onChange={(e) => setEdit({ ...edit, name: e.target.value })} placeholder="Paracetamol 500 mg tablet" /></label>
            <label>Form<input value={edit.form} onChange={(e) => setEdit({ ...edit, form: e.target.value })} placeholder="tablet, injection…" /></label>
            <label>Usual route<select value={edit.defaultRoute} onChange={(e) => setEdit({ ...edit, defaultRoute: e.target.value })}>{ROUTES.map((r) => <option key={r} value={r}>{r || '—'}</option>)}</select></label>
            <label>NZMT id (optional)<input value={edit.nzmtId} onChange={(e) => setEdit({ ...edit, nzmtId: e.target.value })} /></label>
            <label>Barcodes (GTIN, comma separated)<input value={edit.gtins} onChange={(e) => setEdit({ ...edit, gtins: e.target.value })} /></label>
          </div>
          <div className="row"><label className="check"><input type="checkbox" checked={edit.onFormulary} onChange={(e) => setEdit({ ...edit, onFormulary: e.target.checked })} /> On the TDS list</label>
            <label className="check"><input type="checkbox" checked={edit.isActive} onChange={(e) => setEdit({ ...edit, isActive: e.target.checked })} /> Active</label></div>
          <h3>Ingredients and strength</h3>
          <p className="hint">Strength per tablet, capsule or vial – or for liquids the amount in "per mL" millilitres (10 mg/mL: 10 mg per 1 mL; 1 g in 100 mL: 1000 mg per 100 mL).</p>
          <table className="results compact"><thead><tr><th>Ingredient</th><th>Amount</th><th>Unit</th><th>Per mL</th><th></th></tr></thead>
            <tbody>{edit.ingredients.map((i, n) => (
              <tr key={n}><td><input aria-label="Ingredient" value={i.ingredient} onChange={(e) => setIng(n, { ingredient: e.target.value })} /></td>
                <td><input aria-label="Amount" type="number" step="any" value={i.amount} onChange={(e) => setIng(n, { amount: e.target.value })} /></td>
                <td><select aria-label="Unit" value={i.unit} onChange={(e) => setIng(n, { unit: e.target.value })}>{UNITS.map((u) => <option key={u}>{u}</option>)}</select></td>
                <td><input aria-label="Per mL" type="number" step="any" value={i.perMl} onChange={(e) => setIng(n, { perMl: e.target.value })} placeholder="solid" /></td>
                <td>{edit.ingredients.length > 1 && <button className="btn btn-link danger" onClick={() => setEdit({ ...edit, ingredients: edit.ingredients.filter((_, j) => j !== n) })}>Remove</button>}</td></tr>))}</tbody></table>
          <div><button className="btn btn-light btn-small" onClick={() => setEdit({ ...edit, ingredients: [...edit.ingredients, { ingredient: '', amount: '', unit: 'mg', perMl: '' }] })}>Add an ingredient</button></div>
          <div className="row"><button className="btn btn-primary" onClick={() => void save()}>Save medicine</button><button className="btn btn-light" onClick={() => setEdit(null)}>Cancel</button></div>
        </section>
      )}
    </div>
  );
}

const FIELDS: [string, string][] = [['nameColumn', 'Medicine name (needed)'], ['nzmtColumn', 'NZMT id'], ['formColumn', 'Form'], ['routeColumn', 'Route'], ['ingredientColumn', 'Ingredient'], ['amountColumn', 'Strength / amount'], ['unitColumn', 'Unit'], ['perMlColumn', 'Per mL'], ['gtinColumn', 'Barcode (GTIN)']];

function MedicineImport() {
  const [profiles, setProfiles] = useState<any[]>([]);
  const [runs, setRuns] = useState<any[]>([]);
  const [file, setFile] = useState<{ name: string; text: string } | null>(null);
  const [peek, setPeek] = useState<any>(null);
  const [map, setMap] = useState<any>({ name: 'NZULM monthly', source: 'NZULM', deactivateMissing: true, saveProfile: true });
  const [busy, setBusy] = useState(false);
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const load = () => { api<any[]>('/api/medicines/import/profiles').then(setProfiles).catch(() => undefined); api<any[]>('/api/medicines/import/runs').then(setRuns).catch(() => undefined); };
  useEffect(load, []);
  const choose = async (f: File | undefined) => {
    setErrors([]); setMsg(null); setPeek(null);
    if (!f) return;
    const text = await f.text();
    setFile({ name: f.name, text });
    try { setPeek(await api<any>('/api/medicines/import/peek', { body: { text: text.slice(0, 200_000) } })); } catch (e) { setErrors(errs(e)); }
  };
  const useProfile = (id: string) => { const p = profiles.find((x) => x.id === id); if (p) setMap({ ...p, saveProfile: true }); };
  const run = async () => {
    if (!file) return;
    setBusy(true); setErrors([]); setMsg(null);
    try {
      const r = await api<any>('/api/medicines/import', { body: { fileName: file.name, text: file.text, mapping: { ...map, nzmtColumn: map.nzmtColumn || null, formColumn: map.formColumn || null, routeColumn: map.routeColumn || null, ingredientColumn: map.ingredientColumn || null, amountColumn: map.amountColumn || null, unitColumn: map.unitColumn || null, perMlColumn: map.perMlColumn || null, gtinColumn: map.gtinColumn || null } } });
      setMsg(`${r.rows} rows: ${r.added} added, ${r.updated} updated, ${r.deactivated} made inactive, ${r.skipped} skipped.${r.problems ? ' Some lines need a look – see below.' : ''}`); load();
    } catch (e) { setErrors(errs(e)); } finally { setBusy(false); }
  };
  return (
    <div className="stack">
      {errors.length > 0 && <div className="error" role="alert"><ul>{errors.map((e) => <li key={e}>{e}</li>)}</ul></div>}
      {msg && <p className="notice" role="status">{msg}</p>}
      <section className="card stack">
        <h2>Import a medicines file</h2>
        <p className="hint">The NZ Universal List of Medicines (NZULM) is free: ask NZULM for the monthly files, then import them here each month with a saved column layout. TDS's own list can be imported the same way (Source: TDS). CSV, tab or pipe separated, first row headings.</p>
        <label>File<input type="file" accept=".csv,.txt,.tsv" onChange={(e) => void choose(e.target.files?.[0])} /></label>
        {peek && <>
          <div className="form-grid">
            {profiles.length > 0 && <label>Use a saved layout<select value="" onChange={(e) => useProfile(e.target.value)}><option value="">Choose…</option>{profiles.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}</select></label>}
            <label>Source<select value={map.source} onChange={(e) => setMap({ ...map, source: e.target.value })}><option value="NZULM">NZULM</option><option value="TDS">TDS's own list (added to the TDS list)</option></select></label>
            <label>Layout name<input value={map.name} onChange={(e) => setMap({ ...map, name: e.target.value })} /></label>
            {FIELDS.map(([k, t]) => <label key={k}>{t}<select value={map[k] ?? ''} onChange={(e) => setMap({ ...map, [k]: e.target.value })}><option value="">—</option>{peek.headers.map((h: string) => <option key={h}>{h}</option>)}</select></label>)}
          </div>
          <div className="row"><label className="check"><input type="checkbox" checked={map.deactivateMissing} onChange={(e) => setMap({ ...map, deactivateMissing: e.target.checked })} /> Make medicines from this source that are not in the file inactive (full monthly file)</label>
            <label className="check"><input type="checkbox" checked={map.saveProfile} onChange={(e) => setMap({ ...map, saveProfile: e.target.checked })} /> Save this layout</label></div>
          <div className="table-scroll" tabIndex={0} role="region" aria-label="First rows of the file"><table className="results compact"><thead><tr>{peek.headers.map((h: string) => <th key={h}>{h}</th>)}</tr></thead>
            <tbody>{peek.sample.map((r: string[], i: number) => <tr key={i}>{r.map((c, j) => <td key={j} className="small">{c}</td>)}</tr>)}</tbody></table></div>
          <div><button className="btn btn-primary" disabled={busy || !map.nameColumn} onClick={() => void run()}>{busy ? 'Importing…' : `Import ${file?.name}`}</button></div>
        </>}
      </section>
      <section className="card"><h2>Imports</h2>
        {runs.length === 0 ? <p className="muted">None yet.</p> : <table className="results compact"><thead><tr><th>When</th><th>File</th><th>Source</th><th>Rows</th><th>Added</th><th>Updated</th><th>Inactive</th><th>Notes</th></tr></thead>
          <tbody>{runs.map((r) => <tr key={r.id}><td>{nzDateTime(r.importedAt)}</td><td>{r.fileName}</td><td>{r.source}</td><td>{r.rows}</td><td>{r.added}</td><td>{r.updated}</td><td>{r.deactivated}</td><td className="small">{r.problems ? r.problems.split('\n').slice(0, 3).join(' · ') : '—'}</td></tr>)}</tbody></table>}
      </section>
    </div>
  );
}

function OtherNames() {
  const { can } = useSession();
  const [rows, setRows] = useState<any[]>([]);
  const [f, setF] = useState({ ingredient: '', alias: '' });
  const [filter, setFilter] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const load = () => api<any[]>('/api/medicines/aliases').then(setRows).catch((e) => setErrors(errs(e)));
  useEffect(() => { void load(); }, []);
  const add = async () => { setErrors([]); try { await api('/api/medicines/aliases', { body: f }); setF({ ingredient: f.ingredient, alias: '' }); await load(); } catch (e) { setErrors(errs(e)); } };
  const remove = async (alias: string) => { try { await api(`/api/medicines/aliases/${encodeURIComponent(alias)}`, { method: 'DELETE' }); await load(); } catch (e) { setErrors(errs(e)); } };
  const shown = rows.filter((r) => !filter || r.ingredient.includes(filter.toLowerCase()) || r.alias.includes(filter.toLowerCase()));
  return (
    <section className="card stack">
      <p className="hint">Brand, older and overseas names (Panadol, lignocaine, acetaminophen) so allergy alerts and the patient's own medicines list are read correctly.</p>
      {errors.length > 0 && <div className="error" role="alert"><ul>{errors.map((e) => <li key={e}>{e}</li>)}</ul></div>}
      {can(Perm.MedicinesCatalogue) && <div className="row"><label>Ingredient<input value={f.ingredient} onChange={(e) => setF({ ...f, ingredient: e.target.value })} /></label><label>Other name<input value={f.alias} onChange={(e) => setF({ ...f, alias: e.target.value })} /></label><button className="btn btn-light" disabled={!f.ingredient || !f.alias} onClick={() => void add()}>Add</button></div>}
      <label>Find<input value={filter} onChange={(e) => setFilter(e.target.value)} /></label>
      <table className="results compact"><thead><tr><th>Ingredient</th><th>Other name</th>{can(Perm.MedicinesCatalogue) && <th></th>}</tr></thead>
        <tbody>{shown.slice(0, 300).map((r) => <tr key={r.alias}><td>{r.ingredient}</td><td>{r.alias}</td>{can(Perm.MedicinesCatalogue) && <td><button className="btn btn-link danger" onClick={() => void remove(r.alias)}>Remove</button></td>}</tr>)}</tbody></table>
    </section>
  );
}

const csv = (a: string[] | undefined) => (a ?? []).join(', ');
const list = (s: string) => s.split(',').map((x) => x.trim()).filter(Boolean);

function MedicineRules() {
  const { can } = useSession();
  const canRules = can(Perm.MedicinesRules), canApprove = can(Perm.MedicinesApprove);
  const [sets, setSets] = useState<any[]>([]);
  const [sel, setSel] = useState<any>(null);
  const [part, setPart] = useState('dose');
  const [note, setNote] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const loadSets = useCallback(async () => { const s = await api<any[]>('/api/medicines/rules'); setSets(s); return s; }, []);
  const open = async (id: string) => {
    const r = await api<any>(`/api/medicines/rules/${id}`);
    setSel({ ...r, groups: r.groups.map((g: any) => ({ ...g, membersText: csv(g.members), keywordsText: csv(g.keywords) })) });
  };
  useEffect(() => { void loadSets().then((s) => { const pick = s.find((x) => x.status === 'Draft') ?? s.find((x) => x.status === 'Approved'); if (pick) void open(pick.id); }); }, [loadSets]);
  const run = async (f: () => Promise<unknown>, ok: string) => { setErrors([]); setMsg(null); try { await f(); setMsg(ok); return true; } catch (e) { setErrors(errs(e)); return false; } };
  const editable = sel?.status === 'Draft' && canRules;
  const upd = (key: string, i: number, p: any) => setSel({ ...sel, [key]: sel[key].map((r: any, j: number) => (j === i ? { ...r, ...p } : r)) });
  const del = (key: string, i: number) => setSel({ ...sel, [key]: sel[key].filter((_: any, j: number) => j !== i) });
  const save = () => run(() => api(`/api/medicines/rules/${sel.id}`, { method: 'PUT', body: {
    name: sel.name, notes: sel.notes, doseRules: sel.doseRules, crossReactions: sel.crossReactions, interactions: sel.interactions,
    groups: sel.groups.map((g: any) => ({ code: g.code, name: g.name, allergyClass: g.allergyClass, sameGroupSeverity: g.sameGroupSeverity, members: list(g.membersText), keywords: list(g.keywordsText) })),
  } }), 'Draft saved.');
  const t = (key: string, i: number, f: string, v: any, label: string, type = 'text') => <input aria-label={label} type={type} step={type === 'number' ? 'any' : undefined} disabled={!editable} value={v ?? ''} onChange={(e) => upd(key, i, { [f]: type === 'number' ? num(e.target.value) : e.target.value })} />;
  const sev = (key: string, i: number, f: string, v: string, opts = SEV) => <select aria-label="Severity" disabled={!editable} value={v} onChange={(e) => upd(key, i, { [f]: e.target.value })}>{opts.map((s) => <option key={s}>{s}</option>)}</select>;
  return (
    <div className="stack">
      {errors.length > 0 && <div className="error" role="alert"><ul>{errors.map((e) => <li key={e}>{e}</li>)}</ul></div>}
      {msg && <p className="notice" role="status">{msg}</p>}
      <section className="card stack">
        {sets.length === 0 ? <><p className="muted">No medicine rules yet – only allergies to the same medicine are checked.</p>
          {canRules && <div><button className="btn btn-primary" onClick={() => void run(async () => { await api('/api/medicines/rules/example', { body: {} }); const s = await loadSets(); await open(s[0].id); }, 'Example rules added as a draft – check every line against current NZ references before approval.')}>Start from the example rules</button></div>}</> : (
          <table className="results compact"><thead><tr><th>Version</th><th>Name</th><th>Status</th><th>Approved</th><th></th></tr></thead>
            <tbody>{sets.map((s) => <tr key={s.id}><td>v{s.version}</td><td>{s.name}</td><td><span className={`pill ${s.status === 'Approved' ? 'green' : s.status === 'Draft' ? 'amber' : 'grey'}`}>{s.status}</span></td>
              <td className="small">{s.approvedBy ? `${s.approvedBy} (${s.approvedRole}) ${nzDateTime(s.approvedAt)} – ${s.approvalNote}` : '—'}</td>
              <td><div className="row"><button className="btn btn-link" onClick={() => void open(s.id)}>Open</button>{canRules && s.status !== 'Draft' && <button className="btn btn-link" onClick={() => void run(async () => { const r = await api<any>(`/api/medicines/rules/${s.id}/copy`, { body: {} }); await loadSets(); await open(r.id); }, 'New draft made.')}>New draft from this</button>}</div></td></tr>)}</tbody></table>
        )}
      </section>
      {sel && (
        <section className="card stack">
          <h2>v{sel.version} – {sel.status}{sel.isExample && ' (example rules)'}</h2>
          {sel.isExample && sel.status === 'Draft' && <p className="warn-box">Example rules, not clinically approved: check every dose limit, group and interaction against the NZ Formulary and data sheets before approving.</p>}
          <div className="form-grid">
            <label>Name<input disabled={!editable} value={sel.name} onChange={(e) => setSel({ ...sel, name: e.target.value })} /></label>
            <label>Notes<input disabled={!editable} value={sel.notes ?? ''} onChange={(e) => setSel({ ...sel, notes: e.target.value })} /></label>
          </div>
          <div className="tabs" role="tablist">{[['dose', `Dose limits (${sel.doseRules.length})`], ['groups', `Groups (${sel.groups.length})`], ['cross', `Cross-reactions (${sel.crossReactions.length})`], ['inter', `Interactions (${sel.interactions.length})`]].map(([k, l]) =>
            <button key={k} role="tab" aria-selected={part === k} className={part === k ? 'on' : ''} onClick={() => setPart(k)}>{l}</button>)}</div>
          {part === 'dose' && <>
            <p className="hint">Limits per dose, per 24 hours (all doses of the ingredient, every product), per kg, and the shortest time between doses. Ages: from (inclusive) to under (exclusive) – adults are 16 and over.</p>
            <div className="table-scroll" tabIndex={0} role="region" aria-label="Dose limits"><table className="results compact">
              <thead><tr><th>Code</th><th>Ingredient</th><th>Route</th><th>Age from</th><th>Age under</th><th>kg from</th><th>kg under</th><th>Unit</th><th>Max dose</th><th>Max 24 h</th><th>Max dose /kg</th><th>Max 24 h /kg</th><th>Hours apart</th><th>If over</th><th>Note</th>{editable && <th></th>}</tr></thead>
              <tbody>{sel.doseRules.map((r: any, i: number) => (
                <tr key={i}><td>{t('doseRules', i, 'code', r.code, 'Code')}</td><td>{t('doseRules', i, 'ingredient', r.ingredient, 'Ingredient')}</td>
                  <td><select aria-label="Route" disabled={!editable} value={r.route ?? ''} onChange={(e) => upd('doseRules', i, { route: e.target.value || null })}>{ROUTES.map((x) => <option key={x} value={x}>{x || 'Any'}</option>)}</select></td>
                  <td>{t('doseRules', i, 'minAgeYears', r.minAgeYears, 'Age from', 'number')}</td><td>{t('doseRules', i, 'maxAgeYears', r.maxAgeYears, 'Age under', 'number')}</td>
                  <td>{t('doseRules', i, 'minWeightKg', r.minWeightKg, 'Weight from', 'number')}</td><td>{t('doseRules', i, 'maxWeightKg', r.maxWeightKg, 'Weight under', 'number')}</td>
                  <td><select aria-label="Unit" disabled={!editable} value={r.unit} onChange={(e) => upd('doseRules', i, { unit: e.target.value })}>{UNITS.map((u) => <option key={u}>{u}</option>)}</select></td>
                  <td>{t('doseRules', i, 'maxSingle', r.maxSingle, 'Max dose', 'number')}</td><td>{t('doseRules', i, 'maxDaily', r.maxDaily, 'Max 24 hours', 'number')}</td>
                  <td>{t('doseRules', i, 'maxSinglePerKg', r.maxSinglePerKg, 'Max dose per kg', 'number')}</td><td>{t('doseRules', i, 'maxDailyPerKg', r.maxDailyPerKg, 'Max 24 hours per kg', 'number')}</td>
                  <td>{t('doseRules', i, 'minIntervalHours', r.minIntervalHours, 'Hours apart', 'number')}</td><td>{sev('doseRules', i, 'severity', r.severity, ['Stop', 'Warn'])}</td>
                  <td>{t('doseRules', i, 'note', r.note, 'Note')}</td>{editable && <td><button className="btn btn-link danger" onClick={() => del('doseRules', i)}>Remove</button></td>}</tr>))}</tbody>
            </table></div>
            {editable && <NzfSuggest ruleSetId={sel.id} onAdd={(rows) => setSel({ ...sel, doseRules: [...sel.doseRules.filter((d: any) => !rows.some((x) => x.code === d.code)), ...rows] })} />}
            {editable && <div><button className="btn btn-light btn-small" onClick={() => setSel({ ...sel, doseRules: [...sel.doseRules, { code: '', ingredient: '', route: null, minAgeYears: 16, maxAgeYears: null, minWeightKg: null, maxWeightKg: null, unit: 'mg', maxSingle: null, maxDaily: null, maxSinglePerKg: null, maxDailyPerKg: null, minIntervalHours: null, severity: 'Stop', reference: '', note: '' }] })}>Add a dose limit</button></div>}
          </>}
          {part === 'groups' && <>
            <p className="hint">Allergy classes (penicillins) and classes used in interactions (QT-prolonging). Members are ingredient names; keywords are extra words that mean the group in an allergy alert ("sulpha").</p>
            <div className="table-scroll" tabIndex={0} role="region" aria-label="Groups"><table className="results compact">
              <thead><tr><th>Code</th><th>Name</th><th>Allergy class</th><th>Another member</th><th>Members (comma separated)</th><th>Keywords</th>{editable && <th></th>}</tr></thead>
              <tbody>{sel.groups.map((g: any, i: number) => (
                <tr key={i}><td>{t('groups', i, 'code', g.code, 'Group code')}</td><td>{t('groups', i, 'name', g.name, 'Group name')}</td>
                  <td><input aria-label="Allergy class" type="checkbox" disabled={!editable} checked={g.allergyClass} onChange={(e) => upd('groups', i, { allergyClass: e.target.checked })} /></td>
                  <td>{sev('groups', i, 'sameGroupSeverity', g.sameGroupSeverity)}</td>
                  <td><textarea aria-label="Members" rows={2} disabled={!editable} value={g.membersText} onChange={(e) => upd('groups', i, { membersText: e.target.value })} /></td>
                  <td><input aria-label="Keywords" disabled={!editable} value={g.keywordsText} onChange={(e) => upd('groups', i, { keywordsText: e.target.value })} /></td>
                  {editable && <td><button className="btn btn-link danger" onClick={() => del('groups', i)}>Remove</button></td>}</tr>))}</tbody>
            </table></div>
            {editable && <div><button className="btn btn-light btn-small" onClick={() => setSel({ ...sel, groups: [...sel.groups, { code: '', name: '', allergyClass: true, sameGroupSeverity: 'Stop', membersText: '', keywordsText: '' }] })}>Add a group</button></div>}
          </>}
          {part === 'cross' && <>
            <p className="hint">An allergy to the first group that may also apply to the second.</p>
            <table className="results compact"><thead><tr><th>Allergy to</th><th>May react to</th><th>How serious</th><th>Message</th>{editable && <th></th>}</tr></thead>
              <tbody>{sel.crossReactions.map((x: any, i: number) => (
                <tr key={i}><td>{t('crossReactions', i, 'fromGroup', x.fromGroup, 'Allergy group')}</td><td>{t('crossReactions', i, 'toGroup', x.toGroup, 'May react to group')}</td><td>{sev('crossReactions', i, 'severity', x.severity)}</td>
                  <td>{t('crossReactions', i, 'message', x.message, 'Message')}</td>{editable && <td><button className="btn btn-link danger" onClick={() => del('crossReactions', i)}>Remove</button></td>}</tr>))}</tbody></table>
            {editable && <div><button className="btn btn-light btn-small" onClick={() => setSel({ ...sel, crossReactions: [...sel.crossReactions, { fromGroup: '', toGroup: '', severity: 'Warn', message: '' }] })}>Add a cross-reaction</button></div>}
          </>}
          {part === 'inter' && <>
            <p className="hint">Each side is an ingredient or group:CODE. The same group on both sides means any two different members. Checked against medicines given in the last 24 hours and the patient's own medicines list.</p>
            <div className="table-scroll" tabIndex={0} role="region" aria-label="Interactions"><table className="results compact">
              <thead><tr><th>Code</th><th>Medicine</th><th>With</th><th>How serious</th><th>Effect</th><th>Advice</th>{editable && <th></th>}</tr></thead>
              <tbody>{sel.interactions.map((x: any, i: number) => (
                <tr key={i}><td>{t('interactions', i, 'code', x.code, 'Interaction code')}</td><td>{t('interactions', i, 'termA', x.termA, 'Medicine')}</td><td>{t('interactions', i, 'termB', x.termB, 'With')}</td>
                  <td>{sev('interactions', i, 'severity', x.severity)}</td><td>{t('interactions', i, 'effect', x.effect, 'Effect')}</td><td>{t('interactions', i, 'advice', x.advice, 'Advice')}</td>
                  {editable && <td><button className="btn btn-link danger" onClick={() => del('interactions', i)}>Remove</button></td>}</tr>))}</tbody>
            </table></div>
            {editable && <div><button className="btn btn-light btn-small" onClick={() => setSel({ ...sel, interactions: [...sel.interactions, { code: '', termA: '', termB: '', severity: 'Warn', effect: '', advice: '', reference: '' }] })}>Add an interaction</button></div>}
          </>}
          {editable && <div className="row"><button className="btn btn-primary" onClick={() => void save()}>Save draft</button>
            <button className="btn btn-link danger" onClick={() => void run(async () => { await api(`/api/medicines/rules/${sel.id}`, { method: 'DELETE' }); setSel(null); await loadSets(); }, 'Draft deleted.')}>Delete draft</button></div>}
          {sel.status === 'Draft' && canApprove && (
            <div className="card subtle stack">
              <h3>Clinical approval</h3>
              <p className="hint">Approving makes this version the one every charted medicine is checked against and retires the current one. Save any changes first.</p>
              <label>Approval record (who checked the rules, against which references, and where it was agreed)<input value={note} onChange={(e) => setNote(e.target.value)} /></label>
              <div><button className="btn btn-warn" disabled={!note.trim()} onClick={() => void run(async () => { await api(`/api/medicines/rules/${sel.id}/approve`, { body: { note } }); await loadSets(); await open(sel.id); setNote(''); }, 'Rules approved.')}>Approve version {sel.version}</button></div>
            </div>
          )}
        </section>
      )}
    </div>
  );
}

function MedicineSettings() {
  const { can } = useSession();
  const [s, setS] = useState<any>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  useEffect(() => { api('/api/medicines/settings').then(setS).catch((e) => setErrors(errs(e))); }, []);
  if (!s) return <p>{errors.join(' ') || 'Loading…'}</p>;
  const edit = can(Perm.MedicinesApprove);
  const save = async () => { setErrors([]); setMsg(null); try { await api('/api/medicines/settings', { method: 'PUT', body: { mode: s.mode, checkRegularMedicines: s.checkRegularMedicines, duplicateWithinMinutes: Number(s.duplicateWithinMinutes) } }); setMsg('Settings saved.'); } catch (e) { setErrors(errs(e)); } };
  return (
    <section className="card stack">
      {errors.length > 0 && <div className="error" role="alert"><ul>{errors.map((e) => <li key={e}>{e}</li>)}</ul></div>}
      {msg && <p className="notice" role="status">{msg}</p>}
      <p>{s.approvedVersion ? <>Rules in use: <strong>version {s.approvedVersion}</strong>, approved {nzDateTime(s.approvedAt)}.</> : <strong>No approved rules yet – only allergies to the same medicine are checked.</strong>}</p>
      <fieldset className="stack"><legend>How strict</legend>
        {[['Advise', 'Advise – show what the checks find, never block'], ['Enforce', 'Enforce – a "Stop" needs a reason before the medicine can be recorded'], ['Off', 'Off – no checks']].map(([v, l]) =>
          <label key={v} className="check"><input type="radio" name="mode" disabled={!edit} checked={s.mode === v} onChange={() => setS({ ...s, mode: v })} /> {l}</label>)}
      </fieldset>
      <label className="check"><input type="checkbox" disabled={!edit} checked={s.checkRegularMedicines} onChange={(e) => setS({ ...s, checkRegularMedicines: e.target.checked })} /> Check interactions with the medicines the patient listed in the questionnaire</label>
      <label>Same medicine again within (minutes) is flagged as a possible double dose<input type="number" min="0" max="1440" disabled={!edit} value={s.duplicateWithinMinutes} onChange={(e) => setS({ ...s, duplicateWithinMinutes: e.target.value })} /></label>
      {s.updatedBy && <p className="small muted">Last changed by {s.updatedBy}, {nzDateTime(s.updatedAt)}.</p>}
      {edit && <div><button className="btn btn-primary" onClick={() => void save()}>Save settings</button></div>}
    </section>
  );
}

function MedicineReport() {
  const [r, setR] = useState<any>(null);
  const [errors, setErrors] = useState<string[]>([]);
  useEffect(() => { api('/api/medicines/report').then(setR).catch((e) => setErrors(errs(e))); }, []);
  if (!r) return <p>{errors.join(' ') || 'Loading…'}</p>;
  const n = (k: string) => r.byResult[k] ?? 0;
  return (
    <div className="stack">
      <section className="card stack">
        <h2>{formatDate(r.from)} to {formatDate(r.to)}</h2>
        <p>{n('Clear') + n('Warnings') + n('Overridden') + n('Stopped') + n('NotChecked')} medicines checked · {n('Clear')} clear · {n('Warnings')} with warnings · {n('Stopped')} stopped · {n('Overridden')} given despite a stop.</p>
        <p className="small muted">Warnings and stops by kind: {Object.entries(r.byKind).map(([k, v]) => `${k} ${v}`).join(' · ') || 'none'}.</p>
      </section>
      <section className="card"><h2>Stops and overrides</h2>
        {r.notable.length === 0 ? <p className="muted">None.</p> : (
          <table className="results compact"><thead><tr><th>When</th><th>Patient</th><th>Medicine</th><th>Result</th><th>Why it stopped</th><th>Reason given</th></tr></thead>
            <tbody>{r.notable.map((c: any) => <tr key={c.id}><td>{nzDateTime(c.checkedAt)}<div className="small muted">{c.checkedBy}</div></td><td><Link to={`/patients/${c.patientId}`}>{c.patient ?? 'patient'}</Link><div className="small muted">{c.nhi ?? ''}</div></td>
              <td>{c.medicine} {c.dose} {c.route}</td><td><ResultPill result={c.result} /></td><td className="small">{c.findings.join(' ')}</td><td className="small">{c.overrideReason ?? '—'}</td></tr>)}</tbody></table>
        )}
      </section>
    </div>
  );
}

// ------------------------------------------------------------------ NZ Formulary
function NzfSuggest({ ruleSetId, onAdd }: { ruleSetId: string; onAdd: (rows: any[]) => void }) {
  const [rows, setRows] = useState<any[] | null>(null);
  const [picked, setPicked] = useState<Record<string, boolean>>({});
  const [children, setChildren] = useState(false);
  const [busy, setBusy] = useState(false);
  const [errors, setErrors] = useState<string[]>([]);
  const load = async () => {
    setBusy(true); setErrors([]);
    try { const r = await api<any>(`/api/medicines/rules/${ruleSetId}/suggest-nzf`, { body: { ingredients: null, children } }); setRows(r.suggestions); setPicked(Object.fromEntries(r.suggestions.filter((x: any) => !x.existing).map((x: any) => [x.rule.code, true]))); }
    catch (e) { setErrors(errs(e)); } finally { setBusy(false); }
  };
  return (
    <div className="card subtle stack">
      <div className="row"><strong>Suggest dose limits from the NZ Formulary</strong>
        <label className="check"><input type="checkbox" checked={children} onChange={(e) => setChildren(e.target.checked)} /> include children (NZFC)</label>
        <button className="btn btn-light btn-small" disabled={busy} onClick={() => void load()}>{busy ? 'Reading…' : 'Show suggestions for the TDS list'}</button></div>
      <p className="hint">For each medicine on the TDS list: the highest limit the current NZF gives for each route and age or weight band (across its indications). Tick the ones to add, then check each against the NZF text shown, and Save draft.</p>
      {errors.length > 0 && <div className="error" role="alert"><ul>{errors.map((e) => <li key={e}>{e}</li>)}</ul></div>}
      {rows && (rows.length === 0 ? <p className="muted">No suggestions – import the NZF first (NZ Formulary tab), or the TDS list is empty.</p> : <>
        <div className="table-scroll" tabIndex={0} role="region" aria-label="NZ Formulary suggestions"><table className="results compact">
          <thead><tr><th>Add</th><th>Code</th><th>Ingredient</th><th>Route</th><th>Ages</th><th>Weight</th><th>Max dose</th><th>Max 24 h</th><th>/kg</th><th>/kg 24 h</th><th>Hours apart</th><th>NZF says</th></tr></thead>
          <tbody>{rows.map((x) => { const r = x.rule; return (
            <tr key={r.code}><td><input type="checkbox" aria-label={`Add ${r.code}`} checked={!!picked[r.code]} onChange={(e) => setPicked({ ...picked, [r.code]: e.target.checked })} /></td>
              <td>{r.code}{x.existing && <div className="small muted">TDS has rules for this</div>}</td><td>{r.ingredient}</td><td>{r.route}</td><td>{r.minAgeYears ?? ''}–{r.maxAgeYears ? `<${r.maxAgeYears}` : ''}</td>
              <td>{r.minWeightKg ?? ''}{(r.minWeightKg || r.maxWeightKg) ? '–' : ''}{r.maxWeightKg ? `<${r.maxWeightKg}` : ''}</td>
              <td>{r.maxSingle ?? ''} {r.maxSingle ? r.unit : ''}</td><td>{r.maxDaily ?? ''} {r.maxDaily ? r.unit : ''}</td><td>{r.maxSinglePerKg ?? ''}</td><td>{r.maxDailyPerKg ?? ''}</td><td>{r.minIntervalHours ?? ''}</td>
              <td className="small">{x.source}</td></tr>); })}</tbody></table></div>
        <div><button className="btn btn-primary btn-small" onClick={() => { onAdd(rows.filter((x) => picked[x.rule.code]).map((x) => x.rule)); setRows(null); }}>Add ticked to the draft</button></div>
      </>)}
    </div>
  );
}

function NzfTab({ initial }: { initial: string }) {
  const { can } = useSession();
  const [info, setInfo] = useState<any>(null);
  const [q, setQ] = useState(initial);
  const [list, setList] = useState<any[]>([]);
  const [mono, setMono] = useState<any>(null);
  const [busy, setBusy] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const load = useCallback(() => api<any>('/api/medicines/nzf').then(setInfo).catch((e) => setErrors(errs(e))), []);
  useEffect(() => { void load(); }, [load]);
  useEffect(() => {
    const t = window.setTimeout(() => {
      api<any[]>(`/api/medicines/nzf/monographs?q=${encodeURIComponent(q)}`).then(async (r) => {
        setList(r);
        const exact = q.trim() ? (await api<any[]>(`/api/medicines/nzf/lookup?ingredient=${encodeURIComponent(q.trim())}`).catch(() => []))[0] : null;
        if (exact && !mono) setMono(await api(`/api/medicines/nzf/monographs/${exact.id}`));
      }).catch(() => setList([]));
    }, 300);
    return () => window.clearTimeout(t);
  }, [q]); // eslint-disable-line react-hooks/exhaustive-deps
  const upload = async (f: File | undefined) => {
    if (!f) return;
    setErrors([]); setMsg(null); setBusy(`Reading ${f.name} – this takes about a minute…`);
    try {
      const res = await fetch('/api/medicines/nzf/import', { method: 'POST', credentials: 'same-origin', headers: { 'X-BrightPAS-Request': '1', 'X-File-Name': encodeURIComponent(f.name), 'Content-Type': 'application/pdf' }, body: f });
      const body = await res.json().catch(() => null);
      if (!res.ok) setErrors(body?.errors ?? [`The server answered ${res.status}.`]);
      else setMsg(body.alreadyImported ? `${body.edition} release ${body.release} is already the current one.` : `${body.edition} release ${body.release} (${formatDate(body.releaseDate)}): ${body.monographs} monographs, ${body.doses} dosing lines, ${body.preparations} preparations; ${body.ingredientsAdded} ingredients and ${body.aliasesAdded} other names added to the medicines list.`);
      await load();
    } catch { setErrors(['Could not reach BrightPAS – check the network and try again.']); } finally { setBusy(''); }
  };
  const saveSettings = async () => { setErrors([]); setMsg(null); try { await api('/api/medicines/nzf/settings', { method: 'PUT', body: { nzfUrl: info.settings.nzfUrl || null, nzfcUrl: info.settings.nzfcUrl || null, autoDownload: info.settings.autoDownload, watchFolder: info.settings.watchFolder || null } }); setMsg('Settings saved.'); await load(); } catch (e) { setErrors(errs(e)); } };
  if (!info) return <p>{errors.join(' ') || 'Loading…'}</p>;
  const current = info.releases.filter((r: any) => r.isCurrent);
  return (
    <div className="stack">
      {errors.length > 0 && <div className="error" role="alert"><ul>{errors.map((e) => <li key={e}>{e}</li>)}</ul></div>}
      {msg && <p className="notice" role="status">{msg}</p>}
      <section className="card stack">
        <h2>NZ Formulary in BrightPAS</h2>
        <p className="hint">Three ways to bring in the PDFs: import a downloaded file here, put it in the watched folder (imported within the hour, then moved to "imported"), or let BrightPAS download it each month. The NZF (adults) and NZFC (children) PDFs are released on the first working day of each month. BrightPAS reads the monographs – indications, contra-indications, cautions, renal and hepatic impairment, pregnancy, breast-feeding, adverse effects, dosing and preparations – shows them while charting, gives NZF dosing where TDS has no dose limit, and suggests dose limits for the rules. The NZF has no interaction data, so interactions stay in TDS's rules. Check the NZF terms of use before using it outside TDS.</p>
        {current.length === 0 ? <p><strong>Not imported yet.</strong></p> : <table className="results compact"><thead><tr><th>Edition</th><th>Release</th><th>Monographs</th><th>Dosing lines</th><th>Imported</th></tr></thead>
          <tbody>{current.map((r: any) => <tr key={r.id}><td>{r.edition}</td><td>{r.releaseNumber} – {r.releaseDate ? formatDate(r.releaseDate) : '?'}</td><td>{r.monographs}</td><td>{r.doses}</td><td className="small">{nzDateTime(r.importedAt)} by {r.importedBy}</td></tr>)}</tbody></table>}
        {can(Perm.MedicinesCatalogue) && <>
          <label className="btn btn-light file-pick">{busy || 'Import an NZF or NZFC PDF…'}<input type="file" accept="application/pdf,.pdf" disabled={!!busy} onChange={(e) => void upload(e.target.files?.[0])} className="sr-only" /></label>
          <div className="form-grid">
            <label>NZF PDF address (for monthly download)<input value={info.settings.nzfUrl ?? ''} onChange={(e) => setInfo({ ...info, settings: { ...info.settings, nzfUrl: e.target.value } })} placeholder="https://…" /></label>
            <label>NZFC PDF address<input value={info.settings.nzfcUrl ?? ''} onChange={(e) => setInfo({ ...info, settings: { ...info.settings, nzfcUrl: e.target.value } })} placeholder="https://…" /></label>
            <label>Folder to watch for NZF PDFs (share or USB, optional)<input value={info.settings.watchFolder ?? ''} onChange={(e) => setInfo({ ...info, settings: { ...info.settings, watchFolder: e.target.value } })} placeholder="\\server\share\NZF" /></label>
          </div>
          <label className="check"><input type="checkbox" checked={info.settings.autoDownload} onChange={(e) => setInfo({ ...info, settings: { ...info.settings, autoDownload: e.target.checked } })} /> Download and import automatically on the first working day of each month (next: {formatDate(info.nextDue)})</label>
          {info.settings.lastCheckedAt && <p className="small muted">Last checked {nzDateTime(info.settings.lastCheckedAt)}{info.settings.lastProblem ? ` – ${info.settings.lastProblem}` : ' – fine'}.</p>}
          <div><button className="btn btn-primary" onClick={() => void saveSettings()}>Save NZF settings</button></div>
        </>}
      </section>
      <section className="card stack">
        <label>Find a medicine in the NZF<input value={q} onChange={(e) => { setQ(e.target.value); setMono(null); }} placeholder="e.g. paracetamol" /></label>
        {!mono && <ul className="plain">{list.slice(0, 40).map((m) => <li key={m.id}><button className="btn btn-link" onClick={async () => setMono(await api(`/api/medicines/nzf/monographs/${m.id}`))}>{m.name}</button> <span className="pill grey">{m.edition}</span></li>)}</ul>}
        {mono && (
          <div className="stack">
            <div className="row-between"><h2>{mono.name}{mono.synonyms && <span className="muted"> ({mono.synonyms})</span>} <span className="pill blue">{mono.edition} {mono.release}</span></h2><button className="btn btn-light btn-small" onClick={() => setMono(null)}>Back to the list</button></div>
            {mono.sections.map((x: any) => <div key={x.title}><h3>{x.title}</h3><p className="nzf-text">{x.text}</p></div>)}
            {mono.doses.length > 0 && <><h3>Dosing</h3>
              <div className="table-scroll" tabIndex={0} role="region" aria-label="NZF dosing"><table className="results compact"><thead><tr><th>Indication</th><th>Route</th><th>Who</th><th>Dose</th></tr></thead>
                <tbody>{mono.doses.map((d: any, i: number) => <tr key={i}><td className="small">{d.indication}</td><td className="small">{d.route}</td><td className="small">{d.population}</td><td className="small">{d.dose}</td></tr>)}</tbody></table></div></>}
            {mono.preparations.length > 0 && <><h3>Preparations</h3><ul className="plain small">{mono.preparations.map((p: any, i: number) => <li key={i}>{p.form} – {p.strength}{p.subsidy && <span className="muted"> ({p.subsidy})</span>}</li>)}</ul></>}
            <p className="hint">From the New Zealand Formulary (nzf.org.nz). Always check the current NZF and the Medicine Data Sheet.</p>
          </div>
        )}
      </section>
    </div>
  );
}
