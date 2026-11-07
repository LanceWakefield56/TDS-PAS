import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { api, errText, formatDate, PatientSummary, Perm, todayIso } from '../api';
import { useSession } from '../App';
import CalendarGrid, { AllDayItem, CalendarNav, CalItem, daysFor } from '../components/CalendarGrid';
import PatientPicker from '../components/PatientPicker';
import { RefLists, useRefData } from '../components/RefData';

interface Booking { id: string; patientId: string; patientName: string; nhi: string | null; procedure: string; laterality: string; status: string; plannedStart: string; plannedEnd: string; isTentative: boolean; payment: string; checklist: string[]; phone: string | null; notes: string | null }
interface Session { id: string; locationName: string; date: string; startTime: string; endTime: string; type: string; surgeon: string; anaesthetist: string | null; bookings: Booking[] }
interface Appt { id: string; patientId: string; patientName: string; nhi: string | null; date: string; startTime: string; endTime: string; durationMinutes: number; kind: string; clinician: string; noCharge: boolean; isTentative: boolean; status: string; notes: string | null }
const KINDS: [string, string][] = [['Consultation', 'Consultation'], ['FollowUp', 'Follow-up'], ['PhoneCall', 'Phone call'], ['PreAdmission', 'Pre-admission'], ['Other', 'Other']];
const kindLabel = (k: string) => KINDS.find(([v]) => v === k)?.[1] ?? k;
const inactive = (s: string) => s === 'Cancelled' || s === 'DidNotAttend';

/** Booking calendar: theatre cases and clinic appointments by day or week, coloured by clinician; tentative bookings striped. */
export default function Calendar() {
  const { can, writable } = useSession();
  const [params, setParams] = useSearchParams();
  const date = params.get('date') ?? todayIso();
  const view = (params.get('view') as 'day' | 'week') ?? 'week';
  const [data, setData] = useState<{ sessions: Session[]; appointments: Appt[]; colours: Record<string, number> } | null>(null);
  const [who, setWho] = useState('');
  const [selected, setSelected] = useState<{ kind: 'booking'; s: Session; b: Booking } | { kind: 'appt'; a: Appt } | { kind: 'list'; s: Session } | null>(null);
  const [newAppt, setNewAppt] = useState<{ date: string; time: string } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const ref = useRefData();
  const days = daysFor(date, view);
  const canBook = can(Perm.TheatreBook) && writable('theatre');
  const load = () => api<any>(`/api/theatre/calendar?from=${days[0]}&to=${days[days.length - 1]}`).then(setData).catch((e) => setError(errText(e)));
  useEffect(() => { setData(null); load(); }, [date, view]);
  const go = (d: string, v = view) => setParams({ date: d, view: v });
  const colour = (name: string) => data?.colours[name] ?? Math.abs([...name].reduce((h, c) => h * 31 + c.charCodeAt(0), 7)) % 12;

  const items: CalItem[] = []; const allDay: AllDayItem[] = [];
  for (const s of data?.sessions ?? []) {
    if (who && s.surgeon !== who) continue;
    allDay.push({ id: 's' + s.id, date: s.date, colour: colour(s.surgeon), title: `${s.locationName} ${s.startTime.slice(0, 5)} ${s.surgeon}${s.anaesthetist ? ' · ' + s.anaesthetist : ''}`, onClick: () => setSelected({ kind: 'list', s }) });
    for (const b of s.bookings) items.push({ id: b.id, date: s.date, start: b.plannedStart, end: b.plannedEnd, colour: colour(s.surgeon), tentative: b.isTentative, muted: inactive(b.status),
      title: `${b.patientName}${b.nhi ? ` (${b.nhi})` : ''}`, sub: b.procedure + (b.laterality !== 'NotApplicable' ? ` (${b.laterality})` : ''), badge: b.checklist.length ? `✓${b.checklist.length}` : undefined,
      onClick: () => setSelected({ kind: 'booking', s, b }) });
  }
  for (const a of data?.appointments ?? []) {
    if (who && a.clinician !== who) continue;
    items.push({ id: a.id, date: a.date, start: a.startTime, end: a.endTime, colour: colour(a.clinician), tentative: a.isTentative, muted: inactive(a.status),
      title: `${a.noCharge ? '*NO CHARGE* ' : ''}${a.patientName}`, sub: `${kindLabel(a.kind)} · ${a.clinician}`, badge: a.kind === 'PhoneCall' ? '☎' : undefined, onClick: () => setSelected({ kind: 'appt', a }) });
  }
  const clinicians = [...new Set([...(data?.sessions ?? []).map((s) => s.surgeon), ...(data?.appointments ?? []).map((a) => a.clinician)])].sort();

  return (
    <div className="stack-lg">
      <RefLists {...ref} />
      <div className="row-between">
        <div className="page-head"><h1>Calendar</h1><p className="muted">{view === 'week' ? `Week of ${formatDate(days[0])}` : new Date(date + 'T00:00').toLocaleDateString('en-NZ', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })}</p></div>
        <div className="row">
          <CalendarNav date={date} view={view} onDate={(d) => go(d)} onView={(v) => go(date, v)} />
          {canBook && <button className="btn btn-primary btn-small" onClick={() => setNewAppt({ date, time: '09:00' })}>New appointment</button>}
        </div>
      </div>
      <div className="row-between no-print">
        <div className="cal-legend">{clinicians.map((c) => <span key={c} className={`cal-c${colour(c) % 12}`}>{c}</span>)}<span className="cal-c0 tentative">Striped = tentative</span></div>
        <label className="inline">Show<select value={who} onChange={(e) => setWho(e.target.value)}><option value="">Everyone</option>{clinicians.map((c) => <option key={c}>{c}</option>)}</select></label>
      </div>
      {error && <p className="error">{error}</p>}
      {newAppt && <NewAppointment start={newAppt} onDone={() => { setNewAppt(null); load(); }} onCancel={() => setNewAppt(null)} />}
      {selected && <Details sel={selected} canBook={canBook} onClose={() => setSelected(null)} onChange={() => { setSelected(null); load(); }} />}
      {!data ? <p>Loading…</p> : <CalendarGrid days={days} items={items} allDay={allDay} today={todayIso()} onSlot={canBook ? (d, t) => setNewAppt({ date: d, time: t }) : undefined} />}
      <p className="hint no-print">Theatre lists appear in the all-day row; click one to open the list and add cases. Click an empty time to book a consultation, follow-up or phone call.</p>
    </div>
  );
}

