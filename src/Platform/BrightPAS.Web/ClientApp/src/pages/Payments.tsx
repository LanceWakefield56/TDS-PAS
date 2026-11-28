import { useEffect, useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router';
import { api, ApiError, downloadPost, errText, formatDate, money, nzDateTime, Perm, todayIso } from '../api';
import { useSession } from '../App';
import Modal from '../components/Modal';
import PatientPicker from '../components/PatientPicker';

/** Online payments and statements (0.30.0, ADR 0043). */
const errs = (e: unknown) => (e instanceof ApiError && e.body?.errors ? e.body.errors as string[] : [errText(e)]);
const PAY_PILL: Record<string, string> = { Started: 'pill amber', Approved: 'pill green', Declined: 'pill red', Cancelled: 'pill', Failed: 'pill red' };
const PAY_TEXT: Record<string, string> = { Started: 'Not finished yet', Approved: 'Paid', Declined: 'Declined', Cancelled: 'Cancelled', Failed: 'Could not start' };
const LINK_TEXT: Record<string, string> = { Open: 'Open', Paid: 'Paid', Cancelled: 'Cancelled' };
const ST_TEXT: Record<string, string> = { Prepared: 'Not sent yet', Sent: 'Sent', Printed: 'Printed for posting', NotSent: 'Not sent' };
const DELIVERY_TEXT: Record<string, string> = { Email: 'Email', Sms: 'Text', Post: 'Post' };

function Errors({ list }: { list: string[] }) {
  return list.length > 0 ? <div className="error" role="alert"><ul>{list.map((e) => <li key={e}>{e}</li>)}</ul></div> : null;
}

export default function Payments() {
  const [params, setParams] = useSearchParams();
  const { can } = useSession();
  const tab = params.get('tab') ?? 'online';
  const tabs: [string, string][] = [['online', 'Online payments'], ['attention', 'Needs attention'], ['links', 'Payment links'], ['statements', 'Statements'], ['plans', 'Payment plans'], ['refunds', 'Refunds'], ['overdue', 'Overdue reminders']];
  if (can(Perm.OnlinePaySettings)) tabs.push(['settings', 'Settings']);
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Online payments</h1><p className="muted">Patients pay their invoices by card from a secure link. Payments go on the invoices automatically. Statements go out by email, text or post.</p></div>
      <div className="tabs" role="tablist">{tabs.map(([k, t]) => <button key={k} role="tab" aria-selected={tab === k} className={tab === k ? 'on' : ''} onClick={() => setParams({ tab: k })}>{t}</button>)}</div>
      {(tab === 'online' || tab === 'attention') && <OnlineList view={tab === 'attention' ? 'attention' : 'all'} />}
      {tab === 'links' && <LinkList />}
      {tab === 'statements' && <Statements />}
      {tab === 'plans' && <Plans />}
      {tab === 'refunds' && <Refunds />}
      {tab === 'overdue' && <Overdue />}
      {tab === 'settings' && <Settings />}
    </div>
  );
}

function OnlineList({ view }: { view: string }) {
  const { can } = useSession();
  const [rows, setRows] = useState<any[] | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const [note, setNote] = useState<Record<string, string>>({});
  const [refunding, setRefunding] = useState<any | null>(null);
  const load = () => api<any[]>(`/api/payments/online?view=${view}`).then(setRows).catch((e) => setErrors(errs(e)));
  useEffect(() => { setRows(null); void load(); }, [view]); // eslint-disable-line react-hooks/exhaustive-deps
  const act = async (fn: () => Promise<unknown>, done: string) => { setErrors([]); setMsg(null); try { await fn(); setMsg(done); await load(); } catch (e) { setErrors(errs(e)); } };
  return (
    <section className="card stack">
      {view === 'attention' && <p className="muted">Money taken online that could not all be put on an invoice (for example the invoice was paid at the desk meanwhile). Refund it to the card with Refund, or apply it by hand and say what you did.</p>}
      {refunding && <RefundForm payment={refunding} onClose={() => setRefunding(null)} onDone={(m) => { setRefunding(null); setMsg(m); void load(); }} />}
      {msg && <p className="notice" role="status">{msg}</p>}
      <Errors list={errors} />
      {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">Nothing here.</p> : (
        <div className="table-scroll" tabIndex={0} role="region" aria-label="Online payments">
          <table className="results">
            <thead><tr><th>Started</th><th>Patient</th><th>Amount</th><th>Result</th><th>Card</th><th>Put on</th><th>Reference</th></tr></thead>
            <tbody>{rows.map((r) => (
              <tr key={r.id}>
                <td>{nzDateTime(r.startedAt)}</td>
                <td><Link to={`/patients/${r.patientId}`}>{r.patient}</Link></td>
                <td>{money(r.amount)}{r.refunded > 0 && <div className="small muted">Refunded {money(r.refunded)}</div>}
                  {r.status === 'Approved' && r.amount - r.refunded > 0 && can(Perm.OnlinePayRefund) && <div><button className="btn btn-link" onClick={() => setRefunding(r)}>Refund</button></div>}</td>
                <td><span className={PAY_PILL[r.status]}>{PAY_TEXT[r.status]}</span>{r.status !== 'Approved' && r.responseText && <div className="small muted">{r.responseText}</div>}
                  {r.status === 'Started' && can(Perm.OnlinePayManage) && <div><button className="btn btn-link" onClick={() => void act(() => api(`/api/payments/online/${r.id}/check`, { body: {} }), 'Checked with the payment provider.')}>Check now</button></div>}
                  {r.needsAttention && <div className="stack-sm"><span className="pill red">Needs attention</span><div className="small">{r.attentionReason}</div>
                    {can(Perm.OnlinePayManage) && <><label>What was done<input value={note[r.id] ?? ''} onChange={(e) => setNote({ ...note, [r.id]: e.target.value })} /></label>
                      <div><button className="btn btn-light" onClick={() => void act(() => api(`/api/payments/online/${r.id}/resolve`, { body: { note: note[r.id] ?? '' } }), 'Marked as dealt with.')}>Mark as dealt with</button></div></>}</div>}
                  {r.resolvedNote && <div className="small muted">Dealt with: {r.resolvedNote}</div>}</td>
                <td>{r.card ?? '—'}{r.authCode && <div className="small muted">Auth {r.authCode}</div>}</td>
                <td className="small">{r.appliedTo.length ? r.appliedTo.join(', ') : '—'}</td>
                <td className="small">{r.merchantReference}{r.provider === 'Simulator' && <div className="muted">Simulator</div>}</td>
              </tr>))}</tbody>
          </table>
        </div>)}
    </section>
  );
}

