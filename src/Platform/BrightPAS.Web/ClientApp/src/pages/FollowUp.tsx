import { Fragment, useCallback, useEffect, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { addDays, api, ApiError, errText, formatDate, nzDateTime, Perm, todayIso } from '../api';
import ExportButton from '../components/ExportButton';

interface Question { id: string; text: string; type: 'yesno' | 'scale' | 'choice' | 'text'; choices: string[] | null; patientForm: boolean; wound: boolean }
interface Meta { questions: Question[]; escalations: { value: string; label: string }[]; classes: { value: string; label: string }[]; canRecord: boolean; canPlans: boolean; canSsi: boolean; canReport: boolean }
interface Row { id: string; patientId: string; patient: string; nhi: string | null; phone: string | null; bookingId: string; procedure: string; operationDate: string; kind: string; method: string; label: string;
  dueOn: string; status: string; attempts: number; maxAttempts: number; assignedTo: string | null; concern: boolean; concernText: string | null; suspectedSsi: boolean; escalation: string; escalationOpen: boolean;
  doneBy: string | null; doneAt: string | null; linkSentAt: string | null; answeredAt: string | null }

const errs = (e: unknown) => (e instanceof ApiError && e.body?.errors ? e.body.errors as string[] : [errText(e)]);
const STATUS: Record<string, string> = { Due: 'To do', Done: 'Done', NoAnswer: 'No answer – try again', Unreachable: 'Unreachable', Cancelled: 'Cancelled' };
const METHOD: Record<string, string> = { Phone: 'Phone', Clinic: 'Clinic', Surgeon: 'Surgeon', PatientForm: 'Text / email check-in' };
const SOURCES = [['Patient', 'Patient'], ['Gp', 'GP'], ['Surgeon', 'Surgeon'], ['Readmission', 'Readmission'], ['Microbiology', 'Microbiology'], ['Staff', 'TDS staff']];

function useMeta() {
  const [m, setM] = useState<Meta | null>(null);
  useEffect(() => { api<Meta>('/api/followup/meta').then(setM).catch(() => setM(null)); }, []);
  return m;
}

const VIEWS: [string, string][] = [['today', 'Due today'], ['overdue', 'Overdue'], ['concerns', 'Concerns'], ['escalations', 'Escalations'], ['upcoming', 'Next 2 weeks'], ['calendar', 'Calendar'], ['unreachable', 'Unreachable'], ['done', 'Done']];

/** Follow-up worklists and calendar. */
export default function FollowUp() {
  const meta = useMeta();
  const [params, setParams] = useSearchParams();
  const view = params.get('view') ?? 'today';
  const week = params.get('week') ?? todayIso();
  const [rows, setRows] = useState<Row[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => {
    setRows(null);
    const q = view === 'calendar' ? `view=calendar&from=${week}&to=${addDays(week, 6)}` : `view=${view}`;
    api<Row[]>(`/api/followup/worklist?${q}`).then(setRows).catch((e) => setError(errText(e)));
  }, [view, week]);
  return (
    <div className="stack-lg">
      <div className="page-head row-between">
        <div><h1>Follow-up</h1><p className="muted">Calls and check-ins after surgery, concerns, escalations and wound infection (SSI) surveillance.</p></div>
        <div className="row">
          <Link className="btn btn-light" to="/followup/ssi">SSI surveillance</Link>
          {meta?.canReport && <Link className="btn btn-light" to="/followup/report">Report</Link>}
          {meta?.canPlans && <Link className="btn btn-light" to="/followup/plans">Plans &amp; settings</Link>}
        </div>
      </div>
      <div className="tabs" role="tablist">{VIEWS.map(([k, t]) => <button key={k} role="tab" aria-selected={view === k} className={view === k ? 'on' : ''} onClick={() => setParams({ view: k })}>{t}</button>)}</div>
      {error && <p className="error" role="alert">{error}</p>}
      {view === 'calendar' && (
        <div className="row"><button className="btn btn-light" onClick={() => setParams({ view, week: addDays(week, -7) })}>‹ Previous week</button>
          <strong>{formatDate(week)} – {formatDate(addDays(week, 6))}</strong>
          <button className="btn btn-light" onClick={() => setParams({ view, week: addDays(week, 7) })}>Next week ›</button></div>
      )}
      {!rows ? <p>Loading…</p> : view === 'calendar' ? <Calendar rows={rows} from={week} /> : (
        <section className="card">
          {rows.length === 0 ? <p className="muted">Nothing here.</p> : (
            <table className="results">
              <thead><tr><th>Patient</th><th>Contact</th><th>Due</th><th>Operation</th><th>Status</th></tr></thead>
              <tbody>{rows.map((r) => (
                <tr key={r.id} className={r.concern && !r.doneAt ? 'urgent-row' : ''}>
                  <td><Link to={`/followup/${r.id}`}><strong>{r.patient}</strong></Link><div className="small muted">{r.nhi ?? ''}{r.phone ? ` · ${r.phone}` : ''}</div></td>
                  <td>{r.label}<div className="small muted">{METHOD[r.method]}{r.assignedTo ? ` · ${r.assignedTo}` : ''}</div>{r.concern && <div className="small alert-text">⚠ {r.concernText}</div>}</td>
                  <td>{formatDate(r.dueOn)}</td>
                  <td>{r.procedure}<div className="small muted">{formatDate(r.operationDate)}</div></td>
                  <td>{STATUS[r.status] ?? r.status}{r.attempts > 0 && r.status !== 'Done' ? ` (${r.attempts}/${r.maxAttempts})` : ''}{r.escalationOpen && <span className="pill red">escalated</span>}{r.suspectedSsi && <span className="pill amber">wound</span>}</td>
                </tr>))}</tbody>
            </table>
          )}
        </section>
      )}
    </div>
  );
}

function Calendar({ rows, from }: { rows: Row[]; from: string }) {
  const days = Array.from({ length: 7 }, (_, i) => addDays(from, i));
  return (
    <div className="board cal7">
      {days.map((d) => {
        const mine = rows.filter((r) => r.dueOn === d);
        return (
          <section key={d} className="board-col">
            <h2>{new Date(d + 'T12:00:00').toLocaleDateString('en-NZ', { weekday: 'short', day: 'numeric', month: 'short' })} <span className="count neutral">{mine.length}</span></h2>
            {mine.map((r) => (
              <Link key={r.id} to={`/followup/${r.id}`} className={`board-card${r.concern ? ' has-alert' : ''}`}>
                <strong>{r.patient}</strong><span className="small">{r.label}</span><span className="small muted">{STATUS[r.status]}</span>
              </Link>
            ))}
          </section>
        );
      })}
    </div>
  );
}

function Answer({ q, value, onChange, disabled }: { q: Question; value: string; onChange: (v: string) => void; disabled?: boolean }) {
  if (q.type === 'yesno') return (
    <div className="row" role="radiogroup" aria-label={q.text}>{['yes', 'no'].map((v) => <label key={v} className="check"><input type="radio" name={q.id} disabled={disabled} checked={value === v} onChange={() => onChange(v)} /> {v === 'yes' ? 'Yes' : 'No'}</label>)}</div>
  );
  if (q.type === 'scale') return <select aria-label={q.text} disabled={disabled} value={value} onChange={(e) => onChange(e.target.value)}><option value="">–</option>{Array.from({ length: 11 }, (_, i) => <option key={i} value={i}>{i}</option>)}</select>;
  if (q.type === 'choice') return <select aria-label={q.text} disabled={disabled} value={value} onChange={(e) => onChange(e.target.value)}><option value="">–</option>{q.choices!.map((c) => <option key={c}>{c}</option>)}</select>;
  return <textarea aria-label={q.text} disabled={disabled} value={value} onChange={(e) => onChange(e.target.value)} />;
}

/** One follow-up contact: the call script, outcome, escalation, SSI and history. */
export function FollowUpContact() {
  const { id } = useParams();
  const navigate = useNavigate();
  const meta = useMeta();
  const [d, setD] = useState<any>(null);
  const [a, setA] = useState<Record<string, string>>({});
  const [esc, setEsc] = useState('None');
  const [escNote, setEscNote] = useState('');
  const [notes, setNotes] = useState('');
  const [result, setResult] = useState<{ warnings: string[]; suspectedSsi: boolean; signs: string } | null>(null);
  const [ssi, setSsi] = useState<{ onsetOn: string; signs: string; source: string; antibiotics: boolean; returnToTheatre: boolean; readmitted: boolean; microbiology: string } | null>(null);
  const [closeNote, setCloseNote] = useState('');
  const [due, setDue] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const load = useCallback(async () => {
    try { const x = await api<any>(`/api/followup/contacts/${id}`); setD(x); setA(x.contact.answers ?? {}); setDue(x.contact.dueOn); } catch (e) { setErrors(errs(e)); }
  }, [id]);
  useEffect(() => { setResult(null); setSsi(null); void load(); }, [load]);
  if (!d || !meta) return <p>{errors.length ? errors.join(' ') : 'Loading…'}</p>;
  const c = d.contact;
  const open = c.status === 'Due' || c.status === 'NoAnswer';
  const run = async (f: () => Promise<unknown>, ok: string) => { setErrors([]); setMsg(null); try { await f(); setMsg(ok); await load(); return true; } catch (e) { setErrors(errs(e)); return false; } };
  const record = async (outcome: 'Done' | 'NoAnswer') => {
    setErrors([]); setMsg(null);
    try {
      const r = await api<any>(`/api/followup/contacts/${id}/record`, { body: { outcome, how: c.method === 'PatientForm' ? 'Phone' : c.method, answers: outcome === 'Done' ? a : null, escalation: esc, escalationNote: escNote || null, notes: notes || null } });
      setResult(r);
      if (r.suspectedSsi && !d.ssiCaseId) setSsi({ onsetOn: todayIso(), signs: r.signs, source: 'Patient', antibiotics: a.antibiotics === 'yes', returnToTheatre: false, readmitted: false, microbiology: '' });
      setMsg(outcome === 'Done' ? 'Saved.' : 'Recorded.'); await load();
    } catch (e) { setErrors(errs(e)); }
  };
  const nextDue = d.history.find((h: Row) => h.id !== c.id && (h.status === 'Due' || h.status === 'NoAnswer'));
  return (
    <div className="stack-lg">
      <div className="page-head row-between">
        <div>
          <h1>{c.label}</h1>
          <p className="muted"><Link to={`/patients/${c.patientId}`}><strong>{c.patient}</strong></Link> {c.nhi ?? ''} · {c.phone ? <a href={`tel:${c.phone.replace(/\s/g, '')}`}>{c.phone}</a> : 'no phone number'} · {c.procedure} on {formatDate(c.operationDate)} · due {formatDate(c.dueOn)}</p>
        </div>
        <div className="row"><Link className="btn btn-light" to="/followup">Worklist</Link>{nextDue && <Link className="btn btn-light" to={`/followup/${nextDue.id}`}>Next contact</Link>}</div>
      </div>
      {errors.length > 0 && <div className="error" role="alert"><ul>{errors.map((e) => <li key={e}>{e}</li>)}</ul></div>}
      {msg && <p className="notice" role="status">{msg}</p>}
      {c.concern && <div className="warn-box" role="alert"><strong>Concern:</strong> {c.concernText}{c.escalationClosedAt ? ` – dealt with (${c.escalationOutcome})` : ''}</div>}
      {result && result.warnings.length > 0 && <div className="warn-box" role="alert"><strong>Needs attention:</strong><ul>{result.warnings.map((w) => <li key={w}>{w}</li>)}</ul></div>}
      <section className="card stack">
        <div className="row-between"><h2>{open ? 'Call script' : 'Answers'}</h2><span className="pill">{STATUS[c.status]}{c.attempts > 0 ? ` · ${c.attempts} attempt(s)` : ''}</span></div>
        {c.answeredAt && <p className="muted small">Answered by the patient {nzDateTime(c.answeredAt)}.</p>}
        {c.doneAt && !c.answeredAt && <p className="muted small">{c.doneHow} by {c.doneBy}, {nzDateTime(c.doneAt)}.</p>}
        <div className="script">
          {meta.questions.map((q) => (
            <div key={q.id} className={`script-q${q.wound ? ' wound' : ''}`}>
              <span>{q.text}</span>
              <Answer q={q} value={a[q.id] ?? ''} disabled={!open || !meta.canRecord} onChange={(v) => setA({ ...a, [q.id]: v })} />
            </div>
          ))}
        </div>
        {open && meta.canRecord && <>
          <div className="form-grid">
            <label>Escalation<select value={esc} onChange={(e) => setEsc(e.target.value)}>{meta.escalations.map((x) => <option key={x.value} value={x.value}>{x.label}</option>)}</select></label>
            {esc !== 'None' && <label>What was arranged<input value={escNote} onChange={(e) => setEscNote(e.target.value)} /></label>}
          </div>
          <label>Notes<textarea value={notes} onChange={(e) => setNotes(e.target.value)} /></label>
          <div className="row wrap">
            <button className="btn btn-primary btn-big" onClick={() => void record('Done')}>Save the call</button>
            <button className="btn btn-light" onClick={() => void record('NoAnswer')}>No answer</button>
            <button className="btn btn-light" onClick={() => void run(() => api(`/api/followup/contacts/${id}/send-link`, { body: {} }), 'Check-in text sent.')}>Send check-in text instead</button>
          </div>
          <details><summary>Move, assign or cancel</summary>
            <div className="row wrap">
              <label>Due<input type="date" value={due} onChange={(e) => setDue(e.target.value)} /></label>
              <button className="btn btn-light" onClick={() => void run(() => api(`/api/followup/contacts/${id}/reschedule`, { body: { dueOn: due, reason: null } }), 'Moved.')}>Move</button>
              <button className="btn btn-link" onClick={() => { const to = window.prompt('Assign to (name or team)', c.assignedTo ?? ''); if (to !== null) void run(() => api(`/api/followup/contacts/${id}/assign`, { body: { to } }), 'Assigned.'); }}>Assign</button>
              <button className="btn btn-link danger" onClick={() => { const r = window.prompt('Why is this contact not needed?'); if (r) void run(() => api(`/api/followup/contacts/${id}/cancel`, { body: { reason: r } }), 'Cancelled.'); }}>Not needed</button>
            </div>
          </details>
        </>}
        {!open && c.escalation !== 'None' && <p><strong>Escalation:</strong> {c.escalationLabel} – {c.escalationNote}{c.escalationClosedAt ? ` · closed ${nzDateTime(c.escalationClosedAt)} by ${c.escalationClosedBy}: ${c.escalationOutcome}` : ''}</p>}
        {c.notes && <p className="prewrap"><strong>Notes:</strong> {c.notes}</p>}
      </section>
      {!open && meta.canRecord && ((c.escalation !== 'None' && c.escalation !== 'Advice') || c.concern) && !c.escalationClosedAt && (
        <section className="card stack">
          <h2>Close the {c.concern && c.escalation === 'None' ? 'concern' : 'escalation'}</h2>
          <label>How did it end?<input value={closeNote} onChange={(e) => setCloseNote(e.target.value)} placeholder="e.g. GP started antibiotics; wound settling" /></label>
          <div className="row"><button className="btn btn-primary" onClick={() => void run(() => api(`/api/followup/contacts/${id}/close-escalation`, { body: { reason: closeNote } }), 'Closed.')}>Close</button>
            {!c.incidentId && <button className="btn btn-light" onClick={() => { const desc = window.prompt('Describe the event for the incident report'); if (desc) void run(() => api(`/api/followup/contacts/${id}/incident`, { body: { description: desc, harm: 'Minor' } }), 'Incident raised in Quality.'); }}>Raise an incident</button>}</div>
        </section>
      )}
      {(c.suspectedSsi || ssi) && !d.ssiCaseId && meta.canRecord && (
        <section className="card stack">
          <h2>Suspected wound infection (SSI)</h2>
          <p className="hint">The answers meet the signs of a surgical site infection. Open a surveillance record – the infection prevention lead confirms the classification.</p>
          {!ssi ? <div><button className="btn btn-primary" onClick={() => setSsi({ onsetOn: todayIso(), signs: c.concernText ?? '', source: 'Patient', antibiotics: a.antibiotics === 'yes', returnToTheatre: false, readmitted: false, microbiology: '' })}>Open an SSI record</button></div> : <>
            <div className="form-grid">
              <label>Date it started<input type="date" value={ssi.onsetOn} onChange={(e) => setSsi({ ...ssi, onsetOn: e.target.value })} /></label>
              <label>Information from<select value={ssi.source} onChange={(e) => setSsi({ ...ssi, source: e.target.value })}>{SOURCES.map(([v, t]) => <option key={v} value={v}>{t}</option>)}</select></label>
              <label>Swab / microbiology<input value={ssi.microbiology} onChange={(e) => setSsi({ ...ssi, microbiology: e.target.value })} /></label>
            </div>
            <label>Signs<input value={ssi.signs} onChange={(e) => setSsi({ ...ssi, signs: e.target.value })} /></label>
            <div className="row wrap">
              <label className="check"><input type="checkbox" checked={ssi.antibiotics} onChange={(e) => setSsi({ ...ssi, antibiotics: e.target.checked })} /> Antibiotics</label>
              <label className="check"><input type="checkbox" checked={ssi.returnToTheatre} onChange={(e) => setSsi({ ...ssi, returnToTheatre: e.target.checked })} /> Return to theatre</label>
              <label className="check"><input type="checkbox" checked={ssi.readmitted} onChange={(e) => setSsi({ ...ssi, readmitted: e.target.checked })} /> Readmitted</label>
            </div>
            <div><button className="btn btn-primary" onClick={async () => { setErrors([]); try { const r = await api<any>('/api/followup/ssi', { body: { bookingId: c.bookingId, contactId: c.id, ...ssi, microbiology: ssi.microbiology || null } }); navigate(`/followup/ssi/${r.id}`); } catch (e) { setErrors(errs(e)); } }}>Save the SSI record</button></div>
          </>}
        </section>
      )}
      {d.ssiCaseId && <p><Link to={`/followup/ssi/${d.ssiCaseId}`}>Open the SSI surveillance record for this operation</Link></p>}
      <section className="card">
        <h2>This operation's follow-up</h2>
        <table className="results compact"><thead><tr><th>Contact</th><th>Due</th><th>Status</th><th>Concern</th></tr></thead><tbody>{d.history.map((h: Row) => (
          <tr key={h.id} className={h.id === c.id ? 'current' : ''}><td><Link to={`/followup/${h.id}`}>{h.label}</Link></td><td>{formatDate(h.dueOn)}</td><td>{STATUS[h.status]}</td><td className="small">{h.concern ? `⚠ ${h.concernText}` : ''}</td></tr>))}</tbody></table>
      </section>
    </div>
  );
}

/** SSI surveillance list. */
export function SsiList() {
  const meta = useMeta();
  const [rows, setRows] = useState<any[] | null>(null);
  const [open, setOpen] = useState(false);
  const [from, setFrom] = useState(addDays(todayIso(), -365));
  const [to, setTo] = useState(todayIso());
  useEffect(() => { api<any[]>(`/api/followup/ssi?open=${open}`).then(setRows).catch(() => setRows([])); }, [open]);
  return (
    <div className="stack-lg">
      <div className="page-head row-between"><div><h1>SSI surveillance</h1><p className="muted">Surgical site infections within 30 days (90 with an implant), classified against the CDC/HQSC definitions.</p></div>
        <Link className="btn btn-light" to="/followup">Follow-up</Link></div>
      <label className="check"><input type="checkbox" checked={open} onChange={(e) => setOpen(e.target.checked)} /> Only those waiting for confirmation</label>
      <section className="card">
        {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">No SSI records.</p> : (
          <table className="results"><thead><tr><th>Record</th><th>Patient</th><th>Operation</th><th>Onset</th><th>Classification</th><th>Surgeon</th></tr></thead>
            <tbody>{rows.map((r) => <tr key={r.id}><td><Link to={`/followup/ssi/${r.id}`}>{r.number}</Link></td><td>{r.patient}</td><td>{r.procedure}<div className="small muted">{formatDate(r.operationDate)}</div></td>
              <td>day {r.onsetDay}</td><td><span className={`pill ${r.class === 'Suspected' ? 'amber' : r.class === 'NotSsi' ? 'muted' : 'red'}`}>{r.classLabel}</span></td><td>{r.surgeon ?? '—'}</td></tr>)}</tbody></table>
        )}
      </section>
      {meta?.canReport && (
        <section className="card stack"><h2>Export for HQSC / private hospital SSI reporting</h2>
          <p className="hint">One row per operation under surveillance (no names). The file is encrypted.</p>
          <div className="row wrap"><label>From<input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></label><label>To<input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></label>
            <ExportButton label="Download the SSI file" path={`/api/followup/ssi/export?from=${from}&to=${to}`} body={{}} /></div>
        </section>
      )}
    </div>
  );
}