function Details({ sel, canBook, onClose, onChange }: { sel: any; canBook: boolean; onClose: () => void; onChange: () => void }) {
  const [reason, setReason] = useState('');
  const [error, setError] = useState<string | null>(null);
  const setStatus = async (a: Appt, status: string, tentative = a.isTentative) => {
    setError(null);
    try { await api(`/api/theatre/appointments/${a.id}/status`, { body: { status, reason: status === 'Cancelled' ? reason : null, isTentative: tentative, notes: a.notes } }); onChange(); }
    catch (x) { setError(errText(x)); }
  };
  return (
    <section className="card side-panel stack no-print">
      <div className="row-between"><h2>{sel.kind === 'list' ? `${sel.s.locationName} list` : sel.kind === 'booking' ? sel.b.patientName : sel.a.patientName}</h2><button className="btn btn-link" onClick={onClose}>Close</button></div>
      {sel.kind === 'list' && <>
        <p>{formatDate(sel.s.date)} {sel.s.startTime.slice(0, 5)}–{sel.s.endTime.slice(0, 5)} · {sel.s.type} · {sel.s.surgeon}{sel.s.anaesthetist ? ` · Anaesthetist ${sel.s.anaesthetist}` : ''} · {sel.s.bookings.filter((b: Booking) => !inactive(b.status)).length} case(s)</p>
        <div><Link className="btn btn-primary btn-small" to={`/theatre?date=${sel.s.date}`}>Open list (add cases)</Link></div>
      </>}
      {sel.kind === 'booking' && <>
        <p>{formatDate(sel.s.date)} {sel.b.plannedStart.slice(0, 5)} · {sel.b.procedure} · {sel.s.locationName}, {sel.s.surgeon} <span className="pill">{sel.b.status}</span>{sel.b.isTentative && <span className="pill amber">Tentative</span>}</p>
        <p className="small muted">{sel.b.nhi ?? 'No NHI'} · {sel.b.phone ?? 'no phone'} · payment {sel.b.payment === 'NotSet' ? 'not recorded' : sel.b.payment} · checklist {sel.b.checklist.length} done{sel.b.notes ? ` · ${sel.b.notes}` : ''}</p>
        <div className="row"><Link className="btn btn-light btn-small" to={`/patients/${sel.b.patientId}`}>Patient record</Link><Link className="btn btn-primary btn-small" to={`/theatre?date=${sel.s.date}`}>Open list (details, status)</Link></div>
      </>}
      {sel.kind === 'appt' && <>
        <p>{formatDate(sel.a.date)} {sel.a.startTime.slice(0, 5)}–{sel.a.endTime.slice(0, 5)} · {kindLabel(sel.a.kind)} with {sel.a.clinician} <span className="pill">{sel.a.status}</span>{sel.a.noCharge && <span className="pill amber">No charge</span>}{sel.a.isTentative && <span className="pill amber">Tentative</span>}</p>
        {sel.a.notes && <p className="small">{sel.a.notes}</p>}
        <div className="row"><Link className="btn btn-light btn-small" to={`/patients/${sel.a.patientId}`}>Patient record</Link>
          {canBook && !inactive(sel.a.status) && <>
            {sel.a.isTentative && <button className="btn btn-light btn-small" onClick={() => setStatus(sel.a, sel.a.status, false)}>Confirm (not tentative)</button>}
            {['Booked', 'Confirmed'].includes(sel.a.status) && <button className="btn btn-light btn-small" onClick={() => setStatus(sel.a, 'Arrived')}>Arrived</button>}
            {sel.a.status === 'Arrived' && <button className="btn btn-light btn-small" onClick={() => setStatus(sel.a, 'Completed')}>Completed</button>}
            <button className="btn btn-link" onClick={() => setStatus(sel.a, 'DidNotAttend')}>DNA</button>
          </>}
        </div>
        {canBook && !inactive(sel.a.status) && <div className="row"><label className="grow">Cancel – reason<input value={reason} onChange={(e) => setReason(e.target.value)} /></label><button className="btn btn-warn btn-small" onClick={() => setStatus(sel.a, 'Cancelled')}>Cancel appointment</button></div>}
      </>}
      {error && <p className="error">{error}</p>}
    </section>
  );
}