function RefundForm({ payment, onClose, onDone }: { payment: any; onClose: () => void; onDone: (msg: string) => void }) {
  const [amount, setAmount] = useState(String((payment.amount - payment.refunded).toFixed(2)));
  const [reason, setReason] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const go = async () => {
    setErrors([]); setBusy(true);
    try {
      const r = await api<any>(`/api/payments/online/${payment.id}/refund`, { body: { amount: Number(amount), reason } });
      onDone(`Refund ${r.number} sent back to the card.${r.appliedTo ? ` Taken off: ${r.appliedTo}.` : ''}`);
    } catch (e) { setErrors(errs(e)); } finally { setBusy(false); }
  };
  return (
    <Modal title={`Refund to ${payment.card ?? 'the card'}`} onClose={onClose}>
      <p className="small muted">{payment.patient} paid {money(payment.amount)} online{payment.refunded > 0 ? `, ${money(payment.refunded)} already refunded` : ''}. Money that was never put on an invoice is refunded first; the rest comes off the invoices it paid.</p>
      <label>Amount to refund ($)<input value={amount} inputMode="decimal" onChange={(e) => setAmount(e.target.value)} /></label>
      <label>Why<input value={reason} onChange={(e) => setReason(e.target.value)} placeholder="For example paid twice, or procedure cancelled" /></label>
      <Errors list={errors} />
      <div className="row"><button className="btn btn-primary" disabled={busy} onClick={() => void go()}>Refund now</button><button className="btn btn-light" onClick={onClose}>Cancel</button></div>
    </Modal>
  );
}

const PLAN_TEXT: Record<string, string> = { Active: 'Active', Completed: 'Paid off', Cancelled: 'Cancelled' };
const FREQ_TEXT: Record<string, string> = { Weekly: 'weekly', Fortnightly: 'fortnightly', Monthly: 'monthly' };

function Plans() {
  const { can } = useSession();
  const [view, setView] = useState('active');
  const [rows, setRows] = useState<any[] | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const [adding, setAdding] = useState(false);
  const [open, setOpen] = useState<any | null>(null);
  const load = () => api<any[]>(`/api/payments/plans?view=${view}`).then(setRows).catch((e) => setErrors(errs(e)));
  useEffect(() => { setRows(null); void load(); }, [view]); // eslint-disable-line react-hooks/exhaustive-deps
  return (
    <section className="card stack">
      <p className="muted">A payment plan lets a patient pay their balance off in regular amounts. Any payment on the plan's invoices counts – at the desk, online or by bank. Patients get a reminder with a payment link before each instalment, and are left off statements and overdue reminders while they keep up.</p>
      <div className="row">
        <label>Show<select value={view} onChange={(e) => setView(e.target.value)}><option value="active">Active plans</option><option value="behind">Behind with payments</option><option value="all">All plans</option></select></label>
        {can(Perm.OnlinePayManage) && <button className="btn btn-primary" onClick={() => setAdding(true)}>New payment plan</button>}
      </div>
      {msg && <p className="notice" role="status">{msg}</p>}
      <Errors list={errors} />
      {adding && <NewPlan onClose={() => setAdding(false)} onDone={(m) => { setAdding(false); setMsg(m); void load(); }} />}
      {open && <PlanView id={open.id} onClose={() => setOpen(null)} onChange={(m) => { setOpen(null); setMsg(m); void load(); }} />}
      {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">No payment plans here.</p> : (
        <div className="table-scroll" tabIndex={0} role="region" aria-label="Payment plans">
          <table className="results">
            <thead><tr><th>Plan</th><th>Patient</th><th>Amount</th><th>Instalments</th><th>Paid so far</th><th>Still to pay</th><th>Next due</th><th>Status</th></tr></thead>
            <tbody>{rows.map((r) => (
              <tr key={r.id}>
                <td><button className="btn btn-link" onClick={() => setOpen(r)}>{r.number}</button><div className="small muted">{r.invoices.join(', ')}</div></td>
                <td><Link to={`/patients/${r.patientId}`}>{r.patient}</Link></td>
                <td>{money(r.total)}</td>
                <td>{money(r.instalment)} {FREQ_TEXT[r.frequency]}</td>
                <td>{money(r.paidSoFar)}</td>
                <td>{money(r.remaining)}</td>
                <td>{r.nextDue ? formatDate(r.nextDue) : '—'}</td>
                <td><span className={r.status === 'Active' ? (r.behind > 0 ? 'pill red' : 'pill green') : 'pill'}>{r.status === 'Active' && r.behind > 0 ? `Behind ${money(r.behind)}` : PLAN_TEXT[r.status]}</span>
                  {r.endReason && <div className="small muted">{r.endReason}</div>}</td>
              </tr>))}</tbody>
          </table>
        </div>)}
    </section>
  );
}

