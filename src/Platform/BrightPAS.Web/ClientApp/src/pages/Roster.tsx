import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router';
import { addDays, api, errText, formatDate, nzDateTime, todayIso } from '../api';
import ExportButton from '../components/ExportButton';
import CalendarGrid, { AllDayItem, CalendarNav, CalItem, daysFor, weekStart } from '../components/CalendarGrid';
import Modal from '../components/Modal';

interface Staff { id: string; name: string; role: string; colour: number; isActive: boolean; userName: string | null; contractedHours: number | null; phone: string | null }
interface ShiftV { id: string; staffId: string; staffName: string; role: string; colour: number; date: string; allDay: boolean; start: string | null; end: string | null; breakMinutes: number; hours: number; area: string | null; isTentative: boolean; note: string | null; onLeave: boolean }
interface LeaveV { id: string; staffId: string; staffName: string; from: string; to: string; type: string; status: string; note: string | null; requestedAt: string; requestedBy: string; decidedBy: string | null }
const AREAS = ['Theatre', 'Anaesthetics', 'Recovery', 'Endoscopy', 'CSSD', 'Reception', 'Office', 'On call'];
const ROLES: [string, string][] = [['Nurse', 'Nurse'], ['AnaestheticTechnician', 'Anaesthetic technician'], ['HealthCareAssistant', 'Health care assistant'], ['Reception', 'Reception'], ['Cssd', 'CSSD'], ['Administration', 'Administration'], ['Manager', 'Manager'], ['Other', 'Other']];
const LEAVE: [string, string][] = [['Annual', 'Annual leave'], ['Sick', 'Sick leave'], ['Training', 'Training / study'], ['Parental', 'Parental leave'], ['Other', 'Other']];
const DAYS = ['Monday', 'Tuesday', 'Wednesday', 'Thursday', 'Friday', 'Saturday', 'Sunday'];
const errsOf = (x: any) => x?.body?.errors ?? [errText(x)];
function Errs({ list }: { list: string[] }) { return list.length ? <div className="error" role="alert">{list.map((e) => <div key={e}>{e}</div>)}</div> : null; }
const roleLabel = (r: string) => ROLES.find(([k]) => k === r)?.[1] ?? r;

/** Staff roster like TeamUp: shifts by day or week, a colour per person, all-day entries, tentative shifts striped; leave, staff, regular shifts and hours. */
export default function Roster() {
  const [params, setParams] = useSearchParams();
  const tab = params.get('tab') ?? 'roster';
  const [me, setMe] = useState<{ staff: Staff | null; manager: boolean } | null>(null);
  useEffect(() => { api<any>('/api/roster/me').then(setMe).catch(() => setMe({ staff: null, manager: false })); }, []);
  if (!me) return <p>Loading…</p>;
  const tabs: [string, string][] = me.manager
    ? [['roster', 'Roster'], ['leave', 'Leave'], ['swaps', 'Swaps'], ['staff', 'Staff'], ['regular', 'Regular shifts'], ['hours', 'Hours & payroll']]
    : [['roster', 'Roster'], ['leave', 'My leave'], ['swaps', 'Swaps']];
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Roster</h1><p className="muted">{me.manager ? 'Click an empty time to add a shift; click a shift to change it.' : me.staff ? `You are ${me.staff.name} on the roster.` : 'Ask the roster manager to link your account to the roster to request leave here.'}</p></div>
      <div className="tabs no-print">{tabs.map(([k, t]) => <button key={k} className={tab === k ? 'on' : ''} onClick={() => setParams({ tab: k })}>{t}</button>)}</div>
      {tab === 'roster' && <RosterCalendar manager={me.manager} myId={me.staff?.id ?? null} />}
      {tab === 'leave' && <LeaveTab manager={me.manager} me={me.staff} />}
      {tab === 'swaps' && <SwapsTab manager={me.manager} me={me.staff} />}
      {tab === 'staff' && me.manager && <StaffTab />}
      {tab === 'regular' && me.manager && <RegularTab />}
      {tab === 'hours' && me.manager && <HoursTab />}
    </div>
  );
}