function NewAppointment({ start, onDone, onCancel }: { start: { date: string; time: string }; onDone: () => void; onCancel: () => void }) {
  const [patient, setPatient] = useState<PatientSummary | null>(null);
  const [f, setF] = useState({ date: start.date, startTime: start.time, durationMinutes: 15, kind: 'Consultation', clinician: '', noCharge: false, isTentative: false, notes: '' });
  const [warnings, setWarnings] = useState<string[]>([]);
  const [errors, setErrors] = useState<string[]>([]);
  useEffect(() => setF((x) => ({ ...x, date: start.date, startTime: start.time })), [start.date, start.time]);
  const save = async (accept: boolean) => {
    setErrors([]);
    if (!patient) { setErrors(['Choose the patient.']); return; }
    try { await api('/api/theatre/appointments', { body: { ...f, startTime: f.startTime + ':00', patientId: patient.id, acceptWarnings: accept } }); onDone(); }
    catch (x: any) { setWarnings(x?.body?.warnings ?? []); setErrors(x?.body?.errors ?? (x?.body?.warnings?.length ? [] : [errText(x)])); }
  };
  return (
    <section className="card stack no-print">
      <h2>New appointment</h2>
      <PatientPicker value={patient} onChange={setPatient} />
      <div className="form-grid">
        <label>Type<select value={f.kind} onChange={(e) => setF({ ...f, kind: e.target.value, durationMinutes: e.target.value === 'PhoneCall' ? 10 : f.durationMinutes })}>{KINDS.map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
        <label>Clinician<input list="dl-clinicians" value={f.clinician} onChange={(e) => setF({ ...f, clinician: e.target.value })} /></label>
        <label>Date<input type="date" value={f.date} onChange={(e) => setF({ ...f, date: e.target.value })} /></label>
        <label>Time<input type="time" step={300} value={f.startTime} onChange={(e) => setF({ ...f, startTime: e.target.value })} /></label>
        <label>Minutes<input type="number" min={5} max={240} step={5} value={f.durationMinutes} onChange={(e) => setF({ ...f, durationMinutes: Number(e.target.value) })} /></label>
        <label className="check"><input type="checkbox" checked={f.noCharge} onChange={(e) => setF({ ...f, noCharge: e.target.checked })} /> No charge</label>
        <label className="check"><input type="checkbox" checked={f.isTentative} onChange={(e) => setF({ ...f, isTentative: e.target.checked })} /> Tentative (not confirmed)</label>
      </div>
      <label>Notes<input value={f.notes} onChange={(e) => setF({ ...f, notes: e.target.value })} placeholder="e.g. Phone call please – see note re daughter's phone" /></label>
      {errors.length > 0 && <div className="error" role="alert">{errors.map((e) => <div key={e}>{e}</div>)}</div>}
      {warnings.length > 0 && <div className="warn-box"><strong>Please check:</strong><ul>{warnings.map((w) => <li key={w}>{w}</li>)}</ul></div>}
      <div className="row">
        {warnings.length ? <button className="btn btn-warn" onClick={() => save(true)}>Book anyway</button> : <button className="btn btn-primary" onClick={() => save(false)}>Book</button>}
        <button className="btn btn-light" onClick={onCancel}>Cancel</button>
      </div>
    </section>
  );
}