function NewPlan({ onClose, onDone }: { onClose: () => void; onDone: (msg: string) => void }) {
  const [patient, setPatient] = useState<any | null>(null);
  const [options, setOptions] = useState<any[] | null>(null);
  const [chosen, setChosen] = useState<string[]>([]);
  const [instalment, setInstalment] = useState('');
  const [frequency, setFrequency] = useState('Fortnightly');
  const [firstDue, setFirstDue] = useState(todayIso());
  const [note, setNote] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  useEffect(() => {
    setOptions(null); setChosen([]);
    if (patient) api<any[]>(`/api/payments/plans/options/${patient.id}`).then((o) => { setOptions(o); setChosen(o.map((x) => x.invoiceId)); }).catch((e) => setErrors(errs(e)));
  }, [patient]);
  const total = (options ?? []).filter((o) => chosen.includes(o.invoiceId)).reduce((t, o) => t + o.balance, 0);
  const count = Number(instalment) > 0 ? Math.ceil(total / Number(instalment)) : 0;
  const save = async () => {
    setErrors([]);
    try {
      const r = await api<any>('/api/payments/plans', { body: { patientId: patient?.id, invoiceIds: chosen, instalment: Number(instalment), frequency, firstDue, note: note || null } });
      onDone(`Payment plan ${r.number} set up.`);
    } catch (e) { setErrors(errs(e)); }
  };
  return (
    <Modal title="New payment plan" onClose={onClose} wide>
      <PatientPicker value={patient} onChange={setPatient} />
      {patient && options && (options.length === 0 ? <p className="muted">This patient has no balance that can go on a plan.</p> : <>
        <fieldset><legend>Invoices on the plan</legend>
          {options.map((o) => <label key={o.invoiceId} className="check"><input type="checkbox" checked={chosen.includes(o.invoiceId)} onChange={(e) => setChosen(e.target.checked ? [...chosen, o.invoiceId] : chosen.filter((x) => x !== o.invoiceId))} /> {o.number} – {money(o.balance)} owing</label>)}
        </fieldset>
        <div className="form-grid">
          <label>Each instalment ($)<input value={instalment} inputMode="decimal" onChange={(e) => setInstalment(e.target.value)} /></label>
          <label>How often<select value={frequency} onChange={(e) => setFrequency(e.target.value)}><option value="Weekly">Weekly</option><option value="Fortnightly">Fortnightly</option><option value="Monthly">Monthly</option></select></label>
          <label>First instalment due<input type="date" value={firstDue} onChange={(e) => setFirstDue(e.target.value)} /></label>
          <label className="span-all">Note (optional)<input value={note} onChange={(e) => setNote(e.target.value)} placeholder="For example agreed by phone with the patient" /></label>
        </div>
        <p className="small">Total {money(total)}{count > 0 && ` – ${count} instalment${count === 1 ? '' : 's'}`}.</p>
      </>)}
      <Errors list={errors} />
      <div className="row"><button className="btn btn-primary" disabled={!patient || chosen.length === 0} onClick={() => void save()}>Set up plan</button><button className="btn btn-light" onClick={onClose}>Cancel</button></div>
    </Modal>
  );
}

function PlanView({ id, onClose, onChange }: { id: string; onClose: () => void; onChange: (msg: string) => void }) {
  const { can } = useSession();
  const [d, setD] = useState<any | null>(null);
  const [reason, setReason] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  useEffect(() => { api<any>(`/api/payments/plans/${id}`).then(setD).catch((e) => setErrors(errs(e))); }, [id]);
  const cancel = async () => {
    setErrors([]);
    try { await api(`/api/payments/plans/${id}/cancel`, { body: { note: reason } }); onChange('Payment plan cancelled.'); } catch (e) { setErrors(errs(e)); }
  };
  return (
    <Modal title={d ? `Payment plan ${d.plan.number}` : 'Payment plan'} onClose={onClose} wide>
      {!d ? <p>{errors.join(' ') || 'Loading…'}</p> : <>
        <p>{d.plan.patient}: {money(d.plan.total)} as {money(d.plan.instalment)} {FREQ_TEXT[d.plan.frequency]}. Paid so far {money(d.progress.paidSoFar)}, still to pay {money(d.progress.remaining)}.{d.plan.note && ` ${d.plan.note}`}</p>
        <div className="table-scroll" tabIndex={0} role="region" aria-label="Instalments">
          <table className="results">
            <thead><tr><th>Instalment</th><th>Due</th><th>Amount</th><th>Paid</th></tr></thead>
            <tbody>{d.progress.schedule.map((i: any) => <tr key={i.number}><td>{i.number}</td><td>{formatDate(i.dueOn)}</td><td>{money(i.amount)}</td><td>{i.paid ? 'Yes' : '—'}</td></tr>)}</tbody>
          </table>
        </div>
        {d.plan.status === 'Active' && can(Perm.OnlinePayManage) && <div className="stack-sm">
          <label>Reason for cancelling<input value={reason} onChange={(e) => setReason(e.target.value)} /></label>
          <div><button className="btn btn-light" onClick={() => void cancel()}>Cancel plan</button></div>
        </div>}
        <Errors list={errors} />
      </>}
    </Modal>
  );
}

