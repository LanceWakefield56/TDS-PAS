import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router';
import { api, errText, formatDate, Location, money, Perm, todayIso } from '../api';
import { useSession } from '../App';
import Modal from '../components/Modal';

const CATS: [string, string][] = [['Electromedical', 'Electromedical device'], ['Monitor', 'Patient monitor'], ['Washer', 'Washer-disinfector'], ['Steriliser', 'Steriliser'], ['Aer', 'Endoscope reprocessor (AER)'],
  ['DryingCabinet', 'Scope drying cabinet'], ['Endoscope', 'Endoscope'], ['EndoscopyTower', 'Endoscopy tower / video processor'], ['Instrument', 'Instrument (powered)'], ['Furniture', 'Furniture / bed / trolley'], ['It', 'IT equipment'], ['Other', 'Other']];
const KINDS: [string, string][] = [['Ppm', 'Planned maintenance (PPM)'], ['Calibration', 'Calibration'], ['TestAndTag', 'Electrical test-and-tag (AS/NZS 3760)'], ['BiomedicalSafety', 'Biomedical safety check'], ['Validation', 'Validation (IQ/OQ/PQ)']];
const DUE: Record<string, string> = { Overdue: 'pill bad', DueSoon: 'pill', Current: 'pill green', None: 'pill' };
const catName = (c: string) => CATS.find(([k]) => k === c)?.[1] ?? c;
const kindName = (k: string) => KINDS.find(([x]) => x === k)?.[1] ?? (k === 'Repair' ? 'Repair' : k);

