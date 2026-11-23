import { Fragment, useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { addDays, api, errText, formatDate, PatientSummary, Perm, todayIso } from '../api';
import { useSession } from '../App';
import PatientPicker from '../components/PatientPicker';
import ExportButton from '../components/ExportButton';
import { TriageBadge } from './Triage';
import { findProcedure, RefLists, useRefData, ClinicianSelect } from '../components/RefData';

interface Booking {
  id: string; patientId: string; patientName: string; nhi: string | null; mrn: string; dateOfBirth: string; order: number; procedure: string; laterality: string;
  durationMinutes: number; anaesthetic: string; status: string; cancelReason: string | null; notes: string | null; plannedStart: string; plannedEnd: string;
  isTentative: boolean; payment: string; depositNote: string | null; checklist: string[]; billedExternally: boolean; theatreIn: string | null; theatreOut: string | null;
  traysNeeded: string | null; traysUsed: string | null; loanItems: string | null; scopeId: string | null; phone: string | null;
}
const PAYMENTS: [string, string][] = [['NotSet', 'Not recorded'], ['SelfFunded', 'Self-funded'], ['Insurer', 'Health insurer'], ['Acc', 'ACC'], ['HealthNz', 'Health NZ'], ['NoCharge', 'No charge']];
interface Session {
  id: string; locationId: string; locationName: string; date: string; startTime: string; endTime: string; type: string; surgeon: string; anaesthetist: string | null;
  notes: string | null; bookedMinutes: number; availableMinutes: number; overrun: boolean; bookings: Booking[];
}
interface Loc { id: string; code: string; name: string; type: string }

const next: Record<string, [string, string][]> = {
  Booked: [['Confirmed', 'Confirm'], ['Arrived', 'Arrived']], Confirmed: [['Arrived', 'Arrived']], Arrived: [['InTheatre', 'Into theatre']],
  InTheatre: [['Recovery', 'To recovery']], Recovery: [['Discharged', 'Discharged']], Cancelled: [['Booked', 'Re-book']], DidNotAttend: [['Booked', 'Re-book']],
};
const statusClass: Record<string, string> = { Booked: '', Confirmed: 'blue', Arrived: 'blue', InTheatre: 'amber', Recovery: 'amber', Discharged: 'green', Cancelled: 'red', DidNotAttend: 'red' };
const label = (s: string) => ({ InTheatre: 'In theatre', DidNotAttend: 'Did not attend', NotApplicable: '—' } as Record<string, string>)[s] ?? s;
const hm = (t: string) => t.slice(0, 5);

export default function TheatreLists() {
  const { can, writable, mod } = useSession();
  const [params, setParams] = useSearchParams();
  const date = params.get('date') ?? todayIso();
  const patientId = params.get('patient');
  const ref = useRefData();
  const [sessions, setSessions] = useState<Session[] | null>(null);
  const [locations, setLocations] = useState<Loc[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [newList, setNewList] = useState(false);
  const [bookOn, setBookOn] = useState<string | null>(null);
  const [detailsOf, setDetailsOf] = useState<string | null>(null);
  const [checkItems, setCheckItems] = useState<{ key: string; label: string }[]>([]);
  useEffect(() => { api<{ key: string; label: string }[]>('/api/theatre/checklist-items').then(setCheckItems).catch(() => undefined); }, []);
  const canWrite = writable('theatre');
  const [triage, setTriage] = useState<Record<string, any>>({});
  const showTriage = mod('triage') && (can(Perm.TriageView) || can(Perm.TriageAssess) || can(Perm.TriageReview));

  const load = async () => {
    setError(null);
    try {
      const ss = await api<Session[]>(`/api/theatre/sessions?from=${date}`); setSessions(ss);
      const ids = ss.flatMap((x) => x.bookings.map((b) => b.id));
      if (showTriage && ids.length) api<Record<string, any>>(`/api/triage/bookings?ids=${ids.join(',')}`).then(setTriage).catch(() => setTriage({}));
    } catch (e) { setError(errText(e)); }
  };
  useEffect(() => { load(); }, [date]); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => { api<Loc[]>('/api/theatre/locations').then(setLocations).catch(() => setLocations([])); }, []);
  const go = (d: string) => setParams((p) => { p.set('date', d); return p; });

  const setStatus = async (b: Booking, status: string) => {
    let reason: string | null = null;
    if (status === 'Cancelled') { reason = window.prompt(`Reason for cancelling ${b.patientName}?`); if (!reason) return; }
    setError(null);
    try { await api(`/api/theatre/bookings/${b.id}/status`, { body: { status, reason } }); await load(); } catch (e) { setError(errText(e)); }
  };
  const move = async (s: Session, b: Booking, dir: -1 | 1) => {
    const ids = s.bookings.map((x) => x.id);
    const i = ids.indexOf(b.id); const j = i + dir;
    if (j < 0 || j >= ids.length) return;
    [ids[i], ids[j]] = [ids[j], ids[i]];
    try { await api(`/api/theatre/sessions/${s.id}/order`, { body: { bookingIds: ids } }); await load(); } catch (e) { setError(errText(e)); }
  };

  return (
    <div className="stack-lg">
      <RefLists {...ref} />
      <div className="row-between">
        <div className="page-head"><h1>Theatre lists</h1><p className="muted">{new Date(date + 'T00:00').toLocaleDateString('en-NZ', { weekday: 'long', day: 'numeric', month: 'long', year: 'numeric' })}</p></div>
        <div className="row no-print">
          <button className="btn btn-light btn-small" onClick={() => go(addDays(date, -1))}>← Previous day</button>
          <button className="btn btn-light btn-small" onClick={() => go(todayIso())}>Today</button>
          <button className="btn btn-light btn-small" onClick={() => go(addDays(date, 1))}>Next day →</button>
          <input type="date" value={date} onChange={(e) => e.target.value && go(e.target.value)} aria-label="Choose date" />
          <Link className="btn btn-light btn-small" to={`/calendar?date=${date}`}>Calendar</Link>
          <ExportButton label="Day list PDF" className="btn btn-light btn-small" formats={['pdf']} path="/api/theatre/lists/pdf" body={{ date }} />
          <Link className="btn btn-light btn-small" to="/theatre/templates">Regular lists</Link>
        </div>
      </div>
      {!canWrite && <p className="warn-box">Theatre bookings are read-only because of a licence problem.</p>}
      {patientId && <p className="notice">Choose a list below and use “Add booking” – the patient you came from is selected automatically.</p>}
      {error && <p className="error" role="alert">{error}</p>}
      {can(Perm.TheatreManage) && canWrite && (newList ? <NewList date={date} locations={locations} onDone={() => { setNewList(false); load(); }} onCancel={() => setNewList(false)} />
        : <div className="no-print"><button className="btn btn-light" onClick={() => setNewList(true)}>New theatre list</button></div>)}
      {sessions && sessions.length === 0 && <section className="card"><p className="muted">No theatre lists on this day.</p></section>}
      {sessions?.map((s) => (
        <section key={s.id} className={`card session ${s.overrun ? 'over' : ''}`}>
          <div className="session-head">
            <h2>{s.locationName} · <span className="time">{hm(s.startTime)}–{hm(s.endTime)}</span></h2>
            <span className="muted">{s.type} · {s.surgeon}{s.anaesthetist ? ` · Anaesthetist ${s.anaesthetist}` : ''}</span>
            <span className={`pill ${s.overrun ? 'red' : 'green'}`}>{s.bookedMinutes} of {s.availableMinutes} min booked</span>
          </div>
          {s.notes && <p className="muted">{s.notes}</p>}
          {s.bookings.length === 0 ? <p className="muted">No bookings yet.</p> : (
            <table className="results compact">
              <thead><tr><th>Time</th><th>Patient</th><th>Procedure</th><th>Anaesthetic</th><th>Status</th><th className="no-print">Actions</th></tr></thead>
              <tbody>{s.bookings.map((b) => {
                const inactive = b.status === 'Cancelled' || b.status === 'DidNotAttend';
                return (
                  <Fragment key={b.id}>
                  <tr className={inactive ? 'muted' : ''}>
                    <td data-label="Time" className="time">{inactive ? '—' : `${hm(b.plannedStart)}–${hm(b.plannedEnd)}`}<div className="muted small">{b.durationMinutes} min</div></td>
                    <td data-label="Patient"><Link to={`/patients/${b.patientId}`}><strong>{b.patientName}</strong></Link><div className="muted small">{b.nhi ?? '—'} · {b.mrn} · {formatDate(b.dateOfBirth)}</div></td>
                    <td data-label="Procedure">{b.procedure}{b.laterality !== 'NotApplicable' && <strong> ({b.laterality.toUpperCase()})</strong>}{b.notes && <div className="muted small">{b.notes}</div>}{b.cancelReason && <div className="small">Cancelled: {b.cancelReason}</div>}</td>
                    <td data-label="Anaesthetic">{b.anaesthetic}</td>
                    <td data-label="Status"><span className={`pill ${statusClass[b.status]}`}>{label(b.status)}</span>{b.isTentative && <span className="pill amber">Tentative</span>}
                      <div className="small muted">{b.payment !== 'NotSet' ? PAYMENTS.find(([k]) => k === b.payment)?.[1] : ''}{checkItems.length > 0 ? ` · ✓ ${b.checklist.length}/${checkItems.length}` : ''}{b.theatreIn ? ` · in ${hm(b.theatreIn)}${b.theatreOut ? `–${hm(b.theatreOut)}` : ''}` : ''}{b.scopeId ? ` · scope ${b.scopeId}` : ''}</div>{showTriage && !inactive && <TriageBadge t={triage[b.id]} />}</td>
                    <td data-label="Actions" className="no-print">
                      {mod('emr') && can(Perm.EmrView) && !inactive && <Link className="btn btn-link" to={`/emr/${b.id}`}>Clinical record</Link>}
                      {mod('endoscopy') && can(Perm.EndoView) && !inactive && (s.type !== 'Surgical' || /colonoscop|gastroscop|sigmoidoscop|ogd/i.test(b.procedure)) && <Link className="btn btn-link" to={`/endoscopy/${b.id}`}>Endoscopy report</Link>}
                      {can(Perm.TheatreBook) && canWrite && (
                        <div className="status-actions">
                          {(next[b.status] ?? []).map(([st, t]) => <button key={st} className="btn btn-light btn-small" onClick={() => setStatus(b, st)}>{t}</button>)}
                          {['Booked', 'Confirmed', 'Arrived'].includes(b.status) && <button className="btn btn-link danger" onClick={() => setStatus(b, 'Cancelled')}>Cancel</button>}
                          {['Booked', 'Confirmed'].includes(b.status) && <button className="btn btn-link" onClick={() => setStatus(b, 'DidNotAttend')}>DNA</button>}
                          <button className="btn btn-link" aria-label="Move up" onClick={() => move(s, b, -1)}>↑</button>
                          <button className="btn btn-link" aria-label="Move down" onClick={() => move(s, b, 1)}>↓</button>
                          <button className="btn btn-link" onClick={() => setDetailsOf(detailsOf === b.id ? null : b.id)}>Details</button>
                        </div>
                      )}
                    </td>
                  </tr>
                  {detailsOf === b.id && <tr className="detail-row"><td colSpan={6}><BookingDetailsEditor b={b} items={checkItems} onDone={() => { setDetailsOf(null); load(); }} onCancel={() => setDetailsOf(null)} /></td></tr>}
                  </Fragment>
                );
              })}</tbody>
            </table>
          )}
          {can(Perm.TheatreBook) && canWrite && (bookOn === s.id
            ? <NewBooking session={s} patientId={patientId} onDone={() => { setBookOn(null); load(); }} onCancel={() => setBookOn(null)} />
            : <div className="no-print"><button className="btn btn-primary" onClick={() => setBookOn(s.id)}>Add booking</button></div>)}
        </section>
      ))}
      <div className="no-print"><button className="btn btn-light" onClick={() => window.print()}>Print the day's lists</button></div>
    </div>
  );
}

function BookingDetailsEditor({ b, items, onDone, onCancel }: { b: Booking; items: { key: string; label: string }[]; onDone: () => void; onCancel: () => void }) {
  const t = (x: string | null) => x ? x.slice(0, 5) : '';
  const [f, setF] = useState({ isTentative: b.isTentative, payment: b.payment, depositNote: b.depositNote ?? '', checklist: b.checklist, billedExternally: b.billedExternally,
    theatreIn: t(b.theatreIn), theatreOut: t(b.theatreOut), traysNeeded: b.traysNeeded ?? '', traysUsed: b.traysUsed ?? '', loanItems: b.loanItems ?? '', scopeId: b.scopeId ?? '', notes: b.notes ?? '' });
  const [error, setError] = useState<string | null>(null);
  const set = (k: string) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => setF({ ...f, [k]: e.target.value });
  const save = async () => {
    setError(null);
    try { await api(`/api/theatre/bookings/${b.id}/details`, { method: 'PUT', body: { ...f, theatreIn: f.theatreIn ? f.theatreIn + ':00' : null, theatreOut: f.theatreOut ? f.theatreOut + ':00' : null } }); onDone(); }
    catch (x) { setError(errText(x)); }
  };
  return (
    <div className="card subtle stack">
      <h3>Booking details – {b.patientName}{b.phone ? ` · ${b.phone}` : ''}</h3>
      <div className="form-grid">
        <label className="check"><input type="checkbox" checked={f.isTentative} onChange={(e) => setF({ ...f, isTentative: e.target.checked })} /> Tentative (not confirmed)</label>
        <label>Payment type<select value={f.payment} onChange={set('payment')}>{PAYMENTS.map(([k, v]) => <option key={k} value={k}>{v}</option>)}</select></label>
        <label>Deposit required (shortfall / self-funding)<input value={f.depositNote} onChange={set('depositNote')} placeholder="e.g. $350 shortfall" /></label>
        <label className="check"><input type="checkbox" checked={f.billedExternally} onChange={(e) => setF({ ...f, billedExternally: e.target.checked })} /> Office only: entered in Incisive and Xero</label>
      </div>
      <fieldset className="perm-group"><legend>Check list</legend>
        <div className="checklist">{items.map((c) => <label key={c.key} className="check"><input type="checkbox" checked={f.checklist.includes(c.key)}
          onChange={(e) => setF({ ...f, checklist: e.target.checked ? [...f.checklist, c.key] : f.checklist.filter((k) => k !== c.key) })} /> {c.label}</label>)}</div></fieldset>
      <div className="form-grid">
        <label>Theatre time in<input type="time" value={f.theatreIn} onChange={set('theatreIn')} /></label>
        <label>Theatre time out<input type="time" value={f.theatreOut} onChange={set('theatreOut')} /></label>
        <label>Instrument trays / gear needed<input value={f.traysNeeded} onChange={set('traysNeeded')} /></label>
        <label>Instrument trays / gear used<input value={f.traysUsed} onChange={set('traysUsed')} /></label>
        <label>Loan instruments &amp; consumables<input value={f.loanItems} onChange={set('loanItems')} /></label>
        <label>Scope ID<input value={f.scopeId} onChange={set('scopeId')} /></label>
      </div>
      <label>Comments<input value={f.notes} onChange={set('notes')} /></label>
      {error && <p className="error">{error}</p>}
      <div className="row"><button className="btn btn-primary" onClick={save}>Save details</button><button className="btn btn-light" onClick={onCancel}>Cancel</button></div>
    </div>
  );
}

function NewList({ date, locations, onDone, onCancel }: { date: string; locations: Loc[]; onDone: () => void; onCancel: () => void }) {
  const [f, setF] = useState({ locationId: locations[0]?.id ?? '', date, startTime: '08:00', endTime: '12:30', type: 'Surgical', surgeonId: '', anaesthetistId: '', notes: '' });
  const [error, setError] = useState<string | null>(null);
  const save = async (e: React.FormEvent) => {
    e.preventDefault(); setError(null);
    try { await api('/api/theatre/sessions', { body: { ...f, anaesthetistId: f.anaesthetistId || null, startTime: f.startTime + ':00', endTime: f.endTime + ':00' } }); onDone(); } catch (x) { setError(errText(x)); }
  };
  return (
    <form className="card stack" onSubmit={save}>
      <h2>New theatre list</h2>
      <div className="form-grid">
        <label>Theatre / room<select value={f.locationId} onChange={(e) => setF({ ...f, locationId: e.target.value })} required>{locations.map((l) => <option key={l.id} value={l.id}>{l.name}</option>)}</select></label>
        <label>Date<input type="date" value={f.date} onChange={(e) => setF({ ...f, date: e.target.value })} required /></label>
        <label>Starts<input type="time" value={f.startTime} onChange={(e) => setF({ ...f, startTime: e.target.value })} required /></label>
        <label>Finishes<input type="time" value={f.endTime} onChange={(e) => setF({ ...f, endTime: e.target.value })} required /></label>
        <label>Type<select value={f.type} onChange={(e) => setF({ ...f, type: e.target.value })}>{['Surgical', 'Endoscopy', 'Mixed'].map((t) => <option key={t}>{t}</option>)}</select></label>
        <ClinicianSelect label="Surgeon / endoscopist" role="surgeon" required value={f.surgeonId} onChange={(id) => setF({ ...f, surgeonId: id })} />
        <ClinicianSelect label="Anaesthetist" role="anaesthetist" value={f.anaesthetistId} onChange={(id) => setF({ ...f, anaesthetistId: id })} />
        <label>Notes<input value={f.notes} onChange={(e) => setF({ ...f, notes: e.target.value })} /></label>
      </div>
      {error && <p className="error">{error}</p>}
      <div className="row"><button type="button" className="btn btn-light" onClick={onCancel}>Cancel</button><button className="btn btn-primary">Create list</button></div>
    </form>
  );
}

function NewBooking({ session, patientId, onDone, onCancel }: { session: Session; patientId: string | null; onDone: () => void; onCancel: () => void }) {
  const [patient, setPatient] = useState<PatientSummary | null>(null);
  const [f, setF] = useState({ procedure: '', laterality: 'NotApplicable', durationMinutes: 30, anaesthetic: 'General', notes: '', payment: 'NotSet', isTentative: false });
  const [warnings, setWarnings] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => { if (patientId) api<PatientSummary>(`/api/patients/${patientId}`).then(setPatient).catch(() => undefined); }, [patientId]);
  const { procedures } = useRefData();
  const picked = findProcedure(procedures, f.procedure);
  // Picking a catalogue procedure fills in its usual time and anaesthetic.
  const pickProcedure = (name: string) => {
    const p = findProcedure(procedures, name);
    setF(p ? { ...f, procedure: p.name, durationMinutes: p.defaultMinutes, anaesthetic: p.defaultAnaesthetic } : { ...f, procedure: name });
  };
  const sideMissing = !!picked?.sideRequired && f.laterality === 'NotApplicable';
  const save = async (accept: boolean) => {
    if (!patient) return;
    setError(null);
    try { await api('/api/theatre/bookings', { body: { sessionId: session.id, patientId: patient.id, ...f, acceptWarnings: accept } }); onDone(); }
    catch (x: any) { if (x?.body?.warnings?.length && !x.body.errors?.length) setWarnings(x.body.warnings); else setError(errText(x)); }
  };
  return (
    <div className="card subtle stack">
      <h2>Add booking – {session.locationName}, {formatDate(session.date)}</h2>
      <PatientPicker value={patient} onChange={setPatient} />
      <div className="form-grid">
        <label className="span-all">Procedure<input list="dl-procedures" value={f.procedure} onChange={(e) => pickProcedure(e.target.value)} placeholder="Start typing – pick from the catalogue" /></label>
        <label>Side<select value={f.laterality} onChange={(e) => setF({ ...f, laterality: e.target.value })}>{[['NotApplicable', 'Not applicable'], ['Left', 'Left'], ['Right', 'Right'], ['Bilateral', 'Both sides']].map(([v, t]) => <option key={v} value={v}>{t}</option>)}</select></label>
        <label>Expected minutes<input type="number" min={5} max={720} step={5} value={f.durationMinutes} onChange={(e) => setF({ ...f, durationMinutes: Number(e.target.value) })} /></label>
        <label>Anaesthetic<select value={f.anaesthetic} onChange={(e) => setF({ ...f, anaesthetic: e.target.value })}>{['General', 'Sedation', 'Regional', 'Local', 'None'].map((t) => <option key={t}>{t}</option>)}</select></label>
        <label>Payment type<select value={f.payment} onChange={(e) => setF({ ...f, payment: e.target.value })}>{PAYMENTS.map(([k, v]) => <option key={k} value={k}>{v}</option>)}</select></label>
        <label className="check"><input type="checkbox" checked={f.isTentative} onChange={(e) => setF({ ...f, isTentative: e.target.checked })} /> Tentative (not confirmed)</label>
        <label className="span-all">Notes<input value={f.notes} onChange={(e) => setF({ ...f, notes: e.target.value })} /></label>
      </div>
      {sideMissing && <p className="warn-box">{picked!.name} needs a side: choose left, right or both.</p>}
      {warnings.length > 0 && (
        <div className="warn-box"><strong>Please check:</strong><ul>{warnings.map((w) => <li key={w}>{w}</li>)}</ul>
          <div className="row"><button className="btn btn-warn" onClick={() => save(true)}>Book anyway</button></div></div>
      )}
      {error && <p className="error">{error}</p>}
      <div className="row"><button className="btn btn-light" onClick={onCancel}>Cancel</button><button className="btn btn-primary" disabled={!patient || !f.procedure.trim() || sideMissing} onClick={() => save(false)}>Book</button></div>
    </div>
  );
}