export function SsiCase() {
  const { id } = useParams();
  const meta = useMeta();
  const [c, setC] = useState<any>(null);
  const [cls, setCls] = useState('');
  const [note, setNote] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const load = useCallback(() => api<any>(`/api/followup/ssi/${id}`).then((x) => { setC(x); setCls(x.class === 'Suspected' ? '' : x.class); }).catch((e) => setErrors(errs(e))), [id]);
  useEffect(() => { void load(); }, [load]);
  if (!c || !meta) return <p>{errors.length ? errors.join(' ') : 'Loading…'}</p>;
  const run = async (f: () => Promise<unknown>, ok: string) => { setErrors([]); setMsg(null); try { await f(); setMsg(ok); await load(); } catch (e) { setErrors(errs(e)); } };
  return (
    <div className="stack-lg">
      <div className="page-head row-between"><div><h1>{c.number} – {c.classLabel}</h1><p className="muted"><Link to={`/patients/${c.patientId}`}>{c.patient}</Link> · {c.procedure} on {formatDate(c.operationDate)}{c.surgeon ? ` · ${c.surgeon}` : ''}</p></div>
        <Link className="btn btn-light" to="/followup/ssi">SSI list</Link></div>
      {errors.length > 0 && <div className="error" role="alert"><ul>{errors.map((e) => <li key={e}>{e}</li>)}</ul></div>}
      {msg && <p className="notice" role="status">{msg}</p>}
      <div className="grid-2">
        <section className="card stack">
          <h2>What happened</h2>
          <dl className="facts">
            <dt>Started</dt><dd>{formatDate(c.onsetOn)} (day {c.onsetDay} of {c.surveillanceDays})</dd>
            <dt>Signs</dt><dd>{c.signs}</dd><dt>Information from</dt><dd>{c.source}</dd>
            <dt>Antibiotics</dt><dd>{c.antibiotics ? 'Yes' : 'No'}</dd><dt>Return to theatre</dt><dd>{c.returnToTheatre ? 'Yes' : 'No'}</dd><dt>Readmitted</dt><dd>{c.readmitted ? 'Yes' : 'No'}</dd>
            <dt>Microbiology</dt><dd>{c.microbiology ?? '—'}</dd><dt>Recorded</dt><dd>{c.createdBy}, {nzDateTime(c.createdAt)}</dd>
            {c.confirmedBy && <><dt>Classified</dt><dd>{c.classLabel} by {c.confirmedBy}, {nzDateTime(c.confirmedAt)}{c.confirmNote ? ` – ${c.confirmNote}` : ''}</dd></>}
          </dl>
        </section>
        <section className="card stack">
          <h2>Operation risk factors</h2>
          <dl className="facts">
            <dt>Wound class</dt><dd>{c.woundClass ?? 'not recorded'}</dd><dt>ASA</dt><dd>{c.asa ?? 'not recorded'}</dd><dt>Implant</dt><dd>{c.implant ? 'Yes' : 'No'}</dd>
            {Object.entries(c.risk ?? {}).map(([k, v]) => <Fragment key={k}><dt>{k.split('_').join(' ')}</dt><dd>{String(v)}</dd></Fragment>)}
          </dl>
        </section>
      </div>
      {meta.canSsi && (
        <section className="card stack">
          <h2>Classification (infection prevention lead)</h2>
          <p className="hint">Superficial incisional: skin and tissue under it. Deep incisional: fascia and muscle. Organ/space: deeper than the incision. Note which CDC/HQSC criteria are met.</p>
          <div className="form-grid">
            <label>Classification<select value={cls} onChange={(e) => setCls(e.target.value)}><option value="">– choose –</option>{meta.classes.filter((x) => x.value !== 'Suspected').map((x) => <option key={x.value} value={x.value}>{x.label}</option>)}</select></label>
            <label>Evidence / criteria met<input value={note} onChange={(e) => setNote(e.target.value)} /></label>
          </div>
          <div className="row"><button className="btn btn-primary" disabled={!cls} onClick={() => void run(() => api(`/api/followup/ssi/${id}/confirm`, { body: { class: cls, note: note || null } }), 'Classification saved.')}>Save classification</button>
            {c.class !== 'Suspected' && c.class !== 'NotSsi' && !c.incidentId && <button className="btn btn-light" onClick={() => void run(() => api(`/api/followup/ssi/${id}/incident`, { body: {} }), 'Incident raised in Quality.')}>Raise a Quality incident</button>}
            {c.incidentId && <Link className="btn btn-light" to={`/quality/incidents/${c.incidentId}`}>Open the incident</Link>}</div>
        </section>
      )}
    </div>
  );
}

