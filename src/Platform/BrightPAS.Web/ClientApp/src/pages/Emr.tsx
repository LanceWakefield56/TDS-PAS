import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { api, errText, nzDateTime, Perm, todayIso, formatDate } from '../api';
import { useSession } from '../App';

const STAGE: Record<string, string> = { NotStarted: 'Not started', PreAdmission: 'Pre-admission', Admitted: 'Admitted', InTheatre: 'In theatre', Recovery: 'Recovery', ReadyForDischarge: 'Ready for discharge', Discharged: 'Discharged', Transferred: 'Transferred' };
const ewsPill = (n: number | null | undefined) => n == null ? '' : n >= 6 ? 'pill bad' : n >= 1 ? 'pill' : 'pill green';

/** Clinical record: today's patients and the controlled drug register. */
export default function Emr() {
  const { can } = useSession();
  const [params, setParams] = useSearchParams();
  const tab = params.get('tab') ?? 'today';
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Clinical record</h1><p className="muted">Today's patients – admission, Surgical Safety Checklist, theatre record, observations, medicines and discharge.</p></div>
      <section className="card stack">
        <div className="tabs" role="tablist">{[['today', "Today's patients"], ...(can(Perm.EmrControlledDrugs) ? [['cd', 'Controlled drug register']] : [])].map(([k, t]) =>
          <button key={k} role="tab" aria-selected={tab === k} className={tab === k ? 'on' : ''} onClick={() => setParams({ tab: k })}>{t}</button>)}</div>
        {tab === 'today' && <Today />}
        {tab === 'cd' && <Register />}
      </section>
    </div>
  );
}

function Today() {
  const [date, setDate] = useState(todayIso());
  const [rows, setRows] = useState<any[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => { setRows(null); api<any[]>(`/api/emr/today?date=${date}`).then(setRows).catch((e) => setError(errText(e))); }, [date]);
  return (
    <div className="stack">
      <label>Day<input type="date" value={date} onChange={(e) => setDate(e.target.value)} /></label>
      {error && <p className="error" role="alert">{error}</p>}
      {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">No patients booked on {formatDate(date)}.</p> : (
        <table className="results"><thead><tr><th>Time</th><th>Patient</th><th>Procedure</th><th>Where we are</th><th>Checklist</th><th>EWS</th></tr></thead>
          <tbody>{rows.map((r) => <tr key={r.bookingId}>
            <td>{r.start?.slice(0, 5)}<div className="small muted">{r.locationName}</div></td>
            <td><Link to={`/emr/${r.bookingId}`}>{r.patientName}</Link><div className="small muted">NHI {r.nhi ?? '—'} · {r.mrn}</div></td>
            <td>{r.procedure}<div className="small muted">{r.surgeon}{r.anaesthetist ? ` · ${r.anaesthetist}` : ''}</div></td>
            <td>{STAGE[r.stage] ?? r.stage}</td>
            <td className="small">{['SignIn', 'TimeOut', 'SignOut'].map((p) => <span key={p} className={r.checks.includes(p) ? 'pill green' : 'pill'}>{p === 'SignIn' ? 'In' : p === 'TimeOut' ? 'Time out' : 'Out'}</span>)}</td>
            <td>{r.ews != null && <span className={ewsPill(r.ews)}>{r.ews}</span>}</td></tr>)}</tbody></table>)}
    </div>
  );
}