function Refunds() {
  const [rows, setRows] = useState<any[] | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  useEffect(() => { api<any[]>('/api/payments/refunds').then(setRows).catch((e) => setErrors(errs(e))); }, []);
  return (
    <section className="card stack">
      <p className="muted">Card refunds made from BrightPAS. Use Refund on an online payment to make one.</p>
      <Errors list={errors} />
      {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">No refunds yet.</p> : (
        <div className="table-scroll" tabIndex={0} role="region" aria-label="Refunds">
          <table className="results">
            <thead><tr><th>Refund</th><th>When</th><th>Patient</th><th>Amount</th><th>Result</th><th>Taken off</th><th>Why</th></tr></thead>
            <tbody>{rows.map((r) => (
              <tr key={r.id}>
                <td>{r.number}<div className="small muted">{r.paymentReference}</div></td>
                <td>{nzDateTime(r.createdAt)}<div className="small muted">{r.createdBy}</div></td>
                <td><Link to={`/patients/${r.patientId}`}>{r.patient}</Link></td>
                <td>{money(r.amount)}</td>
                <td><span className={r.status === 'Done' ? 'pill green' : 'pill red'}>{r.status === 'Done' ? 'Refunded' : 'Did not go through'}</span>{r.error && <div className="small muted">{r.error}</div>}</td>
                <td className="small">{r.appliedTo ?? '—'}</td>
                <td className="small">{r.reason}</td>
              </tr>))}</tbody>
          </table>
        </div>)}
    </section>
  );
}

function Overdue() {
  const [rows, setRows] = useState<any[] | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  useEffect(() => { api<any[]>('/api/payments/overdue').then(setRows).catch((e) => setErrors(errs(e))); }, []);
  return (
    <section className="card stack">
      <p className="muted">Reminders sent automatically for invoices that are past their due date (set the days in Settings). Each invoice gets each reminder once.</p>
      <Errors list={errors} />
      {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">No overdue reminders sent yet.</p> : (
        <div className="table-scroll" tabIndex={0} role="region" aria-label="Overdue reminders">
          <table className="results">
            <thead><tr><th>Sent</th><th>Patient</th><th>Invoice</th><th>Reminder</th><th>Owing then</th><th>Messages</th></tr></thead>
            <tbody>{rows.map((r) => (
              <tr key={r.id}>
                <td>{nzDateTime(r.sentAt)}</td>
                <td><Link to={`/patients/${r.patientId}`}>{r.patient}</Link></td>
                <td><Link to={`/billing/${r.invoiceId}`}>{r.invoiceNumber}</Link></td>
                <td>{r.stage === 1 ? 'First' : 'Second'}</td>
                <td>{money(r.balance)}</td>
                <td>{r.messages > 0 ? r.messages : <span className="pill amber">Not sent – no consent or contact details</span>}</td>
              </tr>))}</tbody>
          </table>
        </div>)}
    </section>
  );
}

function LinkList() {
  const { can } = useSession();
  const [rows, setRows] = useState<any[] | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const load = () => api<any[]>('/api/payments/links').then(setRows).catch((e) => setErrors(errs(e)));
  useEffect(() => { void load(); }, []);
  return (
    <section className="card stack">
      <p className="muted">Send a payment link from an invoice (Invoices &amp; payments › the invoice › Pay online). Statement links are made when statements are sent.</p>
      <Errors list={errors} />
      {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">No payment links yet.</p> : (
        <div className="table-scroll" tabIndex={0} role="region" aria-label="Payment links">
          <table className="results">
            <thead><tr><th>Made</th><th>Patient</th><th>For</th><th>Status</th><th>Works until</th><th>Last opened</th><th>Action</th></tr></thead>
            <tbody>{rows.map((r) => (
              <tr key={r.id}>
                <td>{nzDateTime(r.createdAt)}<div className="small muted">{r.createdBy}</div></td>
                <td><Link to={`/patients/${r.patientId}`}>{r.patient}</Link></td>
                <td>{r.invoiceId ? <Link to={`/billing/${r.invoiceId}`}>Invoice {r.invoiceNumber}</Link> : `Statement ${r.statementNumber ?? ''}`}</td>
                <td>{r.expired ? 'Expired' : LINK_TEXT[r.status]}</td>
                <td>{nzDateTime(r.expiresAt)}</td>
                <td>{nzDateTime(r.lastOpenedAt)}</td>
                <td>{r.status === 'Open' && !r.expired && can(Perm.OnlinePayManage) && <button className="btn btn-link danger" onClick={async () => { setErrors([]); try { await api(`/api/payments/links/${r.id}/cancel`, { body: {} }); await load(); } catch (e) { setErrors(errs(e)); } }}>Stop link</button>}</td>
              </tr>))}</tbody>
          </table>
        </div>)}
    </section>
  );
}

