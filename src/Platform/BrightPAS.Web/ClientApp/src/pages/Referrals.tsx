import { Fragment, useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { api, errText, formatDate, nzDateTime, PatientSummary, Perm, todayIso } from '../api';
import { useSession } from '../App';
import PatientPicker from '../components/PatientPicker';

export interface ReferralRow {
  id: string; number: string; patientId: string; patientName: string; mrn: string; nhi: string | null; dateOfBirth: string; receivedOn: string; source: string;
  referrerName: string; referrerOrganisation: string | null; referrerHpi: string | null; specialist: string; service: string; funder: string; funderReference: string | null;
  reason: string; status: string; triagedAt: string | null; triagedBy: string | null; triageNotes: string | null; declineReason: string | null; daysSinceReceived: number; waitlistEntryId: string | null;
}

export const SOURCES: [string, string][] = [['GeneralPractice', 'GP / general practice'], ['Specialist', 'Specialist'], ['SelfReferral', 'Self-referral'], ['HealthNz', 'Health NZ (outsourced)'], ['Acc', 'ACC'], ['Insurer', 'Health insurer'], ['Other', 'Other']];
export const FUNDERS: [string, string][] = [['SelfPay', 'Self-pay'], ['Insurer', 'Health insurer'], ['Acc', 'ACC'], ['HealthNz', 'Health NZ (publicly funded)'], ['Other', 'Other']];
export const PRIORITIES: [string, string][] = [['Urgent', 'Urgent'], ['SemiUrgent', 'Semi-urgent'], ['Routine', 'Routine']];
export const SIDES: [string, string][] = [['NotApplicable', 'Not applicable'], ['Left', 'Left'], ['Right', 'Right'], ['Bilateral', 'Bilateral']];
export const ANAES: string[] = ['General', 'Sedation', 'Regional', 'Local', 'None'];
export const label = (list: [string, string][], v: string) => list.find(([k]) => k === v)?.[1] ?? v;
const statusClass: Record<string, string> = { Received: 'amber', Accepted: 'green', Declined: '', Withdrawn: '' };

/** Referral intake and clinical triage. Accepting a referral puts the patient on the waitlist. */
export default function Referrals() {
  const { can, writable } = useSession();
  const [params] = useSearchParams();
  const canWrite = writable('referrals');
  const [view, setView] = useState('to-triage');
  const [rows, setRows] = useState<ReferralRow[] | null>(null);
  const [open, setOpen] = useState<string | null>(null);
  const [adding, setAdding] = useState(params.get('new') === '1');
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const load = () => { setRows(null); api<ReferralRow[]>(`/api/referrals?view=${view}`).then(setRows).catch((e) => setError(errText(e))); };
  useEffect(load, [view]);

  return (
    <div className="stack-lg">
      <div className="row-between">
        <div className="page-head"><h1>Referrals</h1><p className="muted">Record referral letters as they arrive. The specialist triages each one: accept (adds to the waitlist) or decline.</p></div>
        <div className="row">
          <Link className="btn btn-light" to="/waitlist">Waitlist</Link>
          {can(Perm.ReferralsManage) && canWrite && !adding && <button className="btn btn-primary" onClick={() => { setAdding(true); setNotice(null); }}>New referral</button>}
        </div>
      </div>
      {!canWrite && <p className="warn-box">Referrals are read-only because of a licence problem.</p>}
      {notice && <p className="notice" role="status">{notice}</p>}
      {adding && <NewReferral onCancel={() => setAdding(false)} onDone={(n) => { setAdding(false); setNotice(`Referral ${n} recorded and waiting for triage.`); setView('to-triage'); load(); }} />}
      <section className="card">
        <div className="tabs">{[['to-triage', 'To triage'], ['recent', 'Last 90 days'], ['all', 'All']].map(([k, t]) =>
          <button key={k} className={view === k ? 'on' : ''} onClick={() => setView(k)}>{t}</button>)}</div>
        {error && <p className="error">{error}</p>}
        {rows?.length === 0 && <p className="muted">{view === 'to-triage' ? 'No referrals waiting for triage.' : 'No referrals.'}</p>}
        {rows && rows.length > 0 && (
          <table className="results">
            <thead><tr><th>Received</th><th>Patient</th><th>From</th><th>To</th><th>Reason</th><th>Status</th></tr></thead>
            <tbody>{rows.map((r) => (
              <Fragment key={r.id}>
                <tr tabIndex={0} onClick={() => setOpen(open === r.id ? null : r.id)} onKeyDown={(e) => e.key === 'Enter' && setOpen(open === r.id ? null : r.id)} aria-expanded={open === r.id}>
                  <td data-label="Received">{formatDate(r.receivedOn)}<div className="muted small">{r.number} · {r.daysSinceReceived} day{r.daysSinceReceived === 1 ? '' : 's'} ago</div></td>
                  <td data-label="Patient"><strong>{r.patientName}</strong><div className="muted small">{r.nhi ?? '—'} · {r.mrn}</div></td>
                  <td data-label="From">{r.referrerName}<div className="muted small">{r.referrerOrganisation ?? label(SOURCES, r.source)}</div></td>
                  <td data-label="To">{r.specialist}<div className="muted small">{r.service} · {label(FUNDERS, r.funder)}</div></td>
                  <td data-label="Reason" className="small">{r.reason.length > 90 ? r.reason.slice(0, 90) + '…' : r.reason}</td>
                  <td data-label="Status"><span className={`pill ${statusClass[r.status] ?? ''}`}>{r.status === 'Received' ? 'To triage' : r.status}</span></td>
                </tr>
                {open === r.id && <tr className="detail-row"><td colSpan={6}><ReferralDetail r={r} canWrite={canWrite} onChange={(msg) => { setOpen(null); setNotice(msg); load(); }} /></td></tr>}
              </Fragment>
            ))}</tbody>
          </table>
        )}
      </section>
    </div>
  );
}

function NewReferral({ onDone, onCancel }: { onDone: (number: string) => void; onCancel: () => void }) {
  const [patient, setPatient] = useState<PatientSummary | null>(null);
  const [f, setF] = useState({ receivedOn: todayIso(), source: 'GeneralPractice', referrerName: '', referrerOrganisation: '', referrerHpi: '', specialist: '', service: 'Surgical', funder: 'SelfPay', funderReference: '', reason: '' });
  const [errors, setErrors] = useState<string[]>([]);
  const set = (k: keyof typeof f) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>) => setF({ ...f, [k]: e.target.value });
  const save = async (e: React.FormEvent) => {
    e.preventDefault(); setErrors([]);
    if (!patient) { setErrors(['Choose the patient first (register them if they are new).']); return; }
    try { const r = await api<{ number: string }>('/api/referrals', { body: { ...f, patientId: patient.id } }); onDone(r.number); }
    catch (x: any) { setErrors(x?.body?.errors ?? [errText(x)]); }
  };
  return (
    <form className="card stack" onSubmit={save}>
      <h2>New referral</h2>
      <PatientPicker value={patient} onChange={setPatient} />
      {!patient && <p className="hint">New patient? <Link to="/patients/new">Register them first</Link>, then come back.</p>}
      <div className="form-grid">
        <label>Date received<input type="date" value={f.receivedOn} max={todayIso()} onChange={set('receivedOn')} required /></label>
        <label>Referral from<select value={f.source} onChange={set('source')}>{SOURCES.map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
        <label>Referrer's name<input value={f.referrerName} onChange={set('referrerName')} required placeholder="e.g. Dr Mere Smith" /></label>
        <label>Practice / organisation<input value={f.referrerOrganisation} onChange={set('referrerOrganisation')} /></label>
        <label>Referrer HPI number (optional)<input value={f.referrerHpi} onChange={set('referrerHpi')} placeholder="e.g. 12ABC345" /></label>
        <label>Referred to (specialist)<input value={f.specialist} onChange={set('specialist')} required /></label>
        <label>Service<select value={f.service} onChange={set('service')}><option>Surgical</option><option>Endoscopy</option></select></label>
        <label>Funder<select value={f.funder} onChange={set('funder')}>{FUNDERS.map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
        <label>Approval / claim / referral number<input value={f.funderReference} onChange={set('funderReference')} placeholder="Insurer approval, ACC claim or Health NZ id" /></label>
      </div>
      <label>Reason for referral<textarea value={f.reason} onChange={set('reason')} required placeholder="Summary from the letter. Scan the letter itself to the patient record." /></label>
      {errors.length > 0 && <div className="error" role="alert">{errors.map((x) => <div key={x}>{x}</div>)}</div>}
      <div className="row"><button className="btn btn-primary">Save referral</button><button type="button" className="btn btn-light" onClick={onCancel}>Cancel</button></div>
    </form>
  );
}

function ReferralDetail({ r, canWrite, onChange }: { r: ReferralRow; canWrite: boolean; onChange: (msg: string) => void }) {
  const { can } = useSession();
  const [mode, setMode] = useState<'none' | 'accept' | 'decline' | 'withdraw'>('none');
  const [f, setF] = useState({ procedure: '', laterality: 'NotApplicable', anaesthetic: r.service === 'Endoscopy' ? 'Sedation' : 'General', durationMinutes: 60, priority: 'Routine', decisionDate: todayIso(), notes: '', reason: '' });
  const [errors, setErrors] = useState<string[]>([]);
  const set = (k: keyof typeof f) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>) => setF({ ...f, [k]: k === 'durationMinutes' ? Number(e.target.value) : e.target.value });
  const submit = async () => {
    setErrors([]);
    try {
      if (mode === 'withdraw') { await api(`/api/referrals/${r.id}/withdraw`, { body: { reason: f.reason } }); onChange(`Referral ${r.number} withdrawn.`); return; }
      const accept = mode === 'accept';
      await api(`/api/referrals/${r.id}/triage`, { body: accept
        ? { accept, notes: f.notes, procedure: f.procedure, laterality: f.laterality, anaesthetic: f.anaesthetic, durationMinutes: f.durationMinutes, priority: f.priority, decisionDate: f.decisionDate }
        : { accept, notes: f.notes, declineReason: f.reason, laterality: 'NotApplicable', anaesthetic: 'General', durationMinutes: 0, priority: 'Routine' } });
      onChange(accept ? `${r.patientName} added to the waitlist (${label(PRIORITIES, f.priority)}).` : `Referral ${r.number} declined – let the referrer know.`);
    } catch (x: any) { setErrors(x?.body?.errors ?? [errText(x)]); }
  };
  return (
    <div className="stack">
      <dl className="facts">
        <dt>Patient</dt><dd><Link to={`/patients/${r.patientId}`}>{r.patientName}</Link> · born {formatDate(r.dateOfBirth)}</dd>
        <dt>Referrer</dt><dd>{r.referrerName}{r.referrerOrganisation ? `, ${r.referrerOrganisation}` : ''}{r.referrerHpi ? ` (HPI ${r.referrerHpi})` : ''} · {label(SOURCES, r.source)}</dd>
        <dt>Funder</dt><dd>{label(FUNDERS, r.funder)}{r.funderReference ? ` · ${r.funderReference}` : ''}</dd>
        <dt>Reason</dt><dd className="pre">{r.reason}</dd>
        {r.triagedAt && <><dt>Triage</dt><dd>{r.status} by {r.triagedBy}, {nzDateTime(r.triagedAt)}{r.triageNotes ? ` – ${r.triageNotes}` : ''}</dd></>}
        {r.declineReason && <><dt>{r.status === 'Withdrawn' ? 'Withdrawn' : 'Declined'}</dt><dd>{r.declineReason}</dd></>}
        {r.waitlistEntryId && <><dt>Waitlist</dt><dd><Link to={`/waitlist?patient=${r.patientId}`}>On the waitlist</Link></dd></>}
      </dl>
      {r.status === 'Received' && canWrite && mode === 'none' && (
        <div className="row">
          {can(Perm.ReferralsTriage) && <><button className="btn btn-primary" onClick={() => setMode('accept')}>Accept – add to waitlist</button><button className="btn btn-light" onClick={() => setMode('decline')}>Decline</button></>}
          {can(Perm.ReferralsManage) && <button className="btn btn-link" onClick={() => setMode('withdraw')}>Withdraw</button>}
          {!can(Perm.ReferralsTriage) && <p className="hint">Waiting for {r.specialist} to triage.</p>}
        </div>
      )}
      {mode === 'accept' && (
        <div className="card subtle stack">
          <h3>Accept and add to the waitlist</h3>
          <div className="form-grid">
            <label>Planned procedure<input value={f.procedure} onChange={set('procedure')} required /></label>
            <label>Side<select value={f.laterality} onChange={set('laterality')}>{SIDES.map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
            <label>Anaesthetic<select value={f.anaesthetic} onChange={set('anaesthetic')}>{ANAES.map((a) => <option key={a}>{a}</option>)}</select></label>
            <label>Expected minutes<input type="number" min={5} max={720} value={f.durationMinutes} onChange={set('durationMinutes')} /></label>
            <label>Priority<select value={f.priority} onChange={set('priority')}>{PRIORITIES.map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
            <label>Decision to treat (date)<input type="date" max={todayIso()} value={f.decisionDate} onChange={set('decisionDate')} /></label>
          </div>
          <label>Notes for the booking team (optional)<input value={f.notes} onChange={set('notes')} /></label>
          {errors.length > 0 && <div className="error" role="alert">{errors.map((x) => <div key={x}>{x}</div>)}</div>}
          <div className="row"><button className="btn btn-primary" onClick={submit}>Accept</button><button className="btn btn-light" onClick={() => setMode('none')}>Back</button></div>
        </div>
      )}
      {(mode === 'decline' || mode === 'withdraw') && (
        <div className="card subtle stack">
          <h3>{mode === 'decline' ? 'Decline the referral' : 'Withdraw the referral'}</h3>
          <label>Reason{mode === 'decline' ? ' (tell the referrer)' : ''}<input value={f.reason} onChange={set('reason')} required /></label>
          {errors.length > 0 && <div className="error" role="alert">{errors.map((x) => <div key={x}>{x}</div>)}</div>}
          <div className="row"><button className="btn btn-warn" onClick={submit}>{mode === 'decline' ? 'Decline' : 'Withdraw'}</button><button className="btn btn-light" onClick={() => setMode('none')}>Back</button></div>
        </div>
      )}
    </div>
  );
}
