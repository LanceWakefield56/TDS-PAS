import { useCallback, useEffect, useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router';
import { api, errText, fileToText, formatDate, nzDateTime, Perm, todayIso } from '../api';
import { useSession } from '../App';
import ExportButton from '../components/ExportButton';
import Modal, { EditInPopup } from '../components/Modal';

const STATUS: Record<string, string> = { ToCode: 'To code', InProgress: 'In progress', Coded: 'Coded – ready to send', Submitted: 'Sent', Rejected: 'Returned by Health NZ', NotReportable: 'Not reported by TDS' };
const LEVEL: Record<string, string> = { Error: 'check-must', Warning: 'warn-box', Advice: 'muted' };

function Msg({ error, ok }: { error: string | null; ok: string | null }) {
  return <>{error && <p className="error" role="alert">{error}</p>}{ok && <p className="ok-text" role="status">{ok}</p>}</>;
}

function Checks({ checks }: { checks: any[] }) {
  if (!checks?.length) return <p className="ok-text">No edit-check problems.</p>;
  return <ul className="stack-sm" aria-label="Edit checks">{checks.map((c, i) =>
    <li key={i} className={LEVEL[c.level] ?? ''}><strong>{c.level === 'Error' ? 'Must fix' : c.level === 'Warning' ? 'Check' : 'Advice'} – {c.rule}:</strong> {c.message}</li>)}</ul>;
}

// ------------------------------------------------------------------ main page with tabs
export default function Coding() {
  const { can } = useSession();
  const [params, setParams] = useSearchParams();
  const tab = params.get('tab') ?? 'queue';
  const tabs: [string, string][] = [['queue', 'Coding queue'], ['nmds', 'NMDS files'], ['nbrs', 'NBRS'], ['tables', 'Code tables & picklist'], ['advice', 'Advice library'], ['settings', 'Settings']];
  const [sum, setSum] = useState<any>(null);
  useEffect(() => { api('/api/coding/summary').then(setSum).catch(() => undefined); }, [tab]);
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Coding &amp; statutory reporting</h1>
        <p className="muted">Clinical coding in ICD-10-AM/ACHI/ACS (New Zealand edition) with edit checks, and the National Minimum Dataset and National Booking Reporting System files for Health NZ.</p></div>
      {sum && <div className="row">
        <span className="pill">{sum.toCode} to code</span><span className="pill">{sum.inProgress} in progress</span><span className="pill">{sum.coded} ready to send</span>
        {sum.rejected > 0 && <span className="pill amber">{sum.rejected} returned with errors</span>}
        {sum.overdue > 0 && <span className="pill amber">{sum.overdue} past the reporting time</span>}
        {sum.codes === 0 && <span className="pill amber">No code table loaded</span>}
        {!sum.settingsDone && <span className="pill amber">Health NZ identifiers not set</span>}
      </div>}
      <div className="tabs" role="tablist">{tabs.map(([k, t]) => <button key={k} role="tab" aria-selected={tab === k} className={tab === k ? 'on' : ''} onClick={() => setParams({ tab: k })}>{t}</button>)}</div>
      {tab === 'queue' && <Queue />}
      {tab === 'nmds' && <Nmds canSubmit={can(Perm.CodingSubmit)} />}
      {tab === 'nbrs' && <NbrsTab canCode={can(Perm.CodingCode)} canSubmit={can(Perm.CodingSubmit)} canManage={can(Perm.CodingManage)} />}
      {tab === 'tables' && <Tables canManage={can(Perm.CodingManage)} />}
      {tab === 'advice' && <Advice canManage={can(Perm.CodingManage)} />}
      {tab === 'settings' && <Settings canManage={can(Perm.CodingManage)} />}
    </div>
  );
}

function Queue() {
  const [status, setStatus] = useState('open');
  const [rows, setRows] = useState<any[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => { setRows(null); api(`/api/coding/queue?status=${status}`).then(setRows).catch((e) => setError(errText(e))); }, [status]);
  return (
    <section className="card stack">
      <div className="row"><h2>Events</h2>
        <label className="inline">Show <select value={status} onChange={(e) => setStatus(e.target.value)}>
          <option value="open">To code, in progress, returned</option>{Object.entries(STATUS).map(([k, t]) => <option key={k} value={k}>{t}</option>)}<option value="all">All</option></select></label></div>
      <Msg error={error} ok={null} />
      {rows === null ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">Nothing here. Discharged events appear automatically.</p> :
        <table className="table"><thead><tr><th>Discharged</th><th>Patient</th><th>Procedure</th><th>Purchaser</th><th>Codes</th><th>Status</th><th></th></tr></thead>
          <tbody>{rows.map((r) => <tr key={r.id} className={r.overdue ? 'row-warn' : ''}>
            <td>{formatDate(r.discharged)}{r.overdue && <div className="small">past reporting time</div>}</td><td>{r.patient}{r.nhi && <div className="small">{r.nhi}</div>}</td>
            <td>{r.procedure}</td><td>{r.purchaser}</td><td>{r.items}</td><td>{STATUS[r.status] ?? r.status}{r.error && <div className="small error">{r.error}</div>}</td>
            <td><Link className="btn btn-light" to={`/coding/events/${r.id}`}>Open</Link></td></tr>)}</tbody></table>}
    </section>
  );
}

// ------------------------------------------------------------------ coder workbench
type Item = { diagnosisType: string; code: string; description: string; date: string | null; tableDescription?: string | null };

