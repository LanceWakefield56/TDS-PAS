import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { api, ApiError, errText, formatDate, nzDateTime, Perm, todayIso } from '../api';
import { useSession } from '../App';
import Modal from '../components/Modal';

/** Prescribing and barcode medicine checks (0.31.0, ADR 0044). */
const errs = (e: unknown) => (e instanceof ApiError && e.body?.errors ? e.body.errors as string[] : [errText(e)]);
const RX_TEXT: Record<string, string> = { Draft: 'Not signed yet', Signed: 'Signed – print for the patient', Sent: 'Sent to NZePS', Failed: 'Not sent – send again or print', Cancelled: 'Cancelled' };
const RX_PILL: Record<string, string> = { Draft: 'pill amber', Signed: 'pill', Sent: 'pill green', Failed: 'pill red', Cancelled: 'pill' };
const KIND_TEXT: Record<string, string> = { Once: 'Once', Regular: 'Regular', AsNeeded: 'As needed' };
const ORDER_TEXT: Record<string, string> = { Active: 'Active', Given: 'Given', Stopped: 'Stopped' };
const ROUTES = ['IV', 'IM', 'SC', 'Oral', 'SL', 'PR', 'Inhaled', 'Topical', 'Infiltration', 'Nerve block'];

function Errors({ list }: { list: string[] }) {
  return list.length > 0 ? <div className="error" role="alert"><ul>{list.map((e) => <li key={e}>{e}</li>)}</ul></div> : null;
}

export default function Prescribing() {
  const [params, setParams] = useSearchParams();
  const { can } = useSession();
  const tab = params.get('tab') ?? 'prescriptions';
  const tabs: [string, string][] = [['prescriptions', 'Prescriptions'], ['attention', 'Needs attention'], ['prescribers', 'Prescribers']];
  if (can(Perm.RxManage)) tabs.push(['report', 'Barcode check report'], ['settings', 'Settings']);
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Prescribing</h1><p className="muted">Medicine orders on the chart, barcode checks when medicines are given, and prescriptions to take home (printed with the NZePS barcode). Write a prescription from the patient's clinical record (Discharge).</p></div>
      <div className="tabs" role="tablist">{tabs.map(([k, t]) => <button key={k} role="tab" aria-selected={tab === k} className={tab === k ? 'on' : ''} onClick={() => setParams({ tab: k })}>{t}</button>)}</div>
      {(tab === 'prescriptions' || tab === 'attention') && <section className="card stack"><PrescriptionList view={tab === 'attention' ? 'attention' : null} /></section>}
      {tab === 'prescribers' && <Prescribers />}
      {tab === 'report' && <Report />}
      {tab === 'settings' && <Settings />}
    </div>
  );
}