/** Equipment register, planned maintenance, test-and-tag, lockout and suppliers. */
export default function Assets() {
  const { can } = useSession();
  const manage = can(Perm.AssetsManage);
  const [params, setParams] = useSearchParams();
  const tab = params.get('tab') ?? 'register';
  const [rows, setRows] = useState<any[] | null>(null);
  const [q, setQ] = useState('');
  const [cat, setCat] = useState('');
  const [locations, setLocations] = useState<Location[]>([]);
  const [suppliers, setSuppliers] = useState<any[]>([]);
  const [edit, setEdit] = useState<any>(null);
  const [sel, setSel] = useState<any>(null);
  const [due, setDue] = useState<any[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const load = () => {
    api<any[]>(`/api/assets/equipment?${new URLSearchParams({ q, category: cat })}`).then(setRows).catch((e) => setError(errText(e)));
    api<any[]>('/api/assets/maintenance/due?days=60').then(setDue).catch(() => setDue([]));
  };
  useEffect(() => { load(); api<Location[]>('/api/organisation/locations').then(setLocations).catch(() => undefined); api<any[]>('/api/assets/suppliers?all=true').then(setSuppliers).catch(() => undefined); }, [cat]); // eslint-disable-line react-hooks/exhaustive-deps
  const act = async (fn: () => Promise<any>, ok: string) => { setError(null); setMsg(null); try { const r = await fn(); setMsg(ok); return r ?? true; } catch (x: any) { setError(x?.body?.errors?.join(' ') ?? errText(x)); return null; } };
  const open = async (id: string) => { setSel(await api(`/api/assets/equipment/${id}`)); };
  const save = async () => {
    const body = { ...edit, id: edit.id ?? null, locationId: edit.locationId || null, supplierId: edit.supplierId || null, purchasedOn: edit.purchasedOn || null, warrantyUntil: edit.warrantyUntil || null, value: edit.value === '' || edit.value == null ? null : Number(edit.value) };
    const r = await act(() => api('/api/assets/equipment', { body }), 'Saved.');
    if (r) { setEdit(null); load(); if (r.id) open(r.id); }
  };
  const loc = (id: string | null) => locations.find((l) => l.id === id)?.name ?? '';
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Assets &amp; equipment</h1><p className="muted">Equipment register, planned maintenance, electrical test-and-tag and out-of-service lockout.</p></div>
      {error && <p className="error" role="alert">{error}</p>}{msg && <p className="ok-text" role="status">{msg}</p>}
      <section className="card stack">
        <div className="tabs" role="tablist">{[['register', 'Register'], ['due', `Maintenance due${due && due.length ? ` (${due.length})` : ''}`], ['suppliers', 'Suppliers']].map(([k, t]) =>
          <button key={k} role="tab" aria-selected={tab === k} className={tab === k ? 'on' : ''} onClick={() => setParams({ tab: k })}>{t}</button>)}</div>
        {tab === 'register' && <>
          <form className="row" role="search" onSubmit={(e) => { e.preventDefault(); load(); }}>
            <label>Search<input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Asset number, name, serial or model" /></label>
            <label>Category<select value={cat} onChange={(e) => setCat(e.target.value)}><option value="">All</option>{CATS.map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
            <button className="btn btn-light">Search</button>
            {manage && <button type="button" className="btn btn-primary" onClick={() => setEdit({ assetNumber: '', name: '', category: 'Electromedical', make: '', model: '', serialNumber: '', locationId: '', owner: '', supplierId: '', purchasedOn: '', value: '', warrantyUntil: '', risk: 'Medium', networkAddress: '', notes: '' })}>Add equipment</button>}
          </form>
          {edit && <EquipmentForm edit={edit} setEdit={setEdit} locations={locations} suppliers={suppliers} onSave={save} />}
          {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">No equipment found.</p> : (
            <table className="results"><thead><tr><th>Asset</th><th>Category</th><th>Room</th><th>Status</th><th>Next check</th></tr></thead>
              <tbody>{rows.map((r) => <tr key={r.id} className={sel?.equipment?.id === r.id ? 'selected' : ''}>
                <td><button className="btn btn-link" onClick={() => open(r.id)}>{r.assetNumber} {r.name}</button><div className="small muted">{[r.make, r.model, r.serialNumber && `serial ${r.serialNumber}`].filter(Boolean).join(' · ')}</div></td>
                <td>{catName(r.category)}<div className="small muted">{r.risk} risk</div></td><td>{loc(r.locationId)}</td>
                <td>{r.status === 'InService' ? <span className="pill green">In service</span> : r.status === 'OutOfService' ? <span className="pill bad">Out of service</span> : <span className="pill">Retired</span>}
                  {r.outOfServiceReason && <div className="small muted">{r.outOfServiceReason}</div>}</td>
                <td>{r.nextDue ? <><span className={DUE[r.dueState]}>{formatDate(r.nextDue)}</span><div className="small muted">{kindName(r.nextDueKind)}</div></> : '—'}</td></tr>)}</tbody></table>)}
          {sel && <EquipmentDetail sel={sel} manage={manage} locations={locations} suppliers={suppliers} act={act} reload={() => { load(); open(sel.equipment.id); }} onEdit={() => setEdit({ ...sel.equipment,
            locationId: sel.equipment.locationId ?? '', supplierId: sel.equipment.supplierId ?? '', purchasedOn: sel.equipment.purchasedOn ?? '', warrantyUntil: sel.equipment.warrantyUntil ?? '', value: sel.equipment.value ?? '' })} />}
        </>}
        {tab === 'due' && (!due ? <p>Loading…</p> : due.length === 0 ? <p className="muted">Nothing due in the next 60 days.</p> : (
          <table className="results"><thead><tr><th>Equipment</th><th>Check</th><th>Due</th><th>Done by</th></tr></thead>
            <tbody>{due.map((d, i) => <tr key={i}><td>{d.assetNumber} {d.name}</td><td>{kindName(d.kind)}</td><td><span className={DUE[d.state]}>{formatDate(d.nextDue)}</span></td><td>{d.doneBy ?? ''}</td></tr>)}</tbody></table>))}
        {tab === 'suppliers' && <Suppliers rows={suppliers} manage={manage} act={act} reload={() => api<any[]>('/api/assets/suppliers?all=true').then(setSuppliers)} />}
      </section>
    </div>
  );
}