export function CodingWorkbench() {
  const { id } = useParams();
  const { can } = useSession();
  const [w, setW] = useState<any>(null);
  const [lk, setLk] = useState<any>(null);
  const [items, setItems] = useState<Item[]>([]);
  const [head, setHead] = useState({ purchaserCode: '', admissionSource: 'R', eventEndType: 'DR', coderNote: '' });
  const [checks, setChecks] = useState<any[]>([]);
  const [q, setQ] = useState(''); const [types, setTypes] = useState(''); const [hits, setHits] = useState<any[]>([]);
  const [hnz, setHnz] = useState(''); const [reason, setReason] = useState('');
  const [error, setError] = useState<string | null>(null); const [ok, setOk] = useState<string | null>(null);
  const load = useCallback(async () => {
    const d = await api(`/api/coding/events/${id}`);
    setW(d); setChecks(d.checks);
    setItems(d.episode.items.map((i: any) => ({ diagnosisType: i.diagnosisType, code: i.code, description: i.description, date: i.date, tableDescription: i.tableDescription })));
    setHead({ purchaserCode: d.episode.purchaserCode, admissionSource: d.episode.admissionSource, eventEndType: d.episode.eventEndType, coderNote: d.episode.coderNote ?? '' });
  }, [id]);
  useEffect(() => { load().catch((e) => setError(errText(e))); api('/api/coding/settings').then(setLk).catch(() => undefined); }, [load]);
  const search = async () => { setHits(q.trim().length < 2 ? [] : await api<any[]>(`/api/coding/codes?q=${encodeURIComponent(q)}&types=${types}`).catch(() => [])); };
  const typeFor = (codeType: string) => codeType === 'O' ? 'O' : codeType === 'E' ? 'E' : codeType === 'M' ? 'M' : items.some((i) => i.diagnosisType === 'A') ? 'B' : 'A';
  const add = (c: { code: string; type: string; description: string }) => {
    const d = typeFor(c.type);
    setItems([...items, { diagnosisType: d, code: c.code, description: c.description.slice(0, 50), date: d === 'O' || d === 'E' ? w.episode.dischargeDate : null, tableDescription: c.description }]);
  };
  const set = (n: number, patch: Partial<Item>) => setItems(items.map((x, i) => i === n ? { ...x, ...patch } : x));
  const save = async (markCoded: boolean) => {
    setError(null); setOk(null);
    try {
      const r = await api(`/api/coding/events/${id}`, { method: 'PUT', body: { version: w.episode.version, ...head, items: items.map(({ tableDescription: _t, ...i }) => ({ ...i, date: i.date || null })), markCoded } });
      setChecks(r.checks);
      if (r.errors?.length) setError(r.errors.join(' ')); else setOk(markCoded ? 'Coded – the event goes in the next NMDS file.' : 'Saved.');
      await load();
    } catch (e) { setError(errText(e)); }
  };
  const reportable = async (yes: boolean) => { setError(null); try { await api(`/api/coding/events/${id}/reportable`, { body: { reportable: yes, reason } }); setReason(''); await load(); } catch (e) { setError(errText(e)); } };
  const recordError = async () => { setError(null); try { await api(`/api/coding/events/${id}/health-nz-error`, { body: { message: hnz } }); setHnz(''); await load(); } catch (e) { setError(errText(e)); } };
  if (!w) return error ? <p className="error" role="alert">{error}</p> : <p>Loading…</p>;
  const e = w.episode; const editable = can(Perm.CodingCode);
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Code event – {w.patient.name}</h1>
        <p className="muted">{w.patient.nhi ?? 'No NHI'} · {w.patient.mrn} · born {formatDate(w.patient.dateOfBirth)} · {w.patient.sex} · admitted {formatDate(e.admissionDate)}, discharged {formatDate(e.dischargeDate)} · <strong>{STATUS[e.status] ?? e.status}</strong>
          {' '}· <Link to="/coding">Back to the queue</Link> · <Link to={`/patients/${w.patient.id}`}>Patient record</Link> · <Link to={`/emr/${e.bookingId}`}>Clinical record</Link></p></div>
      <section className="card stack">
        <h2>Event</h2>
        <p>Procedure booked: <strong>{w.procedure || '—'}</strong>{w.surgeon && <> · surgeon {w.surgeon}</>}{w.anaesthetic && <> · {w.anaesthetic.toLowerCase()} anaesthetic</>}{w.dischargeDestination && <> · discharged to {w.dischargeDestination}</>}</p>
        {w.patient.problems.length > 0 && <p className="warn-box">Patient record: {w.patient.problems.join('; ')} – fix in the patient record before the NMDS file.</p>}
        {e.notReportableReason && <p className="warn-box">Not reported by TDS: {e.notReportableReason}</p>}
        {e.healthNzError && <p className="error">Returned by Health NZ: {e.healthNzError}</p>}
        {lk && <div className="form-grid">
          <label>Purchaser<select value={head.purchaserCode} disabled={!editable} onChange={(x) => setHead({ ...head, purchaserCode: x.target.value })}>{Object.entries(lk.purchasers).map(([k, t]: any) => <option key={k} value={k}>{k} – {t}</option>)}</select></label>
          <label>Admission source<select value={head.admissionSource} disabled={!editable} onChange={(x) => setHead({ ...head, admissionSource: x.target.value })}><option value="R">R – routine</option><option value="T">T – transferred from another facility</option></select></label>
          <label>Event end type<select value={head.eventEndType} disabled={!editable} onChange={(x) => setHead({ ...head, eventEndType: x.target.value })}>{Object.entries(lk.endTypes).map(([k, t]: any) => <option key={k} value={k}>{k} – {t}</option>)}</select></label>
        </div>}
      </section>
      <section className="card stack">
        <h2>Codes</h2>
        {w.suggestions.length > 0 && editable && <div className="subtle card stack-sm"><strong>From the procedure picklist</strong>
          {w.suggestions.map((s: any, n: number) => <div key={n} className="row">{s.label}:
            {s.diagnosis && <button className="btn btn-link" onClick={() => add({ code: s.diagnosis, type: 'A', description: s.diagnosisDescription ?? s.diagnosis })}>{s.diagnosis}</button>}
            {s.codes.map((c: any) => <button key={c.code} className="btn btn-link" onClick={() => add({ code: c.code, type: 'O', description: c.description ?? c.code })}>{c.code}</button>)}</div>)}</div>}
        {items.length === 0 ? <p className="muted">No codes yet.</p> :
          <table className="table"><thead><tr><th>Type</th><th>Code</th><th>Description (free text from the record)</th><th>Date</th><th></th></tr></thead>
            <tbody>{items.map((i, n) => <tr key={n}>
              <td><select aria-label={`Type of code ${n + 1}`} value={i.diagnosisType} disabled={!editable} onChange={(x) => set(n, { diagnosisType: x.target.value })}>{lk && Object.entries(lk.diagnosisTypes).map(([k, t]: any) => <option key={k} value={k}>{k} – {t}</option>)}</select></td>
              <td><input aria-label={`Code ${n + 1}`} value={i.code} size={9} disabled={!editable} onChange={(x) => set(n, { code: x.target.value.toUpperCase() })} /></td>
              <td><input aria-label={`Description ${n + 1}`} value={i.description} maxLength={50} disabled={!editable} onChange={(x) => set(n, { description: x.target.value })} />{i.tableDescription && <div className="small muted">{i.tableDescription}</div>}</td>
              <td>{(i.diagnosisType === 'O' || i.diagnosisType === 'E') && <input aria-label={`Date ${n + 1}`} type="date" value={i.date ?? ''} disabled={!editable} onChange={(x) => set(n, { date: x.target.value })} />}</td>
              <td>{editable && <button className="btn btn-link" onClick={() => setItems(items.filter((_, k) => k !== n))}>Remove</button>}</td></tr>)}</tbody></table>}
        {editable && <div className="stack-sm">
          <div className="row"><input aria-label="Find a code" value={q} onChange={(x) => setQ(x.target.value)} onKeyDown={(x) => { if (x.key === 'Enter') search(); }} placeholder="Code or words, e.g. K63.5 or polyp colon" />
            <select aria-label="Code types" value={types} onChange={(x) => setTypes(x.target.value)}><option value="">All</option><option value="ABV">Diagnoses</option><option value="O">Procedures</option><option value="E">External causes</option></select>
            <button className="btn btn-light" onClick={search}>Find</button></div>
          {hits.length > 0 && <ul className="stack-sm">{hits.map((h) => <li key={h.code + h.type}><button className="btn btn-link" onClick={() => add(h)}>{h.code}</button> {h.description}{h.neverAssign && <span className="small error"> (never assign)</span>}</li>)}</ul>}
        </div>}
        <label>Coder note (not sent)<textarea rows={2} value={head.coderNote} disabled={!editable} onChange={(x) => setHead({ ...head, coderNote: x.target.value })} /></label>
        <h3>Edit checks</h3><Checks checks={checks} />
        <Msg error={error} ok={ok} />
        {editable && <div className="row"><button className="btn btn-light" onClick={() => save(false)}>Save</button><button className="btn btn-primary" onClick={() => save(true)}>Check and mark coded</button></div>}
        {e.codedBy && <p className="small muted">Coded by {e.codedBy} {nzDateTime(e.codedAt)}.</p>}
      </section>
      {editable && <section className="card stack">
        <h2>Reporting</h2>
        {e.status === 'NotReportable' ? <button className="btn btn-light" onClick={() => reportable(true)}>Report this event after all</button> : e.status !== 'Submitted' &&
          <div className="row"><input aria-label="Reason not reported" value={reason} onChange={(x) => setReason(x.target.value)} placeholder="Why TDS does not report it (e.g. the District reports this contracted event)" /><button className="btn btn-light" onClick={() => reportable(false)}>Not reported by TDS</button></div>}
        {e.sent && <div className="row"><input aria-label="Health NZ error" value={hnz} onChange={(x) => setHnz(x.target.value)} placeholder="Error Health NZ reported for this event" /><button className="btn btn-light" onClick={recordError}>Record Health NZ error</button></div>}
      </section>}
    </div>
  );
}