/** Prescriptions, all or for one patient, with sign / send again / print / cancel. */
export function PrescriptionList({ view, patientId, episodeId, refreshKey }: { view?: string | null; patientId?: string; episodeId?: string; refreshKey?: number }) {
  const { can } = useSession();
  const [rows, setRows] = useState<any[] | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const [cancelling, setCancelling] = useState<any>(null);
  const [reason, setReason] = useState('');
  const q = new URLSearchParams();
  if (patientId) q.set('patientId', patientId);
  if (view) q.set('view', view);
  const load = () => api<any[]>(`/api/prescribing/prescriptions?${q}`).then((x) => setRows(episodeId ? x.filter((p) => p.episodeId === episodeId || !p.episodeId) : x)).catch((e) => setErrors(errs(e)));
  useEffect(() => { void load(); }, [view, patientId, episodeId, refreshKey]); // eslint-disable-line react-hooks/exhaustive-deps
  const act = async (fn: () => Promise<any>, done: (x: any) => string) => {
    setErrors([]); setMsg(null);
    try { const x = await fn(); setMsg(done(x)); await load(); return true; } catch (e) { setErrors(errs(e)); return false; }
  };
  return (
    <div className="stack">
      {msg && <p className="notice" role="status">{msg}</p>}
      <Errors list={errors} />
      {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">No prescriptions.</p> : (
        <div className="table-scroll" tabIndex={0} role="region" aria-label="Prescriptions">
          <table className="results">
            <thead><tr><th>Number</th>{!patientId && <th>Patient</th>}<th>Medicines</th><th>Prescriber</th><th>Status</th><th>Actions</th></tr></thead>
            <tbody>{rows.map((r) => (
              <tr key={r.id}>
                <td>{r.number}<div className="small muted">{nzDateTime(r.createdAt)}</div></td>
                {!patientId && <td><Link to={`/patients/${r.patientId}`}>{r.patient}</Link><div className="small muted">{r.nhi ?? ''}</div></td>}
                <td className="small">{r.items.map((i: any) => <div key={i.id}><strong>{i.medicine}</strong> – {i.directions} ({i.quantity} {i.unit}{i.repeats ? `, ${i.repeats} repeat${i.repeats > 1 ? 's' : ''}` : ''}){i.controlled && <span className="pill">CD</span>}</div>)}</td>
                <td className="small">{r.prescriber}</td>
                <td><span className={RX_PILL[r.status]}>{RX_TEXT[r.status]}</span>{r.sendError && <div className="small error-text">{r.sendError}</div>}{r.cancelReason && <div className="small muted">{r.cancelReason}</div>}
                  <div className="small muted">NZePS {r.nzepsId}</div></td>
                <td><div className="row">
                  {r.status === 'Draft' && can(Perm.RxPrescribe) && <button className="btn btn-primary btn-small" onClick={() => void act(() => api(`/api/prescribing/prescriptions/${r.id}/sign`, { body: {} }), (x) => x?.message ?? (x?.status === 'Sent' ? 'Signed and sent to NZePS.' : 'Signed. Print it for the patient.'))}>Sign</button>}
                  {r.status === 'Failed' && can(Perm.RxPrescribe) && <button className="btn btn-light btn-small" onClick={() => void act(() => api(`/api/prescribing/prescriptions/${r.id}/resend`, { body: {} }), (x) => x?.message ?? 'Sent.')}>Send again</button>}
                  {['Signed', 'Sent', 'Failed'].includes(r.status) && <a className="btn btn-light btn-small" href={`/api/prescribing/prescriptions/${r.id}/pdf`} target="_blank" rel="noreferrer" onClick={() => window.setTimeout(() => void load(), 1500)}>Print{r.timesPrinted > 0 ? ' again' : ''}</a>}
                  {r.status !== 'Cancelled' && can(Perm.RxPrescribe) && <button className="btn btn-light btn-small" onClick={() => { setCancelling(r); setReason(''); }}>Cancel prescription</button>}
                </div></td>
              </tr>))}</tbody>
          </table>
        </div>)}
      {cancelling && <Modal title={`Cancel ${cancelling.number}`} onClose={() => setCancelling(null)}>
        <p>{cancelling.status === 'Sent' ? 'It is also cancelled in NZePS (unless a pharmacy has already dispensed it).' : 'The printed copy, if given out, should be taken back.'}</p>
        <label>Why<input value={reason} onChange={(e) => setReason(e.target.value)} /></label>
        <div className="row"><button className="btn btn-primary" onClick={async () => { if (await act(() => api(`/api/prescribing/prescriptions/${cancelling.id}/cancel`, { body: { reason } }), () => 'Prescription cancelled.')) setCancelling(null); }}>Cancel prescription</button>
          <button className="btn btn-light" onClick={() => setCancelling(null)}>Keep it</button></div>
      </Modal>}
    </div>
  );
}

/** Discharge prescriptions in the clinical record: the list, and "New prescription" for prescribers. */
export function DischargeRx({ patientId, episodeId }: { patientId: string; episodeId: string }) {
  const { can } = useSession();
  const [me, setMe] = useState<any>(undefined);
  const [open, setOpen] = useState(false);
  const [key, setKey] = useState(0);
  useEffect(() => { if (can(Perm.RxPrescribe)) api<any>('/api/prescribing/prescribers/me').then((x) => setMe(x.prescriber)).catch(() => setMe(null)); }, []); // eslint-disable-line react-hooks/exhaustive-deps
  return (
    <div className="stack">
      <div className="row-between"><h2>Prescriptions to take home</h2>
        {can(Perm.RxPrescribe) && me && <button className="btn btn-primary btn-small" onClick={() => setOpen(true)}>New prescription</button>}</div>
      {can(Perm.RxPrescribe) && me === null && <p className="hint">You are not set up as a prescriber yet – ask the administrator to add you under Prescribing › Prescribers.</p>}
      <PrescriptionList patientId={patientId} episodeId={episodeId} refreshKey={key} />
      {open && <NewPrescription patientId={patientId} episodeId={episodeId} onClose={() => setOpen(false)} onDone={() => { setOpen(false); setKey(key + 1); }} />}
    </div>
  );
}

