import { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { api, ApiError, errText, formatDate, nzDateTime, Perm } from '../api';
import { useSession } from '../App';

interface Meta {
  outcomes: { value: string; label: string }[]; staffFacts: { fact: string; label: string }[]; ruleFacts: { fact: string; label: string }[];
  canAssess: boolean; canReview: boolean; canRules: boolean; canApprove: boolean;
}
const errs = (e: unknown) => (e instanceof ApiError && e.body?.errors ? e.body.errors as string[] : [errText(e)]);
export const outcomeClass = (o: string | null | undefined) =>
  o === 'Suitable' ? 'green' : o === 'SuitableWithPrecautions' ? 'blue' : o === 'MoreInformation' ? 'amber' : o === 'AnaesthetistReview' ? 'amber' : o === 'NotSuitable' ? 'red' : '';
const TRIGGERS: Record<string, string> = { Initial: 'First triage', NewAnswers: 'New answers', NewResult: 'New results', BookingChanged: 'Booking changed', Stale: 'Re-check due', Manual: 'Re-run' };

function useMeta() {
  const [meta, setMeta] = useState<Meta | null>(null);
  useEffect(() => { api<Meta>('/api/triage/meta').then(setMeta).catch(() => setMeta(null)); }, []);
  return meta;
}

const VIEWS: [string, string][] = [['open', 'To sign off'], ['review', 'Anaesthetist review'], ['moreinfo', 'More information'], ['worse', 'Got worse'], ['overdue', 'Overdue for surgery date'], ['signedoff', 'Signed off']];

/** Triage worklists. */
export default function Triage() {
  const { can } = useSession();
  const [params, setParams] = useSearchParams();
  const view = params.get('view') ?? 'open';
  const [rows, setRows] = useState<any[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => { setRows(null); api<any[]>(`/api/triage/worklist?view=${view}`).then(setRows).catch((e) => setError(errText(e))); }, [view]);
  return (
    <div className="stack-lg">
      <div className="page-head row-between">
        <div><h1>Triage</h1><p className="muted">Day-surgery suitability before booking and again when anything changes. BrightPAS suggests; a clinician decides.</p></div>
        <div className="row">
          <Link className="btn btn-light" to="/triage/report">Report</Link>
          {(can(Perm.TriageRules) || can(Perm.TriageView)) && <Link className="btn btn-light" to="/triage/rules">Rules</Link>}
        </div>
      </div>
      <div className="tabs" role="tablist">{VIEWS.map(([k, t]) => <button key={k} role="tab" aria-selected={view === k} className={view === k ? 'on' : ''} onClick={() => setParams({ view: k })}>{t}</button>)}</div>
      {error && <p className="error" role="alert">{error}</p>}
      <section className="card">
        {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">Nothing here.</p> : (
          <table className="results">
            <thead><tr><th>Patient</th><th>Procedure</th><th>Surgery</th><th>Suggested</th><th>Outcome</th><th>Why</th><th>Missing</th></tr></thead>
            <tbody>{rows.map((r) => (
              <tr key={r.id}>
                <td><Link to={`/triage/${r.id}`}><strong>{r.patient}</strong></Link><div className="muted small">{r.nhi ?? '—'}</div></td>
                <td>{r.procedure || '—'}</td>
                <td>{r.procedureDate ? formatDate(r.procedureDate) : '—'}{r.overdue && <span className="pill red">overdue</span>}</td>
                <td><span className={`pill ${outcomeClass(r.suggested)}`}>{label(r.suggested)}</span>{!r.rulesApproved && <div className="small muted">no approved rules</div>}</td>
                <td>{r.final ? <span className={`pill ${outcomeClass(r.final)}`}>{label(r.final)}</span> : '—'}</td>
                <td className="small">{TRIGGERS[r.trigger] ?? r.trigger}{r.worse && <span className="pill red">worse</span>}</td>
                <td className="small muted">{r.missing ? r.missing.split('\n').length + ' item(s)' : '—'}</td>
              </tr>))}</tbody>
          </table>
        )}
      </section>
    </div>
  );
}

const LABELS: Record<string, string> = { Suitable: 'Suitable', SuitableWithPrecautions: 'Suitable with precautions', MoreInformation: 'More information needed', AnaesthetistReview: 'Anaesthetist review', NotSuitable: 'Not suitable for day surgery at TDS' };
const label = (o: string) => LABELS[o] ?? o;