// ------------------------------------------------------------------ NMDS
function Nmds({ canSubmit }: { canSubmit: boolean }) {
  const [to, setTo] = useState(todayIso());
  const [pre, setPre] = useState<any>(null);
  const [files, setFiles] = useState<any[]>([]);
  const [error, setError] = useState<string | null>(null); const [ok, setOk] = useState<string | null>(null);
  const load = useCallback(async () => {
    setFiles(await api('/api/coding/nmds/files'));
    if (canSubmit) setPre(await api(`/api/coding/nmds/preview?to=${to}`));
  }, [to, canSubmit]);
  useEffect(() => { load().catch((e) => setError(errText(e))); }, [load]);
  const create = async () => { setError(null); setOk(null); try { const f = await api('/api/coding/nmds/files', { body: { periodTo: to } }); setOk(`${f.fileName} created with ${f.records} event(s). Download it and send it to Health NZ, then mark it sent.`); await load(); } catch (e) { setError(errText(e)); } };
  const mark = async (id: string, status: string) => { setError(null); try { await api(`/api/coding/nmds/files/${id}/mark`, { body: { status, notes: null } }); await load(); } catch (e) { setError(errText(e)); } };
  return (
    <div className="stack-lg">
      {canSubmit && <section className="card stack"><h2>New load file</h2>
        <p className="muted">The private hospitals load file (CSV) Health NZ asks for. Events coded and discharged up to the date below go in it; events Health NZ returned are included again once corrected. Report within the agreed time after discharge (NMDS requirement: 21 days).</p>
        <label className="inline">Discharged up to <input type="date" value={to} max={todayIso()} onChange={(e) => setTo(e.target.value)} /></label>
        {pre && <><p>{pre.ready} event(s) ready{pre.resubmissions > 0 && <> ({pre.resubmissions} corrected resubmission(s))</>}{pre.fileName && <> – file name <strong>{pre.fileName}</strong></>}.</p>
          {pre.problems.length > 0 && <div className="warn-box"><strong>Not included until fixed:</strong><ul>{pre.problems.map((p: string, i: number) => <li key={i}>{p}</li>)}</ul></div>}</>}
        <Msg error={error} ok={ok} />
        <div><button className="btn btn-primary" disabled={!pre?.ready} onClick={create}>Create the file</button></div>
      </section>}
      <section className="card stack"><h2>Submission log</h2>
        {files.length === 0 ? <p className="muted">No files yet.</p> :
          <table className="table"><thead><tr><th>File</th><th>Events</th><th>Created</th><th>Status</th><th></th></tr></thead>
            <tbody>{files.map((f) => <tr key={f.id}><td>{f.fileName}{f.isResubmission && <div className="small">includes resubmissions</div>}</td><td>{f.records}</td><td>{nzDateTime(f.createdAt)} {f.createdBy}</td>
              <td>{f.status}{f.sentAt && <div className="small">sent {nzDateTime(f.sentAt)} {f.sentBy}</div>}</td>
              <td>{canSubmit && <div className="row"><ExportButton label="Download" path={`/api/coding/nmds/files/${f.id}/download`} body={{}} />
                {f.status === 'Created' && <button className="btn btn-light" onClick={() => mark(f.id, 'Sent')}>Mark sent</button>}
                {f.status === 'Sent' && <><button className="btn btn-light" onClick={() => mark(f.id, 'Accepted')}>Accepted</button><button className="btn btn-light" onClick={() => mark(f.id, 'Errors')}>Returned with errors</button></>}</div>}</td></tr>)}</tbody></table>}
        <p className="small muted">When Health NZ returns errors, open each event from the coding queue and use "Record Health NZ error"; once corrected and marked coded it goes in the next file with the same record id.</p>
      </section>
    </div>
  );
}