const blankItem = () => ({ medicine: '', medicineId: null as string | null, directions: '', quantity: '', unit: 'tablet', repeats: '0', controlled: false });

function NewPrescription({ patientId, episodeId, onClose, onDone }: { patientId: string; episodeId: string; onClose: () => void; onDone: () => void }) {
  const [items, setItems] = useState([blankItem()]);
  const [note, setNote] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [options, setOptions] = useState<Record<number, any[]>>({});
  const set = (i: number, v: Partial<ReturnType<typeof blankItem>>) => setItems(items.map((x, k) => (k === i ? { ...x, ...v } : x)));
  const search = (i: number, text: string) => {
    set(i, { medicine: text, medicineId: null });
    if (text.trim().length < 2) return;
    api<any[]>(`/api/medicines/search?q=${encodeURIComponent(text.trim())}`).then((x) => setOptions((o) => ({ ...o, [i]: x }))).catch(() => undefined);
  };
  const save = async (sign: boolean) => {
    setErrors([]);
    try {
      const r = await api<any>('/api/prescribing/prescriptions', { body: { patientId, episodeId, note: note || null, items: items.map((x) => ({ medicineId: x.medicineId, medicine: x.medicine, directions: x.directions, quantity: Number(x.quantity) || 0, unit: x.unit, repeats: Number(x.repeats) || 0, controlled: x.controlled })) } });
      if (sign) await api(`/api/prescribing/prescriptions/${r.id}/sign`, { body: {} });
      onDone();
    } catch (e) { setErrors(errs(e)); }
  };
  return (
    <Modal title="New prescription" onClose={onClose} wide>
      <Errors list={errors} />
      {items.map((x, i) => (
        <fieldset key={i} className="stack"><legend>Medicine {i + 1}</legend>
          <div className="form-grid">
            <label>Medicine<input list={`rx-med-${i}`} value={x.medicine} onChange={(e) => { const o = (options[i] ?? []).find((m) => m.name === e.target.value); if (o) set(i, { medicine: o.name, medicineId: o.id }); else search(i, e.target.value); }} autoComplete="off" />
              <datalist id={`rx-med-${i}`}>{(options[i] ?? []).map((m) => <option key={m.id} value={m.name} />)}</datalist></label>
            <label>Quantity to supply<input type="number" min="1" value={x.quantity} onChange={(e) => set(i, { quantity: e.target.value })} /></label>
            <label>Unit<select value={x.unit} onChange={(e) => set(i, { unit: e.target.value })}>{['tablet', 'capsule', 'mL', 'g', 'patch', 'sachet', 'dose', 'pack'].map((u) => <option key={u}>{u}</option>)}</select></label>
            <label>Repeats<input type="number" min="0" max="11" value={x.repeats} onChange={(e) => set(i, { repeats: e.target.value })} /></label>
            <label className="check"><input type="checkbox" checked={x.controlled} onChange={(e) => set(i, { controlled: e.target.checked })} /> Controlled drug</label>
          </div>
          <label>Directions for the patient<textarea rows={2} value={x.directions} onChange={(e) => set(i, { directions: e.target.value })} placeholder="e.g. Take 2 tablets every 6 hours when needed for pain. No more than 8 in 24 hours." /></label>
          {items.length > 1 && <div><button className="btn btn-link" onClick={() => setItems(items.filter((_, k) => k !== i))}>Remove this medicine</button></div>}
        </fieldset>))}
      {items.length < 10 && <div><button className="btn btn-light btn-small" onClick={() => setItems([...items, blankItem()])}>Add another medicine</button></div>}
      <label>Note (optional)<input value={note} onChange={(e) => setNote(e.target.value)} /></label>
      <div className="row"><button className="btn btn-primary" onClick={() => void save(true)}>Save and sign</button><button className="btn btn-light" onClick={() => void save(false)}>Save as draft</button></div>
    </Modal>
  );
}

