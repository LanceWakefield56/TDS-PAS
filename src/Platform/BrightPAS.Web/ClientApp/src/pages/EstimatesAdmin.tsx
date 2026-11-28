import { useCallback, useEffect, useMemo, useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { api, ApiError, errText, formatDate, money } from '../api';
import ExportButton from '../components/ExportButton';
import Modal from '../components/Modal';

const TABS: [string, string][] = [['templates', 'Templates'], ['procedures', 'Procedures & surgeon fees'], ['surgeons', 'Surgeons'], ['addons', 'Procedure add-ons'], ['specialties', 'Specialties'],
  ['stock', 'Stock & costing'], ['import', 'Import pricing'], ['settings', 'Settings & wording']];
const ANAES: [string, string][] = [['General', 'General anaesthetic'], ['SedationAnaesthetist', 'Sedation by an anaesthetist'], ['SedationConsultant', 'Sedation by the consultant'], ['Local', 'Local anaesthetic only'], ['None', 'No anaesthetic required']];
const num = (v: string) => (v.trim() === '' ? null : Number(v));
const errs = (e: unknown) => (e instanceof ApiError && e.body?.errors ? e.body.errors as string[] : [errText(e)]);

/** Estimates › Admin (estimates administrators): price list, templates, stock, imports and settings. */
export default function EstimatesAdmin() {
  const [params, setParams] = useSearchParams();
  const tab = params.get('tab') ?? 'templates';
  const [cat, setCat] = useState<any>(null);
  const [error, setError] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const load = useCallback(() => { api('/api/estimates/admin/catalogue').then(setCat).catch((e) => setError(errs(e))); }, []);
  useEffect(() => { load(); }, [load]);
  const run = async (f: () => Promise<unknown>, ok?: string) => {
    setError([]); setMsg(null);
    try { await f(); if (ok) setMsg(ok); load(); return true; } catch (e) { setError(errs(e)); return false; }
  };
  const ctx = { cat, run, load };
  return (
    <div className="stack-lg">
      <div className="page-head row-between"><div><h1>Estimates admin</h1><p className="muted">Price list, templates, imports and wording. Anything used on an estimate can only be hidden, never deleted.</p></div><Link className="btn btn-light" to="/estimates">All estimates</Link></div>
      <div className="tabs" role="tablist">{TABS.map(([k, t]) => <button key={k} role="tab" aria-selected={tab === k} className={tab === k ? 'on' : ''} onClick={() => { setParams({ tab: k }); setError([]); setMsg(null); }}>{t}</button>)}</div>
      {error.length > 0 && <div className="error" role="alert"><ul>{error.map((e) => <li key={e}>{e}</li>)}</ul></div>}
      {msg && <p className="notice" role="status">{msg}</p>}
      {!cat ? <p>Loading…</p> : <>
        {tab === 'templates' && <TemplatesTab {...ctx} />}
        {tab === 'procedures' && <ProceduresTab {...ctx} />}
        {tab === 'surgeons' && <SurgeonsTab {...ctx} />}
        {tab === 'addons' && <AddOnsTab {...ctx} />}
        {tab === 'specialties' && <SpecialtiesTab {...ctx} />}
        {tab === 'stock' && <StockTab />}
        {tab === 'import' && <ImportTab {...ctx} setMsg={setMsg} />}
        {tab === 'settings' && <SettingsTab run={run} />}
      </>}
    </div>
  );
}

type Ctx = { cat: any; run: (f: () => Promise<unknown>, ok?: string) => Promise<boolean>; load: () => void };

function TemplatesTab({ cat, run }: Ctx) {
  const [rows, setRows] = useState<any[] | null>(null);
  const [edit, setEdit] = useState<any>(null);
  const reload = useCallback(() => { api<any[]>('/api/estimates/templates?all=true').then(setRows).catch(() => setRows([])); }, []);
  useEffect(() => { reload(); }, [reload]);
  const save = async () => { if (await run(() => api(`/api/estimates/templates/${edit.id}`, { method: 'PUT', body: edit }), 'Template saved.')) { setEdit(null); reload(); } };
  const del = async (t: any) => { if (await run(() => api(`/api/estimates/templates/${t.id}`, { method: 'DELETE' }), 'Template deleted.')) reload(); };
  return (
    <section className="card stack">
      <p className="hint">Make a template from any estimate with <strong>Save as template</strong> on the estimate. Hidden templates aren't offered to staff; favourites are listed first.</p>
      {rows && (rows.length === 0 ? <p className="muted">No templates.</p> : (
        <table className="results"><thead><tr><th>Template</th><th>Procedure</th><th>Order</th><th>Used</th><th>Shown to</th><th></th></tr></thead>
          <tbody>{rows.map((t) => (
            <tr key={t.id}>
              <td>{t.favourite && '★ '}{t.name}{!t.visible && <span className="pill">hidden</span>}{!t.procedureActive && <span className="pill">procedure retired</span>}</td>
              <td>{t.procedureCode} {t.procedureName}</td><td>{t.sortOrder}</td><td>{t.usedCount}</td>
              <td>{t.surgeonIds.length === 0 ? 'Everyone' : t.surgeonIds.map((id: string) => cat.surgeons.find((s: any) => s.id === id)?.name).join(', ')}</td>
              <td><div className="row"><button className="btn btn-link" onClick={() => setEdit({ ...t })}>Edit</button><button className="btn btn-link danger" onClick={() => void del(t)}>Delete</button></div></td>
            </tr>))}</tbody></table>
      ))}
      {edit && (
        <Modal title={`Edit ${edit.name}`} onClose={() => setEdit(null)}>
          <div className="form-grid">
            <label>Name<input value={edit.name} onChange={(e) => setEdit({ ...edit, name: e.target.value })} /></label>
            <label>Description<input value={edit.description ?? ''} onChange={(e) => setEdit({ ...edit, description: e.target.value })} /></label>
            <label>Order<input type="number" value={edit.sortOrder} onChange={(e) => setEdit({ ...edit, sortOrder: Number(e.target.value) })} /></label>
            <label className="check"><input type="checkbox" checked={edit.visible} onChange={(e) => setEdit({ ...edit, visible: e.target.checked })} /> Shown to staff</label>
            <label className="check"><input type="checkbox" checked={edit.favourite} onChange={(e) => setEdit({ ...edit, favourite: e.target.checked })} /> Favourite</label>
          </div>
          <fieldset className="chips"><legend>Only for these surgeons (none = everyone)</legend>
            {cat.surgeons.filter((s: any) => s.isActive).map((s: any) => <label key={s.id} className="check"><input type="checkbox" checked={edit.surgeonIds.includes(s.id)} onChange={(e) => setEdit({ ...edit, surgeonIds: e.target.checked ? [...edit.surgeonIds, s.id] : edit.surgeonIds.filter((x: string) => x !== s.id) })} /> {s.name}</label>)}
          </fieldset>
          <div className="row"><button className="btn btn-primary" onClick={() => void save()}>Save</button><button className="btn btn-light" onClick={() => setEdit(null)}>Close</button></div>
        </Modal>
      )}
    </section>
  );
}

const blankProc = (specialtyId: string) => ({ code: '', specialtyId, group: '', name: '', printTitle: '', minutes: 30, defaultAnaesthetic: 'General', facility: null, facilitySpecial: null, anaesthetist: null, anaesthetistSpecial: null, consumablesLine: null, includedAddOnCodes: '', effectiveFrom: null, isActive: true, source: '' });

function ProceduresTab({ cat, run }: Ctx) {
  const [spec, setSpec] = useState('');
  const [q, setQ] = useState('');
  const [sel, setSel] = useState<string | null>(null);
  const [p, setP] = useState<any>(null);
  const [fees, setFees] = useState<any[]>([]);
  const [cons, setCons] = useState<any[]>([]);
  const [usage, setUsage] = useState<any>(null);
  const [stock, setStock] = useState<any[]>([]);
  const list = useMemo(() => cat.procedures.filter((x: any) => (!spec || x.specialtyId === spec) && (!q || `${x.code} ${x.name}`.toLowerCase().includes(q.toLowerCase()))), [cat, spec, q]);
  const open = async (id: string | null) => {
    setSel(id);
    if (!id) { setP(blankProc(spec || cat.specialties[0]?.id)); setFees([{ surgeonId: null, fee: null, specialFee: null, notes: '' }]); setCons([]); setUsage(null); return; }
    const r = await api<any>(`/api/estimates/admin/procedures/${id}`);
    setP({ ...r.procedure, includedAddOnCodes: r.procedure.includedAddOnCodes ?? '' }); setFees(r.fees); setCons(r.consumables); setUsage(r.usage);
    if (stock.length === 0) api<any[]>('/api/estimates/admin/stock').then(setStock).catch(() => undefined);
  };
  const save = async () => {
    let id = sel;
    const ok = await run(async () => {
      if (sel) await api(`/api/estimates/admin/procedures/${sel}`, { method: 'PUT', body: p });
      else id = (await api<any>('/api/estimates/admin/procedures', { body: p })).id;
      await api(`/api/estimates/admin/procedures/${id}/fees`, { method: 'PUT', body: { fees: fees.filter((f) => f.fee !== null || f.specialFee !== null) } });
      if (sel) await api(`/api/estimates/admin/procedures/${id}/consumables`, { method: 'PUT', body: { lines: cons } });
    }, 'Procedure saved.');
    if (ok && id) await open(id);
  };
  const del = async () => { if (sel && await run(() => api(`/api/estimates/admin/procedures/${sel}`, { method: 'DELETE' }), 'Procedure deleted.')) { setSel(null); setP(null); } };
  const specName = (id: string) => cat.specialties.find((s: any) => s.id === id)?.name ?? '';
  return (
    <div className="split">
      <section className="card stack">
        <div className="row"><button className="btn btn-primary btn-small" onClick={() => void open(null)}>New procedure</button></div>
        <label>Specialty<select value={spec} onChange={(e) => setSpec(e.target.value)}><option value="">All</option>{cat.specialties.map((s: any) => <option key={s.id} value={s.id}>{s.name}</option>)}</select></label>
        <label>Search<input value={q} onChange={(e) => setQ(e.target.value)} /></label>
        {cat.pending.length > 0 && <p className="warn-box">{cat.pending.length} scheduled price change(s) – see Import pricing.</p>}
        <ul className="plain choose-list est-list">{list.map((x: any) => (
          <li key={x.id}><button className={`choice ${sel === x.id ? 'on' : ''}`} onClick={() => void open(x.id)}><strong>{x.code}</strong> {x.name}{!x.isActive && ' (hidden)'}<br /><span className="muted">{specName(x.specialtyId)} · facility {x.facility == null ? '—' : money(x.facility)} · surgeon {x.allSurgeonsFee == null ? '—' : money(x.allSurgeonsFee)}</span></button></li>
        ))}</ul>
      </section>
      {p && (
        <section className="card stack">
          <h2>{sel ? `${p.code} ${p.name}` : 'New procedure'}</h2>
          {usage && <p className="hint">Used by {usage.estimates} estimate(s) and {usage.templates} template(s).{(usage.estimates > 0 || usage.templates > 0) && ' It can be hidden but not deleted; the code is permanent.'}</p>}
          <div className="form-grid">
            <label>Code<input value={p.code} onChange={(e) => setP({ ...p, code: e.target.value.toUpperCase() })} /></label>
            <label>Specialty<select value={p.specialtyId} onChange={(e) => setP({ ...p, specialtyId: e.target.value })}>{cat.specialties.map((s: any) => <option key={s.id} value={s.id}>{s.name}</option>)}</select></label>
            <label>Group<input value={p.group} onChange={(e) => setP({ ...p, group: e.target.value })} /></label>
            <label>Name<input value={p.name} onChange={(e) => setP({ ...p, name: e.target.value })} /></label>
            <label>Print title<input value={p.printTitle} onChange={(e) => setP({ ...p, printTitle: e.target.value })} /></label>
            <label>Minutes<input type="number" value={p.minutes} onChange={(e) => setP({ ...p, minutes: Number(e.target.value) })} /></label>
            <label>Default anaesthetic<select value={p.defaultAnaesthetic} onChange={(e) => setP({ ...p, defaultAnaesthetic: e.target.value })}>{ANAES.map(([v, t]) => <option key={v} value={v}>{t}</option>)}</select></label>
            <label>Facility<input type="number" step="0.01" value={p.facility ?? ''} onChange={(e) => setP({ ...p, facility: num(e.target.value) })} /></label>
            <label>Facility special<input type="number" step="0.01" value={p.facilitySpecial ?? ''} onChange={(e) => setP({ ...p, facilitySpecial: num(e.target.value) })} /></label>
            <label>Anaesthetist<input type="number" step="0.01" value={p.anaesthetist ?? ''} onChange={(e) => setP({ ...p, anaesthetist: num(e.target.value) })} /></label>
            <label>Anaesthetist special<input type="number" step="0.01" value={p.anaesthetistSpecial ?? ''} onChange={(e) => setP({ ...p, anaesthetistSpecial: num(e.target.value) })} /></label>
            <label>Consumables line<input type="number" step="0.01" value={p.consumablesLine ?? ''} onChange={(e) => setP({ ...p, consumablesLine: num(e.target.value) })} /></label>
            <label>Includes add-ons (codes, comma separated)<input value={p.includedAddOnCodes} onChange={(e) => setP({ ...p, includedAddOnCodes: e.target.value.toUpperCase() })} /></label>
            <label>Effective from<input type="date" value={p.effectiveFrom ?? ''} onChange={(e) => setP({ ...p, effectiveFrom: e.target.value || null })} /></label>
            <label>Source / notes<input value={p.source ?? ''} onChange={(e) => setP({ ...p, source: e.target.value })} /></label>
            <label className="check"><input type="checkbox" checked={p.isActive} onChange={(e) => setP({ ...p, isActive: e.target.checked })} /> Active (shown to staff)</label>
          </div>
          <h3>Surgeon fees (GST incl)</h3>
          {fees.map((f, i) => (
            <div key={i} className="form-grid">
              <label>Surgeon<select value={f.surgeonId ?? ''} onChange={(e) => setFees(fees.map((x, j) => (j === i ? { ...x, surgeonId: e.target.value || null } : x)))}><option value="">All surgeons (default)</option>{cat.surgeons.map((s: any) => <option key={s.id} value={s.id}>{s.name}</option>)}</select></label>
              <label>Fee<input type="number" step="0.01" value={f.fee ?? ''} onChange={(e) => setFees(fees.map((x, j) => (j === i ? { ...x, fee: num(e.target.value) } : x)))} /></label>
              <label>Special fee (self-funding)<input type="number" step="0.01" value={f.specialFee ?? ''} onChange={(e) => setFees(fees.map((x, j) => (j === i ? { ...x, specialFee: num(e.target.value) } : x)))} /></label>
              <label>Notes<input value={f.notes ?? ''} onChange={(e) => setFees(fees.map((x, j) => (j === i ? { ...x, notes: e.target.value } : x)))} /></label>
              <div className="row"><button className="btn btn-link danger" onClick={() => setFees(fees.filter((_, j) => j !== i))}>Remove</button></div>
            </div>
          ))}
          <div><button className="btn btn-light btn-small" onClick={() => setFees([...fees, { surgeonId: null, fee: null, specialFee: null, notes: '' }])}>Add a surgeon fee</button></div>
          {sel && <>
            <h3>Internal costing (not printed)</h3>
            {cons.map((c, i) => (
              <div key={i} className="form-grid">
                <label>Stock item<select value={c.stockItemId ?? ''} onChange={(e) => setCons(cons.map((x, j) => (j === i ? { ...x, stockItemId: e.target.value || null } : x)))}><option value="">– manual cost –</option>{stock.map((s) => <option key={s.id} value={s.id}>{s.supplier} – {s.name}</option>)}</select></label>
                <label>Description<input value={c.description} onChange={(e) => setCons(cons.map((x, j) => (j === i ? { ...x, description: e.target.value } : x)))} /></label>
                <label>Quantity<input type="number" step="0.01" value={c.quantity} onChange={(e) => setCons(cons.map((x, j) => (j === i ? { ...x, quantity: Number(e.target.value) } : x)))} /></label>
                {!c.stockItemId && <label>Unit cost ex GST<input type="number" step="0.0001" value={c.unitCost ?? ''} onChange={(e) => setCons(cons.map((x, j) => (j === i ? { ...x, unitCost: num(e.target.value) } : x)))} /></label>}
                <div className="row"><button className="btn btn-link danger" onClick={() => setCons(cons.filter((_, j) => j !== i))}>Remove</button></div>
              </div>
            ))}
            <div><button className="btn btn-light btn-small" onClick={() => setCons([...cons, { stockItemId: null, description: '', quantity: 1, unitCost: null }])}>Add a costing line</button></div>
          </>}
          <div className="row"><button className="btn btn-primary" onClick={() => void save()}>Save</button>{sel && <button className="btn btn-link danger" onClick={() => void del()}>Delete</button>}</div>
        </section>
      )}
    </div>
  );
}

function useSimpleEditor({ title, rows, columns, blank, render, path, run, canDelete = true }:
  { title: string; rows: any[]; columns: string[]; blank: any; render: (r: any) => React.ReactNode[]; path: string; run: Ctx['run']; canDelete?: boolean; }) {
  const [edit, setEdit] = useState<any>(null);
  const save = async (fields: (e: any) => any) => { if (await run(() => api(edit.id ? `${path}/${edit.id}` : path, { method: edit.id ? 'PUT' : 'POST', body: fields(edit) }), `${title} saved.`)) setEdit(null); };
  return { edit, setEdit, save, table: (
    <section className="card stack">
      <div className="row"><button className="btn btn-primary btn-small" onClick={() => setEdit({ ...blank })}>New</button></div>
      <table className="results"><thead><tr>{columns.map((c) => <th key={c}>{c}</th>)}<th></th></tr></thead>
        <tbody>{rows.map((r) => <tr key={r.id}>{render(r).map((c, i) => <td key={i}>{c}</td>)}<td><div className="row"><button className="btn btn-link" onClick={() => setEdit({ ...r })}>Edit</button>{canDelete && <button className="btn btn-link danger" onClick={() => void run(() => api(`${path}/${r.id}`, { method: 'DELETE' }), 'Deleted.')}>Delete</button>}</div></td></tr>)}</tbody></table>
    </section>) };
}

function SurgeonsTab({ cat, run }: Ctx) {
  const ed = useSimpleEditor({ title: 'Surgeon', rows: cat.surgeons, columns: ['Name', 'Specialties', 'Active'], blank: { name: '', clinicianId: null, isActive: true, specialtyIds: [] }, path: '/api/estimates/admin/surgeons', run,
    render: (s) => [s.name, s.specialtyIds.map((id: string) => cat.specialties.find((x: any) => x.id === id)?.name).join(', '), s.isActive ? 'Yes' : 'Hidden'] });
  const e = ed.edit;
  return <>{ed.table}{e && (
    <Modal title={e.id ? `Edit ${e.name}` : 'New surgeon'} onClose={() => ed.setEdit(null)}>
      <label>Name<input value={e.name} onChange={(x) => ed.setEdit({ ...e, name: x.target.value })} /></label>
      <fieldset className="chips"><legend>Specialties</legend>{cat.specialties.map((s: any) => <label key={s.id} className="check"><input type="checkbox" checked={e.specialtyIds.includes(s.id)} onChange={(x) => ed.setEdit({ ...e, specialtyIds: x.target.checked ? [...e.specialtyIds, s.id] : e.specialtyIds.filter((y: string) => y !== s.id) })} /> {s.name}</label>)}</fieldset>
      <label className="check"><input type="checkbox" checked={e.isActive} onChange={(x) => ed.setEdit({ ...e, isActive: x.target.checked })} /> Active</label>
      <div className="row"><button className="btn btn-primary" onClick={() => void ed.save((v) => ({ name: v.name, clinicianId: v.clinicianId, isActive: v.isActive, specialtyIds: v.specialtyIds }))}>Save</button><button className="btn btn-light" onClick={() => ed.setEdit(null)}>Close</button></div>
    </Modal>)}</>;
}

function AddOnsTab({ cat, run }: Ctx) {
  const specName = (id: string) => cat.specialties.find((s: any) => s.id === id)?.name ?? '';
  const ed = useSimpleEditor({ title: 'Add-on', rows: cat.addOns, columns: ['Code', 'Add-on', 'Specialty / group', 'Fee', 'Type', 'Active'], path: '/api/estimates/admin/addons', run,
    blank: { code: '', specialtyId: cat.specialties[0]?.id, group: '', name: '', fee: 0, type: 'AllIn', surgeonId: null, source: '', isActive: true },
    render: (a) => [a.code, a.name, `${specName(a.specialtyId)}${a.group ? ` · ${a.group}` : ''}`, money(a.fee), a.type === 'Surgeon' ? 'Adds to surgeon fee' : 'Own line (all-in)', a.isActive ? 'Yes' : 'Hidden'] });
  const e = ed.edit;
  return <>{ed.table}{e && (
    <Modal title={e.id ? `Edit ${e.name}` : 'New add-on'} onClose={() => ed.setEdit(null)}>
      <div className="form-grid">
        <label>Code<input value={e.code} onChange={(x) => ed.setEdit({ ...e, code: x.target.value.toUpperCase() })} /></label>
        <label>Name<input value={e.name} onChange={(x) => ed.setEdit({ ...e, name: x.target.value })} /></label>
        <label>Specialty<select value={e.specialtyId} onChange={(x) => ed.setEdit({ ...e, specialtyId: x.target.value })}>{cat.specialties.map((s: any) => <option key={s.id} value={s.id}>{s.name}</option>)}</select></label>
        <label>Procedure group (blank = whole specialty)<input value={e.group ?? ''} onChange={(x) => ed.setEdit({ ...e, group: x.target.value })} /></label>
        <label>Fee (GST incl)<input type="number" step="0.01" value={e.fee} onChange={(x) => ed.setEdit({ ...e, fee: Number(x.target.value) })} /></label>
        <label>Type<select value={e.type} onChange={(x) => ed.setEdit({ ...e, type: x.target.value })}><option value="Surgeon">Surgeon – adds to the surgeon fee</option><option value="AllIn">All-in – printed as its own line</option></select></label>
        <label>Only for surgeon<select value={e.surgeonId ?? ''} onChange={(x) => ed.setEdit({ ...e, surgeonId: x.target.value || null })}><option value="">Any surgeon</option>{cat.surgeons.map((s: any) => <option key={s.id} value={s.id}>{s.name}</option>)}</select></label>
        <label>Source / notes<input value={e.source ?? ''} onChange={(x) => ed.setEdit({ ...e, source: x.target.value })} /></label>
        <label className="check"><input type="checkbox" checked={e.isActive} onChange={(x) => ed.setEdit({ ...e, isActive: x.target.checked })} /> Active</label>
      </div>
      <div className="row"><button className="btn btn-primary" onClick={() => void ed.save((v) => ({ code: v.code, specialtyId: v.specialtyId, group: v.group, name: v.name, fee: v.fee, type: v.type, surgeonId: v.surgeonId, source: v.source, isActive: v.isActive }))}>Save</button><button className="btn btn-light" onClick={() => ed.setEdit(null)}>Close</button></div>
    </Modal>)}</>;
}

function SpecialtiesTab({ cat, run }: Ctx) {
  const ed = useSimpleEditor({ title: 'Specialty', rows: cat.specialties, columns: ['Specialty', 'Default notes', 'Active'], blank: { name: '', defaultNotes: '', isActive: true }, path: '/api/estimates/admin/specialties', run,
    render: (s) => [s.name, s.defaultNotes ?? '—', s.isActive ? 'Yes' : 'Hidden'] });
  const e = ed.edit;
  return <>{ed.table}{e && (
    <Modal title={e.id ? `Edit ${e.name}` : 'New specialty'} onClose={() => ed.setEdit(null)}>
      <label>Name<input value={e.name} onChange={(x) => ed.setEdit({ ...e, name: x.target.value })} /></label>
      <label>Default notes (put on new estimates in this specialty)<textarea value={e.defaultNotes ?? ''} onChange={(x) => ed.setEdit({ ...e, defaultNotes: x.target.value })} /></label>
      <label className="check"><input type="checkbox" checked={e.isActive} onChange={(x) => ed.setEdit({ ...e, isActive: x.target.checked })} /> Active</label>
      <div className="row"><button className="btn btn-primary" onClick={() => void ed.save((v) => ({ name: v.name, defaultNotes: v.defaultNotes, isActive: v.isActive }))}>Save</button><button className="btn btn-light" onClick={() => ed.setEdit(null)}>Close</button></div>
    </Modal>)}</>;
}

function StockTab() {
  const [rows, setRows] = useState<any[] | null>(null);
  const [q, setQ] = useState('');
  useEffect(() => { api<any[]>('/api/estimates/admin/stock').then(setRows).catch(() => setRows([])); }, []);
  const list = (rows ?? []).filter((r) => !q || `${r.supplier} ${r.name} ${r.reference ?? ''}`.toLowerCase().includes(q.toLowerCase()));
  return (
    <section className="card stack">
      <p className="hint">Supplier prices for internal costing only (never printed). Charge-out = unit price × (1 + loading) × (1 + markup) × (1 + GST). Import the stock spreadsheet on the Import tab.</p>
      <div className="row"><label>Search<input value={q} onChange={(e) => setQ(e.target.value)} /></label><ExportButton label="Download current stock" path="/api/estimates/admin/files/stock" body={{}} /></div>
      {rows && <table className="results compact"><thead><tr><th>Supplier</th><th>Item</th><th>Reference</th><th className="money">Box</th><th>Units</th><th className="money">Unit</th><th className="money">Charge-out</th><th>Issues</th></tr></thead>
        <tbody>{list.slice(0, 500).map((r) => <tr key={r.id}><td>{r.supplier}</td><td>{r.name}</td><td>{r.reference ?? ''}</td><td className="money">{r.packPrice == null ? '—' : money(r.packPrice)}</td><td>{r.unitsPerPack ?? '—'}</td><td className="money">{r.unitPrice == null ? '—' : money(r.unitPrice)}</td><td className="money">{r.chargeOut == null ? '—' : money(r.chargeOut)}</td><td className="muted">{r.issues ?? ''}</td></tr>)}</tbody></table>}
      {list.length > 500 && <p className="hint">Showing the first 500 – narrow the search.</p>}
    </section>
  );
}

async function upload(path: string, file: File) {
  const res = await fetch(path, { method: 'POST', credentials: 'same-origin', headers: { 'X-BrightPAS-Request': '1', 'Content-Type': 'application/octet-stream', 'X-File-Name': file.name }, body: file });
  const text = await res.text();
  const data = text ? JSON.parse(text) : null;
  if (!res.ok) throw new ApiError(res.status, data);
  return data;
}

function ImportTab({ cat, run, setMsg }: Ctx & { setMsg: (m: string | null) => void }) {
  const [file, setFile] = useState<File | null>(null);
  const [pv, setPv] = useState<any>(null);
  const [opts, setOpts] = useState({ fees: true, addOns: true, retire: false, acceptWarnings: false });
  const [stockFile, setStockFile] = useState<File | null>(null);
  const [spv, setSpv] = useState<any>(null);
  const preview = async () => { if (file) await run(async () => setPv(await upload('/api/estimates/admin/import/pricing', file))); };
  const apply = async () => {
    if (!file) return;
    const p = new URLSearchParams({ apply: 'true', fees: String(opts.fees), addOns: String(opts.addOns), retire: String(opts.retire), acceptWarnings: String(opts.acceptWarnings) });
    await run(async () => { const r = await upload(`/api/estimates/admin/import/pricing?${p}`, file); setPv(null); setFile(null); setMsg(`Imported: ${r.applied}`); });
  };
  const previewStock = async () => { if (stockFile) await run(async () => setSpv(await upload('/api/estimates/admin/import/stock', stockFile))); };
  const applyStock = async () => { if (stockFile) await run(async () => { const r = await upload('/api/estimates/admin/import/stock?apply=true', stockFile); setSpv(null); setStockFile(null); setMsg(`Imported: ${r.applied}`); }); };
  return (
    <div className="stack-lg">
      <section className="card stack">
        <h2>Pricing spreadsheet</h2>
        <p className="hint">Reads the <strong>Export</strong> tab of <em>Estimates - Facility &amp; Anaesthetist.xlsx</em> (headings on row 3, data from row 4), plus the optional "Surgeon Fees" and "Add-ons" tabs. You see every difference before anything changes. Prices with a future "Effective from" date are scheduled.</p>
        <div className="row">
          <label>Spreadsheet (.xlsx)<input type="file" accept=".xlsx" onChange={(e) => { setFile(e.target.files?.[0] ?? null); setPv(null); }} /></label>
          <button className="btn btn-primary" disabled={!file} onClick={() => void preview()}>Show differences</button>
          <ExportButton label="Download current pricing" path="/api/estimates/admin/files/pricing" body={{}} />
        </div>
        {pv && <>
          {pv.errors.length > 0 && <div className="error"><strong>Fix these in the spreadsheet first:</strong><ul>{pv.errors.map((e: string) => <li key={e}>{e}</li>)}</ul></div>}
          <p>{pv.new} new · {pv.changed} changed · {pv.unchanged} unchanged · {pv.notInFile} not in the file · {pv.fees} surgeon fee row(s) · {pv.addOns} add-on(s)</p>
          {pv.warnings.length > 0 && <div className="warn-box"><ul>{pv.warnings.map((w: string) => <li key={w}>{w}</li>)}</ul></div>}
          {pv.procedures.length > 0 && <table className="results compact"><thead><tr><th>Code</th><th>Procedure</th><th>Change</th><th>Details</th></tr></thead>
            <tbody>{pv.procedures.map((p: any) => <tr key={p.code}><td>{p.code}</td><td>{p.name}</td><td>{p.status}{p.scheduled && ` (scheduled ${p.effectiveFrom ? formatDate(p.effectiveFrom) : ''})`}</td><td>{p.diffs.map((d: any) => `${d.field}: ${d.old} → ${d.new}`).join('; ')}</td></tr>)}</tbody></table>}
          {pv.errors.length === 0 && <>
            <label className="check"><input type="checkbox" checked={opts.fees} onChange={(e) => setOpts({ ...opts, fees: e.target.checked })} /> Import surgeon fees</label>
            <label className="check"><input type="checkbox" checked={opts.addOns} onChange={(e) => setOpts({ ...opts, addOns: e.target.checked })} /> Import add-ons</label>
            <label className="check"><input type="checkbox" checked={opts.retire} onChange={(e) => setOpts({ ...opts, retire: e.target.checked })} /> Hide procedures that are not in the file</label>
            {pv.warnings.length > 0 && <label className="check"><input type="checkbox" checked={opts.acceptWarnings} onChange={(e) => setOpts({ ...opts, acceptWarnings: e.target.checked })} /> I have checked the warnings above</label>}
            <div className="row"><button className="btn btn-primary" disabled={pv.warnings.length > 0 && !opts.acceptWarnings} onClick={() => void apply()}>Approve and import</button></div>
          </>}
        </>}
        {cat.pending.length > 0 && <>
          <h3>Scheduled price changes</h3>
          <table className="results compact"><thead><tr><th>Code</th><th>Procedure</th><th>Field</th><th className="money">Now</th><th className="money">From</th><th>Date</th><th>Open estimates on the old price</th></tr></thead>
            <tbody>{cat.pending.map((x: any, i: number) => <tr key={i}><td>{x.code}</td><td>{x.name}</td><td>{x.field}</td><td className="money">{x.current == null ? '—' : money(x.current)}</td><td className="money">{x.next == null ? '—' : money(x.next)}</td><td>{formatDate(x.effectiveFrom)}</td><td>{x.openEstimates}</td></tr>)}</tbody></table>
        </>}
      </section>
      <section className="card stack">
        <h2>Stock spreadsheet</h2>
        <p className="hint">Reads the <strong>Export</strong> tab of the supplier stock list (Supplier, Item, Reference, Box price, Units per box, Unit price, In stock qty, Needed in house, Where used), or the supplier blocks if there is no Export tab.</p>
        <div className="row"><label>Spreadsheet (.xlsx)<input type="file" accept=".xlsx" onChange={(e) => { setStockFile(e.target.files?.[0] ?? null); setSpv(null); }} /></label><button className="btn btn-primary" disabled={!stockFile} onClick={() => void previewStock()}>Check file</button></div>
        {spv && <>
          {spv.errors.length > 0 && <div className="error"><ul>{spv.errors.map((e: string) => <li key={e}>{e}</li>)}</ul></div>}
          <p>{spv.items} item(s) from {spv.suppliers} supplier(s): {spv.new} new, {spv.updated} updated, {spv.issues.length} with issues.</p>
          {spv.issues.length > 0 && <ul className="plain">{spv.issues.slice(0, 20).map((i: any, n: number) => <li key={n} className="muted">{i.supplier} – {i.name}: {i.issues}</li>)}</ul>}
          {spv.errors.length === 0 && <button className="btn btn-primary" onClick={() => void applyStock()}>Import stock</button>}
        </>}
      </section>
    </div>
  );
}

const PW: [string, string][] = [['DobShort', 'Patient date of birth (DDMMYY)'], ['DobLong', 'Patient date of birth (DDMMYYYY)'], ['Nhi', 'Patient NHI (capitals)'], ['Typed', 'Type a password for each estimate'], ['None', 'No password']];

function SettingsTab({ run }: { run: Ctx['run'] }) {
  const [s, setS] = useState<any>(null);
  useEffect(() => { api('/api/estimates/admin/settings').then(setS).catch(() => undefined); }, []);
  if (!s) return <p>Loading…</p>;
  const t = (k: string, label: string, multi = false) => <label key={k}>{label}{multi ? <textarea value={s[k] ?? ''} onChange={(e) => setS({ ...s, [k]: e.target.value })} /> : <input value={s[k] ?? ''} onChange={(e) => setS({ ...s, [k]: e.target.value })} />}</label>;
  const n = (k: string, label: string, step = '1') => <label key={k}>{label}<input type="number" step={step} value={s[k]} onChange={(e) => setS({ ...s, [k]: Number(e.target.value) })} /></label>;
  return (
    <div className="stack-lg">
      <section className="card stack"><h2>Rates and terms</h2><div className="form-grid">
        {n('gstRate', 'GST rate (fraction)', '0.0001')}{n('loading', 'Loading on supplied prices (fraction)', '0.0001')}{n('markup', 'Markup (fraction)', '0.0001')}
        {n('extendedTimePer15', 'Extended theatre time per extra 15 min ($, GST incl)', '0.01')}{n('validityMonths', 'Estimate validity (months)')}{n('depositPercent', 'Self-funding deposit (%)', '0.01')}
        {n('depositDaysBefore', 'Deposit due (days before surgery)')}{n('balanceDaysAfter', 'Balance due (days after surgery)')}{t('surgeonFeeLabel', 'Label for the surgeon line')}
      </div></section>
      <section className="card stack"><h2>Printed contact details and wording</h2>
        <p className="hint">Tokens: {'{validity} {validuntil} {phone} {deposit} {balancedays} {ref} {first} {last} {procedure} {preparedby} {insurer} {policy}'}</p>
        <div className="form-grid">{t('bankAccount', 'Bank account')}{t('phone', 'Phone')}{t('email', 'Email (also the Outlook draft "from")')}{t('address', 'Address')}</div>
        {t('terms', 'Terms (one bullet per line)', true)}{t('selfFundingText', 'Self-funding paragraph', true)}{t('insuranceText', 'Insurance paragraph', true)}{t('acceptanceText', 'Closing line', true)}
      </section>
      <section className="card stack"><h2>Email safety</h2><div className="form-grid">
        <label>How estimates are emailed<select value={s.sendMode} onChange={(e) => setS({ ...s, sendMode: e.target.value })}><option value="OutlookDraft">Outlook draft (staff send it themselves)</option><option value="SendFromApp">Send from BrightPAS (with hold period)</option></select></label>
        {n('holdMinutes', 'Hold period before sending (minutes)')}
        <label className="check"><input type="checkbox" checked={s.requireIssued} onChange={(e) => setS({ ...s, requireIssued: e.target.checked })} /> Only issued estimates can be emailed</label>
        <label>Password for PDFs emailed to patients<select value={s.patientPassword} onChange={(e) => setS({ ...s, patientPassword: e.target.value })}>{PW.map(([v, l]) => <option key={v} value={v}>{l}</option>)}</select></label>
        <label className="check"><input type="checkbox" checked={s.insurerSameAsPatient} onChange={(e) => setS({ ...s, insurerSameAsPatient: e.target.checked })} /> Insurers get the same password rule (default: no password)</label>
        {t('fromName', 'Send as name')}{t('bccAddress', 'Copy every sent estimate to (optional)')}
      </div>
        {t('insurerAddresses', 'Insurer email addresses (one per line: Insurer name = address)', true)}
        {t('emailSubject', 'Email subject (patient)')}{t('emailBody', 'Email body (patient)', true)}{t('emailSubjectInsurer', 'Email subject (insurer)')}{t('emailBodyInsurer', 'Email body (insurer)', true)}
        {t('hintDobShort', 'Email line when the password is the DOB (DDMMYY)', true)}{t('hintDobLong', 'Email line when the password is the DOB (DDMMYYYY)', true)}{t('hintNhi', 'Email line when the password is the NHI', true)}{t('hintTyped', 'Email line for a typed password', true)}
        <p className="hint">"Send from BrightPAS" uses the mail server in SMS &amp; email › Settings.</p>
      </section>
      <section className="card stack"><h2>Exports and costing</h2><div className="form-grid">
        {t('incisiveFolder', 'Incisive scans folder (\\\\server\\share)')}{t('incisiveFileName', 'Incisive file name pattern ({NHI} {LAST} {FIRST} {DATE} {REF} {CODE})')}
        <label className="check"><input type="checkbox" checked={s.archiveIssued} onChange={(e) => setS({ ...s, archiveIssued: e.target.checked })} /> Keep a PDF copy of every issued estimate (and each change)</label>
        {n('staffCostPerMinute', 'Theatre staff cost per minute ex GST (costing)', '0.01')}{n('overheadPerCase', 'Fixed overhead per case ex GST (costing)', '0.01')}
      </div></section>
      <div className="row sticky-actions"><button className="btn btn-primary" onClick={() => void run(() => api('/api/estimates/admin/settings', { method: 'PUT', body: s }), 'Settings saved.')}>Save settings</button></div>
    </div>
  );
}