function RosterCalendar({ manager, myId }: { manager: boolean; myId: string | null }) {
  const [params, setParams] = useSearchParams();
  const date = params.get('date') ?? todayIso();
  const view = (params.get('view') as 'day' | 'week') ?? 'week';
  const days = daysFor(date, view);
  const [shifts, setShifts] = useState<ShiftV[] | null>(null);
  const [leave, setLeave] = useState<LeaveV[]>([]);
  const [staff, setStaff] = useState<Staff[]>([]);
  const [edit, setEdit] = useState<any | null>(null);
  const [mine, setMine] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const load = () => {
    api<ShiftV[]>(`/api/roster/shifts?from=${days[0]}&to=${days[days.length - 1]}`).then(setShifts).catch((e) => setError(errText(e)));
    if (manager) api<LeaveV[]>('/api/roster/leave?view=upcoming').then(setLeave).catch(() => undefined);
  };
  useEffect(() => { load(); }, [date, view]);
  useEffect(() => { api<Staff[]>('/api/roster/staff').then(setStaff).catch(() => undefined); }, []);
  const go = (d: string, v = view) => setParams({ tab: 'roster', date: d, view: v });
  const shown = (shifts ?? []).filter((s) => !mine || s.staffId === myId);
  const items: CalItem[] = shown.filter((s) => !s.allDay).map((s) => ({ id: s.id, date: s.date, start: s.start!, end: s.end!, colour: s.colour, tentative: s.isTentative,
    title: s.staffName, sub: [s.area, s.note].filter(Boolean).join(' · '), badge: s.onLeave ? 'LEAVE' : undefined, onClick: manager ? () => setEdit({ ...s, start: s.start!.slice(0, 5), end: s.end!.slice(0, 5) }) : undefined }));
  const allDay: AllDayItem[] = shown.filter((s) => s.allDay).map((s) => ({ id: s.id, date: s.date, colour: s.colour, tentative: s.isTentative, title: `${s.staffName}${s.area ? ' · ' + s.area : ''}`,
    onClick: manager ? () => setEdit({ ...s, start: '', end: '' }) : undefined }));
  for (const l of leave) for (const d of days) if (d >= l.from && d <= l.to) allDay.push({ id: l.id + d, date: d, colour: staff.find((x) => x.id === l.staffId)?.colour ?? 0, muted: true, title: `${l.staffName} – ${l.type} leave` });
  const totals = new Map<string, number>(); for (const s of shown) totals.set(s.staffName, (totals.get(s.staffName) ?? 0) + s.hours);
  return (
    <div className="stack">
      <div className="row-between no-print">
        <CalendarNav date={date} view={view} onDate={(d) => go(d)} onView={(v) => go(date, v)} />
        <div className="row">
          {myId && <label className="check"><input type="checkbox" checked={mine} onChange={(e) => setMine(e.target.checked)} /> Only my shifts</label>}
          {manager && <button className="btn btn-primary btn-small" onClick={() => setEdit({ staffId: staff[0]?.id, date, allDay: false, start: '08:00', end: '16:30', breakMinutes: 30, area: 'Theatre', isTentative: false, note: '' })}>Add shift</button>}
          <button className="btn btn-light btn-small" onClick={() => window.print()}>Print</button>
        </div>
      </div>
      <div className="cal-legend no-print">{staff.map((s) => <span key={s.id} className={`cal-c${s.colour % 12}`}>{s.name}</span>)}<span className="cal-c0 tentative">Striped = tentative</span></div>
      {error && <p className="error">{error}</p>}
      {edit && <ShiftEditor value={edit} staff={staff} onDone={() => { setEdit(null); load(); }} onCancel={() => setEdit(null)} />}
      {!shifts ? <p>Loading…</p> : <CalendarGrid days={days} items={items} allDay={allDay} today={todayIso()} onSlot={manager ? (d, t) => setEdit({ staffId: staff[0]?.id, date: d, allDay: false, start: t, end: t < '15:00' ? addHours(t, 8) : '20:30', breakMinutes: 30, area: 'Theatre', isTentative: false, note: '' }) : undefined} />}
      {[...totals].some(([, h]) => h > 0) && <p className="small muted">Hours shown: {[...totals].filter(([, h]) => h > 0).sort().map(([n, h]) => `${n} ${h.toFixed(1)}`).join(' · ')}</p>}
    </div>
  );
}
const addHours = (t: string, h: number) => { const [a, b] = t.split(':').map(Number); return `${String(Math.min(a + h, 21)).padStart(2, '0')}:${String(b).padStart(2, '0')}`; };