/** The medicine chart in the clinical record: orders, prescribe (prescribers), stop, and "Give" to fill the charting form. */
export function MedicineChart({ episodeId, write, onGive, refreshKey }: { episodeId: string; write: boolean; onGive: (o: any) => void; refreshKey: number }) {
  const { can } = useSession();
  const [rows, setRows] = useState<any[] | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const [adding, setAdding] = useState(false);
  const [stopping, setStopping] = useState<any>(null);
  const [reason, setReason] = useState('');
  const blank = { medicine: '', medicineId: null as string | null, dose: '', route: 'IV', kind: 'Once', frequency: '', indication: '', notes: '' };
  const [o, setO] = useState(blank);
  const [options, setOptions] = useState<any[]>([]);
  const load = () => api<any[]>(`/api/prescribing/episodes/${episodeId}/orders`).then(setRows).catch((e) => setErrors(errs(e)));
  useEffect(() => { void load(); }, [episodeId, refreshKey]); // eslint-disable-line react-hooks/exhaustive-deps
  const search = (text: string) => {
    const m = options.find((x) => x.name === text);
    if (m) { setO({ ...o, medicine: m.name, medicineId: m.id, route: m.defaultRoute ?? o.route }); return; }
    setO({ ...o, medicine: text, medicineId: null });
    if (text.trim().length >= 2) api<any[]>(`/api/medicines/search?q=${encodeURIComponent(text.trim())}`).then(setOptions).catch(() => undefined);
  };
  const prescribe = async () => {
    setErrors([]);
    try { await api(`/api/prescribing/episodes/${episodeId}/orders`, { body: { ...o, frequency: o.frequency || null, indication: o.indication || null, notes: o.notes || null } }); setAdding(false); setO(blank); await load(); }
    catch (e) { setErrors(errs(e)); }
  };
  const active = (rows ?? []).filter((r) => r.status === 'Active');
  return (
    <div className="stack">
      <div className="row-between"><h2>Medicine chart</h2>{write && can(Perm.RxPrescribe) && <button className="btn btn-light btn-small" onClick={() => { setAdding(true); setErrors([]); }}>Prescribe a medicine</button>}</div>
      {!adding && !stopping && <Errors list={errors} />}
      {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">No medicines ordered on the chart.</p> : (
        <table className="results compact"><thead><tr><th>Medicine</th><th>Dose / route</th><th>How often</th><th>Ordered by</th><th>Status</th><th>Actions</th></tr></thead>
          <tbody>{rows.map((r) => <tr key={r.id} className={r.status === 'Active' ? '' : 'muted'}>
            <td>{r.medicine}{r.indication && <div className="small muted">For {r.indication}</div>}</td><td>{r.dose} {r.route}</td>
            <td>{KIND_TEXT[r.kind]}{r.frequency ? ` – ${r.frequency}` : ''}</td><td className="small">{r.prescribedBy}<div className="muted">{nzDateTime(r.prescribedAt)}</div></td>
            <td>{ORDER_TEXT[r.status]}{r.timesGiven > 0 && <div className="small muted">Given {r.timesGiven}× · last {nzDateTime(r.lastGivenAt)}</div>}{r.stopReason && <div className="small muted">{r.stopReason}</div>}</td>
            <td><div className="row">{write && r.status === 'Active' && <button className="btn btn-light btn-small" onClick={() => onGive(r)}>Give</button>}
              {write && r.status === 'Active' && can(Perm.RxPrescribe) && <button className="btn btn-link" onClick={() => { setStopping(r); setReason(''); setErrors([]); }}>Stop order</button>}</div></td>
          </tr>)}</tbody></table>)}
      {active.length === 0 && rows && rows.length > 0 && <p className="hint">No active orders.</p>}
      {adding && <Modal title="Prescribe a medicine" onClose={() => setAdding(false)}>
        <Errors list={errors} />
        <div className="form-grid">
          <label>Medicine<input list="rx-order-med" value={o.medicine} onChange={(e) => search(e.target.value)} autoComplete="off" /><datalist id="rx-order-med">{options.map((m) => <option key={m.id} value={m.name} />)}</datalist></label>
          <label>Dose<input value={o.dose} onChange={(e) => setO({ ...o, dose: e.target.value })} placeholder="e.g. 1 g" /></label>
          <label>Route<select value={o.route} onChange={(e) => setO({ ...o, route: e.target.value })}>{ROUTES.map((x) => <option key={x}>{x}</option>)}</select></label>
          <label>Kind<select value={o.kind} onChange={(e) => setO({ ...o, kind: e.target.value })}><option value="Once">Once</option><option value="Regular">Regular</option><option value="AsNeeded">As needed</option></select></label>
          {o.kind !== 'Once' && <label>How often<input value={o.frequency} onChange={(e) => setO({ ...o, frequency: e.target.value })} placeholder="e.g. 6-hourly, max 4 doses in 24 h" /></label>}
          <label>{o.kind === 'AsNeeded' ? 'What for' : 'What for (optional)'}<input value={o.indication} onChange={(e) => setO({ ...o, indication: e.target.value })} /></label>
          <label>Notes (optional)<input value={o.notes} onChange={(e) => setO({ ...o, notes: e.target.value })} /></label>
        </div>
        <div className="row"><button className="btn btn-primary" onClick={() => void prescribe()}>Save order</button><button className="btn btn-light" onClick={() => setAdding(false)}>Cancel</button></div>
      </Modal>}
      {stopping && <Modal title={`Stop ${stopping.medicine}`} onClose={() => setStopping(null)}>
        <Errors list={errors} />
        <label>Why<input value={reason} onChange={(e) => setReason(e.target.value)} /></label>
        <div className="row"><button className="btn btn-primary" onClick={async () => { setErrors([]); try { await api(`/api/prescribing/orders/${stopping.id}/stop`, { body: { reason } }); setStopping(null); await load(); } catch (e) { setErrors(errs(e)); } }}>Stop order</button>
          <button className="btn btn-light" onClick={() => setStopping(null)}>Keep it</button></div>
      </Modal>}
    </div>
  );
}