/** Follow-up plans and settings. */
export function FollowUpPlans() {
  const meta = useMeta();
  const [params, setParams] = useSearchParams();
  const tab = params.get('tab') ?? 'plans';
  const [plans, setPlans] = useState<any[]>([]);
  const [edit, setEdit] = useState<any>(null);
  const [settings, setSettings] = useState<any>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const load = useCallback(async () => { setPlans(await api<any[]>('/api/followup/plans')); setSettings(await api('/api/followup/settings')); }, []);
  useEffect(() => { void load(); }, [load]);
  const run = async (f: () => Promise<unknown>, ok: string) => { setErrors([]); setMsg(null); try { await f(); setMsg(ok); await load(); return true; } catch (e) { setErrors(errs(e)); return false; } };
  if (!meta) return <p>Loading…</p>;
  const can = meta.canPlans;
  const step = (i: number, p: any) => setEdit({ ...edit, steps: edit.steps.map((s: any, j: number) => (j === i ? { ...s, ...p } : s)) });
  return (
    <div className="stack-lg">
      <div className="page-head row-between"><div><h1>Follow-up plans</h1><p className="muted">Which contacts each patient gets after surgery. The first plan whose words match the procedure is used, otherwise the default plan.</p></div><Link className="btn btn-light" to="/followup">Follow-up</Link></div>
      <div className="tabs" role="tablist">{[['plans', 'Plans'], ['settings', 'Settings']].map(([k, t]) => <button key={k} role="tab" aria-selected={tab === k} className={tab === k ? 'on' : ''} onClick={() => setParams({ tab: k })}>{t}</button>)}</div>
      {errors.length > 0 && <div className="error" role="alert"><ul>{errors.map((e) => <li key={e}>{e}</li>)}</ul></div>}
      {msg && <p className="notice" role="status">{msg}</p>}
      {tab === 'settings' && settings && (
        <section className="card stack">
          <div className="form-grid">
            <label>Public web address for patient links (https://…)<input disabled={!can} value={settings.publicUrl ?? ''} onChange={(e) => setSettings({ ...settings, publicUrl: e.target.value })} /></label>
            <label>Duty nurse mobile (alert when a patient's answer needs a call)<input disabled={!can} value={settings.dutyMobile ?? ''} onChange={(e) => setSettings({ ...settings, dutyMobile: e.target.value })} /></label>
            <label>Check-in link works for (days)<input type="number" disabled={!can} value={settings.linkDays} onChange={(e) => setSettings({ ...settings, linkDays: Number(e.target.value) })} /></label>
            <label>Start follow-up for operations from<input type="date" disabled={!can} value={settings.startFrom ?? ''} onChange={(e) => setSettings({ ...settings, startFrom: e.target.value || null })} /></label>
          </div>
          <label className="check"><input type="checkbox" disabled={!can} checked={settings.sendCheckInLinks} onChange={(e) => setSettings({ ...settings, sendCheckInLinks: e.target.checked })} /> Send check-in texts/emails automatically</label>
          <label className="check"><input type="checkbox" disabled={!can} checked={settings.gpLetter} onChange={(e) => setSettings({ ...settings, gpLetter: e.target.checked })} /> Draft a letter to the GP asking them to report any wound infection (30 / 90 days)</label>
          {settings.lastScanAt && <p className="small muted">Last checked {nzDateTime(settings.lastScanAt)}.</p>}
          {can && <div><button className="btn btn-primary" onClick={() => void run(() => api('/api/followup/settings', { method: 'PUT', body: settings }), 'Settings saved.')}>Save settings</button></div>}
        </section>
      )}
      {tab === 'plans' && <>
        <section className="card">
          {plans.length === 0 ? <><p className="muted">No plans yet.</p>{can && <button className="btn btn-primary" onClick={() => void run(() => api('/api/followup/plans/examples', { body: {} }), 'Example plans added – agree them with the clinical team.')}>Start from the example plans</button>}</> : (
            <table className="results compact"><thead><tr><th>Plan</th><th>For</th><th>Contacts</th><th></th></tr></thead>
              <tbody>{plans.map((p) => <tr key={p.id}><td><strong>{p.name}</strong>{p.isDefault && <span className="pill blue">default</span>}{!p.isActive && <span className="pill muted">off</span>}</td><td className="small">{p.procedureContains ?? 'all other procedures'}</td>
                <td className="small">{p.steps.map((s: any) => s.label).join(' · ')}</td><td><button className="btn btn-link" onClick={() => setEdit(JSON.parse(JSON.stringify(p)))}>{can ? 'Edit' : 'View'}</button></td></tr>)}</tbody></table>
          )}
          {can && plans.length > 0 && <button className="btn btn-light btn-small" onClick={() => setEdit({ id: null, name: '', procedureContains: '', isDefault: false, isActive: true, maxAttempts: 3, sortOrder: 50, notes: '', steps: [{ kind: 'Call', method: 'Phone', days: 1, daysWithImplant: null, workingDays: true, onlyIfHighRisk: false, assignTo: '', label: 'Next working day phone call' }] })}>New plan</button>}
        </section>
        {edit && (
          <section className="card stack">
            <h2>{edit.id ? edit.name : 'New plan'}</h2>
            <div className="form-grid">
              <label>Name<input disabled={!can} value={edit.name} onChange={(e) => setEdit({ ...edit, name: e.target.value })} /></label>
              <label>For procedures containing (comma separated)<input disabled={!can || edit.isDefault} value={edit.procedureContains ?? ''} onChange={(e) => setEdit({ ...edit, procedureContains: e.target.value })} /></label>
              <label>Phone attempts before "unreachable"<input type="number" disabled={!can} value={edit.maxAttempts} onChange={(e) => setEdit({ ...edit, maxAttempts: Number(e.target.value) })} /></label>
              <label>Order (lower is checked first)<input type="number" disabled={!can} value={edit.sortOrder} onChange={(e) => setEdit({ ...edit, sortOrder: Number(e.target.value) })} /></label>
            </div>
            <div className="row wrap"><label className="check"><input type="checkbox" disabled={!can} checked={edit.isDefault} onChange={(e) => setEdit({ ...edit, isDefault: e.target.checked })} /> Default plan</label>
              <label className="check"><input type="checkbox" disabled={!can} checked={edit.isActive} onChange={(e) => setEdit({ ...edit, isActive: e.target.checked })} /> In use</label></div>
            <div className="table-scroll" tabIndex={0} role="region" aria-label="Contacts"><table className="results compact">
              <thead><tr><th>Label</th><th>Type</th><th>How</th><th>Days after</th><th>With implant</th><th>Working days</th><th>High risk only</th><th>Assign to</th>{can && <th></th>}</tr></thead>
              <tbody>{edit.steps.map((s: any, i: number) => (
                <tr key={i}>
                  <td><input aria-label="Label" disabled={!can} value={s.label} onChange={(e) => step(i, { label: e.target.value })} /></td>
                  <td><select aria-label="Type" disabled={!can} value={s.kind} onChange={(e) => step(i, { kind: e.target.value })}>{[['Call', 'Phone call'], ['WoundCheck', 'Wound check'], ['Review', 'Review'], ['SsiCheck', 'SSI check']].map(([v, t]) => <option key={v} value={v}>{t}</option>)}</select></td>
                  <td><select aria-label="How" disabled={!can} value={s.method} onChange={(e) => step(i, { method: e.target.value })}>{Object.entries(METHOD).map(([v, t]) => <option key={v} value={v}>{t}</option>)}</select></td>
                  <td><input aria-label="Days after" type="number" disabled={!can} value={s.days} onChange={(e) => step(i, { days: Number(e.target.value) })} /></td>
                  <td><input aria-label="Days with implant" type="number" disabled={!can} value={s.daysWithImplant ?? ''} onChange={(e) => step(i, { daysWithImplant: e.target.value === '' ? null : Number(e.target.value) })} /></td>
                  <td><input aria-label="Working days" type="checkbox" disabled={!can} checked={s.workingDays} onChange={(e) => step(i, { workingDays: e.target.checked })} /></td>
                  <td><input aria-label="High risk only" type="checkbox" disabled={!can} checked={s.onlyIfHighRisk} onChange={(e) => step(i, { onlyIfHighRisk: e.target.checked })} /></td>
                  <td><input aria-label="Assign to" disabled={!can} value={s.assignTo ?? ''} onChange={(e) => step(i, { assignTo: e.target.value })} /></td>
                  {can && <td><button className="btn btn-link danger" onClick={() => setEdit({ ...edit, steps: edit.steps.filter((_: any, j: number) => j !== i) })}>Remove</button></td>}
                </tr>))}</tbody></table></div>
            {can && <div className="row"><button className="btn btn-light btn-small" onClick={() => setEdit({ ...edit, steps: [...edit.steps, { kind: 'Call', method: 'Phone', days: 7, daysWithImplant: null, workingDays: false, onlyIfHighRisk: false, assignTo: '', label: '' }] })}>Another contact</button>
              <button className="btn btn-primary" onClick={async () => { if (await run(() => api(edit.id ? `/api/followup/plans/${edit.id}` : '/api/followup/plans', { method: edit.id ? 'PUT' : 'POST', body: edit }), 'Plan saved.')) setEdit(null); }}>Save plan</button></div>}
          </section>
        )}
      </>}
    </div>
  );
}

export function FollowUpReport() {
  const [r, setR] = useState<any>(null);
  const [from, setFrom] = useState(addDays(todayIso(), -90));
  const [to, setTo] = useState(todayIso());
  useEffect(() => { api(`/api/followup/report?from=${from}&to=${to}`).then(setR).catch(() => undefined); }, [from, to]);
  return (
    <div className="stack-lg">
      <div className="page-head row-between"><div><h1>Follow-up report</h1></div><Link className="btn btn-light" to="/followup">Follow-up</Link></div>
      <div className="row wrap"><label>From<input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></label><label>To<input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></label></div>
      {!r ? <p>Loading…</p> : <>
        <section className="card stack">
          <div className="stat-row">
            <div><span className="muted small">Completed</span><strong>{r.completionPercent}%</strong><span className="small">{r.done} of {r.contacts} due</span></div>
            <div><span className="muted small">Concerns</span><strong>{r.concerns}</strong></div>
            <div><span className="muted small">Escalations</span><strong>{r.escalations}</strong></div>
            <div><span className="muted small">Unplanned returns / ED</span><strong>{r.returns}</strong></div>
            <div><span className="muted small">SSIs confirmed</span><strong>{r.ssiConfirmed}</strong><span className="small">{r.ssi} recorded</span></div>
            <div><span className="muted small">Satisfaction</span><strong>{r.meanSatisfaction ?? '—'}</strong><span className="small">out of 10</span></div>
          </div>
          <p className="small muted">{r.operations} operation(s) discharged; {r.withFollowUp} with a follow-up plan; {r.unreachable} contact(s) unreachable. SSI rate charts with control limits are in Quality › Indicators.</p>
        </section>
        <section className="card"><h2>SSI by procedure and surgeon</h2>
          {r.rates.length === 0 ? <p className="muted">No operations under surveillance in this period.</p> : (
            <table className="results compact"><thead><tr><th></th><th>Name</th><th>Operations</th><th>SSIs</th><th>Rate</th></tr></thead>
              <tbody>{r.rates.map((x: any) => <tr key={x.group + x.name}><td className="muted small">{x.group}</td><td>{x.name}</td><td>{x.group === 'Procedure' ? x.operations : '—'}</td><td>{x.ssi}</td><td>{x.group === 'Procedure' ? `${x.percent}%` : '—'}</td></tr>)}</tbody></table>
          )}
        </section>
        <section className="card"><h2>Escalations</h2><ul>{Object.entries(r.escalationTypes).map(([k, v]) => <li key={k}>{k}: {String(v)}</li>)}</ul></section>
      </>}
    </div>
  );
}

/** Follow-up panel on the patient record. */
export function PatientFollowUp({ patientId }: { patientId: string }) {
  const [rows, setRows] = useState<Row[] | null>(null);
  useEffect(() => { api<Row[]>(`/api/followup/worklist?view=patient&patientId=${patientId}`).then(setRows).catch(() => setRows([])); }, [patientId]);
  if (!rows || rows.length === 0) return null;
  return (
    <section className="card">
      <h2>Follow-up</h2>
      <ul className="plain">{rows.slice(-8).map((r) => <li key={r.id}><Link to={`/followup/${r.id}`}>{r.label}</Link> · {formatDate(r.dueOn)} · {STATUS[r.status]}{r.concern ? ' ⚠' : ''}</li>)}</ul>
    </section>
  );
}

/** Patient-facing check-in opened from the text message: link + date of birth. */
export function PatientCheckIn() {
  const { token } = useParams();
  const [stage, setStage] = useState<'loading' | 'dob' | 'form' | 'done' | 'closed' | 'submitted'>('loading');
  const [dob, setDob] = useState('');
  const [name, setName] = useState('');
  const [qs, setQs] = useState<Question[]>([]);
  const [a, setA] = useState<Record<string, string>>({});
  const [concern, setConcern] = useState(false);
  const [errors, setErrors] = useState<string[]>([]);
  useEffect(() => { api<{ status: string }>(`/api/c/${token}`).then((r) => setStage(r.status === 'open' ? 'dob' : r.status === 'submitted' ? 'submitted' : 'closed')).catch(() => setStage('closed')); }, [token]);
  const fail = (e: unknown) => setErrors(errs(e));
  return (
    <div className="q-page">
      <header className="q-head"><strong>Tasman Day Surgery</strong> · How are you getting on?</header>
      <main className="q-body stack-lg">
        <p className="warn-box">If you are very unwell, bleeding heavily or have trouble breathing, call <strong>111</strong> now.</p>
        {stage === 'loading' && <p>Loading…</p>}
        {stage === 'closed' && <section className="card"><h1>This link is no longer active</h1><p>Please phone Tasman Day Surgery if you have any concerns.</p></section>}
        {stage === 'submitted' && <section className="card"><h1>Thank you</h1><p>We already have your answers.</p></section>}
        {stage === 'done' && <section className="card"><h1>Thank you{name ? `, ${name}` : ''}</h1>{concern ? <p><strong>A nurse will phone you today</strong> about your answers. If things get worse before then, phone us or call 111.</p> : <p>Your answers have been sent to our nurses. We hope your recovery continues to go well.</p>}</section>}
        {stage === 'dob' && (
          <form className="card stack" onSubmit={async (e) => { e.preventDefault(); setErrors([]); try { const r = await api<any>(`/api/c/${token}/open`, { body: { dateOfBirth: dob } }); setName(r.givenName); setQs(r.questions); setStage('form'); } catch (x) { fail(x); } }}>
            <h1>Kia ora</h1><p>To keep your information private, enter your date of birth.</p>
            <label>Date of birth<input type="date" value={dob} onChange={(e) => setDob(e.target.value)} required /></label>
            {errors.map((x) => <p key={x} className="error">{x}</p>)}
            <div><button className="btn btn-primary btn-big">Start</button></div>
          </form>
        )}
        {stage === 'form' && (
          <section className="card stack">
            <h1>Kia ora {name}</h1><p>A few quick questions about your recovery.</p>
            {errors.length > 0 && <div className="error" role="alert"><ul>{errors.map((x) => <li key={x}>{x}</li>)}</ul></div>}
            {qs.map((q) => <div key={q.id} className="script-q"><span>{q.text}</span><Answer q={q} value={a[q.id] ?? ''} onChange={(v) => setA({ ...a, [q.id]: v })} /></div>)}
            <div><button className="btn btn-primary btn-big" onClick={async () => { setErrors([]); try { const r = await api<any>(`/api/c/${token}/submit`, { body: { dateOfBirth: dob, answers: a } }); setConcern(r.concern); setStage('done'); } catch (x) { fail(x); window.scrollTo(0, 0); } }}>Send my answers</button></div>
          </section>
        )}
      </main>
    </div>
  );
}

export const followUpPerms = [Perm.FollowUpView, Perm.FollowUpRecord, Perm.FollowUpPlans, Perm.FollowUpSsi, Perm.FollowUpReport];
