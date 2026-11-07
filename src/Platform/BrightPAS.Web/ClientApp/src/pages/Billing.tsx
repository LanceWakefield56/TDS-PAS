import { useEffect, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { api, errText, formatDate, money, Perm } from '../api';
import { useSession } from '../App';

export interface Totals { subtotal: number; gst: number; total: number; paid: number; balance: number; surgeonShare: number; anaesthetistShare: number; facilityShare: number }
interface DocRow { id: string; kind: string; number: string | null; patientId: string; patientName: string; mrn: string; payer: string; payerName: string | null; status: string; createdAt: string; issuedOn: string | null; dueOn: string | null; totals: Totals }
export interface Fee { id: string; code: string; description: string; price: number; gstApplicable: boolean; split: boolean; surgeonPercent: number | null; anaesthetistPercent: number | null; isActive: boolean }
interface Settings { surgeonPercent: number; anaesthetistPercent: number; gstPercent: number; estimateValidDays: number; paymentTermsDays: number; gstNumber: string | null; bankAccount: string | null }
export const docStatusClass = (s: string) => ({ Paid: 'green', Accepted: 'green', Issued: 'blue', PartPaid: 'amber', Draft: '', Void: 'red', Declined: 'red', Invoiced: 'green' } as Record<string, string>)[s] ?? '';
export const payerText = (p: string, n?: string | null) => ({ SelfFunded: 'Self-funded', Insurer: `Insurer${n ? ` – ${n}` : ''}`, Acc: 'ACC', HealthNz: 'Health NZ', Other: n ?? 'Other' } as Record<string, string>)[p] ?? p;

export default function Billing() {
  const { can, writable } = useSession();
  const navigate = useNavigate();
  const [params] = useSearchParams();
  const [tab, setTab] = useState(['fees', 'settings', 'outstanding', 'Invoice'].includes(params.get('tab') ?? '') ? params.get('tab')! : 'Estimate');
  const [docs, setDocs] = useState<DocRow[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    if (tab === 'fees' || tab === 'settings') return;
    setDocs(null);
    const q = tab === 'outstanding' ? 'kind=Invoice&outstanding=true' : `kind=${tab}`;
    api<DocRow[]>(`/api/billing/documents?${q}`).then(setDocs).catch((e) => setError(errText(e)));
  }, [tab]);
  return (
    <div className="stack-lg">
      <div className="row-between">
        <div className="page-head"><h1>Estimates &amp; invoices</h1><p className="muted">Prices come from the fee schedule. Fee splits are worked out automatically.</p></div>
        {can(Perm.BillingManage) && writable('billing') && <div className="row"><Link className="btn btn-primary" to="/billing/new?kind=Estimate">New estimate</Link><Link className="btn btn-light" to="/billing/new?kind=Invoice">New invoice</Link></div>}
      </div>
      <section className="card">
        <div className="tabs">{[['Estimate', 'Estimates'], ['Invoice', 'Invoices'], ['outstanding', 'Owing'], ['fees', 'Fee schedule'], ...(can(Perm.BillingFees) ? [['settings', 'Settings']] : [])].map(([k, t]) =>
          <button key={k} className={tab === k ? 'on' : ''} onClick={() => setTab(k)}>{t}</button>)}</div>
        {error && <p className="error">{error}</p>}
        {tab === 'fees' && <Fees />}
        {tab === 'settings' && <SettingsForm />}
        {docs && tab !== 'fees' && tab !== 'settings' && (docs.length === 0 ? <p className="muted">Nothing here yet.</p> : (
          <table className="results">
            <thead><tr><th>Number</th><th>Patient</th><th>Payer</th><th>Date</th><th className="money">Total</th><th className="money">Owing</th><th>Status</th></tr></thead>
            <tbody>{docs.map((d) => (
              <tr key={d.id} tabIndex={0} onClick={() => navigate(`/billing/${d.id}`)} onKeyDown={(e) => e.key === 'Enter' && navigate(`/billing/${d.id}`)}>
                <td data-label="Number"><strong>{d.number ?? 'Draft'}</strong></td>
                <td data-label="Patient">{d.patientName}<div className="muted small">{d.mrn}</div></td>
                <td data-label="Payer">{payerText(d.payer, d.payerName)}</td>
                <td data-label="Date">{d.issuedOn ? formatDate(d.issuedOn) : formatDate(d.createdAt.slice(0, 10))}</td>
                <td data-label="Total" className="money">{money(d.totals.total)}</td>
                <td data-label="Owing" className="money">{d.kind === 'Invoice' ? money(d.totals.balance) : '—'}</td>
                <td data-label="Status"><span className={`pill ${docStatusClass(d.status)}`}>{d.status === 'PartPaid' ? 'Part paid' : d.status}</span></td>
              </tr>
            ))}</tbody>
          </table>
        ))}
      </section>
    </div>
  );
}

