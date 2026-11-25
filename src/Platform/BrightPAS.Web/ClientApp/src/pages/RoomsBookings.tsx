import { useCallback, useEffect, useState } from 'react';
import { useSearchParams } from 'react-router';
import { api, ApiError, errText, nzDateTime } from '../api';
import { BookingRequestForm, RequestDetail, RequestList, RequestRow } from '../components/BookingRequests';

const errs = (e: unknown) => (e instanceof ApiError && e.body?.errors ? e.body.errors as string[] : [errText(e)]);
const VIEWS = [['open', 'To do'], ['query', 'Waiting for the rooms'], ['accepted', 'Accepted'], ['closed', 'Declined / withdrawn'], ['all', 'All']];

/** Booking office: requests from specialists' rooms (0.28.0, ADR 0041). */
export default function RoomsBookings() {
  const [params, setParams] = useSearchParams();
  const tab = params.get('tab') ?? 'requests';
  const tabs = [['requests', 'Requests'], ['new', 'Enter a request'], ['import', 'Import a file'], ['settings', 'Settings']];
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Bookings from rooms</h1><p className="muted">Booking requests sent by specialists and their practice assistants through the portal, or by their practice systems as files. Check, ask, then accept onto the waitlist or decline.</p></div>
      <div className="tabs" role="tablist">{tabs.map(([k, t]) => <button key={k} role="tab" aria-selected={tab === k} className={tab === k ? 'on' : ''} onClick={() => setParams({ tab: k })}>{t}</button>)}</div>
      {tab === 'requests' && <Requests />}
      {tab === 'new' && <section className="card stack"><h2>Enter a request received by phone, email or fax</h2>
        <BookingRequestForm base="/api/ebookings" onDone={(id) => setParams({ tab: 'requests', open: id })} /></section>}
      {tab === 'import' && <ImportFile />}
      {tab === 'settings' && <Settings />}
    </div>
  );
}

function Requests() {
  const [params, setParams] = useSearchParams();
  const [view, setView] = useState('open');
  const [rows, setRows] = useState<RequestRow[] | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const openId = params.get('open');
  const load = useCallback(() => api<RequestRow[]>(`/api/ebookings?view=${view}`).then(setRows).catch((e) => setErrors(errs(e))), [view]);
  useEffect(() => { void load(); }, [load]);
  return (
    <div className="stack">
      <section className="card stack">
        <div className="row">{VIEWS.map(([k, t]) => <button key={k} className={`btn btn-small ${view === k ? 'btn-primary' : 'btn-light'}`} aria-pressed={view === k} onClick={() => setView(k)}>{t}</button>)}</div>
        {errors.length > 0 && <div className="error" role="alert">{errors.join(' ')}</div>}
        {rows ? <RequestList rows={rows} office label="Booking requests" onOpen={(id) => setParams({ tab: 'requests', open: id })} /> : <p>Loading…</p>}
      </section>
      {openId && <RequestDetail id={openId} base="/api/ebookings" office onChanged={() => void load()} onClose={() => setParams({ tab: 'requests' })} />}
    </div>
  );
}

function ImportFile() {
  const [busy, setBusy] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const upload = async (f: File | undefined) => {
    if (!f) return;
    setErrors([]); setMsg(null); setBusy(`Reading ${f.name}…`);
    try {
      const res = await fetch('/api/ebookings/import', { method: 'POST', credentials: 'same-origin', headers: { 'X-BrightPAS-Request': '1', 'X-File-Name': encodeURIComponent(f.name), 'Content-Type': 'application/octet-stream' }, body: f });
      const body = await res.json().catch(() => null);
      if (!res.ok) setErrors(body?.errors ?? [`The server answered ${res.status}.`]);
      else { setMsg(`${body.received} request${body.received === 1 ? '' : 's'} received${body.numbers.length ? `: ${body.numbers.join(', ')}` : ''}.`); if (body.problems.length) setErrors(body.problems); }
    } catch { setErrors(['Could not reach BrightPAS – check the network and try again.']); } finally { setBusy(''); }
  };
  return (
    <section className="card stack">
      <h2>Files from practice systems</h2>
      <p>Practices whose system can export a booking send a <strong>BrightPAS booking file (JSON)</strong> or an <strong>HL7 v2 REF^I12</strong> referral message. Import one here, or set a shared folder on the Settings tab so BrightPAS picks the files up every two minutes. A booking already received (same rooms reference) is not added twice.</p>
      <p>The specialist is found by their HPI number (CPN) or exact name on the clinician register.</p>
      <div className="row">
        <label className="btn btn-primary file-pick">{busy || 'Import a booking file…'}<input type="file" accept=".json,.hl7,.txt,.msg,.ref,application/json,text/plain" disabled={!!busy} onChange={(e) => { void upload(e.target.files?.[0]); e.target.value = ''; }} className="sr-only" /></label>
        <a className="btn btn-light" href="/api/ebookings/example.json" download>Download an example JSON file for practices</a>
      </div>
      {msg && <p className="notice" role="status">{msg}</p>}
      {errors.length > 0 && <div className="error" role="alert"><ul>{errors.map((e) => <li key={e}>{e}</li>)}</ul></div>}
    </section>
  );
}

function Settings() {
  const [s, setS] = useState<any>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const load = () => api('/api/ebookings/settings').then(setS).catch((e) => setErrors(errs(e)));
  useEffect(() => { void load(); }, []);
  const save = async () => {
    setErrors([]); setMsg(null);
    try { await api('/api/ebookings/settings', { method: 'PUT', body: { watchFolder: s.watchFolder || null, emailRooms: s.emailRooms, maxFileMegabytes: Number(s.maxFileMegabytes), instructions: s.instructions || null } }); setMsg('Settings saved.'); await load(); }
    catch (e) { setErrors(errs(e)); }
  };
  if (!s) return <section className="card"><p>{errors.join(' ') || 'Loading…'}</p></section>;
  return (
    <section className="card stack">
      <h2>Settings</h2>
      <div className="form-grid">
        <label className="span-all">Folder for practice-system files (share or USB, optional)<input value={s.watchFolder ?? ''} onChange={(e) => setS({ ...s, watchFolder: e.target.value })} placeholder="\\server\share\Bookings" /></label>
        <label>Largest file the rooms can attach (MB)<input value={s.maxFileMegabytes} onChange={(e) => setS({ ...s, maxFileMegabytes: e.target.value })} inputMode="numeric" /></label>
        <label className="span-all">Instructions shown to the rooms on the request form<textarea rows={3} value={s.instructions ?? ''} onChange={(e) => setS({ ...s, instructions: e.target.value })} /></label>
      </div>
      <label className="check"><input type="checkbox" checked={s.emailRooms} onChange={(e) => setS({ ...s, emailRooms: e.target.checked })} /> Email the rooms contact when we ask a question, accept or decline (no patient details in the email; uses the Reminders email settings)</label>
      {s.lastCheckedAt && <p className="small muted">Folder last checked {nzDateTime(s.lastCheckedAt)}{s.lastProblem ? ` – ${s.lastProblem}` : ' – fine'}.</p>}
      {msg && <p className="notice" role="status">{msg}</p>}
      {errors.length > 0 && <div className="error" role="alert"><ul>{errors.map((e) => <li key={e}>{e}</li>)}</ul></div>}
      <div><button className="btn btn-primary" onClick={() => void save()}>Save settings</button></div>
    </section>
  );
}