function ShiftEditor({ value, staff, onDone, onCancel }: { value: any; staff: Staff[]; onDone: () => void; onCancel: () => void }) {
  const [f, setF] = useState(value);
  const [errors, setErrors] = useState<string[]>([]);
  const [warnings, setWarnings] = useState<string[]>([]);
  useEffect(() => { setF(value); setErrors([]); setWarnings([]); }, [value]);
  const save = async (accept: boolean) => {
    setErrors([]);
    try {
      await api('/api/roster/shifts', { body: { id: f.id ?? null, staffId: f.staffId, date: f.date, allDay: f.allDay, start: f.allDay ? null : f.start + ':00', end: f.allDay ? null : f.end + ':00',
        breakMinutes: Number(f.breakMinutes ?? 0), area: f.area, isTentative: f.isTentative, note: f.note, acceptWarnings: accept } });
      onDone();
    } catch (x: any) { setWarnings(x?.body?.warnings ?? []); setErrors(x?.body?.errors ?? (x?.body?.warnings?.length ? [] : [errText(x)])); }
  };
  const del = async () => { try { await api(`/api/roster/shifts/${f.id}/delete`, { body: {} }); onDone(); } catch (x) { setErrors(errsOf(x)); } };
  return (
    <section className="card side-panel stack no-print">
      <h2>{f.id ? 'Change shift' : 'New shift'}</h2>
      <div className="form-grid">
        <label>Staff<select value={f.staffId} onChange={(e) => setF({ ...f, staffId: e.target.value })}>{staff.map((s) => <option key={s.id} value={s.id}>{s.name} ({roleLabel(s.role)})</option>)}</select></label>
        <label>Date<input type="date" value={f.date} onChange={(e) => setF({ ...f, date: e.target.value })} /></label>
        <label className="check"><input type="checkbox" checked={f.allDay} onChange={(e) => setF({ ...f, allDay: e.target.checked })} /> All day (e.g. office or study day)</label>
        {!f.allDay && <>
          <label>Start<input type="time" step={900} value={f.start} onChange={(e) => setF({ ...f, start: e.target.value })} /></label>
          <label>Finish<input type="time" step={900} value={f.end} onChange={(e) => setF({ ...f, end: e.target.value })} /></label>
          <label>Break (minutes)<input type="number" min={0} max={120} step={5} value={f.breakMinutes} onChange={(e) => setF({ ...f, breakMinutes: e.target.value })} /></label>
        </>}
        <label>Area<input list="dl-areas" value={f.area ?? ''} onChange={(e) => setF({ ...f, area: e.target.value })} /></label>
        <label className="check"><input type="checkbox" checked={f.isTentative} onChange={(e) => setF({ ...f, isTentative: e.target.checked })} /> Tentative (not confirmed)</label>
      </div>
      <datalist id="dl-areas">{AREAS.map((a) => <option key={a} value={a} />)}</datalist>
      <label>Note<input value={f.note ?? ''} onChange={(e) => setF({ ...f, note: e.target.value })} placeholder="e.g. back if needed after 12" /></label>
      <Errs list={errors} />
      {warnings.length > 0 && <div className="warn-box"><strong>Please check:</strong><ul>{warnings.map((w) => <li key={w}>{w}</li>)}</ul></div>}
      <div className="row">
        {warnings.length ? <button className="btn btn-warn" onClick={() => save(true)}>Save anyway</button> : <button className="btn btn-primary" onClick={() => save(false)}>Save shift</button>}
        <button className="btn btn-light" onClick={onCancel}>Cancel</button>
        {f.id && <button className="btn btn-link danger" onClick={del}>Delete shift</button>}
      </div>
    </section>
  );
}