function Fees() {
  const { can, writable } = useSession();
  const [fees, setFees] = useState<Fee[] | null>(null);
  const [edit, setEdit] = useState<Fee | null>(null);
  const [error, setError] = useState<string | null>(null);
  const load = () => api<Fee[]>('/api/billing/fees?all=true').then(setFees).catch((e) => setError(errText(e)));
  useEffect(() => { load(); }, []);
  const save = async () => { setError(null); try { await api('/api/billing/fees', { body: edit }); setEdit(null); load(); } catch (e) { setError(errText(e)); } };
  const example = async () => { await api('/api/billing/fees/example', { body: {} }); load(); };
  const editable = can(Perm.BillingFees) && writable('billing');
  const blank: Fee = { id: crypto.randomUUID(), code: '', description: '', price: 0, gstApplicable: true, split: false, surgeonPercent: null, anaesthetistPercent: null, isActive: true };
  return (
    <div className="stack">
      {error && <p className="error">{error}</p>}
      {fees?.length === 0 && <p className="muted">The fee schedule is empty.{editable && <> <button className="btn btn-link" onClick={example}>Load example items (sandbox)</button></>}</p>}
      {fees && fees.length > 0 && (
        <table className="results compact">
          <thead><tr><th>Code</th><th>Description</th><th className="money">Price (excl GST)</th><th>Split</th><th>Status</th>{editable && <th></th>}</tr></thead>
          <tbody>{fees.map((f) => (
            <tr key={f.id}>
              <td data-label="Code"><strong>{f.code}</strong></td><td data-label="Description">{f.description}</td><td data-label="Price" className="money">{money(f.price)}</td>
              <td data-label="Split">{f.split ? (f.surgeonPercent != null ? `${f.surgeonPercent}% / ${f.anaesthetistPercent ?? 0}%` : 'Default') : 'Facility only'}</td>
              <td data-label="Status">{f.isActive ? 'In use' : 'Not used'}</td>
              {editable && <td><button className="btn btn-link" onClick={() => setEdit(f)}>Edit</button></td>}
            </tr>
          ))}</tbody>
        </table>
      )}
      {editable && !edit && <div><button className="btn btn-light" onClick={() => setEdit(blank)}>Add fee item</button></div>}
      {edit && (
        <div className="card subtle stack">
          <div className="form-grid">
            <label>Code<input value={edit.code} onChange={(e) => setEdit({ ...edit, code: e.target.value })} /></label>
            <label className="span-all">Description<input value={edit.description} onChange={(e) => setEdit({ ...edit, description: e.target.value })} /></label>
            <label>Price excl GST<input type="number" step="0.01" value={edit.price} onChange={(e) => setEdit({ ...edit, price: Number(e.target.value) })} /></label>
            <label className="check"><input type="checkbox" checked={edit.gstApplicable} onChange={(e) => setEdit({ ...edit, gstApplicable: e.target.checked })} /> GST applies</label>
            <label className="check"><input type="checkbox" checked={edit.split} onChange={(e) => setEdit({ ...edit, split: e.target.checked })} /> Procedure fee (split with clinicians)</label>
            {edit.split && <label>Surgeon % (blank = default)<input type="number" value={edit.surgeonPercent ?? ''} onChange={(e) => setEdit({ ...edit, surgeonPercent: e.target.value === '' ? null : Number(e.target.value) })} /></label>}
            {edit.split && <label>Anaesthetist % (blank = default)<input type="number" value={edit.anaesthetistPercent ?? ''} onChange={(e) => setEdit({ ...edit, anaesthetistPercent: e.target.value === '' ? null : Number(e.target.value) })} /></label>}
            <label className="check"><input type="checkbox" checked={edit.isActive} onChange={(e) => setEdit({ ...edit, isActive: e.target.checked })} /> In use</label>
          </div>
          <div className="row"><button className="btn btn-light" onClick={() => setEdit(null)}>Cancel</button><button className="btn btn-primary" onClick={save}>Save</button></div>
        </div>
      )}
    </div>
  );
}

function SettingsForm() {
  const [s, setS] = useState<Settings | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => { api<Settings>('/api/billing/settings').then(setS).catch((e) => setError(errText(e))); }, []);
  if (!s) return error ? <p className="error">{error}</p> : <p>Loading…</p>;
  const save = async () => { setError(null); setMsg(null); try { await api('/api/billing/settings', { method: 'PUT', body: s }); setMsg('Saved.'); } catch (e) { setError(errText(e)); } };
  return (
    <div className="stack">
      <p className="hint">Default split of procedure fees. The facility receives the remainder ({100 - s.surgeonPercent - s.anaesthetistPercent}%).</p>
      <div className="form-grid">
        <label>Surgeon %<input type="number" value={s.surgeonPercent} onChange={(e) => setS({ ...s, surgeonPercent: Number(e.target.value) })} /></label>
        <label>Anaesthetist %<input type="number" value={s.anaesthetistPercent} onChange={(e) => setS({ ...s, anaesthetistPercent: Number(e.target.value) })} /></label>
        <label>GST %<input type="number" value={s.gstPercent} onChange={(e) => setS({ ...s, gstPercent: Number(e.target.value) })} /></label>
        <label>Estimates valid (days)<input type="number" value={s.estimateValidDays} onChange={(e) => setS({ ...s, estimateValidDays: Number(e.target.value) })} /></label>
        <label>Invoice terms (days)<input type="number" value={s.paymentTermsDays} onChange={(e) => setS({ ...s, paymentTermsDays: Number(e.target.value) })} /></label>
        <label>GST number<input value={s.gstNumber ?? ''} onChange={(e) => setS({ ...s, gstNumber: e.target.value })} /></label>
        <label>Bank account for payments<input value={s.bankAccount ?? ''} onChange={(e) => setS({ ...s, bankAccount: e.target.value })} /></label>
      </div>
      {msg && <p className="notice">{msg}</p>}{error && <p className="error">{error}</p>}
      <div><button className="btn btn-primary" onClick={save}>Save settings</button></div>
    </div>
  );
}