/** One patient's risk profile: scores, findings, missing information, precautions, findings form and sign-off. */
export function TriageAssessment() {
  const { id } = useParams();
  const navigate = useNavigate();
  const meta = useMeta();
  const [a, setA] = useState<any>(null);
  const [history, setHistory] = useState<any[]>([]);
  const [inputs, setInputs] = useState<Record<string, string>>({});
  const [reasons, setReasons] = useState<Record<string, string>>({});
  const [sign, setSign] = useState<{ outcome: string; asa: string; precautions: string; notes: string; overrideReason: string }>({ outcome: '', asa: '', precautions: '', notes: '', overrideReason: '' });
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const load = useCallback(async () => {
    try {
      const x = await api<any>(`/api/triage/assessments/${id}`);
      setA(x); setInputs(x.staff ?? {}); setReasons({});
      setSign({ outcome: x.suggested, asa: x.confirmedAsa ? String(x.confirmedAsa) : '', precautions: x.precautions ?? '', notes: '', overrideReason: '' });
      setHistory(await api<any[]>(`/api/triage/patients/${x.patientId}`));
    } catch (e) { setErrors(errs(e)); }
  }, [id]);
  useEffect(() => { void load(); }, [load]);
  if (!a || !meta) return <p>{errors.length ? errors.join(' ') : 'Loading…'}</p>;
  const open = a.status === 'Open';
  const saveInputs = async () => {
    setErrors([]); setMsg(null);
    const changed = Object.keys({ ...a.staff, ...inputs }).filter((k) => (inputs[k] ?? '') !== (a.staff?.[k] ?? '')).map((k) => ({ fact: k, value: inputs[k] ?? '', reason: reasons[k] || null }));
    if (changed.length === 0) { setMsg('No changes.'); return; }
    try { const r = await api<any>(`/api/triage/assessments/${id}/inputs`, { body: { inputs: changed } }); navigate(`/triage/${r.id}`); setMsg('Findings saved and triage re-run.'); }
    catch (e) { setErrors(errs(e)); }
  };
  const signOff = async () => {
    setErrors([]); setMsg(null);
    try {
      await api(`/api/triage/assessments/${id}/sign-off`, { body: { outcome: sign.outcome, confirmedAsa: sign.asa ? Number(sign.asa) : null, precautions: sign.precautions || null, notes: sign.notes || null, overrideReason: sign.overrideReason || null } });
      setMsg('Signed off.'); await load();
    } catch (e) { setErrors(errs(e)); }
  };
  const rerun = async () => { try { const r = await api<any>('/api/triage/run', { body: { patientId: a.patientId, bookingId: a.bookingId } }); navigate(`/triage/${r.id}`); } catch (e) { setErrors(errs(e)); } };
  return (
    <div className="stack-lg">
      <div className="page-head row-between">
        <div>
          <h1>Triage – <Link to={`/patients/${a.patientId}`}>patient record</Link></h1>
          <p className="muted">{a.procedure || 'No booking'}{a.anaesthetic && ` · ${a.anaesthetic}`}{a.procedureDate && ` · ${formatDate(a.procedureDate)}`} · {TRIGGERS[a.trigger]}{a.triggerDetail && ` (${a.triggerDetail})`} · {nzDateTime(a.createdAt)}
            {a.ruleSetVersion ? ` · rules v${a.ruleSetVersion}` : ''}</p>
        </div>
        <div className="row"><Link className="btn btn-light" to="/triage">Worklist</Link>{meta.canAssess && <button className="btn btn-light" onClick={() => void rerun()}>Run triage again</button>}</div>
      </div>
      {errors.length > 0 && <div className="error" role="alert"><ul>{errors.map((e) => <li key={e}>{e}</li>)}</ul></div>}
      {msg && <p className="notice" role="status">{msg}</p>}
      {!a.rulesApproved && <p className="warn-box">No clinically approved rule set – the scores are shown, but suitability must be judged by the clinician.</p>}
      {a.status === 'Superseded' && <p className="warn-box">This triage has been replaced by a newer one – see the history below.</p>}
      <section className="card stack">
        <div className="row-between">
          <div><span className="muted">Suggested</span><h2><span className={`pill big ${outcomeClass(a.suggested)}`}>{a.suggestedLabel}</span></h2></div>
          {a.final && <div><span className="muted">Signed off</span><h2><span className={`pill big ${outcomeClass(a.final)}`}>{a.finalLabel}</span></h2><p className="small muted">{a.signedOffBy} ({a.signedOffRole}) {nzDateTime(a.signedOffAt)}</p></div>}
        </div>
        {a.worse && <p className="error">Worse than the last triage.</p>}
        {a.changeSummary && a.previousId && <details><summary>What changed since the last triage</summary><pre className="prewrap">{a.changeSummary}</pre></details>}
        {a.precautions && <div><strong>Precautions</strong><ul>{a.precautions.split('\n').map((p: string) => <li key={p}>{p}</li>)}</ul></div>}
        {a.overrideReason && <p><strong>Reason for a different outcome:</strong> {a.overrideReason}</p>}
        {a.notes && <p className="prewrap"><strong>Notes:</strong> {a.notes}</p>}
      </section>
      <div className="grid-2">
        <section className="card stack">
          <h2>Scores</h2>
          <table className="results compact"><tbody>{a.scores.map((s: any) => <tr key={s.name}><th scope="row">{s.name}</th><td><strong>{s.value}</strong></td><td className="small muted">{s.detail}</td></tr>)}</tbody></table>
          <h2>Why</h2>
          {a.findings.length === 0 ? <p className="muted">No rule or medicine prompt applied.</p> : (
            <ul className="plain">{a.findings.map((f: any, i: number) => <li key={i}><span className={`pill ${outcomeClass(f.outcome)}`}>{f.label}</span> {f.text}{f.precaution && <div className="small muted">Precaution: {f.precaution}</div>}</li>)}</ul>
          )}
          {a.missing && <><h2>Missing information</h2><ul>{a.missing.split('\n').map((m: string) => <li key={m}>{m}</li>)}</ul></>}
        </section>
        <section className="card stack">
          <h2>Findings from staff</h2>
          <p className="hint">These override what the patient entered. Give a reason when you correct an answer. Saving re-runs triage.</p>
          <div className="form-grid">{meta.staffFacts.filter((f) => f.fact !== 'asa_confirmed' || meta.canReview).map((f) => (
            <label key={f.fact}>{f.label}<input value={inputs[f.fact] ?? ''} placeholder={a.facts?.[f.fact] ?? ''} disabled={!meta.canAssess} onChange={(e) => setInputs({ ...inputs, [f.fact]: e.target.value })} />
              {(inputs[f.fact] ?? '') !== (a.staff?.[f.fact] ?? '') && a.facts?.[f.fact] && <input className="small" placeholder="Reason for the correction" value={reasons[f.fact] ?? ''} onChange={(e) => setReasons({ ...reasons, [f.fact]: e.target.value })} />}
            </label>))}</div>
          {meta.canAssess && <div><button className="btn btn-light" onClick={() => void saveInputs()}>Save findings and re-run</button></div>}
        </section>
      </div>
      {open && meta.canAssess && (
        <section className="card stack">
          <h2>Sign off</h2>
          <div className="form-grid">
            <label>Outcome<select value={sign.outcome} onChange={(e) => setSign({ ...sign, outcome: e.target.value })}>{meta.outcomes.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}</select></label>
            {meta.canReview && <label>ASA grade (confirmed)<select value={sign.asa} onChange={(e) => setSign({ ...sign, asa: e.target.value })}><option value="">– not confirmed –</option>{[1, 2, 3, 4, 5].map((n) => <option key={n} value={n}>ASA {n}{n === a.suggestedAsa ? ' (suggested)' : ''}</option>)}</select></label>}
            {sign.outcome !== a.suggested && <label>Reason for a different outcome<input value={sign.overrideReason} onChange={(e) => setSign({ ...sign, overrideReason: e.target.value })} /></label>}
          </div>
          <label>Precautions (shown on the booking and theatre list)<textarea value={sign.precautions} onChange={(e) => setSign({ ...sign, precautions: e.target.value })} /></label>
          <label>Notes<textarea value={sign.notes} onChange={(e) => setSign({ ...sign, notes: e.target.value })} /></label>
          <div><button className="btn btn-primary" onClick={() => void signOff()}>Sign off</button></div>
        </section>
      )}
      <section className="card">
        <h2>History</h2>
        <table className="results compact"><thead><tr><th>When</th><th>Why</th><th>Procedure</th><th>Suggested</th><th>Signed off</th><th>Status</th></tr></thead>
          <tbody>{history.map((h) => <tr key={h.id}><td><Link to={`/triage/${h.id}`}>{nzDateTime(h.createdAt)}</Link></td><td>{TRIGGERS[h.trigger]}{h.worse && <span className="pill red">worse</span>}</td><td>{h.procedure}</td><td>{h.suggested}</td><td>{h.final ? `${h.final} – ${h.signedOffBy}` : '—'}</td><td>{h.status}</td></tr>)}</tbody></table>
      </section>
    </div>
  );
}