function EquipmentForm({ edit, setEdit, locations, suppliers, onSave }: { edit: any; setEdit: (x: any) => void; locations: Location[]; suppliers: any[]; onSave: () => void }) {
  const f = (k: string, label: string, type = 'text', hint?: string) => <label>{label}<input type={type} value={edit[k] ?? ''} placeholder={hint} onChange={(e) => setEdit({ ...edit, [k]: e.target.value })} /></label>;
  return (
    <Modal title={edit.id ? 'Edit equipment' : 'Add equipment'} onClose={() => setEdit(null)}>
      <div className="form-grid">
        {f('assetNumber', 'Asset number', 'text', 'From the asset label')}{f('name', 'What it is', 'text', 'e.g. Diathermy unit')}
        <label>Category<select value={edit.category} onChange={(e) => setEdit({ ...edit, category: e.target.value })}>{CATS.map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
        {f('make', 'Make')}{f('model', 'Model')}{f('serialNumber', 'Serial number')}
        <label>Room<select value={edit.locationId ?? ''} onChange={(e) => setEdit({ ...edit, locationId: e.target.value })}><option value="">Not fixed</option>{locations.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}</select></label>
        {f('owner', 'Owner / department')}
        <label>Supplier<select value={edit.supplierId ?? ''} onChange={(e) => setEdit({ ...edit, supplierId: e.target.value })}><option value="">—</option>{suppliers.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}</select></label>
        {f('purchasedOn', 'Purchased', 'date')}{f('value', 'Value ($)', 'number')}{f('warrantyUntil', 'Warranty until', 'date')}
        <label>Risk class<select value={edit.risk} onChange={(e) => setEdit({ ...edit, risk: e.target.value })}>{['Low', 'Medium', 'High'].map((r) => <option key={r}>{r}</option>)}</select></label>
        {['Monitor', 'EndoscopyTower', 'Washer', 'Steriliser', 'Aer'].includes(edit.category) && f('networkAddress', 'Network address (for device connection)', 'text', 'e.g. 192.168.1.170')}
        {f('notes', 'Notes')}
      </div>
      <div className="row"><button className="btn btn-primary" onClick={onSave}>Save</button><button className="btn btn-light" onClick={() => setEdit(null)}>Cancel</button></div>
    </Modal>
  );
}

function EquipmentDetail({ sel, manage, act, reload, onEdit }: { sel: any; manage: boolean; locations: Location[]; suppliers: any[]; act: (fn: () => Promise<any>, ok: string) => Promise<any>; reload: () => void; onEdit: () => void }) {
  const e = sel.equipment;
  const [plan, setPlan] = useState<any>(null);
  const [rec, setRec] = useState<any>(null);
  const [reason, setReason] = useState('');
  return (
    <div className="card subtle stack">
      <div className="row"><h2>{e.assetNumber} {e.name}</h2>{manage && <button className="btn btn-light btn-small" onClick={onEdit}>Edit</button>}</div>
      <p className="small muted">{[e.make, e.model, e.serialNumber && `serial ${e.serialNumber}`, e.value != null && money(e.value), e.warrantyUntil && `warranty to ${formatDate(e.warrantyUntil)}`].filter(Boolean).join(' · ')}</p>
      {e.status === 'OutOfService' && <p className="error-text"><strong>OUT OF SERVICE</strong> – {e.outOfServiceReason} (by {e.lockedOutBy})</p>}
      <div className="row">
        <label>{e.status === 'OutOfService' ? 'What was done to make it safe?' : 'Reason (for lockout or retiring)'}<input value={reason} onChange={(x) => setReason(x.target.value)} /></label>
        {e.status === 'InService' && <button className="btn btn-warn" onClick={async () => { if (await act(() => api(`/api/assets/equipment/${e.id}/lockout`, { body: { reason } }), 'Taken out of service – put the lockout tag on it.')) { setReason(''); reload(); } }}>Take out of service</button>}
        {e.status === 'OutOfService' && manage && <button className="btn btn-primary" onClick={async () => { if (await act(() => api(`/api/assets/equipment/${e.id}/return`, { body: { reason } }), 'Back in service.')) { setReason(''); reload(); } }}>Return to service</button>}
        {e.status !== 'Retired' && manage && <button className="btn btn-light" onClick={async () => { if (await act(() => api(`/api/assets/equipment/${e.id}/retire`, { body: { reason } }), 'Retired.')) { setReason(''); reload(); } }}>Retire</button>}
      </div>
      <h3>Schedule</h3>
      {sel.plans.length === 0 ? <p className="muted">No checks scheduled.</p> : <ul className="plain">{sel.plans.map((p: any) => <li key={p.id}>{kindName(p.kind)} every {p.intervalMonths} months – next due <strong>{formatDate(p.nextDue)}</strong>{p.doneBy && ` (${p.doneBy})`}</li>)}</ul>}
      {manage && !plan && <div className="row"><button className="btn btn-light" onClick={() => setPlan({ kind: 'TestAndTag', intervalMonths: 12, nextDue: todayIso(), doneBy: '' })}>Schedule a check</button>
        <button className="btn btn-light" onClick={() => setRec({ kind: 'TestAndTag', doneOn: todayIso(), doneBy: '', passed: true, tagNumber: '', notes: '' })}>Record a check</button></div>}
      {plan && <div className="form-grid">
        <label>Check<select value={plan.kind} onChange={(x) => setPlan({ ...plan, kind: x.target.value })}>{KINDS.map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
        <label>Every (months)<input type="number" value={plan.intervalMonths} onChange={(x) => setPlan({ ...plan, intervalMonths: x.target.value })} /></label>
        <label>Next due<input type="date" value={plan.nextDue} onChange={(x) => setPlan({ ...plan, nextDue: x.target.value })} /></label>
        <label>Done by<input value={plan.doneBy} onChange={(x) => setPlan({ ...plan, doneBy: x.target.value })} placeholder="Person or company" /></label>
        <div className="row"><button className="btn btn-primary" onClick={async () => { if (await act(() => api('/api/assets/equipment/plans', { body: { ...plan, equipmentId: e.id, intervalMonths: Number(plan.intervalMonths) } }), 'Schedule saved.')) { setPlan(null); reload(); } }}>Save</button>
          <button className="btn btn-light" onClick={() => setPlan(null)}>Cancel</button></div></div>}
      {rec && <div className="form-grid">
        <label>Check<select value={rec.kind} onChange={(x) => setRec({ ...rec, kind: x.target.value })}>{[...KINDS, ['Repair', 'Repair']].map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
        <label>Date<input type="date" value={rec.doneOn} onChange={(x) => setRec({ ...rec, doneOn: x.target.value })} /></label>
        <label>Done by<input value={rec.doneBy} onChange={(x) => setRec({ ...rec, doneBy: x.target.value })} /></label>
        <label>Result<select value={rec.passed ? 'pass' : 'fail'} onChange={(x) => setRec({ ...rec, passed: x.target.value === 'pass' })}><option value="pass">Pass</option><option value="fail">Fail</option></select></label>
        {rec.kind === 'TestAndTag' && <label>Tag number<input value={rec.tagNumber} onChange={(x) => setRec({ ...rec, tagNumber: x.target.value })} /></label>}
        <label>Notes<input value={rec.notes} onChange={(x) => setRec({ ...rec, notes: x.target.value })} /></label>
        <div className="row"><button className="btn btn-primary" onClick={async () => { if (await act(() => api('/api/assets/equipment/maintenance', { body: { ...rec, equipmentId: e.id } }), rec.passed ? 'Recorded.' : 'Recorded as FAILED – the equipment has been taken out of service.')) { setRec(null); reload(); } }}>Save</button>
          <button className="btn btn-light" onClick={() => setRec(null)}>Cancel</button></div></div>}
      <h3>History</h3>
      {sel.history.length === 0 ? <p className="muted">Nothing recorded yet.</p> : (
        <table className="results compact"><thead><tr><th>Date</th><th>Check</th><th>Result</th><th>By</th><th>Notes</th></tr></thead>
          <tbody>{sel.history.map((h: any) => <tr key={h.id}><td>{formatDate(h.doneOn)}</td><td>{kindName(h.kind)}{h.tagNumber && <div className="small muted">Tag {h.tagNumber}</div>}</td>
            <td>{h.passed ? <span className="pill green">Pass</span> : <span className="pill bad">Fail</span>}</td><td>{h.doneBy}</td><td className="small">{h.notes}</td></tr>)}</tbody></table>)}
    </div>
  );
}

function Suppliers({ rows, manage, act, reload }: { rows: any[]; manage: boolean; act: (fn: () => Promise<any>, ok: string) => Promise<any>; reload: () => void }) {
  const [edit, setEdit] = useState<any>(null);
  const f = (k: string, label: string) => <label>{label}<input value={edit[k] ?? ''} onChange={(e) => setEdit({ ...edit, [k]: e.target.value })} /></label>;
  return (
    <div className="stack">
      {rows.length === 0 ? <p className="muted">No suppliers yet.</p> : (
        <table className="results compact"><thead><tr><th>Supplier</th><th>Contact</th><th>Xero contact</th><th></th></tr></thead>
          <tbody>{rows.map((s) => <tr key={s.id}><td>{s.name}{!s.isActive && ' (inactive)'}</td><td className="small">{[s.contactName, s.email, s.phone].filter(Boolean).join(' · ')}</td><td>{s.xeroContactName ?? ''}</td>
            <td>{manage && <button className="btn btn-link" onClick={() => setEdit(s)}>Edit</button>}</td></tr>)}</tbody></table>)}
      {manage && !edit && <div><button className="btn btn-light" onClick={() => setEdit({ name: '', contactName: '', email: '', phone: '', xeroContactName: '', isActive: true })}>Add supplier</button></div>}
      {edit && <Modal title={edit.id ? `Edit ${edit.name}` : 'New supplier'} onClose={() => setEdit(null)}><div className="form-grid">{f('name', 'Name')}{f('contactName', 'Contact person')}{f('email', 'Orders email')}{f('phone', 'Phone')}{f('xeroContactName', 'Name in Xero (for bills)')}
        <label className="check"><input type="checkbox" checked={edit.isActive} onChange={(e) => setEdit({ ...edit, isActive: e.target.checked })} /> Active</label></div>
        <div className="row"><button className="btn btn-primary" onClick={async () => { if (await act(() => api('/api/assets/suppliers', { body: { ...edit, id: edit.id ?? null } }), 'Supplier saved.')) { setEdit(null); reload(); } }}>Save</button>
          <button className="btn btn-light" onClick={() => setEdit(null)}>Cancel</button></div></Modal>}
    </div>
  );
}