function Statements() {
  const { can } = useSession();
  const [runs, setRuns] = useState<any[] | null>(null);
  const [open, setOpen] = useState<string | null>(null);
  const [rows, setRows] = useState<any[] | null>(null);
  const [date, setDate] = useState(todayIso());
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const load = async () => { try { setRuns(await api<any[]>('/api/payments/statements/runs')); } catch (e) { setErrors(errs(e)); } };
  const show = async (id: string) => { setOpen(id); setRows(null); try { setRows(await api<any[]>(`/api/payments/statements/runs/${id}`)); } catch (e) { setErrors(errs(e)); } };
  useEffect(() => { void load(); }, []);
  const act = async (fn: () => Promise<string>) => { setErrors([]); setMsg(null); try { setMsg(await fn()); await load(); if (open) await show(open); } catch (e) { setErrors(errs(e)); } };
  const manage = can(Perm.OnlinePayManage);
  return (
    <div className="stack-lg">
      {manage && (
        <section className="card stack">
          <h2>Prepare statements</h2>
          <p className="muted">A statement is made for every patient with self-funded invoices still owing. ACC, insurer and Health NZ invoices go through Claims instead. Nothing is sent until you press Send.</p>
          <div className="form-grid"><label>Statement date<input type="date" value={date} max={todayIso()} onChange={(e) => setDate(e.target.value)} /></label></div>
          <div><button className="btn btn-primary" onClick={() => void act(async () => { const r = await api<any>('/api/payments/statements/runs', { body: { statementDate: date } }); await show(r.id); return `${r.count} statements prepared, ${money(r.total)} owing in total. Check them, then press Send.`; })}>Prepare statements</button></div>
        </section>)}
      {msg && <p className="notice" role="status">{msg}</p>}
      <Errors list={errors} />
      <section className="card stack">
        <h2>Statement runs</h2>
        {!runs ? <p>Loading…</p> : runs.length === 0 ? <p className="muted">No statements yet.</p> : (
          <div className="table-scroll" tabIndex={0} role="region" aria-label="Statement runs">
            <table className="results">
              <thead><tr><th>Statement date</th><th>Statements</th><th>Owing</th><th>Sent</th><th>To post</th><th>Action</th></tr></thead>
              <tbody>{runs.map((r) => (
                <tr key={r.id}>
                  <td><button className="btn btn-link" onClick={() => void show(r.id)}>{formatDate(r.statementDate)}</button><div className="small muted">{r.createdBy}</div></td>
                  <td>{r.count}</td>
                  <td>{money(r.total)}</td>
                  <td>{r.sentAt ? nzDateTime(r.sentAt) : 'Not sent yet'}</td>
                  <td>{r.toPrint}{r.printed > 0 && <span className="small muted"> ({r.printed} printed)</span>}</td>
                  <td className="row">
                    {manage && !r.sentAt && <button className="btn btn-primary" onClick={() => void act(async () => { const x = await api<any>(`/api/payments/statements/runs/${r.id}/send`, { body: {} }); return `${x.sent} statements sent by email or text; ${x.toPost} to post.`; })}>Send statements</button>}
                    {manage && r.sentAt && r.toPrint > 0 && <button className="btn btn-light" onClick={() => void act(async () => { await downloadPost(`/api/payments/statements/runs/${r.id}/print`, {}); return 'Statements to post downloaded. Print them and post them.'; })}>Print statements to post</button>}
                    {manage && !r.sentAt && <button className="btn btn-link danger" onClick={() => void act(async () => { await api(`/api/payments/statements/runs/${r.id}`, { method: 'DELETE' }); setOpen(null); return 'Run deleted.'; })}>Delete run</button>}
                  </td>
                </tr>))}</tbody>
            </table>
          </div>)}
      </section>
      {open && (
        <section className="card stack">
          <h2>Statements in this run</h2>
          {!rows ? <p>Loading…</p> : <StatementTable rows={rows} label="Statements in this run" />}
        </section>)}
    </div>
  );
}

export function StatementTable({ rows, label }: { rows: any[]; label: string }) {
  return (
    <div className="table-scroll" tabIndex={0} role="region" aria-label={label}>
      <table className="results">
        <thead><tr><th>Statement</th><th>Patient</th><th>Owing then</th><th>Overdue</th><th>Owing now</th><th>Goes by</th><th>Status</th></tr></thead>
        <tbody>{rows.map((s) => (
          <tr key={s.id}>
            <td><a href={`/api/payments/statements/${s.id}/pdf`} target="_blank" rel="noreferrer">{s.number}</a></td>
            <td><Link to={`/patients/${s.patientId}`}>{s.patient}</Link></td>
            <td>{money(s.totalDue)}</td>
            <td>{s.overdue > 0 ? money(s.overdue) : '—'}</td>
            <td>{money(s.balanceNow)}</td>
            <td>{DELIVERY_TEXT[s.delivery]}</td>
            <td>{ST_TEXT[s.status]}{s.note && <div className="small muted">{s.note}</div>}</td>
          </tr>))}</tbody>
      </table>
    </div>
  );
}