/** Triage panel on the patient record. */
export function PatientTriage({ patientId }: { patientId: string }) {
  const { can } = useSession();
  const navigate = useNavigate();
  const [rows, setRows] = useState<any[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => { api<any[]>(`/api/triage/patients/${patientId}`).then(setRows).catch(() => setRows([])); }, [patientId]);
  const run = async () => { try { const r = await api<any>('/api/triage/run', { body: { patientId } }); navigate(`/triage/${r.id}`); } catch (e) { setError(errText(e)); } };
  if (!rows) return null;
  const current = rows.find((r) => r.status === 'Open' || r.status === 'SignedOff');
  return (
    <section className="card">
      <div className="row-between"><h2>Triage</h2>{can(Perm.TriageAssess) && <button className="btn btn-light btn-small" onClick={() => void run()}>Run triage</button>}</div>
      {error && <p className="error">{error}</p>}
      {!current ? <p className="muted">Not triaged.</p> : (
        <p><Link to={`/triage/${current.id}`}>{current.procedure || 'Triage'}</Link>{current.procedureDate && ` · ${formatDate(current.procedureDate)}`} · suggested <strong>{current.suggested}</strong>
          {current.final ? <> · signed off <strong>{current.final}</strong> by {current.signedOffBy}</> : <span className="pill amber">waiting for sign-off</span>}
          {current.precautions && <span className="small muted"><br />Precautions: {current.precautions.split('\n').join('; ')}</span>}</p>
      )}
    </section>
  );
}

