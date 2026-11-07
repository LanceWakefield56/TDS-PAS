import { Fragment, useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { addDays, api, errText, formatDate, PatientSummary, Perm, todayIso } from '../api';
import { useSession } from '../App';
import PatientPicker from '../components/PatientPicker';
import { ANAES, FUNDERS, label, PRIORITIES, SIDES } from './Referrals';

export interface WaitRow {
  id: string; referralId: string | null; referralNumber: string | null; patientId: string; patientName: string; mrn: string; nhi: string | null; dateOfBirth: string;
  procedure: string; laterality: string; anaesthetic: string; durationMinutes: number; service: string; specialist: string; funder: string; priority: string;
  addedOn: string; targetDate: string; daysWaiting: number; daysToTarget: number; overdue: boolean; status: string; holdReason: string | null; holdUntil: string | null;
  bookingId: string | null; bookedFor: string | null; removedReason: string | null; notes: string | null; publiclyFunded: boolean;
}
interface Summary { toTriage: number; waiting: number; onHold: number; overdue: number; dueIn14Days: number; byPriority: { priority: string; count: number }[]; specialists: string[] }
interface Session { id: string; locationName: string; date: string; startTime: string; endTime: string; type: string; surgeon: string; bookedMinutes: number; availableMinutes: number }
const prClass: Record<string, string> = { Urgent: 'red', SemiUrgent: 'amber', Routine: '' };
const hm = (t: string) => t.slice(0, 5);

/** The surgical waitlist: who is waiting, for how long, against which target – and booking them onto a theatre list. */
export default function Waitlist() {
  const { can, writable, mod } = useSession();
  const [params] = useSearchParams();
  const patientFilter = params.get('patient');
  const canWrite = writable('referrals');
  const [view, setView] = useState('waiting');
  const [specialist, setSpecialist] = useState('');
  const [priority, setPriority] = useState('');
  const [rows, setRows] = useState<WaitRow[] | null>(null);
  const [summary, setSummary] = useState<Summary | null>(null);
  const [open, setOpen] = useState<{ id: string; action: 'book' | 'hold' | 'edit' | 'remove' } | null>(null);
  const [adding, setAdding] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = async () => {
    setError(null); setRows(null);
    try {
      const q = patientFilter ? `patientId=${patientFilter}` : `view=${view}${specialist ? `&specialist=${encodeURIComponent(specialist)}` : ''}${priority ? `&priority=${priority}` : ''}`;
      const [r, s] = await Promise.all([api<WaitRow[]>(`/api/waitlist?${q}`), api<Summary>('/api/referrals/summary')]);
      setRows(r); setSummary(s);
    } catch (e) { setError(errText(e)); }
  };
  useEffect(() => { load(); }, [view, specialist, priority, patientFilter]);
  const done = (msg: string) => { setOpen(null); setNotice(msg); load(); };

  return (
    <div className="stack-lg">
      <div className="row-between">
        <div className="page-head"><h1>Waitlist</h1><p className="muted">Patients accepted for a procedure, oldest target first. Book them onto a theatre list from here.</p></div>
        <div className="row">
          <Link className="btn btn-light" to="/referrals">Referrals{summary?.toTriage ? <span className="count">{summary.toTriage}</span> : null}</Link>
          {can(Perm.ReferralsManage) && <a className="btn btn-light" href={`/api/waitlist/export?view=${view === 'waiting' ? 'all-open' : view}`} download>Export (CSV)</a>}
          {can(Perm.ReferralsManage) && canWrite && !adding && <button className="btn btn-primary" onClick={() => setAdding(true)}>Add to waitlist</button>}
        </div>
      </div>
      {!canWrite && <p className="warn-box">The waitlist is read-only because of a licence problem.</p>}
      {summary && !patientFilter && (
        <div className="tiles">
          <div className="tile"><span className="big">{summary.waiting}</span>waiting</div>
          <div className={`tile ${summary.overdue ? 'bad' : ''}`}><span className="big">{summary.overdue}</span>past target date</div>
          <div className={`tile ${summary.dueIn14Days ? 'warn' : ''}`}><span className="big">{summary.dueIn14Days}</span>reach target in 14 days</div>
          <div className="tile"><span className="big">{summary.onHold}</span>on hold</div>
        </div>
      )}
      {notice && <p className="notice" role="status">{notice}</p>}
      {error && <p className="error" role="alert">{error}</p>}
      {adding && <AddEntry onCancel={() => setAdding(false)} onDone={(m) => { setAdding(false); done(m); }} />}
      <section className="card">
        {patientFilter
          ? <p className="notice">Showing one patient. <Link to="/waitlist">Show everyone</Link></p>
          : <div className="row-between">
              <div className="tabs">{[['waiting', 'Waiting'], ['on-hold', 'On hold'], ['booked', 'Booked'], ['closed', 'Done / removed']].map(([k, t]) =>
                <button key={k} className={view === k ? 'on' : ''} onClick={() => setView(k)}>{t}</button>)}</div>
              <div className="row">
                <label className="inline">Specialist<select value={specialist} onChange={(e) => setSpecialist(e.target.value)}><option value="">All</option>{summary?.specialists.map((s) => <option key={s}>{s}</option>)}</select></label>
                <label className="inline">Priority<select value={priority} onChange={(e) => setPriority(e.target.value)}><option value="">All</option>{PRIORITIES.map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
              </div>
            </div>}
        {rows?.length === 0 && <p className="muted">No patients here.</p>}
        {rows && rows.length > 0 && (
          <table className="results">
            <thead><tr><th>Patient</th><th>Procedure</th><th>Priority</th><th>Waiting</th><th>Target</th><th>Status</th><th className="no-print">Actions</th></tr></thead>
            <tbody>{rows.map((r) => {
              const isOpen = r.status === 'Waiting' || r.status === 'OnHold';
              return (
                <Fragment key={r.id}>
                  <tr className={r.overdue ? 'overdue' : ''}>
                    <td data-label="Patient"><Link to={`/patients/${r.patientId}`}><strong>{r.patientName}</strong></Link><div className="muted small">{r.nhi ?? '—'} · {r.mrn} · {formatDate(r.dateOfBirth)}</div></td>
                    <td data-label="Procedure">{r.procedure}{r.laterality !== 'NotApplicable' && <strong> ({r.laterality.toUpperCase()})</strong>}
                      <div className="muted small">{r.specialist} · {r.durationMinutes} min · {r.anaesthetic} · {label(FUNDERS, r.funder)}{r.publiclyFunded && <span className="badge">NBRS</span>}</div>
                      {r.notes && <div className="small">{r.notes}</div>}</td>
                    <td data-label="Priority"><span className={`pill ${prClass[r.priority]}`}>{label(PRIORITIES, r.priority)}</span></td>
                    <td data-label="Waiting">{r.daysWaiting} days<div className="muted small">since {formatDate(r.addedOn)}</div></td>
                    <td data-label="Target">{formatDate(r.targetDate)}<div className={`small ${r.overdue ? 'warn-text' : 'muted'}`}>{isOpen ? (r.overdue ? `${-r.daysToTarget} days over` : `${r.daysToTarget} days left`) : ''}</div></td>
                    <td data-label="Status">
                      {r.status === 'Booked' ? <><span className="pill green">Booked</span><div className="small"><Link to={`/theatre?date=${r.bookedFor}`}>{r.bookedFor && formatDate(r.bookedFor)}</Link></div></>
                        : r.status === 'OnHold' ? <><span className="pill amber">On hold</span><div className="small">{r.holdReason}{r.holdUntil ? ` (until ${formatDate(r.holdUntil)})` : ''}</div></>
                        : r.status === 'Removed' ? <><span className="pill">Removed</span><div className="small">{r.removedReason}</div></>
                        : <span className={`pill ${r.status === 'Completed' ? 'green' : ''}`}>{r.status === 'Completed' ? 'Done' : 'Waiting'}</span>}
                    </td>
                    <td data-label="Actions" className="no-print">
                      {can(Perm.ReferralsManage) && canWrite && isOpen && (
                        <div className="status-actions">
                          {r.status === 'Waiting' && mod('theatre') && can(Perm.TheatreBook) && <button className="btn btn-primary btn-small" onClick={() => setOpen({ id: r.id, action: 'book' })}>Book</button>}
                          <button className="btn btn-light btn-small" onClick={() => setOpen({ id: r.id, action: 'hold' })}>{r.status === 'OnHold' ? 'Resume' : 'Hold'}</button>
                          <button className="btn btn-link" onClick={() => setOpen({ id: r.id, action: 'edit' })}>Edit</button>
                          <button className="btn btn-link danger" onClick={() => setOpen({ id: r.id, action: 'remove' })}>Remove</button>
                        </div>
                      )}
                    </td>
                  </tr>
                  {open?.id === r.id && (
                    <tr className="detail-row"><td colSpan={7}>
                      {open.action === 'book' && <BookFromWaitlist r={r} onDone={done} onCancel={() => setOpen(null)} />}
                      {open.action === 'hold' && <HoldEntry r={r} onDone={done} onCancel={() => setOpen(null)} />}
                      {open.action === 'edit' && <EditEntry r={r} onDone={done} onCancel={() => setOpen(null)} />}
                      {open.action === 'remove' && <RemoveEntry r={r} onDone={done} onCancel={() => setOpen(null)} />}
                    </td></tr>
                  )}
                </Fragment>
              );
            })}</tbody>
          </table>
        )}
      </section>
    </div>
  );
}

function Errors({ list }: { list: string[] }) { return list.length > 0 ? <div className="error" role="alert">{list.map((x) => <div key={x}>{x}</div>)}</div> : null; }

function BookFromWaitlist({ r, onDone, onCancel }: { r: WaitRow; onDone: (m: string) => void; onCancel: () => void }) {
  const [from, setFrom] = useState(todayIso());
  const [sessions, setSessions] = useState<Session[] | null>(null);
  const [choice, setChoice] = useState<string | null>(null);
  const [warnings, setWarnings] = useState<string[]>([]);
  const [errors, setErrors] = useState<string[]>([]);
  useEffect(() => {
    setSessions(null); setChoice(null); setWarnings([]);
    api<Session[]>(`/api/theatre/sessions?from=${from}&to=${addDays(from, 41)}`).then(setSessions).catch((e) => setErrors([errText(e)]));
  }, [from]);
  const book = async (accept: boolean) => {
    setErrors([]);
    try {
      await api(`/api/waitlist/${r.id}/book`, { body: { sessionId: choice, acceptWarnings: accept } });
      const s = sessions!.find((x) => x.id === choice)!;
      onDone(`${r.patientName} booked on ${formatDate(s.date)} (${s.locationName}, ${s.surgeon}). A confirmation SMS goes out automatically if reminders are on.`);
    } catch (x: any) { setWarnings(x?.body?.warnings ?? []); setErrors(x?.body?.errors ?? (x?.body?.warnings?.length ? [] : [errText(x)])); }
  };
  const mine = (s: Session) => s.surgeon.toLowerCase().includes(r.specialist.toLowerCase()) || r.specialist.toLowerCase().includes(s.surgeon.toLowerCase());
  const sorted = sessions ? [...sessions].sort((a, b) => Number(mine(b)) - Number(mine(a)) || a.date.localeCompare(b.date)) : null;
  return (
    <div className="card subtle stack">
      <div className="row-between"><h3>Book {r.procedure} – {r.patientName}</h3><label className="inline">Lists from<input type="date" value={from} onChange={(e) => e.target.value && setFrom(e.target.value)} /></label></div>
      <p className="hint">Next 6 weeks. {r.specialist}'s lists are shown first. Needs {r.durationMinutes} minutes; target date {formatDate(r.targetDate)}.</p>
      {sorted?.length === 0 && <p className="muted">No theatre lists in these 6 weeks. Create lists in <Link to="/theatre">Theatre lists</Link>.</p>}
      {sorted && sorted.length > 0 && (
        <ul className="choose-list">{sorted.map((s) => {
          const free = s.availableMinutes - s.bookedMinutes;
          return (
            <li key={s.id}><label className={`choice ${choice === s.id ? 'on' : ''}`}>
              <input type="radio" name={`s-${r.id}`} checked={choice === s.id} onChange={() => { setChoice(s.id); setWarnings([]); }} />
              <span><strong>{formatDate(s.date)}</strong> {hm(s.startTime)}–{hm(s.endTime)} · {s.locationName} · {s.surgeon}{mine(s) && <span className="badge ok">{r.specialist}</span>}
                <span className={`small ${free < r.durationMinutes ? 'warn-text' : 'muted'}`}> · {free > 0 ? `${free} min free` : 'full'}</span>{s.date > r.targetDate && <span className="small warn-text"> · after target</span>}</span>
            </label></li>
          );
        })}</ul>
      )}
      <Errors list={errors} />
      {warnings.length > 0 && <div className="warn-box"><strong>Please check:</strong><ul>{warnings.map((w) => <li key={w}>{w}</li>)}</ul></div>}
      <div className="row">
        {warnings.length === 0 ? <button className="btn btn-primary" disabled={!choice} onClick={() => book(false)}>Book</button>
          : <button className="btn btn-warn" onClick={() => book(true)}>Book anyway</button>}
        <button className="btn btn-light" onClick={onCancel}>Cancel</button>
      </div>
    </div>
  );
}

function HoldEntry({ r, onDone, onCancel }: { r: WaitRow; onDone: (m: string) => void; onCancel: () => void }) {
  const resume = r.status === 'OnHold';
  const [reason, setReason] = useState('');
  const [until, setUntil] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const save = async () => {
    setErrors([]);
    try { await api(`/api/waitlist/${r.id}/hold`, { body: { hold: !resume, reason, until: until || null } }); onDone(resume ? `${r.patientName} is waiting again.` : `${r.patientName} put on hold.`); }
    catch (x: any) { setErrors(x?.body?.errors ?? [errText(x)]); }
  };
  return (
    <div className="card subtle stack">
      <h3>{resume ? 'Resume' : 'Put on hold'}: {r.patientName}</h3>
      {resume ? <p>On hold because: {r.holdReason}. Resume waiting now?</p> : (
        <div className="form-grid">
          <label>Reason<input value={reason} onChange={(e) => setReason(e.target.value)} placeholder="e.g. not fit for surgery yet, overseas" /></label>
          <label>Until (optional – resumes automatically)<input type="date" min={addDays(todayIso(), 1)} value={until} onChange={(e) => setUntil(e.target.value)} /></label>
        </div>
      )}
      <Errors list={errors} />
      <div className="row"><button className="btn btn-primary" onClick={save}>{resume ? 'Resume' : 'Put on hold'}</button><button className="btn btn-light" onClick={onCancel}>Cancel</button></div>
    </div>
  );
}

function EditEntry({ r, onDone, onCancel }: { r: WaitRow; onDone: (m: string) => void; onCancel: () => void }) {
  const [f, setF] = useState({ procedure: r.procedure, laterality: r.laterality, anaesthetic: r.anaesthetic, durationMinutes: r.durationMinutes, specialist: r.specialist, priority: r.priority, notes: r.notes ?? '' });
  const [errors, setErrors] = useState<string[]>([]);
  const set = (k: keyof typeof f) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => setF({ ...f, [k]: k === 'durationMinutes' ? Number(e.target.value) : e.target.value });
  const save = async () => {
    setErrors([]);
    try { await api(`/api/waitlist/${r.id}`, { method: 'PUT', body: f }); onDone(`Waitlist entry for ${r.patientName} updated${f.priority !== r.priority ? ' – target date recalculated' : ''}.`); }
    catch (x: any) { setErrors(x?.body?.errors ?? [errText(x)]); }
  };
  return (
    <div className="card subtle stack">
      <h3>Edit: {r.patientName}</h3>
      <div className="form-grid">
        <label>Procedure<input value={f.procedure} onChange={set('procedure')} /></label>
        <label>Side<select value={f.laterality} onChange={set('laterality')}>{SIDES.map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
        <label>Anaesthetic<select value={f.anaesthetic} onChange={set('anaesthetic')}>{ANAES.map((a) => <option key={a}>{a}</option>)}</select></label>
        <label>Expected minutes<input type="number" min={5} max={720} value={f.durationMinutes} onChange={set('durationMinutes')} /></label>
        <label>Specialist<input value={f.specialist} onChange={set('specialist')} /></label>
        <label>Priority<select value={f.priority} onChange={set('priority')}>{PRIORITIES.map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
      </div>
      <label>Notes<input value={f.notes} onChange={set('notes')} /></label>
      <Errors list={errors} />
      <div className="row"><button className="btn btn-primary" onClick={save}>Save</button><button className="btn btn-light" onClick={onCancel}>Cancel</button></div>
    </div>
  );
}

function RemoveEntry({ r, onDone, onCancel }: { r: WaitRow; onDone: (m: string) => void; onCancel: () => void }) {
  const [reason, setReason] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const save = async () => {
    setErrors([]);
    try { await api(`/api/waitlist/${r.id}/remove`, { body: { reason } }); onDone(`${r.patientName} removed from the waitlist.`); }
    catch (x: any) { setErrors(x?.body?.errors ?? [errText(x)]); }
  };
  return (
    <div className="card subtle stack">
      <h3>Remove from the waitlist: {r.patientName}</h3>
      <label>Reason<input value={reason} onChange={(e) => setReason(e.target.value)} placeholder="e.g. treated elsewhere, no longer wants surgery, deceased" /></label>
      <Errors list={errors} />
      <div className="row"><button className="btn btn-warn" onClick={save}>Remove</button><button className="btn btn-light" onClick={onCancel}>Cancel</button></div>
    </div>
  );
}

function AddEntry({ onDone, onCancel }: { onDone: (m: string) => void; onCancel: () => void }) {
  const [patient, setPatient] = useState<PatientSummary | null>(null);
  const [f, setF] = useState({ procedure: '', laterality: 'NotApplicable', anaesthetic: 'General', durationMinutes: 60, service: 'Surgical', specialist: '', funder: 'SelfPay', priority: 'Routine', addedOn: todayIso(), notes: '' });
  const [errors, setErrors] = useState<string[]>([]);
  const set = (k: keyof typeof f) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => setF({ ...f, [k]: k === 'durationMinutes' ? Number(e.target.value) : e.target.value });
  const save = async (e: React.FormEvent) => {
    e.preventDefault(); setErrors([]);
    if (!patient) { setErrors(['Choose the patient.']); return; }
    try { const r = await api<{ targetDate: string }>('/api/waitlist', { body: { ...f, patientId: patient.id } }); onDone(`${patient.familyName.toUpperCase()}, ${patient.givenNames} added – target date ${formatDate(r.targetDate)}.`); }
    catch (x: any) { setErrors(x?.body?.errors ?? [errText(x)]); }
  };
  return (
    <form className="card stack" onSubmit={save}>
      <h2>Add to the waitlist</h2>
      <p className="hint">For patients already seen by the specialist. New referral letters go in <Link to="/referrals">Referrals</Link> instead.</p>
      <PatientPicker value={patient} onChange={setPatient} />
      <div className="form-grid">
        <label>Procedure<input value={f.procedure} onChange={set('procedure')} required /></label>
        <label>Side<select value={f.laterality} onChange={set('laterality')}>{SIDES.map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
        <label>Service<select value={f.service} onChange={set('service')}><option>Surgical</option><option>Endoscopy</option></select></label>
        <label>Anaesthetic<select value={f.anaesthetic} onChange={set('anaesthetic')}>{ANAES.map((a) => <option key={a}>{a}</option>)}</select></label>
        <label>Expected minutes<input type="number" min={5} max={720} value={f.durationMinutes} onChange={set('durationMinutes')} /></label>
        <label>Specialist<input value={f.specialist} onChange={set('specialist')} required /></label>
        <label>Funder<select value={f.funder} onChange={set('funder')}>{FUNDERS.map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
        <label>Priority<select value={f.priority} onChange={set('priority')}>{PRIORITIES.map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
        <label>Decision to treat (date)<input type="date" max={todayIso()} value={f.addedOn} onChange={set('addedOn')} /></label>
      </div>
      <label>Notes (optional)<input value={f.notes} onChange={set('notes')} /></label>
      <Errors list={errors} />
      <div className="row"><button className="btn btn-primary">Add to waitlist</button><button type="button" className="btn btn-light" onClick={onCancel}>Cancel</button></div>
    </form>
  );
}