/** Wristband scan and order choice in the "Medicines given" form, with what the scan says straight away. */
export function ScanFields({ patientId, wristband, barcode, orderId, orders, onChange }: { patientId: string; wristband: string; barcode: string; orderId: string; orders: any[]; onChange: (v: { wristband?: string; orderId?: string }) => void }) {
  const [look, setLook] = useState<any>(null);
  useEffect(() => {
    if (!wristband.trim() && !barcode.trim()) { setLook(null); return; }
    const t = window.setTimeout(() => { api<any>('/api/prescribing/scan', { body: { patientId, wristband: wristband || null, pack: barcode || null } }).then(setLook).catch(() => setLook(null)); }, 300);
    return () => window.clearTimeout(t);
  }, [patientId, wristband, barcode]);
  return (
    <>
      <label>Scan wristband<input value={wristband} onChange={(e) => onChange({ wristband: e.target.value })} autoComplete="off" placeholder="Scan the patient's wristband" /></label>
      <label>Given against order<select value={orderId} onChange={(e) => onChange({ orderId: e.target.value })}><option value="">No order / not on the chart</option>{orders.filter((o) => o.status === 'Active').map((o) => <option key={o.id} value={o.id}>{o.medicine} {o.dose} {o.route}</option>)}</select></label>
      {look && <div className="med-scan small" role="status">
        {look.patientText && <div className={look.patientMatched ? 'ok-text' : 'error-text'}>Wristband: {look.patientText}</div>}
        {look.pack && <div>Pack: {look.medicine ?? look.pack.gtin}{look.pack.batch ? ` · batch ${look.pack.batch}` : ''}{look.pack.expiry ? ` · expires ${formatDate(look.pack.expiry)}` : ''}{look.expired && <strong className="error-text"> – EXPIRED</strong>}</div>}
        {look.message && <div className="error-text">{look.message}</div>}
      </div>}
    </>
  );
}