// ------------------------------------------------------------------ NBRS
const EMPTY_ENTRY = { id: null, patientId: '', waitlistEntryId: null, localId: '', bookingSource: '', referralDate: '', firstAssessmentDate: '', cpacDate: '', cpacScore: '', cpacSystem: '', purchaserCode: '35',
  healthSpecialty: '', clinicalCode: '', clinicalCodeType: 'O', domicileCode: '', professionalGroup: 'MC', responsibilityCode: '', assessorCode: '', assessorGroup: 'MC', stagedFlag: '1' };

function NbrsTab({ canCode, canSubmit, canManage }: { canCode: boolean; canSubmit: boolean; canManage: boolean }) {
  const [entries, setEntries] = useState<any[]>([]);
  const [wait, setWait] = useState<any[]>([]);
  const [batches, setBatches] = useState<any[]>([]);
  const [lk, setLk] = useState<any>(null);
  const [edit, setEdit] = useState<any>(null);
  const [ev, setEv] = useState<any>(null);
  const [clin, setClin] = useState<any[]>([]);
  const [error, setError] = useState<string | null>(null); const [ok, setOk] = useState<string | null>(null);
  const load = useCallback(async () => {
    setEntries(await api('/api/coding/nbrs/entries')); setBatches(await api('/api/coding/nbrs/batches')); setWait(await api('/api/coding/nbrs/waitlist'));
    setClin(await api('/api/coding/clinicians'));
  }, []);
  useEffect(() => { load().catch((e) => setError(errText(e))); api('/api/coding/settings').then(setLk).catch(() => undefined); }, [load]);
  const draft = async (wid: string) => { setError(null); try { const d = await api(`/api/coding/nbrs/draft/${wid}`); setEdit({ ...EMPTY_ENTRY, ...Object.fromEntries(Object.entries(d).map(([k, v]) => [k, v ?? ''])) }); } catch (e) { setError(errText(e)); } };
  const saveEntry = async () => {
    setError(null);
    const nul = (v: any) => v === '' ? null : v;
    try { await api('/api/coding/nbrs/entries', { body: Object.fromEntries(Object.entries(edit).map(([k, v]) => [k, nul(v)])) }); setEdit(null); setOk('Entry saved.'); await load(); } catch (e) { setError(errText(e)); }
  };
  const startEvent = async (entry: any, kind: string) => {
    const d = await api(`/api/coding/nbrs/entries/${entry.id}/defaults`).catch(() => ({}));
    setEv({ entryId: entry.id, kind, statusDate: todayIso(), certaintyDate: '', bookingMadeDate: d.bookingMadeDate ?? '', bookedForDate: d.bookedForDate ?? '', deferredBy: '', exitDate: d.bookedForDate ?? '', exitCategory: kind === 'treated' ? '11' : '', clientSystemId: d.clientSystemId ?? '' });
  };
  const saveEvent = async () => {
    setError(null);
    const nul = (v: any) => v === '' ? null : v;
    try { await api(`/api/coding/nbrs/entries/${ev.entryId}/events`, { body: Object.fromEntries(Object.entries(ev).filter(([k]) => k !== 'entryId').map(([k, v]) => [k, nul(v)])) }); setEv(null); setOk('Event added – it goes in the next batch.'); await load(); } catch (e) { setError(errText(e)); }
  };
  const removeEvent = async (id: string) => { try { await api(`/api/coding/nbrs/events/${id}`, { method: 'DELETE' }); await load(); } catch (e) { setError(errText(e)); } };
  const batch = async () => { setError(null); setOk(null); try { const b = await api('/api/coding/nbrs/batches', { body: {} }); setOk(`${b.fileName} (${b.environment}) created with ${b.records} record(s).`); await load(); } catch (e) { setError(errText(e)); } };
  const ack = async (f: File | undefined) => { if (!f) return; setError(null); try { const a = await api('/api/coding/nbrs/acknowledgement', { body: { text: await fileToText(f) } }); setOk(`${a.fileName}: ${a.accepted} accepted, ${a.rejected} rejected, ${a.warnings} warning(s).`); await load(); } catch (e) { setError(errText(e)); } };
  const saveReg = async (c: any, group: string, number: string) => { try { await api(`/api/coding/clinicians/${c.id}`, { method: 'PUT', body: { professionalGroup: group, registrationNumber: number } }); setOk(`Saved ${c.name}.`); await load(); } catch (e) { setError(errText(e)); } };
  const F = (k: string, t: string, type = 'text') => <label key={k}>{t}<input type={type} value={edit[k] ?? ''} onChange={(x) => setEdit({ ...edit, [k]: type === 'text' ? x.target.value.toUpperCase() : x.target.value })} /></label>;
  return (
    <div className="stack-lg">
      <p className="muted">National Booking Reporting System (file specification 4.5, file version V04.0) for Health NZ funded electives outsourced to TDS. Usually the District reports NBRS for its own bookings – use this only where the contract makes TDS responsible.</p>
      <Msg error={error} ok={ok} />
      {canCode && wait.length > 0 && <section className="card stack"><h2>Health NZ funded waitlist entries not yet in NBRS</h2>
        <table className="table"><thead><tr><th>Added</th><th>Patient</th><th>Procedure</th><th>Status</th><th></th></tr></thead>
          <tbody>{wait.map((w) => <tr key={w.id}><td>{formatDate(w.addedOn)}</td><td>{w.patient}</td><td>{w.procedure}</td><td>{w.status}</td><td><button className="btn btn-light" onClick={() => draft(w.id)}>Create NBRS entry</button></td></tr>)}</tbody></table></section>}
      {edit && <Modal title={`NBRS entry ${edit.localId ?? ''}`} onClose={() => setEdit(null)}>
        <div className="form-grid">
          {F('localId', 'Local booking id (max 14)')}{F('bookingSource', 'Booking source code')}{F('referralDate', 'Date of referral', 'date')}{F('firstAssessmentDate', 'First assessment date', 'date')}
          {F('cpacDate', 'CPAC assessment date', 'date')}{F('cpacScore', 'CPAC score')}{F('cpacSystem', 'CPAC scoring system id')}{F('purchaserCode', 'Purchaser code')}
          {F('healthSpecialty', 'Health specialty code')}{F('clinicalCode', 'Planned procedure code')}{F('clinicalCodeType', 'Clinical code type')}{F('domicileCode', 'Domicile code')}
          {F('professionalGroup', 'Professional group')}{F('responsibilityCode', 'Clinical responsibility code')}{F('assessorCode', 'Assessor code')}{F('assessorGroup', 'Assessor group')}
          {F('stagedFlag', 'Staged/planned flag (1–4)')}
        </div>
        <div className="row"><button className="btn btn-primary" onClick={saveEntry}>Save entry</button><button className="btn btn-light" onClick={() => setEdit(null)}>Cancel</button></div></Modal>}
      {ev && <Modal title={lk?.nbrsKinds?.[ev.kind] ?? ev.kind} onClose={() => setEv(null)}>
        <div className="form-grid">
          {!['treated', 'exit', 'delete', 'erase', 'change'].includes(ev.kind) && <label>Booking status date<input type="date" value={ev.statusDate} onChange={(x) => setEv({ ...ev, statusDate: x.target.value })} /></label>}
          {ev.kind === 'certainty' && <label>Date certainty given<input type="date" value={ev.certaintyDate} onChange={(x) => setEv({ ...ev, certaintyDate: x.target.value })} /></label>}
          {['book', 'rebook'].includes(ev.kind) && <><label>Date booking made<input type="date" value={ev.bookingMadeDate} onChange={(x) => setEv({ ...ev, bookingMadeDate: x.target.value })} /></label>
            <label>Date booked for treatment<input type="date" value={ev.bookedForDate} onChange={(x) => setEv({ ...ev, bookedForDate: x.target.value })} /></label></>}
          {ev.kind === 'defer' && <label>Deferred by code<input value={ev.deferredBy} onChange={(x) => setEv({ ...ev, deferredBy: x.target.value })} /></label>}
          {['treated', 'exit'].includes(ev.kind) && <><label>Date of exit category<input type="date" value={ev.exitDate} onChange={(x) => setEv({ ...ev, exitDate: x.target.value })} /></label>
            {ev.kind === 'exit' && <label>Exit category<select value={ev.exitCategory} onChange={(x) => setEv({ ...ev, exitCategory: x.target.value })}><option value="">Choose…</option>{lk && Object.entries(lk.exitCategories).map(([k, t]: any) => <option key={k} value={k}>{k} – {t}</option>)}</select></label>}
            <label>Client system identifier (NMDS record id)<input value={ev.clientSystemId} onChange={(x) => setEv({ ...ev, clientSystemId: x.target.value })} /></label></>}
        </div>
        <div className="row"><button className="btn btn-primary" onClick={saveEvent}>Check and add</button><button className="btn btn-light" onClick={() => setEv(null)}>Cancel</button></div></Modal>}
      <section className="card stack"><h2>Entries</h2>
        {entries.length === 0 ? <p className="muted">No NBRS entries.</p> : entries.map((n) => <div key={n.id} className="subtle card stack-sm">
          <div className="row"><strong>{n.localId}</strong> {n.patient} · {n.healthSpecialty} · {n.clinicalCode}
            {canCode && <button className="btn btn-link" onClick={() => setEdit({ ...EMPTY_ENTRY, ...Object.fromEntries(Object.entries(n).filter(([k]) => k in EMPTY_ENTRY).map(([k, v]) => [k, v ?? ''])), id: n.id })}>Edit</button>}</div>
          {n.events.length > 0 && <table className="table"><thead><tr><th>Action</th><th>Status</th><th>Date</th><th>Result</th><th></th></tr></thead>
            <tbody>{n.events.map((e: any) => <tr key={e.id}><td>{e.action}</td><td>{e.statusCode}{e.exitCategory && ` / exit ${e.exitCategory}`}</td><td>{e.statusDate ? formatDate(e.statusDate) : e.exitDate ? formatDate(e.exitDate) : ''}</td>
              <td>{e.result}{e.messages && <div className="small">{e.messages}</div>}</td><td>{canCode && e.result === 'Pending' && <button className="btn btn-link" onClick={() => removeEvent(e.id)}>Remove</button>}</td></tr>)}</tbody></table>}
          {canCode && <div className="row">{Object.entries(lk?.nbrsKinds ?? {}).map(([k, t]: any) => <button key={k} className="btn btn-light btn-small" onClick={() => startEvent(n, k)}>{t}</button>)}</div>}
        </div>)}
      </section>
      <section className="card stack"><h2>Batches</h2>
        {canSubmit && <div className="row"><button className="btn btn-primary" onClick={batch}>Create batch from waiting events</button>
          <label className="btn btn-light">Load acknowledgement (.NBK)<input type="file" accept=".nbk,.NBK,.txt,.csv" hidden onChange={(e) => ack(e.target.files?.[0])} /></label></div>}
        {batches.length === 0 ? <p className="muted">No batches yet.</p> :
          <table className="table"><thead><tr><th>File</th><th>Environment</th><th>Records</th><th>Created</th><th>Result</th><th></th></tr></thead>
            <tbody>{batches.map((b) => <tr key={b.id}><td>{b.fileName}</td><td>{b.environment}</td><td>{b.records}</td><td>{nzDateTime(b.createdAt)}</td>
              <td>{b.acknowledgedAt ? `${b.accepted} accepted, ${b.rejected} rejected, ${b.warnings} warning(s)` : b.status}</td>
              <td>{canSubmit && <ExportButton label="Download" path={`/api/coding/nbrs/batches/${b.id}/download`} body={{}} />}</td></tr>)}</tbody></table>}
      </section>
      <section className="card stack"><h2>Clinician registrations</h2>
        <p className="muted">Medical Council numbers used as NBRS clinical responsibility and assessor codes.</p>
        <table className="table"><thead><tr><th>Clinician</th><th>Group</th><th>Number</th><th></th></tr></thead>
          <tbody>{clin.map((c) => <RegRow key={c.id} c={c} canManage={canManage} save={saveReg} />)}</tbody></table>
      </section>
    </div>
  );
}