/** Triage outcome badge for a theatre booking. */
export function TriageBadge({ t }: { t: { assessmentId: string; status: string; outcome: string; label: string; precautions: string | null } | undefined }) {
  if (!t) return <div className="small muted">Triage: not done</div>;
  return (
    <div className="small"><Link to={`/triage/${t.assessmentId}`}><span className={`pill ${outcomeClass(t.outcome)}`}>Triage: {t.label}{t.status === 'Open' ? ' (to sign off)' : ''}</span></Link>
      {t.precautions && <div className="muted">{t.precautions.split('\n').join('; ')}</div>}</div>
  );
}

// ------------------------------------------------------------------ rules
const OPS: [string, string][] = [['AtLeast', 'is at least'], ['AtMost', 'is at most'], ['Above', 'is above'], ['Below', 'is below'], ['IsYes', 'is yes'], ['Equals', 'equals'], ['Missing', 'is missing']];

export function TriageRules() {
  const meta = useMeta();
  const [params, setParams] = useSearchParams();
  const tab = params.get('tab') ?? 'rules';
  const [sets, setSets] = useState<any[]>([]);
  const [sel, setSel] = useState<any>(null);
  const [settings, setSettings] = useState<any>(null);
  const [note, setNote] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const loadSets = useCallback(async () => { const s = await api<any[]>('/api/triage/rulesets'); setSets(s); return s; }, []);
  const open = async (id: string) => setSel(await api<any>(`/api/triage/rulesets/${id}`));
  useEffect(() => { void loadSets().then((s) => { const pick = s.find((x) => x.status === 'Draft') ?? s.find((x) => x.status === 'Approved'); if (pick) void open(pick.id); }); api('/api/triage/settings').then(setSettings).catch(() => undefined); }, [loadSets]);
  const run = async (f: () => Promise<unknown>, ok: string) => { setErrors([]); setMsg(null); try { await f(); setMsg(ok); return true; } catch (e) { setErrors(errs(e)); return false; } };
  if (!meta) return <p>Loading…</p>;
  const draft = sel?.status === 'Draft';
  const editable = draft && meta.canRules;
  const setRule = (i: number, p: any) => setSel({ ...sel, rules: sel.rules.map((r: any, j: number) => (j === i ? { ...r, ...p } : r)) });
  const setMed = (i: number, p: any) => setSel({ ...sel, medicines: sel.medicines.map((r: any, j: number) => (j === i ? { ...r, ...p } : r)) });
  const save = () => run(() => api(`/api/triage/rulesets/${sel.id}`, { method: 'PUT', body: { name: sel.name, notes: sel.notes, rules: sel.rules.map((r: any) => ({ ...r, operator: r.op })), medicines: sel.medicines } }), 'Draft saved.');
  return (
    <div className="stack-lg">
      <div className="page-head row-between"><div><h1>Triage rules</h1><p className="muted">Suitability rules and medicine prompts are versioned. Only an approved version is used for patients; changes start a new draft.</p></div><Link className="btn btn-light" to="/triage">Worklist</Link></div>
      <div className="tabs" role="tablist">{[['rules', 'Rule sets'], ['settings', 'Settings']].map(([k, t]) => <button key={k} role="tab" aria-selected={tab === k} className={tab === k ? 'on' : ''} onClick={() => setParams({ tab: k })}>{t}</button>)}</div>
      {errors.length > 0 && <div className="error" role="alert"><ul>{errors.map((e) => <li key={e}>{e}</li>)}</ul></div>}
      {msg && <p className="notice" role="status">{msg}</p>}
      {tab === 'settings' && settings && (
        <section className="card stack">
          <label className="check"><input type="checkbox" disabled={!meta.canRules} checked={settings.holdBookingUntilSignedOff} onChange={(e) => setSettings({ ...settings, holdBookingUntilSignedOff: e.target.checked })} /> Bookings can't be confirmed until triage is signed off as suitable</label>
          <div className="form-grid">
            <label>Only for procedures containing (comma separated; blank = all)<input disabled={!meta.canRules} value={settings.holdProcedures ?? ''} onChange={(e) => setSettings({ ...settings, holdProcedures: e.target.value })} /></label>
            <label>Re-triage when the sign-off is older than (days, on the day of surgery)<input type="number" disabled={!meta.canRules} value={settings.retriageAfterDays} onChange={(e) => setSettings({ ...settings, retriageAfterDays: Number(e.target.value) })} /></label>
            <label>Sign off at least this many days before surgery<input type="number" disabled={!meta.canRules} value={settings.signOffDaysBefore} onChange={(e) => setSettings({ ...settings, signOffDaysBefore: Number(e.target.value) })} /></label>
          </div>
          {meta.canRules && <div><button className="btn btn-primary" onClick={() => void run(() => api('/api/triage/settings', { method: 'PUT', body: settings }), 'Settings saved.')}>Save settings</button></div>}
        </section>
      )}
      {tab === 'rules' && <>
        <section className="card stack">
          {sets.length === 0 ? <><p className="muted">No rule sets yet.</p>{meta.canRules && <div><button className="btn btn-primary" onClick={() => void run(async () => { await api('/api/triage/rulesets/example', { body: {} }); const s = await loadSets(); await open(s[0].id); }, 'Example rules added as a draft – review every limit before approval.')}>Start from the example rules</button></div>}</> : (
            <table className="results compact"><thead><tr><th>Version</th><th>Name</th><th>Status</th><th>Approved</th><th></th></tr></thead>
              <tbody>{sets.map((s) => <tr key={s.id}><td>v{s.version}</td><td>{s.name}</td><td><span className={`pill ${s.status === 'Approved' ? 'green' : s.status === 'Draft' ? 'amber' : 'muted'}`}>{s.status}</span></td>
                <td className="small">{s.approvedBy ? `${s.approvedBy} (${s.approvedRole}) ${nzDateTime(s.approvedAt)} – ${s.approvalNote}` : '—'}</td>
                <td><div className="row"><button className="btn btn-link" onClick={() => void open(s.id)}>Open</button>{meta.canRules && s.status !== 'Draft' && <button className="btn btn-link" onClick={() => void run(async () => { const r = await api<any>(`/api/triage/rulesets/${s.id}/copy`, { body: {} }); await loadSets(); await open(r.id); }, 'New draft made.')}>New draft from this</button>}</div></td></tr>)}</tbody></table>
          )}
        </section>
        {sel && (
          <section className="card stack">
            <h2>v{sel.version} – {sel.status}{sel.isExample && ' (example rules)'}</h2>
            {sel.isExample && sel.status === 'Draft' && <p className="warn-box">Example rules: change every limit to TDS's agreed criteria before approving.</p>}
            <div className="form-grid">
              <label>Name<input disabled={!editable} value={sel.name} onChange={(e) => setSel({ ...sel, name: e.target.value })} /></label>
              <label>Notes<input disabled={!editable} value={sel.notes ?? ''} onChange={(e) => setSel({ ...sel, notes: e.target.value })} /></label>
            </div>
            <h3>Suitability rules</h3>
            <div className="table-scroll" tabIndex={0} role="region" aria-label="Suitability rules"><table className="results compact">
              <thead><tr><th>Code</th><th>Rule</th><th>Checks</th><th>Test</th><th>Limit</th><th>Procedures</th><th>Anaesthetic</th><th>Outcome</th><th>Precaution</th><th>On</th>{editable && <th></th>}</tr></thead>
              <tbody>{sel.rules.map((r: any, i: number) => (
                <tr key={i}>
                  <td><input aria-label="Code" disabled={!editable} value={r.code} onChange={(e) => setRule(i, { code: e.target.value.toUpperCase() })} /></td>
                  <td><input aria-label="Rule name" disabled={!editable} value={r.name} onChange={(e) => setRule(i, { name: e.target.value })} /></td>
                  <td><select aria-label="Checks" disabled={!editable} value={r.fact} onChange={(e) => setRule(i, { fact: e.target.value })}>{meta.ruleFacts.map((f) => <option key={f.fact} value={f.fact}>{f.label}</option>)}</select></td>
                  <td><select aria-label="Test" disabled={!editable} value={r.op} onChange={(e) => setRule(i, { op: e.target.value })}>{OPS.map(([v, t]) => <option key={v} value={v}>{t}</option>)}</select></td>
                  <td>{r.op === 'Equals' ? <input aria-label="Value" disabled={!editable} value={r.textValue ?? ''} onChange={(e) => setRule(i, { textValue: e.target.value })} />
                    : ['IsYes', 'Missing'].includes(r.op) ? '—' : <input aria-label="Limit" type="number" step="0.1" disabled={!editable} value={r.threshold ?? ''} onChange={(e) => setRule(i, { threshold: e.target.value === '' ? null : Number(e.target.value) })} />}</td>
                  <td><input aria-label="Procedures" disabled={!editable} value={r.procedureContains ?? ''} placeholder="all" onChange={(e) => setRule(i, { procedureContains: e.target.value })} /></td>
                  <td><select aria-label="Anaesthetic" disabled={!editable} value={r.anaesthetic ?? ''} onChange={(e) => setRule(i, { anaesthetic: e.target.value || null })}><option value="">Any</option>{['General', 'Sedation', 'Regional', 'Local'].map((x) => <option key={x}>{x}</option>)}</select></td>
                  <td><select aria-label="Outcome" disabled={!editable} value={r.outcome} onChange={(e) => setRule(i, { outcome: e.target.value })}>{meta.outcomes.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}</select></td>
                  <td><input aria-label="Precaution" disabled={!editable} value={r.precaution ?? ''} onChange={(e) => setRule(i, { precaution: e.target.value })} /></td>
                  <td><input aria-label="Active" type="checkbox" disabled={!editable} checked={r.isActive} onChange={(e) => setRule(i, { isActive: e.target.checked })} /></td>
                  {editable && <td><button className="btn btn-link danger" onClick={() => setSel({ ...sel, rules: sel.rules.filter((_: any, j: number) => j !== i) })}>Remove</button></td>}
                </tr>))}</tbody>
            </table></div>
            {editable && <div><button className="btn btn-light btn-small" onClick={() => setSel({ ...sel, rules: [...sel.rules, { code: '', name: '', fact: 'bmi', op: 'AtLeast', threshold: null, textValue: null, procedureContains: '', anaesthetic: null, outcome: 'AnaesthetistReview', precaution: '', explanation: '', sortOrder: (sel.rules.length + 1) * 10, isActive: true }] })}>Add a rule</button></div>}
            <h3>Medicine prompts</h3>
            <div className="table-scroll" tabIndex={0} role="region" aria-label="Medicine prompts"><table className="results compact">
              <thead><tr><th>Medicine</th><th>Words to look for (comma separated)</th><th>Instruction</th><th>Outcome</th><th>On</th>{editable && <th></th>}</tr></thead>
              <tbody>{sel.medicines.map((m: any, i: number) => (
                <tr key={i}>
                  <td><input aria-label="Medicine" disabled={!editable} value={m.name} onChange={(e) => setMed(i, { name: e.target.value })} /></td>
                  <td><input aria-label="Words" disabled={!editable} value={m.keywords} onChange={(e) => setMed(i, { keywords: e.target.value })} /></td>
                  <td><input aria-label="Instruction" disabled={!editable} value={m.instruction} onChange={(e) => setMed(i, { instruction: e.target.value })} /></td>
                  <td><select aria-label="Medicine outcome" disabled={!editable} value={m.outcome} onChange={(e) => setMed(i, { outcome: e.target.value })}>{meta.outcomes.map((o) => <option key={o.value} value={o.value}>{o.label}</option>)}</select></td>
                  <td><input aria-label="Medicine active" type="checkbox" disabled={!editable} checked={m.isActive} onChange={(e) => setMed(i, { isActive: e.target.checked })} /></td>
                  {editable && <td><button className="btn btn-link danger" onClick={() => setSel({ ...sel, medicines: sel.medicines.filter((_: any, j: number) => j !== i) })}>Remove</button></td>}
                </tr>))}</tbody>
            </table></div>
            {editable && <div><button className="btn btn-light btn-small" onClick={() => setSel({ ...sel, medicines: [...sel.medicines, { name: '', keywords: '', instruction: '', outcome: 'SuitableWithPrecautions', isActive: true }] })}>Add a medicine prompt</button></div>}
            {editable && <div className="row"><button className="btn btn-primary" onClick={() => void save()}>Save draft</button>
              <button className="btn btn-link danger" onClick={() => void run(async () => { await api(`/api/triage/rulesets/${sel.id}`, { method: 'DELETE' }); setSel(null); await loadSets(); }, 'Draft deleted.')}>Delete draft</button></div>}
            {draft && meta.canApprove && (
              <div className="card subtle stack">
                <h3>Clinical approval</h3>
                <p className="hint">Approving makes this version the one used for every patient and retires the current one. Save any changes first.</p>
                <label>Approval record (who reviewed the rules and where it was agreed)<input value={note} onChange={(e) => setNote(e.target.value)} /></label>
                <div><button className="btn btn-warn" disabled={!note.trim()} onClick={() => void run(async () => { await api(`/api/triage/rulesets/${sel.id}/approve`, { body: { note } }); await loadSets(); await open(sel.id); setNote(''); }, 'Rule set approved.')}>Approve version {sel.version}</button></div>
              </div>
            )}
          </section>
        )}
      </>}
    </div>
  );
}

export function TriageReport() {
  const [r, setR] = useState<any>(null);
  useEffect(() => { api('/api/triage/report').then(setR).catch(() => undefined); }, []);
  return (
    <div className="stack-lg">
      <div className="page-head row-between"><div><h1>Triage report</h1><p className="muted">Last 3 months.</p></div><Link className="btn btn-light" to="/triage">Worklist</Link></div>
      {!r ? <p>Loading…</p> : <>
        <section className="card stack">
          <p>{r.signedOff} signed off · {r.overridden} with a different outcome from the suggestion · {r.retriaged} re-triaged · {r.worse} got worse · median {r.medianDaysToSignOff ?? '—'} day(s) from first triage to sign-off.</p>
          <table className="results compact"><thead><tr><th>Outcome</th><th>Patients</th></tr></thead><tbody>{r.outcomes.map((o: any) => <tr key={o.outcome}><td>{o.outcome}</td><td>{o.count}</td></tr>)}</tbody></table>
        </section>
        <section className="card"><h2>By procedure</h2>
          <table className="results compact"><thead><tr><th>Procedure</th><th>Signed off</th><th>Anaesthetist review</th><th>Not suitable</th></tr></thead><tbody>{r.byProcedure.map((p: any) => <tr key={p.procedure}><td>{p.procedure || '—'}</td><td>{p.count}</td><td>{p.review}</td><td>{p.notSuitable}</td></tr>)}</tbody></table>
        </section>
      </>}
    </div>
  );
}