function Prescribers() {
  const { can } = useSession();
  const [rows, setRows] = useState<any[] | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const [edit, setEdit] = useState<any>(null);
  const load = () => api<any[]>('/api/prescribing/prescribers').then(setRows).catch((e) => setErrors(errs(e)));
  useEffect(() => { void load(); }, []);
  const save = async () => {
    setErrors([]);
    try { await api(edit.id ? `/api/prescribing/prescribers/${edit.id}` : '/api/prescribing/prescribers', { method: edit.id ? 'PUT' : 'POST', body: edit }); setEdit(null); await load(); }
    catch (e) { setErrors(errs(e)); }
  };
  return (
    <section className="card stack">
      <div className="row-between"><p className="muted">Doctors and nurse practitioners who write prescriptions in BrightPAS, with their HPI person number (CPN) – printed and sent on every prescription.</p>
        {can(Perm.RxManage) && <button className="btn btn-light btn-small" onClick={() => setEdit({ userName: '', name: '', hpiCpn: '', registration: '', scope: 'Medical practitioner', isActive: true })}>Add a prescriber</button>}</div>
      {!edit && <Errors list={errors} />}
      {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">No prescribers yet.</p> : (
        <table className="results"><thead><tr><th>Name</th><th>User</th><th>HPI CPN</th><th>Registration</th><th>Scope</th><th>Active</th>{can(Perm.RxManage) && <th>Actions</th>}</tr></thead>
          <tbody>{rows.map((r) => <tr key={r.id}><td>{r.name}</td><td>{r.userName}</td><td>{r.hpiCpn}</td><td>{r.registration ?? '—'}</td><td>{r.scope}</td><td>{r.isActive ? 'Yes' : 'No'}</td>
            {can(Perm.RxManage) && <td><button className="btn btn-link" onClick={() => setEdit({ ...r })}>Edit</button></td>}</tr>)}</tbody></table>)}
      {edit && <Modal title={edit.id ? `Edit ${edit.name}` : 'Add a prescriber'} onClose={() => setEdit(null)}>
        <Errors list={errors} />
        <div className="form-grid">
          <label>BrightPAS user name<input value={edit.userName} onChange={(e) => setEdit({ ...edit, userName: e.target.value })} /></label>
          <label>Name as printed<input value={edit.name} onChange={(e) => setEdit({ ...edit, name: e.target.value })} /></label>
          <label>HPI person number (CPN)<input value={edit.hpiCpn} onChange={(e) => setEdit({ ...edit, hpiCpn: e.target.value })} placeholder="12ABCD" /></label>
          <label>Council registration<input value={edit.registration ?? ''} onChange={(e) => setEdit({ ...edit, registration: e.target.value })} /></label>
          <label>Scope<select value={edit.scope} onChange={(e) => setEdit({ ...edit, scope: e.target.value })}>{['Medical practitioner', 'Nurse practitioner', 'Dentist', 'Designated prescriber'].map((s) => <option key={s}>{s}</option>)}</select></label>
          <label className="check"><input type="checkbox" checked={edit.isActive} onChange={(e) => setEdit({ ...edit, isActive: e.target.checked })} /> Active</label>
        </div>
        <div className="row"><button className="btn btn-primary" onClick={() => void save()}>Save</button><button className="btn btn-light" onClick={() => setEdit(null)}>Cancel</button></div>
      </Modal>}
    </section>
  );
}

function Report() {
  const [from, setFrom] = useState(() => { const d = new Date(); d.setDate(d.getDate() - 30); return d.toLocaleDateString('en-CA', { timeZone: 'Pacific/Auckland' }); });
  const [to, setTo] = useState(todayIso());
  const [r, setR] = useState<any>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const load = () => { setErrors([]); api<any>(`/api/prescribing/report?from=${from}&to=${to}`).then(setR).catch((e) => setErrors(errs(e))); };
  useEffect(load, []); // eslint-disable-line react-hooks/exhaustive-deps
  const pct = (n: number, d: number) => (d === 0 ? '—' : `${Math.round((100 * n) / d)}%`);
  return (
    <section className="card stack">
      <div className="row"><label>From<input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></label><label>To<input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></label>
        <button className="btn btn-light" onClick={load}>Show</button></div>
      <Errors list={errors} />
      {r && <>
        <div className="stat-row">
          <div className="stat"><strong>{r.given}</strong><span>medicines charted</span></div>
          <div className="stat"><strong>{pct(r.checked, r.given)}</strong><span>barcode checked</span></div>
          <div className="stat"><strong>{pct(r.patientScanned, r.checked)}</strong><span>wristband scanned</span></div>
          <div className="stat"><strong>{pct(r.packScanned, r.checked)}</strong><span>pack scanned</span></div>
          <div className="stat"><strong>{r.stopped}</strong><span>stopped</span></div>
          <div className="stat"><strong>{r.overridden}</strong><span>overridden</span></div>
        </div>
        <h2>Stops and overrides</h2>
        {r.exceptions.length === 0 ? <p className="muted">None in this period.</p> : (
          <div className="table-scroll" tabIndex={0} role="region" aria-label="Stops and overrides">
            <table className="results compact"><thead><tr><th>When</th><th>Who</th><th>Medicine</th><th>Result</th><th>What was found</th></tr></thead>
              <tbody>{r.exceptions.map((x: any) => <tr key={x.id}><td>{nzDateTime(x.at)}</td><td>{x.user}</td><td>{x.medicine}</td><td>{x.result === 'Overridden' ? 'Overridden' : 'Stopped'}{x.overrideReason && <div className="small muted">Reason: {x.overrideReason}</div>}</td>
                <td className="small prewrap">{x.findings}</td></tr>)}</tbody></table></div>)}
      </>}
    </section>
  );
}