function RegRow({ c, canManage, save }: { c: any; canManage: boolean; save: (c: any, g: string, n: string) => void }) {
  const [g, setG] = useState(c.group ?? 'MC'); const [n, setN] = useState(c.number ?? '');
  return <tr><td>{c.name}</td><td><input aria-label={`Group for ${c.name}`} size={3} value={g} disabled={!canManage} onChange={(e) => setG(e.target.value.toUpperCase())} /></td>
    <td><input aria-label={`Registration number for ${c.name}`} size={10} value={n} disabled={!canManage} onChange={(e) => setN(e.target.value)} /></td>
    <td>{canManage && <button className="btn btn-link" onClick={() => save(c, g, n)}>Save</button>}</td></tr>;
}

// ------------------------------------------------------------------ code tables and picklist
function Tables({ canManage }: { canManage: boolean }) {
  const [maps, setMaps] = useState<any[]>([]);
  const [m, setM] = useState({ id: null as string | null, term: '', label: '', procedureCodes: '', diagnosisCode: '' });
  const [q, setQ] = useState(''); const [hits, setHits] = useState<any[]>([]);
  const [error, setError] = useState<string | null>(null); const [ok, setOk] = useState<string | null>(null);
  const load = useCallback(async () => setMaps(await api('/api/coding/picklist')), []);
  useEffect(() => { load().catch((e) => setError(errText(e))); }, [load]);
  const importTable = async (f: File | undefined) => {
    if (!f) return; setError(null); setOk(null);
    try { const r = await api('/api/coding/codes/import', { body: { text: await fileToText(f) } }); setOk(`${r.added} added, ${r.updated} updated.${r.problems.length ? ' ' + r.problems.join(' ') : ''}`); } catch (e) { setError(errText(e)); }
  };
  const save = async () => { setError(null); try { await api('/api/coding/picklist', { body: { ...m, diagnosisCode: m.diagnosisCode || null } }); setM({ id: null, term: '', label: '', procedureCodes: '', diagnosisCode: '' }); setOk('Picklist entry saved.'); await load(); } catch (e) { setError(errText(e)); } };
  const search = async () => setHits(await api<any[]>(`/api/coding/codes?q=${encodeURIComponent(q)}`).catch(() => []));
  return (
    <div className="stack-lg">
      <Msg error={error} ok={ok} />
      {canManage && <section className="card stack"><h2>Clinical code table</h2>
        <p className="muted">Import the ICD-10-AM/ACHI code table supplied by Health NZ (CSV with a header row: clinical_code_system, clinical_code, clinical_code_type, clinical_code_description, and optional sex_flag, age_from, age_to, never_assigned_diagnosis_flag, end_date). The classification is licensed and is not included in BrightPAS. Importing again updates existing codes.</p>
        <label className="btn btn-light">Choose code table file<input type="file" accept=".csv,.txt" hidden onChange={(e) => importTable(e.target.files?.[0])} /></label>
      </section>}
      <section className="card stack"><h2>Look up a code</h2>
        <div className="row"><input aria-label="Look up a code" value={q} onChange={(e) => setQ(e.target.value)} onKeyDown={(e) => { if (e.key === 'Enter') search(); }} placeholder="Code or words" /><button className="btn btn-light" onClick={search}>Find</button></div>
        {hits.length > 0 && <table className="table"><tbody>{hits.map((h) => <tr key={h.code + h.type}><td>{h.code}</td><td>{h.type}</td><td>{h.description}</td></tr>)}</tbody></table>}
      </section>
      <section className="card stack"><h2>Procedure picklist</h2>
        <p className="muted">Surgeon-friendly terms (the theatre procedure list code or name) mapped to ACHI codes; the workbench offers them for the booked procedure. The coder still confirms everything against the clinical record.</p>
        <table className="table"><thead><tr><th>Term</th><th>Label</th><th>Procedure codes</th><th>Usual diagnosis</th><th></th></tr></thead>
          <tbody>{maps.map((x) => <tr key={x.id}><td>{x.term}</td><td>{x.label}</td><td>{x.procedureCodes}</td><td>{x.diagnosisCode}</td>
            <td>{canManage && <button className="btn btn-link" onClick={() => setM({ id: x.id, term: x.term, label: x.label, procedureCodes: x.procedureCodes, diagnosisCode: x.diagnosisCode ?? '' })}>Edit</button>}</td></tr>)}</tbody></table>
        {canManage && <EditInPopup editing={!!m.id} title={`Edit ${m.term}`} onClose={() => setM({ id: null, term: '', label: '', procedureCodes: '', diagnosisCode: '' })} className="form-grid">
          <label>Term<input value={m.term} onChange={(e) => setM({ ...m, term: e.target.value })} /></label>
          <label>Label<input value={m.label} onChange={(e) => setM({ ...m, label: e.target.value })} /></label>
          <label>ACHI procedure codes (space separated)<input value={m.procedureCodes} onChange={(e) => setM({ ...m, procedureCodes: e.target.value })} /></label>
          <label>Usual principal diagnosis (optional)<input value={m.diagnosisCode} onChange={(e) => setM({ ...m, diagnosisCode: e.target.value.toUpperCase() })} /></label>
          <div><button className="btn btn-primary" onClick={save}>{m.id ? 'Save changes' : 'Add to picklist'}</button></div>
        </EditInPopup>}
      </section>
    </div>
  );
}

