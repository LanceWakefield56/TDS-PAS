import { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { addDays, api, ApiError, errText, fileToText, formatDate, money, nzDateTime, PatientSummary, Perm, todayIso } from '../api';
import { useSession } from '../App';
import ExportButton from '../components/ExportButton';
import PatientPicker from '../components/PatientPicker';
import Modal from '../components/Modal';

const errs = (e: unknown) => (e instanceof ApiError && e.body?.errors ? e.body.errors as string[] : [errText(e)]);
const STATUS: Record<string, [string, string]> = {
  Draft: ['Needs fixing', 'amber'], Ready: ['Ready to send', 'blue'], Submitted: ['Sent – waiting', 'blue'], Accepted: ['Accepted – waiting for payment', 'blue'],
  Rejected: ['Rejected', 'red'], PartPaid: ['Part paid', 'amber'], Paid: ['Paid', 'green'], WrittenOff: ['Closed', 'grey'],
};
const APPROVAL: Record<string, [string, string]> = {
  Draft: ['Not sent yet', 'amber'], Requested: ['Waiting for the funder', 'blue'], Approved: ['Approved', 'green'], PartApproved: ['Part approved', 'amber'], Declined: ['Declined', 'red'], Expired: ['Expired', 'grey'],
};
const Pill = ({ s, map }: { s: string; map: Record<string, [string, string]> }) => <span className={`pill ${map[s]?.[1] ?? ''}`}>{map[s]?.[0] ?? s}</span>;
const Errors = ({ list }: { list: string[] }) => list.length > 0 ? <div className="error" role="alert"><ul>{list.map((e) => <li key={e}>{e}</li>)}</ul></div> : null;

interface Funder { id: string; code: string; name: string; kind: string; submitBy: string; priorApprovalRequired: boolean; claimsEmail: string | null; providerNumber: string | null; isActive: boolean; notes: string | null; api: string; apiUrl: string | null; apiKeySet: boolean; lastApiAt: string | null; lastApiError: string | null }

function useMeta() {
  const [funders, setFunders] = useState<Funder[]>([]);
  const load = useCallback(() => api<any>('/api/claims/meta').then((m) => setFunders(m.funders)).catch(() => undefined), []);
  useEffect(() => { void load(); }, [load]);
  return { funders, reload: load };
}

const TABS: [string, string][] = [['todo', 'To do'], ['sent', 'Waiting on funders'], ['approvals', 'Prior approvals'], ['remittances', 'Payments in (remittances)'], ['all', 'All claims']];

/** Claims & payments (spec 7.12): everything that is owed by ACC and insurers, in the order Finance works it. */
export default function Claims() {
  const { can } = useSession();
  const [sp, setSp] = useSearchParams();
  const tab = sp.get('tab') ?? 'todo';
  const { funders } = useMeta();
  const [rows, setRows] = useState<any[] | null>(null);
  const [aged, setAged] = useState<any[]>([]);
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState('');
  const load = useCallback(() => {
    api<any[]>('/api/claims/').then(setRows).catch((e) => setErrors(errs(e)));
    api<any[]>('/api/claims/aged').then(setAged).catch(() => undefined);
  }, []);
  useEffect(() => { load(); }, [load]);
  const manage = can(Perm.ClaimsManage);
  const draft = rows?.filter((r) => r.status === 'Draft' || r.status === 'Rejected') ?? [];
  const ready = rows?.filter((r) => r.status === 'Ready') ?? [];
  const readyByFunder = [...new Set(ready.map((r) => r.funderId))].map((id) => ({ funder: funders.find((f) => f.id === id), rows: ready.filter((r) => r.funderId === id) }));
  const sent = rows?.filter((r) => ['Submitted', 'Accepted', 'PartPaid'].includes(r.status)) ?? [];
  const sendDirect = async (list: any[]) => { setErrors([]); setMsg(''); const bad: string[] = []; for (const r of list) { try { await api(`/api/claims/${r.id}/send`, { body: {} }); } catch (e) { bad.push(`${r.number}: ${errText(e)}`); } } setErrors(bad); setMsg(`${list.length - bad.length} claim(s) sent.`); load(); };
  const checkNow = async () => { setErrors([]); try { const r = await api<any>('/api/claims/direct/check', { body: {} }); setMsg(`Checked ${r.checked}: ${r.changed} changed, ${r.paid} paid.`); setErrors(r.errors ?? []); load(); } catch (e) { setErrors(errs(e)); } };
  const markSent = async (id: string) => { setErrors([]); try { await api(`/api/claims/${id}/submitted`, { body: {} }); setMsg('Marked as sent.'); load(); } catch (e) { setErrors(errs(e)); } };

  const Table = ({ list }: { list: any[] }) => list.length === 0 ? <p className="muted">Nothing here.</p> : (
    <table className="results"><thead><tr><th>Claim</th><th>Patient</th><th>Funder</th><th className="money">Amount</th><th className="money">Paid</th><th>Status</th><th>Age</th></tr></thead>
      <tbody>{list.map((r) => <tr key={r.id}><td data-label="Claim"><Link to={`/claims/${r.id}`}><strong>{r.number}</strong></Link> <span className="small muted">{r.invoice}</span></td>
        <td data-label="Patient">{r.patient} <span className="small muted">{r.mrn}</span></td><td data-label="Funder">{r.funder}</td><td data-label="Amount" className="money">{money(r.amount)}</td>
        <td data-label="Paid" className="money">{money(r.paid)}</td><td data-label="Status"><Pill s={r.status} map={STATUS} />{r.responseText && <div className="small muted">{r.responseText}</div>}</td>
        <td data-label="Age" className={r.ageDays > 30 && ['Submitted', 'Accepted'].includes(r.status) ? 'warn' : ''}>{r.ageDays} days</td></tr>)}</tbody></table>
  );

  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Claims &amp; payments</h1><p className="muted">ACC and insurer claims from approval to payment. Work down the list: fix, send, then match the payments when they arrive.</p>
        <div className="row">{can(Perm.ClaimsManage) && <Link className="btn btn-light" to="/claims/xero">Xero</Link>}{(can(Perm.PaymentsTake) || manage) && <Link className="btn btn-light" to="/claims/eftpos">EFTPOS today</Link>}{can(Perm.ClaimsAdmin) && <Link className="btn btn-light" to="/claims/settings">Settings</Link>}</div></div>
      <nav className="tabs" aria-label="Claims views">{TABS.map(([k, t]) => <button key={k} className={tab === k ? 'active' : ''} aria-pressed={tab === k} onClick={() => setSp({ tab: k })}>{t}</button>)}</nav>
      <Errors list={errors} />{msg && <p className="ok" role="status">{msg}</p>}
      {tab === 'todo' && (
        <>
          <section className="card stack">
            <h2>1. Fix these before they can go <span className="count">{draft.length}</span></h2>
            <p className="hint">Open a claim to see exactly what's missing – each check tells you how to fix it.</p>
            {rows ? <Table list={draft} /> : <p>Loading…</p>}
          </section>
          <section className="card stack">
            <h2>2. Ready to send <span className="count">{ready.length}</span></h2>
            {readyByFunder.length === 0 ? <p className="muted">Nothing ready. New claims are made from an issued invoice (open the invoice › Claim from funder).</p> : readyByFunder.map(({ funder, rows: list }) => (
              <div key={funder?.id ?? 'x'} className="stack">
                <h3>{funder?.name ?? 'Funder'} <span className="small muted">{list.length} claim(s), {money(list.reduce((a, r) => a + r.amount, 0))}</span></h3>
                <Table list={list} />
                {manage && (funder?.api && funder.api !== 'Off'
                  ? <div className="row"><button className="btn btn-primary" onClick={() => void sendDirect(list)}>Send straight to {funder.name}</button><span className="hint">Sent directly{funder.api === 'Simulator' ? ' (training simulator)' : ''}. Answers and payments come back by themselves every 15 minutes.</span></div>
                  : funder?.submitBy === 'Portal'
                  ? <p className="hint">{funder.name} claims are entered on their portal. Enter each one there, then press <em>Sent on the portal</em>: {list.map((r) => <button key={r.id} className="btn btn-light" onClick={() => void markSent(r.id)}>{r.number} sent on the portal</button>)}</p>
                  : <div className="row"><ExportButton className="btn btn-primary" label={`Make the claim file for ${funder?.name ?? 'this funder'}`} path="/api/claims/batches" body={{ funderId: funder?.id }} />
                    <span className="hint">Upload or email the file{funder?.claimsEmail ? ` to ${funder.claimsEmail}` : ''}. The claims move to “waiting on funders”.</span></div>)}
              </div>))}
            {ready.length > 0 && <button className="btn btn-link" onClick={load}>Refresh after sending</button>}
          </section>
          <section className="card stack">
            <h2>3. Owed by funders</h2>
            {aged.length === 0 ? <p className="muted">Nothing outstanding.</p> : (
              <table className="results compact"><thead><tr><th>Funder</th><th className="money">Open claims</th><th className="money">Owing</th><th className="money">Over 30 days</th><th className="money">Over 60 days</th></tr></thead>
                <tbody>{aged.map((a) => <tr key={a.funder}><td>{a.funder}</td><td className="money">{a.open}</td><td className="money">{money(a.owing)}</td><td className="money">{a.over30}</td><td className={`money${a.over60 > 0 ? ' warn' : ''}`}>{a.over60}</td></tr>)}</tbody></table>)}
          </section>
        </>
      )}
      {tab === 'sent' && <section className="card stack"><h2>Waiting on funders</h2><p className="hint">Record the funder's answer on the claim, or import their remittance file under “Payments in”. Claims sent directly are checked every 15 minutes.</p>{manage && funders.some((f) => f.api !== 'Off') && <div><button className="btn btn-light" onClick={() => void checkNow()}>Check funders now</button></div>}{rows ? <Table list={sent} /> : <p>Loading…</p>}</section>}
      {tab === 'all' && <section className="card stack"><h2>All claims</h2>{rows ? <Table list={rows} /> : <p>Loading…</p>}</section>}
      {tab === 'approvals' && <Approvals funders={funders} />}
      {tab === 'remittances' && <Remittances funders={funders} onDone={load} />}
    </div>
  );
}

/** Prior approvals: request before surgery, record the funder's answer, attach to the claim. */
export function Approvals({ funders, patientId, bookingId }: { funders: Funder[]; patientId?: string; bookingId?: string }) {
  const { can } = useSession();
  const [list, setList] = useState<any[] | null>(null);
  const [patient, setPatient] = useState<PatientSummary | null>(null);
  const [f, setF] = useState({ funderId: '', memberNumber: '', accClaimNumber: '', procedure: '', amountRequested: '', notes: '' });
  const [decide, setDecide] = useState<any>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const q = new URLSearchParams(); if (patientId) q.set('patientId', patientId); if (bookingId) q.set('bookingId', bookingId);
  const load = useCallback(() => { api<any[]>(`/api/claims/approvals?${q}`).then(setList).catch((e) => setErrors(errs(e))); }, [patientId, bookingId]); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => { load(); }, [load]);
  const fund = funders.find((x) => x.id === f.funderId);
  const run = async (fn: () => Promise<unknown>) => { setErrors([]); try { await fn(); load(); } catch (e) { setErrors(errs(e)); } };
  const create = () => run(async () => {
    await api('/api/claims/approvals', { body: { patientId: patientId ?? patient?.id, bookingId: bookingId ?? null, funderId: f.funderId, memberNumber: f.memberNumber || null, accClaimNumber: f.accClaimNumber || null, procedure: f.procedure, amountRequested: Number(f.amountRequested), notes: f.notes || null } });
    setF({ ...f, procedure: '', amountRequested: '', notes: '' });
  });
  const name = (id: string) => funders.find((x) => x.id === id)?.name ?? '';
  return (
    <section className="card stack">
      <h2>Prior approvals</h2>
      <Errors list={errors} />
      {!list ? <p>Loading…</p> : list.length === 0 ? <p className="muted">None.</p> : (
        <table className="results"><thead><tr><th>Approval</th><th>Funder</th><th>Procedure</th><th className="money">Asked</th><th className="money">Approved</th><th>Status</th><th></th></tr></thead>
          <tbody>{list.map((a) => <tr key={a.id}><td data-label="Approval"><strong>{a.number}</strong>{!patientId && <> · <Link to={`/patients/${a.patientId}`}>patient</Link></>}</td><td data-label="Funder">{name(a.funderId)}<div className="small muted">{a.memberNumber ?? a.accClaimNumber}</div></td>
            <td data-label="Procedure">{a.procedure}</td><td data-label="Asked" className="money">{money(a.amountRequested)}</td><td data-label="Approved" className="money">{a.approvedAmount != null ? money(a.approvedAmount) : '—'}{a.validUntil && <div className="small muted">until {formatDate(a.validUntil)}</div>}</td>
            <td data-label="Status"><Pill s={a.status} map={APPROVAL} />{a.approvalReference && <div className="small muted">{a.approvalReference}</div>}</td>
            <td>{can(Perm.ClaimsManage) && a.status === 'Draft' && <button className="btn btn-light" onClick={() => void run(() => api(`/api/claims/approvals/${a.id}/requested`, { body: {} }))}>Sent to funder</button>}
              {can(Perm.ClaimsManage) && a.status === 'Draft' && funders.find((f) => f.id === a.funderId)?.api !== 'Off' && <button className="btn btn-primary" onClick={() => void run(() => api(`/api/claims/approvals/${a.id}/send`, { body: {} }))}>Send straight to funder</button>}
              {can(Perm.ClaimsManage) && ['Draft', 'Requested'].includes(a.status) && <button className="btn btn-light" onClick={() => setDecide({ id: a.id, status: 'Approved', reference: '', approvedAmount: String(a.amountRequested), validUntil: addDays(todayIso(), 90), notes: '' })}>Record answer</button>}</td></tr>)}</tbody></table>)}
      {decide && (
        <div className="card inset stack">
          <h3>The funder's answer</h3>
          <div className="form-grid">
            <label>Answer<select value={decide.status} onChange={(e) => setDecide({ ...decide, status: e.target.value })}><option value="Approved">Approved in full</option><option value="PartApproved">Part approved</option><option value="Declined">Declined</option></select></label>
            {decide.status !== 'Declined' && <><label>Approval reference<input value={decide.reference} onChange={(e) => setDecide({ ...decide, reference: e.target.value })} /></label>
              <label>Approved amount<input type="number" step="0.01" value={decide.approvedAmount} onChange={(e) => setDecide({ ...decide, approvedAmount: e.target.value })} /></label>
              <label>Valid until<input type="date" value={decide.validUntil} onChange={(e) => setDecide({ ...decide, validUntil: e.target.value })} /></label></>}
            <label>Notes{decide.status === 'Declined' ? ' (reason)' : ''}<input value={decide.notes} onChange={(e) => setDecide({ ...decide, notes: e.target.value })} /></label>
          </div>
          <div className="row"><button className="btn btn-primary" onClick={() => void run(async () => { await api(`/api/claims/approvals/${decide.id}/decide`, { body: { status: decide.status, reference: decide.reference || null, approvedAmount: decide.status === 'Declined' ? null : Number(decide.approvedAmount), validUntil: decide.status === 'Declined' ? null : decide.validUntil, notes: decide.notes || null } }); setDecide(null); })}>Save answer</button><button className="btn btn-link" onClick={() => setDecide(null)}>Cancel</button></div>
        </div>)}
      {can(Perm.ClaimsManage) && (
        <details className="stack"><summary>Request a prior approval</summary>
          <div className="form-grid">
            {!patientId && <PatientPicker value={patient} onChange={setPatient} />}
            <label>Funder<select value={f.funderId} onChange={(e) => setF({ ...f, funderId: e.target.value })}><option value="">Choose…</option>{funders.filter((x) => x.isActive).map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
            {fund?.kind === 'Acc' ? <label>ACC claim number<input value={f.accClaimNumber} onChange={(e) => setF({ ...f, accClaimNumber: e.target.value })} /></label>
              : <label>Membership / policy number<input value={f.memberNumber} onChange={(e) => setF({ ...f, memberNumber: e.target.value })} /></label>}
            <label>Planned procedure<input value={f.procedure} onChange={(e) => setF({ ...f, procedure: e.target.value })} /></label>
            <label>Amount to request (incl GST)<input type="number" step="0.01" value={f.amountRequested} onChange={(e) => setF({ ...f, amountRequested: e.target.value })} /></label>
            <label>Notes<input value={f.notes} onChange={(e) => setF({ ...f, notes: e.target.value })} /></label>
          </div>
          <p className="hint">Send the request through the funder's portal or form as usual, then press <em>Sent to funder</em> here.</p>
          <div><button className="btn btn-primary" onClick={() => void create()}>Save the request</button></div>
        </details>)}
    </section>
  );
}

function Remittances({ funders, onDone }: { funders: Funder[]; onDone: () => void }) {
  const { can } = useSession();
  const [list, setList] = useState<any[] | null>(null);
  const [open, setOpen] = useState<any>(null);
  const [funderId, setFunderId] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState('');
  const load = useCallback(() => { api<any[]>('/api/claims/remittances').then(setList).catch((e) => setErrors(errs(e))); }, []);
  useEffect(() => { load(); }, [load]);
  const upload = async (file: File | undefined) => {
    if (!file) return;
    setErrors([]); setMsg('');
    try { const r = await api<any>('/api/claims/remittances', { body: { funderId, fileName: file.name, text: await fileToText(file) } }); setMsg(`Imported: ${r.matched} of ${r.lineCount} lines matched, ${money(r.total)} recorded as paid.`); load(); onDone(); setOpen(await api(`/api/claims/remittances/${r.id}`)); }
    catch (e) { setErrors(errs(e)); }
  };
  const name = (id: string) => funders.find((x) => x.id === id)?.name ?? '';
  return (
    <section className="card stack">
      <h2>Payments in (remittances)</h2>
      <Errors list={errors} />{msg && <p className="ok" role="status">{msg}</p>}
      {can(Perm.ClaimsManage) && (
        <div className="form-grid">
          <label>From<select value={funderId} onChange={(e) => setFunderId(e.target.value)}><option value="">Choose the funder…</option>{funders.map((x) => <option key={x.id} value={x.id}>{x.name}</option>)}</select></label>
          <label>Remittance file (CSV)<input type="file" accept=".csv,text/csv" disabled={!funderId} onChange={(e) => void upload(e.target.files?.[0])} /></label>
        </div>)}
      <p className="hint">Each paid line is matched to its claim by the claim number and recorded as a payment on the invoice. Lines that don't match are kept for you to follow up. The same file can't be imported twice.</p>
      {!list ? <p>Loading…</p> : list.length === 0 ? <p className="muted">None yet.</p> : (
        <table className="results"><thead><tr><th>File</th><th>Funder</th><th className="money">Lines</th><th className="money">Matched</th><th className="money">Paid</th><th>Imported</th></tr></thead>
          <tbody>{list.map((r) => <tr key={r.id}><td><button className="btn btn-link" onClick={() => void api(`/api/claims/remittances/${r.id}`).then(setOpen)}>{r.fileName}</button></td><td>{name(r.funderId)}</td><td className="money">{r.lineCount}</td>
            <td className={`money${r.matched < r.lineCount ? ' warn' : ''}`}>{r.matched}</td><td className="money">{money(r.total)}</td><td className="small">{r.importedBy}, {nzDateTime(r.importedAt)}</td></tr>)}</tbody></table>)}
      {open && (
        <div className="card inset"><h3>{open.fileName}</h3>
          <table className="results compact"><thead><tr><th>Claim</th><th className="money">Amount</th><th>Outcome</th><th>What BrightPAS did</th></tr></thead>
            <tbody>{open.lines.map((l: any) => <tr key={l.sequence}><td>{l.claimId ? <Link to={`/claims/${l.claimId}`}>{l.claimReference}</Link> : l.claimReference}</td><td className="money">{money(l.amount)}</td><td>{l.outcome}{l.reason && <div className="small muted">{l.reason}</div>}</td>
              <td className={l.claimId ? '' : 'warn'}>{l.result}</td></tr>)}</tbody></table></div>)}
    </section>
  );
}

/** One claim: what's missing (in plain words), then the next action. */
export function ClaimPage() {
  const { id } = useParams();
  const { can } = useSession();
  const [x, setX] = useState<any>(null);
  const [approvals, setApprovals] = useState<any[]>([]);
  const [fix, setFix] = useState({ memberNumber: '', accClaimNumber: '', approvalId: '' });
  const [resp, setResp] = useState({ accepted: true, text: '' });
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState('');
  const load = useCallback(async () => {
    try {
      const c = await api<any>(`/api/claims/${id}`); setX(c);
      setFix({ memberNumber: c.claim.memberNumber ?? '', accClaimNumber: c.claim.accClaimNumber ?? '', approvalId: c.claim.priorApprovalId ?? '' });
      setApprovals((await api<any[]>(`/api/claims/approvals?patientId=${c.claim.patientId}`)).filter((a) => a.funderId === c.claim.funderId));
    } catch (e) { setErrors(errs(e)); }
  }, [id]);
  useEffect(() => { void load(); }, [load]);
  if (!x) return errors.length ? <Errors list={errors} /> : <p>Loading…</p>;
  const c = x.claim;
  const manage = can(Perm.ClaimsManage);
  const run = async (fn: () => Promise<any>, ok: string) => { setErrors([]); setMsg(''); try { const r = await fn(); setMsg(r?.ready === false ? 'Saved – still not ready: see the checks.' : ok); await load(); } catch (e) { setErrors(errs(e)); } };
  const editable = ['Draft', 'Ready', 'Rejected'].includes(c.status);
  return (
    <div className="stack-lg">
      <p><Link to="/claims">← Claims</Link></p>
      <div className="page-head"><h1>Claim {c.number}</h1><p className="muted">{c.funder} · <Link to={`/patients/${c.patientId}`}>{c.patient}</Link> {c.mrn} · invoice <Link to={`/billing/${c.invoiceId}`}>{c.invoice}</Link></p><Pill s={c.status} map={STATUS} /></div>
      <Errors list={errors} />{msg && <p className="ok" role="status">{msg}</p>}
      <section className="card stack">
        <h2>Checks</h2>
        <ul className="checks">{x.checks.map((k: any) => <li key={k.item} className={k.ok ? 'ok' : 'todo'}><span aria-hidden="true">{k.ok ? '✓' : '!'}</span> <strong>{k.item}</strong>{!k.ok && <div className="small">{k.fix}</div>}</li>)}</ul>
        {manage && editable && (
          <>
            <div className="form-grid">
              <label>Membership / policy number<input value={fix.memberNumber} onChange={(e) => setFix({ ...fix, memberNumber: e.target.value })} /></label>
              <label>ACC claim number<input value={fix.accClaimNumber} onChange={(e) => setFix({ ...fix, accClaimNumber: e.target.value })} /></label>
              <label>Prior approval<select value={fix.approvalId} onChange={(e) => setFix({ ...fix, approvalId: e.target.value })}><option value="">None</option>{approvals.map((a) => <option key={a.id} value={a.id}>{a.number} – {APPROVAL[a.status]?.[0]} {a.approvedAmount != null ? money(a.approvedAmount) : ''}</option>)}</select></label>
            </div>
            <div className="row"><button className="btn btn-primary" onClick={() => void run(() => api(`/api/claims/${id}/ready`, { body: { memberNumber: fix.memberNumber, accClaimNumber: fix.accClaimNumber, approvalId: fix.approvalId || null } }), 'Ready to send.')}>Save and check</button>
              {approvals.length === 0 && <Link className="btn btn-link" to="/claims?tab=approvals">Record a prior approval</Link>}</div>
          </>)}
      </section>
      <section className="card"><h2>Lines</h2>
        <table className="results compact"><thead><tr><th>Code</th><th>Description</th><th className="money">Amount incl GST</th></tr></thead>
          <tbody>{x.lines.map((l: any) => <tr key={l.sequence}><td>{l.code}</td><td>{l.description}</td><td className="money">{money(l.amount)}</td></tr>)}</tbody></table>
        <p className="small">Claimed {money(c.amount)} · paid {money(c.paid)}{c.submittedAt ? ` · sent ${nzDateTime(c.submittedAt)}` : ''}</p></section>
      {manage && ['Submitted', 'Accepted'].includes(c.status) && (
        <section className="card stack"><h2>The funder's answer</h2>
          <div className="form-grid">
            <label>Answer<select value={resp.accepted ? 'y' : 'n'} onChange={(e) => setResp({ ...resp, accepted: e.target.value === 'y' })}><option value="y">Accepted – payment to come</option><option value="n">Rejected</option></select></label>
            <label>Note{resp.accepted ? '' : ' (their reason)'}<input value={resp.text} onChange={(e) => setResp({ ...resp, text: e.target.value })} /></label>
          </div>
          <div><button className="btn btn-primary" onClick={() => void run(() => api(`/api/claims/${id}/response`, { body: resp }), 'Saved.')}>Save</button></div>
          <p className="hint">Payments are recorded automatically when you import the funder's remittance file.</p>
        </section>)}
      {manage && !['Paid', 'WrittenOff'].includes(c.status) && (
        <details className="card"><summary>Close this claim without payment</summary>
          <p className="hint">The balance stays on the invoice – bill the patient or claim from another funder.</p>
          <button className="btn btn-link danger" onClick={() => { const r = window.prompt('Why is this claim being closed?'); if (r) void run(() => api(`/api/claims/${id}/write-off`, { body: { reason: r } }), 'Closed.'); }}>Close claim</button>
        </details>)}
    </div>
  );
}

/** On an invoice: make the claim (ACC/insurer payers) or open the existing one. */
export function InvoiceClaim({ invoiceId, payer, payerName, status }: { invoiceId: string; payer: string; payerName: string | null; status: string }) {
  const { can } = useSession();
  const navigate = useNavigate();
  const { funders } = useMeta();
  const [claimId, setClaimId] = useState<string | null | undefined>(undefined);
  const [funderId, setFunderId] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  useEffect(() => { api<any>(`/api/claims/invoice/${invoiceId}`).then((r) => setClaimId(r.claimId)).catch(() => setClaimId(null)); }, [invoiceId]);
  useEffect(() => {
    if (funderId || funders.length === 0) return;
    const guess = funders.find((f) => (payer === 'Acc' ? f.kind === 'Acc' : f.kind !== 'Acc' && payerName && (payerName.toLowerCase().includes(f.name.toLowerCase().split(' ')[0]) || f.name.toLowerCase().includes(payerName.toLowerCase()))));
    if (guess) setFunderId(guess.id);
  }, [funders, payer, payerName, funderId]);
  if (!['Insurer', 'Acc', 'HealthNz'].includes(payer) || claimId === undefined) return null;
  if (claimId) return <section className="card no-print"><h2>Claim</h2><p><Link className="btn btn-light" to={`/claims/${claimId}`}>Open the claim</Link></p></section>;
  if (!can(Perm.ClaimsManage) || !['Issued', 'PartPaid'].includes(status)) return null;
  const make = async () => { setErrors([]); try { const r = await api<any>('/api/claims/', { body: { invoiceId, funderId } }); navigate(`/claims/${r.id}`); } catch (e) { setErrors(errs(e)); } };
  return (
    <section className="card no-print stack"><h2>Claim from the funder</h2>
      <Errors list={errors} />
      <div className="form-grid"><label>Funder<select value={funderId} onChange={(e) => setFunderId(e.target.value)}><option value="">Choose…</option>{funders.filter((f) => f.isActive && (payer === 'Acc') === (f.kind === 'Acc')).map((f) => <option key={f.id} value={f.id}>{f.name}</option>)}</select></label></div>
      <div><button className="btn btn-primary" disabled={!funderId} onClick={() => void make()}>Make the claim</button></div>
    </section>
  );
}

/** EFTPOS at the desk, on an invoice. The terminal does the card; BrightPAS records the result (never the card number). */
export function TakePayment({ invoiceId, balance, onPaid }: { invoiceId: string; balance: number; onPaid: () => void }) {
  const [meta, setMeta] = useState<{ cardTypes: string[]; terminal: string } | null>(null);
  const [txns, setTxns] = useState<any[]>([]);
  const [amount, setAmount] = useState('');
  const [result, setResult] = useState({ approved: true, authCode: '', cardType: 'Visa', last4: '' });
  const [errors, setErrors] = useState<string[]>([]);
  const [refund, setRefund] = useState<{ id: string; max: number; amount: string; reason: string } | null>(null);
  const { can } = useSession();
  const load = useCallback(() => { api<any[]>(`/api/claims/terminal/invoice/${invoiceId}`).then(setTxns).catch(() => undefined); }, [invoiceId]);
  useEffect(() => { api('/api/claims/terminal/meta').then(setMeta).catch(() => undefined); load(); }, [load]);
  const pending = txns.find((t) => t.status === 'Pending');
  const run = async (fn: () => Promise<any>) => { setErrors([]); try { const r = await fn(); if (r?.error) setErrors([r.error]); load(); onPaid(); } catch (e) { setErrors(errs(e)); } };
  // Integrated terminal: ask for the result every 3 seconds while a transaction is waiting.
  const waitingId = pending?.provider === 'Windcave' ? pending.id : null;
  useEffect(() => {
    if (!waitingId) return undefined;
    const t = window.setInterval(() => { api<any>(`/api/claims/terminal/${waitingId}/check`, { body: {} }).then((r) => { if (r.status !== 'Pending') { load(); onPaid(); } }).catch(() => undefined); }, 3000);
    return () => window.clearInterval(t);
  }, [waitingId, load, onPaid]);
  if (!meta || balance <= 0 && !pending && txns.length === 0) return null;
  return (
    <section className="card no-print stack"><h2>Take payment by EFTPOS</h2>
      <Errors list={errors} />
      {refund && (
        <Modal title="Refund to the card" onClose={() => setRefund(null)}>
          <div className="stack">
            <p className="hint">Up to {money(refund.max)} goes back to the same card. {meta.terminal === 'Manual' ? 'Do the refund on the terminal, then record the receipt.' : 'The terminal will ask for the card.'}</p>
            <label>Amount<input type="number" step="0.01" value={refund.amount} onChange={(e) => setRefund({ ...refund, amount: e.target.value })} /></label>
            <label>Why<input value={refund.reason} onChange={(e) => setRefund({ ...refund, reason: e.target.value })} /></label>
            <div className="row"><button className="btn btn-primary" onClick={() => { const r = refund; setRefund(null); void run(() => api(`/api/claims/terminal/${r.id}/refund`, { body: { amount: Number(r.amount), reason: r.reason } })); }}>Refund now</button></div>
          </div>
        </Modal>)}
      {pending?.provider === 'Windcave' ? (
        <div className="stack"><p role="status"><strong>Waiting for the terminal…</strong> {pending.isRefund ? 'Refund' : 'Payment'} of {money(pending.amount)}. Follow the prompts on the terminal.</p>
          <div><button className="btn btn-link" onClick={() => void run(() => api(`/api/claims/terminal/${pending.id}/cancel`, { body: {} }))}>Cancel</button></div></div>
      ) : !pending ? (balance > 0 &&
        <div className="row">
          <label>Amount<input type="number" step="0.01" value={amount} placeholder={String(balance)} onChange={(e) => setAmount(e.target.value)} /></label>
          <button className="btn btn-primary" onClick={() => void run(() => api('/api/claims/terminal', { body: { invoiceId, amount: Number(amount || balance) } }))}>{meta.terminal === 'Simulator' ? 'Send to terminal (training)' : 'Start EFTPOS payment'}</button>
        </div>
      ) : (
        <div className="stack">
          <p><strong>Key {pending.isRefund ? 'a refund of ' : ''}{money(pending.amount)} into the terminal</strong>, reference {pending.merchantReference}. When the receipt prints, record the result:</p>
          <div className="form-grid">
            <label>Result<select value={result.approved ? 'y' : 'n'} onChange={(e) => setResult({ ...result, approved: e.target.value === 'y' })}><option value="y">Approved</option><option value="n">Declined</option></select></label>
            {result.approved && <label>Authorisation code<input value={result.authCode} onChange={(e) => setResult({ ...result, authCode: e.target.value })} /></label>}
            <label>Card<select value={result.cardType} onChange={(e) => setResult({ ...result, cardType: e.target.value })}>{meta.cardTypes.map((c) => <option key={c}>{c}</option>)}</select></label>
            <label>Last 4 digits only<input inputMode="numeric" maxLength={4} value={result.last4} onChange={(e) => setResult({ ...result, last4: e.target.value.replace(/\D/g, '') })} /></label>
          </div>
          <div className="row"><button className="btn btn-primary" onClick={() => void run(() => api(`/api/claims/terminal/${pending.id}/complete`, { body: { ...result, authCode: result.authCode || null, last4: result.last4 || null, responseText: null } }))}>Save result</button>
            <button className="btn btn-link" onClick={() => void run(() => api(`/api/claims/terminal/${pending.id}/cancel`, { body: {} }))}>Cancel</button></div>
        </div>)}
      {txns.length > 0 && <ul className="plain small">{txns.map((t) => <li key={t.id}>{nzDateTime(t.startedAt)} · {money(t.amount)} · <strong>{t.status}</strong>{t.cardType ? ` · ${t.cardType} …${t.last4 ?? ''}` : ''}{t.authCode ? ` · auth ${t.authCode}` : ''}{t.responseText && t.status !== 'Approved' ? ` · ${t.responseText}` : ''}{t.isRefund ? ` · refund: ${t.reason ?? ''}` : ''}{t.refunded > 0 ? ` · ${money(t.refunded)} refunded` : ''}
        {can(Perm.OnlinePayRefund) && !t.isRefund && t.status === 'Approved' && t.amount > t.refunded && !pending && <> <button className="btn btn-link" onClick={() => setRefund({ id: t.id, max: t.amount - t.refunded, amount: String(t.amount - t.refunded), reason: '' })}>Refund to card</button></>}</li>)}</ul>}
    </section>
  );
}

/** End-of-day EFTPOS settlement – compare with the terminal's settlement slip. */
export function EftposDay() {
  const [date, setDate] = useState(todayIso());
  const [s, setS] = useState<any>(null);
  const [error, setError] = useState('');
  useEffect(() => { api(`/api/claims/terminal/settlement?date=${date}`).then(setS).catch((e) => setError(errText(e))); }, [date]);
  return (
    <div className="stack-lg">
      <p><Link to="/claims">← Claims &amp; payments</Link></p>
      <div className="page-head"><h1>EFTPOS settlement</h1><p className="muted">Check these totals against the terminal's settlement slip at the end of the day.</p></div>
      <label>Day<input type="date" value={date} onChange={(e) => setDate(e.target.value)} /></label>
      {error && <p className="error">{error}</p>}
      {s && (
        <section className="card stack">
          <dl className="facts"><dt>Approved</dt><dd>{s.approved} · {money(s.total)}</dd><dt>Refunds</dt><dd>{s.refunds} · {money(s.refundTotal)}</dd><dt>Declined</dt><dd>{s.declined}</dd><dt>Still waiting</dt><dd className={s.pending > 0 ? 'warn' : ''}>{s.pending}</dd></dl>
          {s.byCard.length > 0 && <table className="results compact"><thead><tr><th>Card</th><th className="money">Count</th><th className="money">Total</th></tr></thead><tbody>{s.byCard.map((r: any) => <tr key={r.cardType}><td>{r.cardType}</td><td className="money">{r.count}</td><td className="money">{money(r.total)}</td></tr>)}</tbody></table>}
          <table className="results compact"><thead><tr><th>Time</th><th>Reference</th><th className="money">Amount</th><th>Result</th><th>By</th></tr></thead>
            <tbody>{s.transactions.map((t: any) => <tr key={t.id}><td>{nzDateTime(t.startedAt)}</td><td><Link to={`/billing/${t.invoiceId}`}>{t.merchantReference}</Link></td><td className="money">{money(t.amount)}</td><td>{t.status}{t.cardType ? ` · ${t.cardType} …${t.last4 ?? ''}` : ''}</td><td>{t.startedBy}</td></tr>)}</tbody></table>
        </section>)}
    </div>
  );
}

/** Xero: send issued invoices (and payments in API mode). File mode is always there as the fallback. */
export function XeroPage() {
  const [from, setFrom] = useState(addDays(todayIso(), -30));
  const [to, setTo] = useState(todayIso());
  const [pending, setPending] = useState<any[] | null>(null);
  const [settings, setSettings] = useState<any>(null);
  const [history, setHistory] = useState<any[]>([]);
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState('');
  const [byHand, setByHand] = useState<string[]>([]);
  const load = useCallback(() => {
    api<any[]>(`/api/claims/xero/pending?from=${from}&to=${to}`).then(setPending).catch((e) => setErrors(errs(e)));
    api('/api/claims/xero/settings').then(setSettings).catch(() => undefined);
    api<any[]>('/api/claims/xero/exports').then(setHistory).catch(() => undefined);
  }, [from, to]);
  useEffect(() => { load(); }, [load]);
  const sendApi = async () => { setErrors([]); setMsg(''); try { const r = await api<any>('/api/claims/xero/export', { body: { from, to } }); setMsg(`Sent ${r.invoices} invoice(s) and ${r.payments} payment(s) or credit note(s) to Xero. Contacts are matched by name and email, or made new.`); if (r.errors?.length) setErrors(r.errors); setByHand(r.byHand ?? []); load(); } catch (e) { setErrors(errs(e)); } };
  return (
    <div className="stack-lg">
      <p><Link to="/claims">← Claims &amp; payments</Link></p>
      <div className="page-head"><h1>Xero</h1><p className="muted">Invoices go to Xero once. {settings?.mode === 'Api' ? 'Connected – invoices and payments are sent directly.' : settings?.mode === 'File' ? 'File mode – import the file in Xero (Business › Invoices › Import, choose “tax exclusive”).' : 'Xero is turned off – an administrator can turn it on in Claims settings.'}</p></div>
      <Errors list={errors} />{msg && <p className="ok" role="status">{msg}</p>}
      {byHand.length > 0 && <section className="card stack"><h2>Record these in Xero by hand</h2><p className="hint">Xero can't take refunds through the connection. Record each one in Xero as a refund on the invoice. This list shows once.</p><ul>{byHand.map((b) => <li key={b}>{b}</li>)}</ul></section>}
      {settings?.lastError && <p className="warn">Last problem: {settings.lastError}</p>}
      <section className="card stack">
        <div className="row"><label>Issued from<input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></label><label>to<input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></label></div>
        {!pending ? <p>Loading…</p> : pending.length === 0 ? <p className="muted">Everything issued in this period is already in Xero.</p> : (
          <table className="results compact"><thead><tr><th>Invoice</th><th>Date</th><th>Payer</th><th className="money">Total</th></tr></thead>
            <tbody>{pending.map((d) => <tr key={d.id}><td><Link to={`/billing/${d.id}`}>{d.number}</Link></td><td>{formatDate(d.issuedOn)}</td><td>{d.payerName ?? d.payer}</td><td className="money">{money(d.total)}</td></tr>)}</tbody></table>)}
        {pending && pending.length > 0 && settings?.mode === 'File' && <ExportButton className="btn btn-primary" label={`Make the Xero file (${pending.length} invoices)`} path="/api/claims/xero/export" body={{ from, to }} />}
        {pending && settings?.mode === 'Api' && <div><button className="btn btn-primary" onClick={() => void sendApi()}>Send to Xero now</button></div>}
      </section>
      {history.length > 0 && (
        <section className="card"><h2>Sent before</h2>
          <table className="results compact"><thead><tr><th>When</th><th>How</th><th className="money">Invoices</th><th className="money">Payments and credits</th><th>Problems</th><th></th></tr></thead>
            <tbody>{history.map((h) => <tr key={h.id}><td>{nzDateTime(h.createdAt)} <span className="small muted">{h.createdBy}</span></td><td>{h.mode}</td><td className="money">{h.invoices}</td><td className="money">{h.payments}</td><td className="small">{h.errors ?? '—'}</td>
              <td>{h.mode === 'File' && h.fileName && <ExportButton label="Download again" path={`/api/claims/xero/exports/${h.id}/download`} body={{}} />}</td></tr>)}</tbody></table></section>)}
    </div>
  );
}

const SETTINGS_TABS: [string, string][] = [['funders', 'Funders'], ['xero', 'Xero'], ['terminal', 'EFTPOS terminal'], ['identity', 'NHI & HPI'], ['practices', 'GP practices']];

/** Claims set-up: funders, Xero, terminal, NHI/HPI and the GP practice list. */
export function ClaimsSettings() {
  const { can } = useSession();
  const [sp, setSp] = useSearchParams();
  const tab = sp.get('tab') ?? 'funders';
  const { funders, reload } = useMeta();
  const [edit, setEdit] = useState<any>(null);
  const [xero, setXero] = useState<any>(null);
  const [term, setTerm] = useState({ provider: 'Manual', terminalId: '', windcaveUrl: '', windcaveUser: '', windcaveKey: '', windcaveKeySet: false });
  const [apiEdit, setApiEdit] = useState<{ id: string; name: string; api: string; apiUrl: string; apiKey: string; apiKeySet: boolean } | null>(null);
  const [idn, setIdn] = useState<any>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState('');
  useEffect(() => {
    api('/api/claims/xero/settings').then((x: any) => setXero({ ...x, clientSecret: '', refreshToken: '' })).catch(() => undefined);
    api('/api/identity/settings').then((x: any) => setIdn({ ...x, clientSecret: '' })).catch(() => undefined);
    api('/api/claims/meta').then((m: any) => { const x = m.terminalSettings; if (x) setTerm({ provider: x.provider, terminalId: x.terminalId ?? '', windcaveUrl: x.windcaveUrl ?? '', windcaveUser: x.windcaveUser ?? '', windcaveKey: '', windcaveKeySet: x.windcaveKeySet }); }).catch(() => undefined);
  }, []);
  const save = async (fn: () => Promise<unknown>) => { setErrors([]); setMsg(''); try { await fn(); setMsg('Saved.'); } catch (e) { setErrors(errs(e)); } };
  const admin = can(Perm.ClaimsAdmin);
  return (
    <div className="stack-lg">
      <p><Link to="/claims">← Claims &amp; payments</Link></p>
      <div className="page-head"><h1>Claims settings</h1></div>
      <nav className="tabs" aria-label="Settings">{SETTINGS_TABS.map(([k, t]) => <button key={k} className={tab === k ? 'active' : ''} aria-pressed={tab === k} onClick={() => { setSp({ tab: k }); setMsg(''); setErrors([]); }}>{t}</button>)}</nav>
      <Errors list={errors} />{msg && <p className="ok" role="status">{msg}</p>}
      {tab === 'funders' && (
        <section className="card stack">
          <table className="results"><thead><tr><th>Funder</th><th>Sent by</th><th>Prior approval</th><th>Provider number</th><th>Direct claiming</th><th></th></tr></thead>
            <tbody>{funders.map((f) => <tr key={f.id} className={f.isActive ? '' : 'muted'}><td><strong>{f.code}</strong> {f.name}</td><td>{f.submitBy}{f.claimsEmail ? ` · ${f.claimsEmail}` : ''}</td><td>{f.priorApprovalRequired ? 'Needed' : '—'}</td><td>{f.providerNumber ?? <span className="warn">not set</span>}</td>
              <td>{f.api === 'Off' ? '—' : f.api === 'Simulator' ? 'Training simulator' : 'Connected'}{f.lastApiError && <div className="small warn">{f.lastApiError}</div>}</td>
              <td>{admin && <div className="row"><button className="btn btn-light" onClick={() => setEdit({ ...f })}>Change</button><button className="btn btn-light" onClick={() => setApiEdit({ id: f.id, name: f.name, api: f.api, apiUrl: f.apiUrl ?? '', apiKey: '', apiKeySet: f.apiKeySet })}>Direct claiming</button></div>}</td></tr>)}</tbody></table>
          {admin && <div><button className="btn btn-light" onClick={() => setEdit({ id: null, code: '', name: '', kind: 'Insurer', submitBy: 'File', priorApprovalRequired: true, claimsEmail: '', providerNumber: '', isActive: true, notes: '' })}>Add a funder</button></div>}
          {apiEdit && (
            <Modal title={`Direct claiming – ${apiEdit.name}`} onClose={() => setApiEdit(null)}>
              <div className="stack">
                <p className="hint">Claims and prior approvals go straight to the funder, and answers and payments come back by themselves. Needs a provider agreement with the funder, who gives you the address and key. The claim file stays as the fallback.</p>
                <label>Send directly<select value={apiEdit.api} onChange={(e) => setApiEdit({ ...apiEdit, api: e.target.value })}><option value="Off">Off (claim file or portal)</option><option value="Simulator">Training simulator</option><option value="Fhir">Connected (FHIR)</option></select></label>
                {apiEdit.api === 'Fhir' && <>
                  <label>Funder's API address<input value={apiEdit.apiUrl} placeholder="https://" onChange={(e) => setApiEdit({ ...apiEdit, apiUrl: e.target.value })} /></label>
                  <label>API key {apiEdit.apiKeySet && <span className="small muted">(saved – leave blank to keep)</span>}<input type="password" autoComplete="off" value={apiEdit.apiKey} onChange={(e) => setApiEdit({ ...apiEdit, apiKey: e.target.value })} /></label>
                </>}
                {apiEdit.api === 'Simulator' && <p className="hint">Simulator: claims are accepted on the first check and paid on the next; amounts ending in .13 are rejected or declined.</p>}
                <div className="row"><button className="btn btn-primary" onClick={() => void save(async () => { await api(`/api/claims/funders/${apiEdit.id}/api`, { method: 'PUT', body: { api: apiEdit.api, apiUrl: apiEdit.apiUrl || null, apiKey: apiEdit.apiKey || null } }); setApiEdit(null); await reload(); })}>Save</button><button className="btn btn-link" onClick={() => setApiEdit(null)}>Cancel</button></div>
              </div>
            </Modal>)}
          {edit && (
            <Modal title={edit.id ? `Change ${edit.name}` : 'New funder'} onClose={() => setEdit(null)}>
              <div className="form-grid">
                <label>Code<input value={edit.code} onChange={(e) => setEdit({ ...edit, code: e.target.value })} /></label>
                <label>Name<input value={edit.name} onChange={(e) => setEdit({ ...edit, name: e.target.value })} /></label>
                <label>Type<select value={edit.kind} onChange={(e) => setEdit({ ...edit, kind: e.target.value })}><option value="Insurer">Health insurer</option><option value="Acc">ACC</option><option value="HealthNz">Health NZ</option></select></label>
                <label>How claims are sent<select value={edit.submitBy} onChange={(e) => setEdit({ ...edit, submitBy: e.target.value })}><option value="File">Claim file (upload or email)</option><option value="Portal">Funder's portal</option><option value="Api">Direct (agreement needed)</option></select></label>
                <label>Claims email<input value={edit.claimsEmail ?? ''} onChange={(e) => setEdit({ ...edit, claimsEmail: e.target.value })} /></label>
                <label>TDS provider number<input value={edit.providerNumber ?? ''} onChange={(e) => setEdit({ ...edit, providerNumber: e.target.value })} /></label>
              </div>
              <label className="check"><input type="checkbox" checked={edit.priorApprovalRequired} onChange={(e) => setEdit({ ...edit, priorApprovalRequired: e.target.checked })} /> Needs prior approval</label>
              <label className="check"><input type="checkbox" checked={edit.isActive} onChange={(e) => setEdit({ ...edit, isActive: e.target.checked })} /> In use</label>
              <div className="row"><button className="btn btn-primary" onClick={() => void save(async () => { await api('/api/claims/funders', { body: { ...edit, claimsEmail: edit.claimsEmail || null, providerNumber: edit.providerNumber || null, notes: edit.notes || null } }); setEdit(null); await reload(); })}>Save</button><button className="btn btn-link" onClick={() => setEdit(null)}>Cancel</button></div>
            </Modal>)}
        </section>)}
      {tab === 'xero' && xero && (
        <section className="card stack">
          <div className="form-grid">
            <label>Send to Xero by<select value={xero.mode} onChange={(e) => setXero({ ...xero, mode: e.target.value })}><option value="Off">Off</option><option value="File">File (import in Xero)</option><option value="Api">Direct connection (Xero app)</option></select></label>
            <label>Sales account code<input value={xero.salesAccountCode} onChange={(e) => setXero({ ...xero, salesAccountCode: e.target.value })} /></label>
            <label>GST tax type<input value={xero.gstTaxType} onChange={(e) => setXero({ ...xero, gstTaxType: e.target.value })} /></label>
            <label>No-GST tax type<input value={xero.noGstTaxType} onChange={(e) => setXero({ ...xero, noGstTaxType: e.target.value })} /></label>
            {xero.mode === 'Api' && <>
              <label>Bank account code for payments<input value={xero.paymentAccountCode ?? ''} onChange={(e) => setXero({ ...xero, paymentAccountCode: e.target.value })} /></label>
              <label>Tenant ID<input value={xero.tenantId ?? ''} onChange={(e) => setXero({ ...xero, tenantId: e.target.value })} /></label>
              <label>App client ID<input value={xero.clientId ?? ''} onChange={(e) => setXero({ ...xero, clientId: e.target.value })} /></label>
              <label>App client secret {xero.hasSecret && <span className="small muted">(saved – leave blank to keep)</span>}<input type="password" autoComplete="off" value={xero.clientSecret} onChange={(e) => setXero({ ...xero, clientSecret: e.target.value })} /></label>
              <label>Refresh token {xero.connected && <span className="small muted">(connected – leave blank to keep)</span>}<input type="password" autoComplete="off" value={xero.refreshToken} onChange={(e) => setXero({ ...xero, refreshToken: e.target.value })} /></label>
            </>}
          </div>
          {admin && <div><button className="btn btn-primary" onClick={() => void save(() => api('/api/claims/xero/settings', { method: 'PUT', body: { ...xero, clientSecret: xero.clientSecret || null, refreshToken: xero.refreshToken || null } }))}>Save</button></div>}
          <p className="hint">Secrets are stored encrypted and never shown again. Xero rotates the refresh token each time; BrightPAS saves the new one.</p>
        </section>)}
      {tab === 'terminal' && (
        <section className="card stack">
          <div className="form-grid">
            <label>Terminal<select value={term.provider} onChange={(e) => setTerm({ ...term, provider: e.target.value })}><option value="Manual">Stand-alone terminal (record the result)</option><option value="Simulator">Training simulator</option><option value="Windcave">Windcave terminal (linked)</option></select></label>
            <label>{term.provider === 'Windcave' ? 'Station ID' : 'Terminal ID (optional)'}<input value={term.terminalId} onChange={(e) => setTerm({ ...term, terminalId: e.target.value })} /></label>
            {term.provider === 'Windcave' && <>
              <label>Windcave address<input value={term.windcaveUrl} placeholder="https://uat.windcave.com" onChange={(e) => setTerm({ ...term, windcaveUrl: e.target.value })} /></label>
              <label>HIT user name<input value={term.windcaveUser} onChange={(e) => setTerm({ ...term, windcaveUser: e.target.value })} /></label>
              <label>HIT key {term.windcaveKeySet && <span className="small muted">(saved – leave blank to keep)</span>}<input type="password" autoComplete="off" value={term.windcaveKey} onChange={(e) => setTerm({ ...term, windcaveKey: e.target.value })} /></label>
            </>}
          </div>
          {admin && <div><button className="btn btn-primary" onClick={() => void save(() => api('/api/claims/terminal/settings', { method: 'PUT', body: { provider: term.provider, terminalId: term.terminalId || null, windcaveUrl: term.windcaveUrl || null, windcaveUser: term.windcaveUser || null, windcaveKey: term.windcaveKey || null } }))}>Save</button></div>}
          <p className="hint">Simulator: amounts ending in .51 are declined and .52 time out, for training. Windcave: the amount goes to the terminal by itself and the result comes back – needs a Windcave merchant agreement and HIT user and key. Test against uat.windcave.com first.</p>
        </section>)}
      {tab === 'identity' && idn && (
        <section className="card stack">
          <div className="form-grid">
            <label>Lookups<select value={idn.mode} onChange={(e) => setIdn({ ...idn, mode: e.target.value })}><option value="Off">Off (use the Health NZ portal and the practice file)</option><option value="Simulator">Training simulator</option><option value="Live">Live – Health NZ APIs</option></select></label>
            {idn.mode === 'Live' && <>
              <label>NHI API address<input value={idn.nhiBaseUrl ?? ''} onChange={(e) => setIdn({ ...idn, nhiBaseUrl: e.target.value })} /></label>
              <label>HPI API address<input value={idn.hpiBaseUrl ?? ''} onChange={(e) => setIdn({ ...idn, hpiBaseUrl: e.target.value })} /></label>
              <label>Token address<input value={idn.tokenUrl ?? ''} onChange={(e) => setIdn({ ...idn, tokenUrl: e.target.value })} /></label>
              <label>Client ID<input value={idn.clientId ?? ''} onChange={(e) => setIdn({ ...idn, clientId: e.target.value })} /></label>
              <label>Client secret {idn.hasSecret && <span className="small muted">(saved – leave blank to keep)</span>}<input type="password" autoComplete="off" value={idn.clientSecret} onChange={(e) => setIdn({ ...idn, clientSecret: e.target.value })} /></label>
            </>}
          </div>
          {idn.lastError && <p className="warn">Last problem: {idn.lastError}</p>}
          {admin && <div><button className="btn btn-primary" onClick={() => void save(() => api('/api/identity/settings', { method: 'PUT', body: { ...idn, clientSecret: idn.clientSecret || null } }))}>Save</button></div>}
          <p className="hint">Live lookups need a Health NZ integration agreement (test environment first). Every lookup is audited and limited to 30 a minute per person.</p>
        </section>)}
      {tab === 'practices' && <PracticeImport />}
    </div>
  );
}

function PracticeImport() {
  const { can } = useSession();
  const [q, setQ] = useState('');
  const [found, setFound] = useState<any>(null);
  const [result, setResult] = useState<any>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const upload = async (file: File | undefined) => { if (!file) return; setErrors([]); try { setResult(await api('/api/identity/practices/import', { body: { text: await fileToText(file) } })); } catch (e) { setErrors(errs(e)); } };
  const add = async (f: any) => { setErrors([]); try { await api('/api/identity/hpi/facilities/add', { body: f }); setResult({ added: 1, updated: 0, errors: [] }); } catch (e) { setErrors(errs(e)); } };
  if (!can(Perm.LettersTemplates)) return <p className="muted">Ask an administrator to update the GP practice list.</p>;
  return (
    <section className="card stack">
      <h2>GP practices</h2>
      <Errors list={errors} />
      <div className="row"><label>Find in the HPI<input value={q} onChange={(e) => setQ(e.target.value)} placeholder="Practice name, town or F-number" /></label>
        <button className="btn btn-light" onClick={() => void api(`/api/identity/hpi/facilities?q=${encodeURIComponent(q)}`).then(setFound).catch((e) => setErrors(errs(e)))}>Search</button></div>
      {found?.error && <p className="warn">{found.error}</p>}
      {found?.rows?.length > 0 && <ul className="plain">{found.rows.map((f: any) => <li key={f.id}><strong>{f.name}</strong> {f.id} · {[f.addressLine1, f.city].filter(Boolean).join(', ')} <button className="btn btn-light" onClick={() => void add(f)}>Add / update</button></li>)}</ul>}
      <label>Or import a list (CSV: facility ID, name, address, city, postcode, phone, HealthLink EDI)<input type="file" accept=".csv,text/csv" onChange={(e) => void upload(e.target.files?.[0])} /></label>
      {result && <p className="ok" role="status">{result.added} added, {result.updated} updated.{result.errors?.length > 0 && <> {result.errors.length} line(s) skipped:</>}</p>}
      {result?.errors?.length > 0 && <ul className="small">{result.errors.map((e: string) => <li key={e}>{e}</li>)}</ul>}
      <p className="hint">Practices are matched by HPI facility ID, so importing again updates them. Letters use these for GP addresses and HealthLink.</p>
    </section>
  );
}

/** Patient page: compare our details with the NHI and accept contact changes. Identity changes go through an identity correction. */
export function NhiCheck({ patientId, onChanged }: { patientId: string; onChanged: () => void }) {
  const { can } = useSession();
  const [r, setR] = useState<any>(null);
  const [pick, setPick] = useState<string[]>([]);
  const [busy, setBusy] = useState(false);
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState('');
  const check = async () => { setBusy(true); setErrors([]); setMsg(''); try { const x = await api<any>(`/api/identity/nhi/${patientId}`); setR(x); setPick(x.fields?.filter((f: any) => f.differs && !f.identity && f.theirs).map((f: any) => f.key) ?? []); } catch (e) { setErrors(errs(e)); } finally { setBusy(false); } };
  const accept = async () => { setErrors([]); try { await api(`/api/identity/nhi/${patientId}/accept`, { body: { fields: pick } }); setMsg('Updated from the NHI.'); onChanged(); await check(); } catch (e) { setErrors(errs(e)); } };
  const diffs = r?.fields?.filter((f: any) => f.differs) ?? [];
  return (
    <div className="stack">
      <div><button className="btn btn-light" disabled={busy} onClick={() => void check()}>{busy ? 'Checking…' : 'Check with the NHI'}</button></div>
      <Errors list={errors} />{msg && <p className="ok" role="status">{msg}</p>}
      {r?.error && <p className="warn">{r.error}</p>}
      {r?.found && (
        <div className="stack">
          {!r.isLive && <p className="error">This NHI is no longer in use – the live NHI is <strong>{r.liveNhi}</strong>. Correct it with an identity correction (second-person check).</p>}
          {r.deceased && <p className="error">The NHI records this person as deceased. Check before going further.</p>}
          {diffs.length === 0 ? <p className="ok">Our details match the NHI.</p> : (
            <>
              <table className="results compact"><thead><tr><th></th><th>Detail</th><th>BrightPAS</th><th>NHI</th></tr></thead>
                <tbody>{diffs.map((f: any) => <tr key={f.key}><td>{!f.identity && f.theirs && <input type="checkbox" aria-label={`Use the NHI ${f.label}`} checked={pick.includes(f.key)} onChange={() => setPick(pick.includes(f.key) ? pick.filter((k) => k !== f.key) : [...pick, f.key])} />}</td>
                  <td>{f.label}</td><td>{f.ours ?? '—'}</td><td><strong>{f.theirs ?? '—'}</strong>{f.identity && <div className="small muted">Change with an identity correction</div>}</td></tr>)}</tbody></table>
              {can(Perm.PatientEdit) && pick.length > 0 && <div><button className="btn btn-primary" onClick={() => void accept()}>Use the ticked NHI details</button></div>}
            </>)}
        </div>)}
    </div>
  );
}