function Settings() {
  const [s, setS] = useState<any>(null);
  const [key, setKey] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const load = () => api('/api/payments/settings').then(setS).catch((e) => setErrors(errs(e)));
  useEffect(() => { void load(); }, []);
  if (!s) return <section className="card"><p>{errors.join(' ') || 'Loading…'}</p></section>;
  const save = async () => {
    setErrors([]); setMsg(null);
    try {
      await api('/api/payments/settings', { method: 'PUT', body: { provider: s.provider, publicUrl: s.publicUrl || null, linkDays: Number(s.linkDays), minimumPayment: Number(s.minimumPayment), windcaveUrl: s.windcaveUrl || null, windcaveUser: s.windcaveUser || null, windcaveKey: key || null, statementMinimum: Number(s.statementMinimum), statementMessage: s.statementMessage || null,
        autoStatementDay: Number(s.autoStatementDay), autoSendStatements: !!s.autoSendStatements, overdueFirstDays: Number(s.overdueFirstDays), overdueSecondDays: Number(s.overdueSecondDays),
        includeShortfalls: !!s.includeShortfalls, planReminderDays: Number(s.planReminderDays) } });
      setKey(''); setMsg('Settings saved.'); await load();
    } catch (e) { setErrors(errs(e)); }
  };
  return (
    <section className="card stack">
      <h2>Settings</h2>
      <div className="form-grid">
        <label>How patients pay online<select value={s.provider} onChange={(e) => setS({ ...s, provider: e.target.value })}>
          <option value="Off">Off – no online payments</option><option value="Simulator">Simulator – training only, no money moves</option><option value="Windcave">Windcave hosted payment page</option></select></label>
        <label className="span-all">Public web address for patient links<input value={s.publicUrl ?? ''} onChange={(e) => setS({ ...s, publicUrl: e.target.value })} placeholder="https://portal.example.co.nz" /></label>
        <label>Links work for (days)<input value={s.linkDays} inputMode="numeric" onChange={(e) => setS({ ...s, linkDays: e.target.value })} /></label>
        <label>Smallest part-payment online ($)<input value={s.minimumPayment} inputMode="decimal" onChange={(e) => setS({ ...s, minimumPayment: e.target.value })} /></label>
        {s.provider === 'Windcave' && <>
          <label className="span-all">Windcave address<input value={s.windcaveUrl ?? ''} onChange={(e) => setS({ ...s, windcaveUrl: e.target.value })} placeholder="https://sec.windcave.com" /></label>
          <label>Windcave API user name<input value={s.windcaveUser ?? ''} onChange={(e) => setS({ ...s, windcaveUser: e.target.value })} autoComplete="off" /></label>
          <label>Windcave API key{s.windcaveKeySet && <span className="small muted"> (saved – leave blank to keep it)</span>}<input type="password" value={key} onChange={(e) => setKey(e.target.value)} autoComplete="new-password" /></label>
        </>}
        <label>No statement for balances under ($)<input value={s.statementMinimum} inputMode="decimal" onChange={(e) => setS({ ...s, statementMinimum: e.target.value })} /></label>
        <label className="span-all">Message printed on every statement (optional)<input value={s.statementMessage ?? ''} onChange={(e) => setS({ ...s, statementMessage: e.target.value })} /></label>
        <label className="check span-all"><input type="checkbox" checked={!!s.includeShortfalls} onChange={(e) => setS({ ...s, includeShortfalls: e.target.checked })} /> Include insurer shortfalls (what is left on an insurer invoice after the insurer has paid) on statements and reminders</label>
      </div>
      <h3>Every day, automatically</h3>
      <div className="form-grid">
        <label>Prepare statements on day of the month (0 = only by hand)<input value={s.autoStatementDay} inputMode="numeric" onChange={(e) => setS({ ...s, autoStatementDay: e.target.value })} /></label>
        <label className="check"><input type="checkbox" checked={!!s.autoSendStatements} onChange={(e) => setS({ ...s, autoSendStatements: e.target.checked })} /> Send them straight away (otherwise they wait for you to check and send)</label>
        <label>First overdue reminder, days after the due date (0 = none)<input value={s.overdueFirstDays} inputMode="numeric" onChange={(e) => setS({ ...s, overdueFirstDays: e.target.value })} /></label>
        <label>Second overdue reminder, days after the due date (0 = none)<input value={s.overdueSecondDays} inputMode="numeric" onChange={(e) => setS({ ...s, overdueSecondDays: e.target.value })} /></label>
        <label>Payment plan reminders, days before each instalment<input value={s.planReminderDays} inputMode="numeric" onChange={(e) => setS({ ...s, planReminderDays: e.target.value })} /></label>
      </div>
      <p className="small muted">This runs once a day after 9 am{s.lastAutomationOn ? ` (last run ${formatDate(s.lastAutomationOn)})` : ''}. Patients keeping up with a payment plan are left off statements and overdue reminders.</p>
      <RunNow />
      <p className="small muted">Card numbers never reach BrightPAS: patients type them on Windcave's own page. Use https://uat.windcave.com while testing and https://sec.windcave.com when live.</p>
      {msg && <p className="notice" role="status">{msg}</p>}
      <Errors list={errors} />
      <div><button className="btn btn-primary" onClick={() => void save()}>Save settings</button></div>
    </section>
  );
}