// ------------------------------------------------------------------ advice library
const SOURCES: [string, string][] = [['IhacpaRule', 'IHACPA coding rule'], ['IhacpaFaq', 'IHACPA FAQ'], ['NzConvention', 'NZ convention'], ['NmdsRequirement', 'NMDS requirement'], ['Local', 'TDS local']];
function Advice({ canManage }: { canManage: boolean }) {
  const blank = { id: null as string | null, reference: '', title: '', source: 'NzConvention', edition: 'Twelfth Edition', appliesToNz: true, reviewedOn: '', codePrefixes: '', summary: '', link: '', isActive: true };
  const [rows, setRows] = useState<any[]>([]);
  const [a, setA] = useState<any>(null);
  const [error, setError] = useState<string | null>(null);
  const load = useCallback(async () => setRows(await api('/api/coding/advice')), []);
  useEffect(() => { load().catch((e) => setError(errText(e))); }, [load]);
  const save = async () => { setError(null); try { await api('/api/coding/advice', { body: { ...a, reviewedOn: a.reviewedOn || null, link: a.link || null } }); setA(null); await load(); } catch (e) { setError(errText(e)); } };
  return (
    <section className="card stack"><h2>Coding advice library</h2>
      <p className="muted">New Zealand codes in ICD-10-AM/ACHI/ACS Twelfth Edition. IHACPA advice published for Thirteenth Edition applies in NZ only after Health NZ has reviewed it – mark each item. Advice that applies is shown on the workbench when one of its codes is used. Write summaries in your own words and link to the published text.</p>
      <Msg error={error} ok={null} />
      <table className="table"><thead><tr><th>Reference</th><th>Title</th><th>Source</th><th>Applies in NZ</th><th>Codes</th><th></th></tr></thead>
        <tbody>{rows.map((r) => <tr key={r.id} className={r.isActive ? '' : 'muted'}><td>{r.reference}</td><td>{r.title}<div className="small">{r.summary}</div>{r.link && <a className="small" href={r.link} target="_blank" rel="noreferrer">Published text</a>}</td>
          <td>{SOURCES.find(([k]) => k === r.source)?.[1] ?? r.source}<div className="small">{r.edition}</div></td><td>{r.appliesToNz ? 'Yes' : 'No – not reviewed for NZ'}{r.reviewedOn && <div className="small">reviewed {formatDate(r.reviewedOn)}</div>}</td>
          <td>{r.codePrefixes}</td><td>{canManage && <button className="btn btn-link" onClick={() => setA({ ...blank, ...r, reviewedOn: r.reviewedOn ?? '', link: r.link ?? '' })}>Edit</button>}</td></tr>)}</tbody></table>
      {canManage && !a && <div><button className="btn btn-light" onClick={() => setA(blank)}>Add advice</button></div>}
      {a && <Modal title={a.id ? `Edit ${a.reference}` : 'Add advice'} onClose={() => setA(null)}>
        <div className="form-grid">
          <label>Reference<input value={a.reference} onChange={(e) => setA({ ...a, reference: e.target.value })} placeholder="Q3725 or ACS 0031" /></label>
          <label>Title<input value={a.title} onChange={(e) => setA({ ...a, title: e.target.value })} /></label>
          <label>Source<select value={a.source} onChange={(e) => setA({ ...a, source: e.target.value })}>{SOURCES.map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
          <label>Edition<input value={a.edition} onChange={(e) => setA({ ...a, edition: e.target.value })} /></label>
          <label>Reviewed by Health NZ on<input type="date" value={a.reviewedOn} onChange={(e) => setA({ ...a, reviewedOn: e.target.value })} /></label>
          <label>Code prefixes (space separated)<input value={a.codePrefixes} onChange={(e) => setA({ ...a, codePrefixes: e.target.value })} /></label>
          <label>Link to the published text<input value={a.link} onChange={(e) => setA({ ...a, link: e.target.value })} placeholder="https://" /></label>
        </div>
        <label>Summary in your own words<textarea rows={3} value={a.summary} onChange={(e) => setA({ ...a, summary: e.target.value })} /></label>
        <label className="check"><input type="checkbox" checked={a.appliesToNz} onChange={(e) => setA({ ...a, appliesToNz: e.target.checked })} /> Applies to NZ Twelfth Edition coding</label>
        <label className="check"><input type="checkbox" checked={a.isActive} onChange={(e) => setA({ ...a, isActive: e.target.checked })} /> In use</label>
        <div className="row"><button className="btn btn-primary" onClick={save}>Save</button><button className="btn btn-light" onClick={() => setA(null)}>Cancel</button></div>
      </Modal>}
    </section>
  );
}

// ------------------------------------------------------------------ settings
function Settings({ canManage }: { canManage: boolean }) {
  const [d, setD] = useState<any>(null);
  const [s, setS] = useState<any>(null);
  const [error, setError] = useState<string | null>(null); const [ok, setOk] = useState<string | null>(null);
  useEffect(() => { api('/api/coding/settings').then((x) => { setD(x); setS(x.settings); }).catch((e) => setError(errText(e))); }, []);
  const save = async () => { setError(null); setOk(null); try { await api('/api/coding/settings', { method: 'PUT', body: s }); setOk('Settings saved.'); } catch (e) { setError(errText(e)); } };
  if (!s) return error ? <p className="error">{error}</p> : <p>Loading…</p>;
  const T = (k: string, t: string, max?: number) => <label key={k}>{t}<input value={s[k] ?? ''} maxLength={max} disabled={!canManage} onChange={(e) => setS({ ...s, [k]: e.target.value.toUpperCase() })} /></label>;
  const P = (k: string, t: string) => <label key={k}>{t}<select value={s[k] ?? ''} disabled={!canManage} onChange={(e) => setS({ ...s, [k]: e.target.value })}>{Object.entries(d.purchasers).map(([c, n]: any) => <option key={c} value={c}>{c} – {n}</option>)}</select></label>;
  return (
    <section className="card stack"><h2>Settings</h2>
      <h3>Classification</h3>
      <div className="form-grid">
        <label>Clinical coding system id<input value={s.codingSystem} maxLength={2} disabled={!canManage} onChange={(e) => setS({ ...s, codingSystem: e.target.value })} /></label>
        <label>Edition in use<input value={s.editionName} disabled={!canManage} onChange={(e) => setS({ ...s, editionName: e.target.value })} /></label>
      </div>
      <p className="small muted">NZ uses ICD-10-AM/ACHI/ACS Twelfth Edition (system 16) from 1 July 2023. When Health NZ moves to a new edition, import its code table and change these two fields – no rebuild.</p>
      <h3>Health NZ identifiers</h3>
      <div className="form-grid">{T('healthAgencyCode', 'Health agency code', 4)}{T('facilityCode', 'Health agency facility (HAF) code', 4)}{T('fileAcronym', 'File acronym (3 letters)', 3)}{T('nbrsAgencyCode', 'NBRS agency code', 4)}
        <label>NBRS environment<select value={s.nbrsEnvironment} disabled={!canManage} onChange={(e) => setS({ ...s, nbrsEnvironment: e.target.value })}><option value="CMPL">CMPL – compliance testing</option><option value="PROD">PROD – production</option></select></label></div>
      <p className="small muted">Next NBRS batch numbers: compliance {s.nextNbrsBatchCmpl}, production {s.nextNbrsBatchProd} (gap-free, kept separately).</p>
      <h3>Purchaser codes by payment type</h3>
      <div className="form-grid">{P('purchaserSelfFunded', 'Self-funded')}{P('purchaserInsurer', 'Health insurer')}{P('purchaserAcc', 'ACC')}{P('purchaserHealthNz', 'Health NZ (outsourced)')}{P('purchaserNoCharge', 'No charge')}</div>
      <label className="check"><input type="checkbox" checked={s.tdsReportsContractedEvents} disabled={!canManage} onChange={(e) => setS({ ...s, tdsReportsContractedEvents: e.target.checked })} /> TDS reports Health NZ contracted events to the NMDS (otherwise the District does – NZ convention on ACS 0029; the contract says which)</label>
      <label className="inline">Report within<input type="number" min={1} max={90} value={s.reportWithinDays} disabled={!canManage} onChange={(e) => setS({ ...s, reportWithinDays: Number(e.target.value) })} /> days of discharge</label>
      <Msg error={error} ok={ok} />
      {canManage && <div><button className="btn btn-primary" onClick={save}>Save settings</button></div>}
    </section>
  );
}
