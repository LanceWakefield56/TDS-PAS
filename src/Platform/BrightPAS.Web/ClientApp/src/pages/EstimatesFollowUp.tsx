import { useEffect, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { api, ApiError, errText, formatDate, money, nzDateTime, Perm, todayIso } from '../api';
import { useSession } from '../App';
import Modal from '../components/Modal';

/** Estimate follow-up (0.33.0, ADR 0046): where each issued estimate is up to, lists to chase, invoice from an estimate, report. */
const errs = (e: unknown) => (e instanceof ApiError && e.body?.errors ? e.body.errors as string[] : [errText(e)]);
export const FOLLOW_TEXT: Record<string, string> = {
  Waiting: 'Waiting to hear', Accepted: 'Accepted', Declined: 'Declined', ApprovalRequested: 'Insurer approval requested', Approved: 'Insurer approved',
  ApprovalDeclined: 'Insurer declined', DepositPaid: 'Deposit paid', Booked: 'Booked', Invoiced: 'Invoiced', Expired: 'Expired',
};
const FOLLOW_PILL: Record<string, string> = { Accepted: 'pill green', Approved: 'pill green', DepositPaid: 'pill green', Booked: 'pill green', Invoiced: 'pill', Declined: 'pill red', ApprovalDeclined: 'pill red', Expired: 'pill', Waiting: 'pill amber', ApprovalRequested: 'pill amber' };

function Errors({ list }: { list: string[] }) {
  return list.length > 0 ? <div className="error" role="alert"><ul>{list.map((e) => <li key={e}>{e}</li>)}</ul></div> : null;
}

export default function EstimatesFollowUp() {
  const [params, setParams] = useSearchParams();
  const tab = params.get('tab') ?? 'chase';
  const tabs: [string, string][] = [['chase', 'To chase'], ['expiring', 'Running out'], ['won', 'Accepted – to book or invoice'], ['expired', 'Expired'], ['open', 'All open'], ['report', 'Report']];
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Estimate follow-up</h1><p className="muted">Where each issued estimate is up to: who to chase, which are running out, and how many turn into bookings.</p></div>
      <div className="tabs" role="tablist">{tabs.map(([k, t]) => <button key={k} role="tab" aria-selected={tab === k} className={tab === k ? 'on' : ''} onClick={() => setParams({ tab: k })}>{t}</button>)}</div>
      {tab === 'report' ? <Report /> : <FollowList view={tab} />}
    </div>
  );
}

function FollowList({ view }: { view: string }) {
  const [rows, setRows] = useState<any[] | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const [open, setOpen] = useState<any | null>(null);
  const load = () => api<any[]>(`/api/estimates/followup?view=${view}`).then(setRows).catch((e) => setErrors(errs(e)));
  useEffect(() => { setRows(null); void load(); }, [view]); // eslint-disable-line react-hooks/exhaustive-deps
  return (
    <section className="card stack">
      {msg && <p className="notice" role="status">{msg}</p>}
      <Errors list={errors} />
      {open && <FollowUpDialog est={open} onClose={() => setOpen(null)} onDone={(m) => { setOpen(null); setMsg(m); void load(); }} />}
      {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">Nothing here.</p> : (
        <div className="table-scroll" tabIndex={0} role="region" aria-label="Estimates to follow up">
          <table className="results">
            <thead><tr><th>Estimate</th><th>Patient</th><th>Procedure</th><th className="money">Total</th><th>Funding</th><th>Valid until</th><th>Where it is up to</th><th>Why listed</th><th>Action</th></tr></thead>
            <tbody>{rows.map((r) => (
              <tr key={r.id}>
                <td><Link to={`/estimates/${r.id}`}>{r.ref}</Link><div className="small muted">Issued {r.issuedAt ? nzDateTime(r.issuedAt) : '—'}</div></td>
                <td>{r.patientId ? <Link to={`/patients/${r.patientId}`}>{r.patient}</Link> : r.patient}</td>
                <td>{r.procedure ?? '—'}<div className="small muted">{r.surgeon}</div></td>
                <td className="money">{money(r.subtotal)}</td>
                <td>{r.funding === 'Insurance' ? `Insurance${r.insurer ? ` – ${r.insurer}` : ''}` : r.funding === 'SelfFunding' ? 'Self-funding' : 'Not yet known'}</td>
                <td>{r.validUntil ? formatDate(r.validUntil) : '—'}{r.daysLeft !== null && r.daysLeft >= 0 && r.daysLeft <= 14 && <div className="small muted">{r.daysLeft} day{r.daysLeft === 1 ? '' : 's'} left</div>}</td>
                <td><span className={FOLLOW_PILL[r.followUp ?? 'Waiting']}>{FOLLOW_TEXT[r.followUp ?? 'Waiting']}</span>{r.nextActionOn && <div className="small muted">Next: {formatDate(r.nextActionOn)}</div>}</td>
                <td className="small">{r.why || '—'}</td>
                <td><button className="btn btn-link" onClick={() => setOpen(r)}>Record what happened</button></td>
              </tr>))}</tbody>
          </table>
        </div>)}
    </section>
  );
}