function LeaveTab({ manager, me }: { manager: boolean; me: Staff | null }) {
  const [view, setView] = useState(manager ? 'requested' : 'all');
  const [rows, setRows] = useState<LeaveV[] | null>(null);
  const [staff, setStaff] = useState<Staff[]>([]);
  const [f, setF] = useState({ staffId: me?.id ?? '', from: todayIso(), to: todayIso(), type: 'Annual', note: '' });
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const load = () => api<LeaveV[]>(`/api/roster/leave?view=${view}`).then(setRows).catch((e) => setErrors(errsOf(e)));
  useEffect(() => { load(); }, [view]);
  useEffect(() => { if (manager) api<Staff[]>('/api/roster/staff').then((s) => { setStaff(s); setF((x) => ({ ...x, staffId: x.staffId || s[0]?.id || '' })); }).catch(() => undefined); }, []);
  const request = async () => { setErrors([]); setMsg(null); try { await api('/api/roster/leave', { body: f }); setMsg('Leave requested.'); load(); } catch (x) { setErrors(errsOf(x)); } };
  const decide = async (id: string, decision: string) => {
    setErrors([]); setMsg(null);
    try { const r = await api<{ shiftsNeedingCover: number }>(`/api/roster/leave/${id}/decide`, { body: { decision } }); setMsg(r.shiftsNeedingCover ? `${decision}. ${r.shiftsNeedingCover} rostered shift(s) in that time need cover.` : `${decision}.`); load(); }
    catch (x) { setErrors(errsOf(x)); }
  };
  const canRequest = manager || !!me;
  return (
    <div className="stack">
      {canRequest && (
        <section className="card stack">
          <h2>Request leave</h2>
          <div className="form-grid">
            {manager ? <label>Staff<select value={f.staffId} onChange={(e) => setF({ ...f, staffId: e.target.value })}>{staff.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}</select></label> : <p>For: <strong>{me!.name}</strong></p>}
            <label>Type<select value={f.type} onChange={(e) => setF({ ...f, type: e.target.value })}>{LEAVE.map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
            <label>First day<input type="date" value={f.from} onChange={(e) => setF({ ...f, from: e.target.value, to: e.target.value > f.to ? e.target.value : f.to })} /></label>
            <label>Last day<input type="date" value={f.to} min={f.from} onChange={(e) => setF({ ...f, to: e.target.value })} /></label>
          </div>
          <label>Note<input value={f.note} onChange={(e) => setF({ ...f, note: e.target.value })} /></label>
          <Errs list={errors} />{msg && <p className="notice" role="status">{msg}</p>}
          <div><button className="btn btn-primary" onClick={request}>Request leave</button></div>
        </section>
      )}
      <section className="card">
        {manager && <div className="tabs">{[['requested', 'To approve'], ['upcoming', 'Approved (upcoming)'], ['all', 'All recent']].map(([k, t]) => <button key={k} className={view === k ? 'on' : ''} onClick={() => setView(k)}>{t}</button>)}</div>}
        {rows?.length === 0 && <p className="muted">No leave here.</p>}
        {rows && rows.length > 0 && (
          <table className="results compact">
            <thead><tr><th>Staff</th><th>Dates</th><th>Type</th><th>Status</th><th></th></tr></thead>
            <tbody>{rows.map((l) => (
              <tr key={l.id}><td data-label="Staff"><strong>{l.staffName}</strong><div className="small muted">requested {nzDateTime(l.requestedAt)}</div></td>
                <td data-label="Dates">{formatDate(l.from)}{l.to !== l.from ? ` – ${formatDate(l.to)}` : ''}</td>
                <td data-label="Type">{LEAVE.find(([k]) => k === l.type)?.[1]}{l.note && <div className="small muted">{l.note}</div>}</td>
                <td data-label="Status"><span className={`pill ${l.status === 'Approved' ? 'green' : l.status === 'Requested' ? 'amber' : ''}`}>{l.status}</span>{l.decidedBy && <div className="small muted">{l.decidedBy}</div>}</td>
                <td>{manager && l.status === 'Requested' && <div className="status-actions"><button className="btn btn-primary btn-small" onClick={() => decide(l.id, 'Approved')}>Approve</button><button className="btn btn-link" onClick={() => decide(l.id, 'Declined')}>Decline</button></div>}
                  {l.status === 'Approved' && manager && <button className="btn btn-link" onClick={() => decide(l.id, 'Cancelled')}>Cancel</button>}</td></tr>
            ))}</tbody>
          </table>
        )}
      </section>
    </div>
  );
}

function StaffTab() {
  const [rows, setRows] = useState<Staff[] | null>(null);
  const [edit, setEdit] = useState<any | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const load = () => api<Staff[]>('/api/roster/staff?all=true').then(setRows).catch((e) => setErrors(errsOf(e)));
  useEffect(() => { load(); }, []);
  const save = async () => { setErrors([]); try { await api('/api/roster/staff', { body: { ...edit, contractedHours: edit.contractedHours === '' || edit.contractedHours == null ? null : Number(edit.contractedHours), colour: Number(edit.colour ?? -1) } }); setEdit(null); load(); } catch (x) { setErrors(errsOf(x)); } };
  return (
    <div className="stack">
      <div className="row-between"><p className="hint">Everyone who is rostered. Link a staff member to their BrightPAS username so they can see the roster and request leave.</p>
        {!edit && <button className="btn btn-primary" onClick={() => setEdit({ role: 'Nurse', isActive: true, colour: -1 })}>Add staff member</button>}</div>
      {edit && (
        <Modal title={edit.id ? `Edit ${edit.name}` : 'New staff member'} onClose={() => setEdit(null)}>
          <div className="form-grid">
            <label>Name<input value={edit.name ?? ''} onChange={(e) => setEdit({ ...edit, name: e.target.value })} /></label>
            <label>Role<select value={edit.role} onChange={(e) => setEdit({ ...edit, role: e.target.value })}>{ROLES.map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
            <label>Contracted hours a week<input type="number" min={0} max={80} step={0.5} value={edit.contractedHours ?? ''} onChange={(e) => setEdit({ ...edit, contractedHours: e.target.value })} /></label>
            <label>Phone<input value={edit.phone ?? ''} onChange={(e) => setEdit({ ...edit, phone: e.target.value })} /></label>
            <label>BrightPAS username (optional)<input value={edit.userName ?? ''} onChange={(e) => setEdit({ ...edit, userName: e.target.value })} /></label>
            <label>Payroll / Xero employee ID<input value={edit.payrollCode ?? ''} onChange={(e) => setEdit({ ...edit, payrollCode: e.target.value })} /></label>
            <label>Xero earnings rate<input value={edit.earningsRate ?? ''} placeholder="Ordinary Hours" onChange={(e) => setEdit({ ...edit, earningsRate: e.target.value })} /></label>
            <label>Colour<div className="row">{Array.from({ length: 12 }, (_, i) => <button type="button" key={i} aria-label={`Colour ${i + 1}`} className={`swatch cal-c${i} ${edit.colour === i ? 'on' : ''}`} onClick={() => setEdit({ ...edit, colour: i })} />)}</div></label>
            <label className="check"><input type="checkbox" checked={!!edit.isActive} onChange={(e) => setEdit({ ...edit, isActive: e.target.checked })} /> Active</label>
          </div>
          <Errs list={errors} />
          <div className="row"><button className="btn btn-primary" onClick={save}>Save</button><button className="btn btn-light" onClick={() => setEdit(null)}>Cancel</button></div>
        </Modal>
      )}
      {rows && rows.length > 0 && (
        <table className="results compact">
          <thead><tr><th>Name</th><th>Role</th><th>Hours</th><th>Account</th><th></th></tr></thead>
          <tbody>{rows.map((s) => (
            <tr key={s.id} className={s.isActive ? '' : 'muted'}><td data-label="Name"><span className={`dot-c cal-c${s.colour % 12}`} /> <strong>{s.name}</strong></td><td data-label="Role">{roleLabel(s.role)}</td>
              <td data-label="Hours">{s.contractedHours ?? '—'}</td><td data-label="Account">{s.userName ?? '—'}</td><td><button className="btn btn-link" onClick={() => setEdit(s)}>Edit</button></td></tr>
          ))}</tbody>
        </table>
      )}
      {rows?.length === 0 && <p className="muted">No staff yet.</p>}
    </div>
  );
}

function RegularTab() {
  const [rows, setRows] = useState<any[] | null>(null);
  const [staff, setStaff] = useState<Staff[]>([]);
  const [edit, setEdit] = useState<any | null>(null);
  const [gen, setGen] = useState({ from: weekStart(todayIso()), to: addDays(weekStart(todayIso()), 27) });
  const [result, setResult] = useState<{ created: number; skipped: string[] } | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const load = () => api<any[]>('/api/roster/templates').then(setRows).catch((e) => setErrors(errsOf(e)));
  useEffect(() => { load(); api<Staff[]>('/api/roster/staff').then(setStaff).catch(() => undefined); }, []);
  const save = async () => {
    setErrors([]);
    try { await api('/api/roster/templates', { body: { ...edit, start: edit.allDay ? null : edit.start.slice(0, 5) + ':00', end: edit.allDay ? null : edit.end.slice(0, 5) + ':00', everyWeeks: Number(edit.everyWeeks), breakMinutes: Number(edit.breakMinutes ?? 0), endsOn: edit.endsOn || null } }); setEdit(null); load(); }
    catch (x) { setErrors(errsOf(x)); }
  };
  const name = (id: string) => staff.find((s) => s.id === id)?.name ?? '?';
  return (
    <div className="stack">
      <div className="row-between"><p className="hint">Each person's usual weekly shifts. Create the roster for the coming weeks in one step; existing shifts and approved leave are respected.</p>
        {!edit && <button className="btn btn-primary" onClick={() => setEdit({ staffId: staff[0]?.id, dayOfWeek: 'Monday', allDay: false, start: '08:00', end: '16:30', breakMinutes: 30, area: 'Theatre', everyWeeks: 1, startsOn: weekStart(todayIso()), endsOn: '', isActive: true })}>Add regular shift</button>}</div>
      {edit && (
        <Modal title={edit.id ? 'Edit regular shift' : 'New regular shift'} onClose={() => setEdit(null)}>
          <div className="form-grid">
            <label>Staff<select value={edit.staffId} onChange={(e) => setEdit({ ...edit, staffId: e.target.value })}>{staff.map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}</select></label>
            <label>Day<select value={edit.dayOfWeek} onChange={(e) => setEdit({ ...edit, dayOfWeek: e.target.value })}>{DAYS.map((d) => <option key={d}>{d}</option>)}</select></label>
            <label className="check"><input type="checkbox" checked={edit.allDay} onChange={(e) => setEdit({ ...edit, allDay: e.target.checked })} /> All day</label>
            {!edit.allDay && <><label>Start<input type="time" value={(edit.start ?? '').slice(0, 5)} onChange={(e) => setEdit({ ...edit, start: e.target.value })} /></label>
              <label>Finish<input type="time" value={(edit.end ?? '').slice(0, 5)} onChange={(e) => setEdit({ ...edit, end: e.target.value })} /></label>
              <label>Break<input type="number" min={0} max={120} value={edit.breakMinutes} onChange={(e) => setEdit({ ...edit, breakMinutes: e.target.value })} /></label></>}
            <label>Area<input value={edit.area ?? ''} onChange={(e) => setEdit({ ...edit, area: e.target.value })} /></label>
            <label>Repeat<select value={edit.everyWeeks} onChange={(e) => setEdit({ ...edit, everyWeeks: e.target.value })}>{[1, 2, 3, 4].map((n) => <option key={n} value={n}>{n === 1 ? 'Every week' : `Every ${n} weeks`}</option>)}</select></label>
            <label>From<input type="date" value={edit.startsOn} onChange={(e) => setEdit({ ...edit, startsOn: e.target.value })} /></label>
            <label>Until (optional)<input type="date" value={edit.endsOn ?? ''} onChange={(e) => setEdit({ ...edit, endsOn: e.target.value })} /></label>
            <label className="check"><input type="checkbox" checked={edit.isActive} onChange={(e) => setEdit({ ...edit, isActive: e.target.checked })} /> Active</label>
          </div>
          <Errs list={errors} />
          <div className="row"><button className="btn btn-primary" onClick={save}>Save</button><button className="btn btn-light" onClick={() => setEdit(null)}>Cancel</button></div>
        </Modal>
      )}
      {rows && rows.length > 0 && <>
        <table className="results compact">
          <thead><tr><th>Staff</th><th>Day</th><th>Time</th><th>Area</th><th>Repeat</th><th></th></tr></thead>
          <tbody>{rows.map((t) => (
            <tr key={t.id} className={t.isActive ? '' : 'muted'}><td data-label="Staff"><strong>{name(t.staffId)}</strong></td><td data-label="Day">{t.dayOfWeek}</td>
              <td data-label="Time">{t.allDay ? 'All day' : `${t.start.slice(0, 5)}–${t.end.slice(0, 5)}`}</td><td data-label="Area">{t.area ?? ''}</td>
              <td data-label="Repeat">{t.everyWeeks === 1 ? 'Weekly' : `Every ${t.everyWeeks} weeks`}</td><td><button className="btn btn-link" onClick={() => setEdit({ ...t, start: t.start ?? '', end: t.end ?? '', endsOn: t.endsOn ?? '' })}>Edit</button></td></tr>
          ))}</tbody>
        </table>
        <section className="card stack">
          <h2>Create the roster from regular shifts</h2>
          <div className="row"><label>From<input type="date" value={gen.from} onChange={(e) => setGen({ ...gen, from: e.target.value })} /></label>
            <label>To<input type="date" value={gen.to} onChange={(e) => setGen({ ...gen, to: e.target.value })} /></label>
            <button className="btn btn-primary" onClick={async () => { setResult(null); try { setResult(await api('/api/roster/templates/generate', { body: gen })); } catch (x) { setErrors(errsOf(x)); } }}>Create shifts</button></div>
          {result && <div className="notice"><strong>{result.created} shift(s) created.</strong>{result.skipped.length > 0 && <> {result.skipped.length} skipped:<ul>{result.skipped.slice(0, 12).map((s) => <li key={s}>{s}</li>)}</ul></>}</div>}
        </section>
      </>}
    </div>
  );
}

function HoursTab() {
  const [from, setFrom] = useState(weekStart(todayIso()));
  const [rows, setRows] = useState<any[] | null>(null);
  const to = addDays(from, 13);
  useEffect(() => { setRows(null); api<any[]>(`/api/roster/hours?from=${from}&to=${to}`).then(setRows).catch(() => setRows([])); }, [from]);
  return (
    <section className="card stack">
      <div className="row"><label>Fortnight starting<input type="date" value={from} onChange={(e) => e.target.value && setFrom(weekStart(e.target.value))} /></label><span className="muted">to {formatDate(to)}</span>
        <ExportButton label="Export timesheet for Xero payroll" path="/api/roster/payroll-export" body={{ from, to }} /></div>
      <p className="hint">The export has a Xero-style timesheet (hours per person per day, by earnings rate) and a shift detail sheet. Tentative shifts are left out. Set each person's payroll ID and earnings rate on the Staff tab.</p>
      {rows && (
        <table className="results compact">
          <thead><tr><th>Staff</th><th>Role</th><th className="money">Rostered</th><th className="money">Contracted</th><th className="money">Shifts</th><th className="money">Leave days</th></tr></thead>
          <tbody>{rows.map((r) => {
            const diff = r.contracted != null ? r.rostered - r.contracted : null;
            return <tr key={r.staffId}><td data-label="Staff"><strong>{r.staffName}</strong></td><td data-label="Role">{roleLabel(r.role)}</td>
              <td data-label="Rostered" className="money">{r.rostered.toFixed(2)}</td>
              <td data-label="Contracted" className="money">{r.contracted ?? '—'}{diff != null && Math.abs(diff) >= 1 && <div className={`small ${diff > 0 ? 'warn-text' : 'muted'}`}>{diff > 0 ? '+' : ''}{diff.toFixed(1)}</div>}</td>
              <td data-label="Shifts" className="money">{r.shifts}</td><td data-label="Leave days" className="money">{r.leaveDays}</td></tr>;
          })}</tbody>
        </table>
      )}
      <p className="hint">Hours exclude breaks. A payroll export (Xero) comes later.</p>
    </section>
  );
}

function SwapsTab({ manager, me }: { manager: boolean; me: Staff | null }) {
  const [rows, setRows] = useState<any[] | null>(null);
  const [all, setAll] = useState(false);
  const [mine, setMine] = useState<ShiftV[]>([]);
  const [staff, setStaff] = useState<Staff[]>([]);
  const [theirs, setTheirs] = useState<ShiftV[]>([]);
  const [f, setF] = useState({ shiftId: '', toStaffId: '', offeredShiftId: '', note: '' });
  const [errors, setErrors] = useState<string[]>([]);
  const from = todayIso(), to = addDays(todayIso(), 56);
  const load = () => api<any[]>(`/api/roster/swaps?all=${all}`).then(setRows).catch((e) => setErrors(errsOf(e)));
  useEffect(() => { load(); }, [all]); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => {
    api<Staff[]>('/api/roster/staff').then(setStaff).catch(() => undefined);
    if (me) api<ShiftV[]>(`/api/roster/shifts?from=${from}&to=${to}&staffId=${me.id}`).then(setMine).catch(() => undefined);
  }, [me]); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => { setTheirs([]); if (f.toStaffId) api<ShiftV[]>(`/api/roster/shifts?from=${from}&to=${to}&staffId=${f.toStaffId}`).then(setTheirs).catch(() => undefined); }, [f.toStaffId]); // eslint-disable-line react-hooks/exhaustive-deps
  const act = async (path: string, body: unknown) => { setErrors([]); try { await api(path, { body }); load(); return true; } catch (x) { setErrors(errsOf(x)); return false; } };
  const ask = async () => { if (await act('/api/roster/swaps', { shiftId: f.shiftId, toStaffId: f.toStaffId || null, offeredShiftId: f.offeredShiftId || null, note: f.note || null })) setF({ shiftId: '', toStaffId: '', offeredShiftId: '', note: '' }); };
  const label = (s: ShiftV) => `${formatDate(s.date)} ${s.allDay ? 'all day' : `${s.start?.slice(0, 5)}–${s.end?.slice(0, 5)}`} ${s.area ?? ''}`;
  return (
    <div className="stack">
      {me && <section className="card subtle stack"><h2>Offer one of my shifts</h2>
        <div className="form-grid">
          <label>My shift<select value={f.shiftId} onChange={(e) => setF({ ...f, shiftId: e.target.value })}><option value="">Choose…</option>{mine.map((s) => <option key={s.id} value={s.id}>{label(s)}</option>)}</select></label>
          <label>To<select value={f.toStaffId} onChange={(e) => setF({ ...f, toStaffId: e.target.value, offeredShiftId: '' })}><option value="">Anyone (open offer)</option>{staff.filter((s) => s.id !== me.id).map((s) => <option key={s.id} value={s.id}>{s.name}</option>)}</select></label>
          {f.toStaffId && <label>In exchange for (optional)<select value={f.offeredShiftId} onChange={(e) => setF({ ...f, offeredShiftId: e.target.value })}><option value="">Nothing – they just take it</option>{theirs.map((s) => <option key={s.id} value={s.id}>{label(s)}</option>)}</select></label>}
          <label>Note<input value={f.note} onChange={(e) => setF({ ...f, note: e.target.value })} /></label>
        </div>
        <button className="btn btn-primary" disabled={!f.shiftId} onClick={ask}>Ask</button>
        <p className="hint">Your colleague accepts, then the roster manager approves – only then do the shifts change.</p>
      </section>}
      <label className="check"><input type="checkbox" checked={all} onChange={(e) => setAll(e.target.checked)} /> Show closed requests</label>
      <Errs list={errors} />
      {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">No swap requests.</p> : (
        <table className="results compact"><thead><tr><th>Shift</th><th>From</th><th>To</th><th>Exchange</th><th>Status</th><th></th></tr></thead>
          <tbody>{rows.map((r) => <tr key={r.id}><td>{r.shiftText}{r.note && <div className="small muted">{r.note}</div>}</td><td>{r.fromName}</td><td>{r.toName ?? 'anyone'}</td><td>{r.offeredText ?? '—'}</td>
            <td>{r.status}{r.decisionNote && <div className="small muted">{r.decisionNote}</div>}</td>
            <td className="actions">
              {r.status === 'Requested' && me && r.fromStaffId !== me.id && (!r.toStaffId || r.toStaffId === me.id) && <button className="btn btn-link" onClick={() => act(`/api/roster/swaps/${r.id}/accept`, {})}>Accept</button>}
              {r.status === 'Accepted' && manager && <button className="btn btn-link" onClick={() => act(`/api/roster/swaps/${r.id}/decide`, { decision: 'Approved', note: null })}>Approve</button>}
              {['Requested', 'Accepted'].includes(r.status) && (manager || r.toStaffId === me?.id) && <button className="btn btn-link" onClick={() => act(`/api/roster/swaps/${r.id}/decide`, { decision: 'Declined', note: null })}>Decline</button>}
              {['Requested', 'Accepted'].includes(r.status) && r.fromStaffId === me?.id && <button className="btn btn-link" onClick={() => act(`/api/roster/swaps/${r.id}/decide`, { decision: 'Cancelled', note: null })}>Cancel</button>}
            </td></tr>)}</tbody></table>)}
    </div>
  );
}