function Settings() {
  const [s, setS] = useState<any>(null);
  const [key, setKey] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  useEffect(() => { api<any>('/api/prescribing/settings').then(setS).catch((e) => setErrors(errs(e))); }, []);
  if (!s) return <section className="card"><Errors list={errors} />{errors.length === 0 && <p>Loading…</p>}</section>;
  const save = async () => {
    setErrors([]); setMsg(null);
    try { await api('/api/prescribing/settings', { method: 'PUT', body: { ...s, nzepsKey: key || null } }); setKey(''); setMsg('Saved.'); setS(await api('/api/prescribing/settings')); }
    catch (e) { setErrors(errs(e)); }
  };
  return (
    <section className="card stack">
      {msg && <p className="notice" role="status">{msg}</p>}
      <Errors list={errors} />
      <h2>Barcode check when giving medicines</h2>
      <label>How it works<select value={s.bcmaMode} onChange={(e) => setS({ ...s, bcmaMode: e.target.value })}>
        <option value="Off">Off</option><option value="Advise">Advise – show what is found, never block</option><option value="Enforce">Enforce – a stop needs a reason (a wristband for someone else always stops)</option></select></label>
      <label className="check"><input type="checkbox" checked={s.warnWithoutOrder} onChange={(e) => setS({ ...s, warnWithoutOrder: e.target.checked })} /> Warn when a medicine has no order on the chart</label>
      <p className="hint">Wristbands carry the NHI (or MRN) as a barcode. Medicine pack barcodes (GTIN) come from the medicines list. Start with Advise and check the report before enforcing.</p>
      <h2>Prescriptions to take home</h2>
      <label>Sending<select value={s.provider} onChange={(e) => setS({ ...s, provider: e.target.value })}>
        <option value="Off">Print only</option><option value="Simulator">Simulator (training – nothing leaves BrightPAS)</option><option value="Nzeps">NZ ePrescription Service (NZePS)</option></select></label>
      {s.provider === 'Nzeps' && <div className="form-grid">
        <label>NZePS web address<input value={s.nzepsUrl ?? ''} onChange={(e) => setS({ ...s, nzepsUrl: e.target.value })} placeholder="https://…" /></label>
        <label>Client id<input value={s.nzepsClientId ?? ''} onChange={(e) => setS({ ...s, nzepsClientId: e.target.value })} /></label>
        <label>Key {s.nzepsKeySet && <span className="small muted">(saved – leave blank to keep)</span>}<input type="password" value={key} onChange={(e) => setKey(e.target.value)} autoComplete="new-password" /></label>
      </div>}
      <label>TDS's HPI facility id<input value={s.facilityHpi ?? ''} onChange={(e) => setS({ ...s, facilityHpi: e.target.value })} placeholder="F12345-A" /></label>
      <label>Folder for a copy of each prescription (optional)<input value={s.exportFolder ?? ''} onChange={(e) => setS({ ...s, exportFolder: e.target.value })} placeholder="e.g. \\server\share\prescriptions" /></label>
      <p className="hint">Connecting to NZePS needs Health NZ's agreement and keys. Until then prescriptions are printed with the NZePS barcode. The folder copy (FHIR file) is the fallback for when the connection is down.</p>
      {s.updatedAt && <p className="small muted">Last changed {nzDateTime(s.updatedAt)} by {s.updatedBy}</p>}
      <div><button className="btn btn-primary" onClick={() => void save()}>Save settings</button></div>
    </section>
  );
}