function RunNow() {
  const [msg, setMsg] = useState<string | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const run = async () => {
    setMsg(null); setErrors([]);
    try {
      const r = await api<any>('/api/payments/automation/run', { body: {} });
      setMsg(`Done: ${r.statementCount} statement${r.statementCount === 1 ? '' : 's'} prepared (${r.statementsSent} sent), ${r.overdueSent} overdue reminder${r.overdueSent === 1 ? '' : 's'} sent${r.overdueNotSent ? ` (${r.overdueNotSent} could not be sent)` : ''}, ${r.planReminders} plan reminder${r.planReminders === 1 ? '' : 's'}, ${r.plansCompleted} plan${r.plansCompleted === 1 ? '' : 's'} paid off.`);
    } catch (e) { setErrors(errs(e)); }
  };
  return <div className="stack-sm">{msg && <p className="notice" role="status">{msg}</p>}<Errors list={errors} /><div><button className="btn btn-light" onClick={() => void run()}>Run the daily round now</button></div></div>;
}

/** On the invoice page: send a payment link and see online payments for this invoice. */
export function InvoicePayOnline({ invoiceId, status, onChange }: { invoiceId: string; status: string; onChange: () => void }) {
  const { can } = useSession();
  const [links, setLinks] = useState<any[]>([]);
  const [pays, setPays] = useState<any[]>([]);
  const [made, setMade] = useState<{ link: string; sent: number } | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const load = async () => {
    try { setLinks(await api<any[]>(`/api/payments/links?invoiceId=${invoiceId}`)); setPays(await api<any[]>(`/api/payments/online?invoiceId=${invoiceId}`)); } catch (e) { setErrors(errs(e)); }
  };
  useEffect(() => { void load(); }, [invoiceId]); // eslint-disable-line react-hooks/exhaustive-deps
  const make = async (send: boolean) => {
    setErrors([]); setMade(null);
    try { setMade(await api<any>('/api/payments/links', { body: { invoiceId, send } })); await load(); onChange(); } catch (e) { setErrors(errs(e)); }
  };
  const owing = ['Issued', 'PartPaid'].includes(status);
  const open = links.find((l) => l.status === 'Open' && !l.expired);
  return (
    <section className="card stack no-print">
      <h2>Pay online</h2>
      {open ? <p>A payment link is open until {nzDateTime(open.expiresAt)}{open.lastOpenedAt ? ` (last opened ${nzDateTime(open.lastOpenedAt)})` : ' (not opened yet)'}.</p> : owing ? <p className="muted">Send the patient a secure link to pay this invoice by card.</p> : <p className="muted">Nothing is owing on this invoice.</p>}
      {made && <div className="notice" role="status">{made.sent > 0 ? `Link sent to the patient (${made.sent} message${made.sent > 1 ? 's' : ''}).` : 'Link made but not sent (no mobile or email, or the patient has opted out). Copy it and give it to them.'}<div><code className="break">{made.link}</code></div></div>}
      <Errors list={errors} />
      {owing && can(Perm.OnlinePayManage) && <div className="row">
        <button className="btn btn-primary" onClick={() => void make(true)}>{open ? 'Send a new payment link' : 'Send payment link'}</button>
        <button className="btn btn-light" onClick={() => void make(false)}>Make a link to copy</button>
      </div>}
      {pays.length > 0 && <ul className="plain">{pays.map((p) => <li key={p.id}>{nzDateTime(p.startedAt)} · {money(p.amount)} · <span className={PAY_PILL[p.status]}>{PAY_TEXT[p.status]}</span>{p.card ? ` · ${p.card}` : ''}{p.needsAttention && <> · <span className="pill red">Needs attention</span></>}</li>)}</ul>}
    </section>
  );
}