function Register() {
  const [drugs, setDrugs] = useState<any[]>([]);
  const [sel, setSel] = useState<any>(null);
  const [rows, setRows] = useState<any[]>([]);
  const [add, setAdd] = useState({ name: '', strength: '', form: 'ampoule' });
  const [mv, setMv] = useState({ movement: 'Received', quantity: '', witnessedBy: '', reference: '' });
  const [error, setError] = useState<string | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const load = () => api<any[]>('/api/emr/controlled-drugs').then(setDrugs).catch((e) => setError(errText(e)));
  useEffect(() => { load(); }, []);
  const open = async (d: any) => { setSel(d); setRows(await api(`/api/emr/controlled-drugs/${d.id}/register`)); };
  const act = async (fn: () => Promise<any>, ok: string) => { setError(null); setMsg(null); try { await fn(); setMsg(ok); return true; } catch (x: any) { setError(x?.body?.errors?.join(' ') ?? errText(x)); return false; } };
  return (
    <div className="stack">
      {error && <p className="error" role="alert">{error}</p>}{msg && <p className="ok-text" role="status">{msg}</p>}
      <p className="muted">Each controlled drug has its own register (Misuse of Drugs Regulations 1977). Every entry is witnessed by a second person. Doses given to patients are entered from the patient's record.</p>
      {drugs.length === 0 ? <p className="muted">No controlled drugs yet.</p> : (
        <table className="results"><thead><tr><th>Drug</th><th>Balance</th></tr></thead>
          <tbody>{drugs.map((d) => <tr key={d.id} className={sel?.id === d.id ? 'selected' : ''}><td><button className="btn btn-link" onClick={() => open(d)}>{d.name} {d.strength} {d.form}</button></td><td>{d.balance}</td></tr>)}</tbody></table>)}
      <div className="card subtle row"><label>Drug<input value={add.name} onChange={(e) => setAdd({ ...add, name: e.target.value })} placeholder="e.g. Fentanyl" /></label>
        <label>Strength<input value={add.strength} onChange={(e) => setAdd({ ...add, strength: e.target.value })} placeholder="e.g. 100 mcg/2 ml" /></label>
        <label>Form<input value={add.form} onChange={(e) => setAdd({ ...add, form: e.target.value })} /></label>
        <button className="btn btn-light" onClick={async () => { if (await act(() => api('/api/emr/controlled-drugs', { body: add }), 'Added – now record the opening balance as "Received".')) { setAdd({ name: '', strength: '', form: 'ampoule' }); load(); } }}>Add to register</button></div>
      {sel && <div className="card subtle stack">
        <h2>{sel.name} {sel.strength} {sel.form} – balance {sel.balance}</h2>
        <div className="form-grid">
          <label>Entry<select value={mv.movement} onChange={(e) => setMv({ ...mv, movement: e.target.value })}>{[['Received', 'Received from pharmacy'], ['Wasted', 'Wasted (part used / dropped)'], ['Returned', 'Returned to pharmacy'], ['Correction', 'Correction after stock check (+/-)']].map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
          <label>Quantity<input type="number" value={mv.quantity} onChange={(e) => setMv({ ...mv, quantity: e.target.value })} /></label>
          <label>Witness (second person)<input value={mv.witnessedBy} onChange={(e) => setMv({ ...mv, witnessedBy: e.target.value })} /></label>
          <label>Reference / reason<input value={mv.reference} onChange={(e) => setMv({ ...mv, reference: e.target.value })} placeholder="Order number, stock-check reference…" /></label>
          <div><button className="btn btn-primary" onClick={async () => { if (await act(() => api('/api/emr/controlled-drugs/register', { body: { controlledDrugId: sel.id, movement: mv.movement, quantity: Number(mv.quantity), witnessedBy: mv.witnessedBy, reference: mv.reference || null } }), 'Entry recorded.')) { setMv({ ...mv, quantity: '', reference: '' }); await load(); const d = (await api<any[]>('/api/emr/controlled-drugs')).find((x) => x.id === sel.id); if (d) open(d); } }}>Record entry</button></div>
        </div>
        <table className="results compact"><thead><tr><th>When</th><th>Entry</th><th>Qty</th><th>Balance</th><th>Patient / reference</th><th>By / witness</th></tr></thead>
          <tbody>{rows.map((r) => <tr key={r.id}><td>{nzDateTime(r.at)}</td><td>{r.movement}</td><td>{r.quantity}</td><td>{r.balanceAfter}</td><td>{r.patient ?? ''} {r.reference ?? ''}</td><td>{r.by} / {r.witnessedBy}</td></tr>)}</tbody></table>
      </div>}
    </div>
  );
}