/** Pop-up to record the patient's or insurer's answer. */
export function FollowUpDialog({ est, onClose, onDone }: { est: { id: string; ref: string; funding: string }; onClose: () => void; onDone: (msg: string) => void }) {
  const insured = est.funding === 'Insurance';
  const options: [string, string][] = [['Waiting', 'Still waiting – chase again later'], ['Accepted', 'Accepted by the patient'], ['Declined', 'Declined by the patient'],
    ...(insured ? [['ApprovalRequested', 'Sent to the insurer for approval'], ['Approved', 'Insurer approved'], ['ApprovalDeclined', 'Insurer declined']] as [string, string][] : []),
    ['DepositPaid', 'Deposit paid'], ['Booked', 'Booked for surgery']];
  const [status, setStatus] = useState(options[1][0]);
  const [note, setNote] = useState('');
  const [next, setNext] = useState('');
  const [approval, setApproval] = useState('');
  const [deposit, setDeposit] = useState('');
  const [depositOn, setDepositOn] = useState(todayIso());
  const [errors, setErrors] = useState<string[]>([]);
  const save = async () => {
    setErrors([]);
    try {
      await api(`/api/estimates/${est.id}/followup`, { body: { status, note: note || null, nextActionOn: next || null, approvalNumber: approval || null, depositAmount: deposit ? Number(deposit) : null, depositPaidOn: depositOn || null, bookingId: null } });
      onDone(`${est.ref}: ${FOLLOW_TEXT[status].toLowerCase()} recorded.`);
    } catch (e) { setErrors(errs(e)); }
  };
  return (
    <Modal title={`Follow up ${est.ref}`} onClose={onClose}>
      <label>What happened<select value={status} onChange={(e) => setStatus(e.target.value)}>{options.map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
      {status === 'Approved' && <label>Insurer's approval number<input value={approval} onChange={(e) => setApproval(e.target.value)} /></label>}
      {status === 'DepositPaid' && <div className="form-grid">
        <label>Deposit paid ($)<input value={deposit} inputMode="decimal" onChange={(e) => setDeposit(e.target.value)} /></label>
        <label>Paid on<input type="date" value={depositOn} onChange={(e) => setDepositOn(e.target.value)} /></label>
      </div>}
      <label>{status === 'Declined' || status === 'ApprovalDeclined' ? 'Why it was declined' : 'Note (optional)'}<input value={note} onChange={(e) => setNote(e.target.value)} /></label>
      {!['Declined', 'ApprovalDeclined', 'Booked'].includes(status) && <label>Follow up again on (optional)<input type="date" value={next} onChange={(e) => setNext(e.target.value)} /></label>}
      <Errors list={errors} />
      <div className="row"><button className="btn btn-primary" onClick={() => void save()}>Save</button><button className="btn btn-light" onClick={onClose}>Cancel</button></div>
    </Modal>
  );
}

/** On the estimate page: where it is up to, its history, and Make invoice. */
export function EstimateFollowUpCard({ est, onChange }: { est: any; onChange: (msg: string) => void }) {
  const { can } = useSession();
  const navigate = useNavigate();
  const [history, setHistory] = useState<any[]>([]);
  const [open, setOpen] = useState(false);
  const [errors, setErrors] = useState<string[]>([]);
  useEffect(() => { api<any[]>(`/api/estimates/${est.id}/history`).then(setHistory).catch(() => setHistory([])); }, [est.id, est.followUpAt]);
  if (est.status !== 'Issued') return null;
  const invoice = async () => {
    setErrors([]);
    try { const r = await api<any>(`/api/estimates/${est.id}/invoice`, { body: {} }); navigate(`/billing/${r.id}`); } catch (e) { setErrors(errs(e)); }
  };
  return (
    <section className="card stack">
      <h2>Follow-up</h2>
      <p><span className={FOLLOW_PILL[est.followUp ?? 'Waiting']}>{FOLLOW_TEXT[est.followUp ?? 'Waiting']}</span>
        {est.nextActionOn && <span className="small muted"> · follow up {formatDate(est.nextActionOn)}</span>}
        {est.depositPaid && <span className="small"> · deposit {money(est.depositPaid)} paid {formatDate(est.depositPaidOn)}</span>}</p>
      {est.declineReason && <p className="small muted">Declined: {est.declineReason}</p>}
      {est.invoiceId && <p>Invoice <Link to={`/billing/${est.invoiceId}`}>{est.invoiceNumber ?? 'made'}</Link></p>}
      {history.length > 0 && <ul className="plain small">{history.map((h, i) => <li key={i}>{nzDateTime(h.at)} · {FOLLOW_TEXT[h.status]} · {h.by}{h.note && ` – ${h.note}`}</li>)}</ul>}
      {can(Perm.EstimatesBuild) && !est.invoiceId && <div className="row">
        <button className="btn btn-light" onClick={() => setOpen(true)}>Record what happened</button>
        {can(Perm.BillingManage) && <button className="btn btn-light" onClick={() => void invoice()}>Make invoice</button>}
      </div>}
      <Errors list={errors} />
      {open && <FollowUpDialog est={est} onClose={() => setOpen(false)} onDone={(m) => { setOpen(false); onChange(m); }} />}
    </section>
  );
}

function Report() {
  const today = todayIso();
  const [from, setFrom] = useState(`${today.slice(0, 4)}-01-01`);
  const [to, setTo] = useState(today);
  const [r, setR] = useState<any | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  useEffect(() => { setErrors([]); api<any>(`/api/estimates/followup/report?from=${from}&to=${to}`).then(setR).catch((e) => setErrors(errs(e))); }, [from, to]);
  const table = (title: string, rows: any[]) => (
    <div className="table-scroll" tabIndex={0} role="region" aria-label={title}>
      <table className="results">
        <thead><tr><th>{title}</th><th>Issued</th><th>Accepted</th><th>Declined</th><th>Booked</th><th>Expired</th><th>Still open</th><th className="money">Value issued</th><th className="money">Value accepted</th><th>Accept rate</th><th>Days to accept</th></tr></thead>
        <tbody>{rows.map((g) => (
          <tr key={g.name}><td>{g.name}</td><td>{g.issued}</td><td>{g.accepted}</td><td>{g.declined}</td><td>{g.booked}</td><td>{g.expired}</td><td>{g.open}</td>
            <td className="money">{money(g.issuedValue)}</td><td className="money">{money(g.wonValue)}</td><td>{g.acceptRate === null ? '—' : `${g.acceptRate}%`}</td><td>{g.avgDaysToAccept ?? '—'}</td></tr>))}</tbody>
      </table>
    </div>
  );
  return (
    <section className="card stack">
      <div className="row"><label>Issued from<input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></label><label>To<input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></label></div>
      <Errors list={errors} />
      {r && <>
        <div className="stat-row"><div className="stat"><strong>{r.total.issued}</strong><span>issued</span></div><div className="stat"><strong>{r.total.accepted}</strong><span>accepted</span></div>
          <div className="stat"><strong>{r.total.acceptRate === null ? '—' : `${r.total.acceptRate}%`}</strong><span>accept rate</span></div><div className="stat"><strong>{money(r.total.wonValue)}</strong><span>value accepted</span></div></div>
        <p className="small muted">Accept rate = accepted ÷ (accepted + declined + expired). Estimates still waiting are not counted.</p>
        {table('Surgeon', r.bySurgeon)}
        {table('Procedure', r.byProcedure)}
        {table('Funding', r.byFunding)}
        {table('Month', r.byMonth)}
      </>}
    </section>
  );
}