// ------------------------------------------------------------------ the patient's page (no sign-in)
export function PatientPay() {
  const { token, step } = useParams();
  const [params] = useSearchParams();
  const [stage, setStage] = useState<'loading' | 'dob' | 'view' | 'closed' | 'paid' | 'sim' | 'result'>('loading');
  const [dob, setDob] = useState('');
  const [v, setV] = useState<any>(null);
  const [amount, setAmount] = useState('');
  const [result, setResult] = useState<string | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    if (step === 'done') {
      const sessionId = params.get('sessionId') ?? params.get('sessionid');
      api<{ status: string }>(`/api/pay/${token}/check`, { body: { sessionId } }).then((r) => { setResult(r.status); setStage('result'); }).catch((e) => { setErrors(errs(e)); setStage('result'); });
      return;
    }
    if (step === 'simulator') { setStage('sim'); return; }
    api<{ status: string }>(`/api/pay/${token}`).then((r) => setStage(r.status === 'closed' ? 'closed' : 'dob')).catch(() => setStage('closed'));
  }, [token, step]); // eslint-disable-line react-hooks/exhaustive-deps
  const fail = (e: unknown) => { setErrors(errs(e)); window.scrollTo(0, 0); };
  const open = async () => { const r = await api<any>(`/api/pay/${token}/open`, { body: { dateOfBirth: dob } }); setV(r); setAmount(r.balance > 0 ? r.balance.toFixed(2) : ''); setStage(r.balance > 0 ? 'view' : 'paid'); };
  const pay = async () => {
    setErrors([]); setBusy(true);
    try { const r = await api<{ redirectUrl: string }>(`/api/pay/${token}/start`, { body: { dateOfBirth: dob, amount: Number(amount) } }); window.location.assign(r.redirectUrl); }
    catch (x) { fail(x); setBusy(false); }
  };
  const sim = async (outcome: string) => {
    setErrors([]);
    try { const r = await api<{ status: string }>(`/api/pay/${token}/simulator`, { body: { reference: params.get('ref'), outcome } }); setResult(r.status); setStage('result'); } catch (x) { fail(x); }
  };
  const statement = async () => { setErrors([]); try { await downloadPost(`/api/pay/${token}/statement`, { dateOfBirth: dob }); } catch (x) { fail(x); } };
  return (
    <div className="q-page">
      <header className="q-head"><strong>{v?.clinicName ?? 'Tasman Day Surgery'}</strong> · Pay your account</header>
      <main className="q-body stack-lg">
        {stage === 'loading' && <p>Loading…</p>}
        {stage === 'closed' && <section className="card"><h1>This link is no longer active</h1><p>It may have expired or been replaced by a newer one. Please phone us if you would like to pay your account.</p></section>}
        {stage === 'dob' && (
          <form className="card stack" onSubmit={async (e) => { e.preventDefault(); setErrors([]); try { await open(); } catch (x) { fail(x); } }}>
            <h1>Kia ora</h1><p>To keep your information private, enter your date of birth.</p>
            <label>Date of birth<input type="date" value={dob} onChange={(e) => setDob(e.target.value)} required /></label>
            <Errors list={errors} />
            <div><button className="btn btn-primary btn-big">Open</button></div>
          </form>
        )}
        {stage === 'paid' && v && <section className="card stack"><h1>Nothing to pay</h1><p>Thank you, {v.firstName} – your account is paid.</p>{v.statementNumber && <div><button className="btn btn-light" onClick={() => void statement()}>Download statement {v.statementNumber}</button></div>}</section>}
        {stage === 'view' && v && (
          <>
            <section className="card stack">
              <h1>Kia ora {v.firstName}</h1>
              {v.statementNumber ? <p>Your statement {v.statementNumber} of {formatDate(v.statementDate)}. Amounts below are up to date as of today.</p> : <p>Here is what you owe on your account.</p>}
              <div className="table-scroll" tabIndex={0} role="region" aria-label="What you owe">
                <table className="results">
                  <thead><tr><th>Invoice</th><th>Date</th><th>Due</th><th>Amount</th><th>Paid</th><th>Owing</th></tr></thead>
                  <tbody>{v.invoices.map((i: any) => <tr key={i.invoiceId}><td>{i.number}</td><td>{i.issuedOn ? formatDate(i.issuedOn) : '—'}</td><td>{i.dueOn ? formatDate(i.dueOn) : '—'}</td><td>{money(i.total)}</td><td>{money(i.paid)}</td><td>{money(i.balance)}</td></tr>)}</tbody>
                </table>
              </div>
              <p><strong>Total owing: {money(v.balance)}</strong></p>
              {v.statementNumber && <div><button className="btn btn-light" onClick={() => void statement()}>Download statement</button></div>}
            </section>
            <section className="card stack">
              <h2>Pay by card</h2>
              {!v.canPay ? <p>{v.whyNot}</p> : <>
                <label>Amount to pay ($)<input value={amount} inputMode="decimal" onChange={(e) => setAmount(e.target.value)} /></label>
                <p className="small muted">You can pay the whole amount, or part of it (at least {money(v.minimum)}). You will go to our payment provider's secure page to enter your card details – we never see your card number.</p>
                {v.simulator && <p className="notice">Training system: no real money is taken.</p>}
                <Errors list={errors} />
                <div><button className="btn btn-primary btn-big" disabled={busy} onClick={() => void pay()}>Pay {amount && !Number.isNaN(Number(amount)) ? money(Number(amount)) : ''} by card</button></div>
              </>}
              {v.payments.length > 0 && <><h3>Earlier attempts</h3><ul className="plain">{v.payments.map((p: any, i: number) => <li key={i}>{nzDateTime(p.startedAt)} · {money(p.amount)} · {PAY_TEXT[p.status]}{p.card ? ` · ${p.card}` : ''}</li>)}</ul></>}
              <p className="small muted">Questions about your account? Phone us{v.clinicPhone ? ` on ${v.clinicPhone}` : ''}.</p>
            </section>
          </>
        )}
        {stage === 'sim' && (
          <section className="card stack">
            <h1>Payment simulator</h1>
            <p>This is a training system. No card details are needed and no money moves. Choose what the "bank" says:</p>
            <Errors list={errors} />
            <div className="row">
              <button className="btn btn-primary btn-big" onClick={() => void sim('approve')}>Approve the payment</button>
              <button className="btn btn-light" onClick={() => void sim('decline')}>Decline the card</button>
              <button className="btn btn-link" onClick={() => void sim('cancel')}>Cancel</button>
            </div>
          </section>
        )}
        {stage === 'result' && (
          <section className="card stack">
            {result === 'Approved' ? <><h1>Thank you – payment received</h1><p>Your payment has been put on your account. You can close this page.</p></>
              : result === 'Started' ? <><h1>We are checking your payment</h1><p>Your bank has not confirmed it yet. Please do not pay again – open your link in a few minutes to see whether it went through.</p></>
                : result ? <><h1>{result === 'Declined' ? 'Your card was declined' : 'Payment not made'}</h1><p>No money was taken. You can open your link again to try another card, or phone us.</p></>
                  : <h1>We could not check your payment</h1>}
            <Errors list={errors} />
            <div><a className="btn btn-light" href={`/pay/${token}`}>Back to my account</a></div>
          </section>
        )}
      </main>
    </div>
  );
}
