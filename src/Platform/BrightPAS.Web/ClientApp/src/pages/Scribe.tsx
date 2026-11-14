import { useEffect, useState } from 'react';
import { Link, useNavigate, useSearchParams } from 'react-router';
import { addDays, api, errText, nzDateTime, Perm, todayIso, type PatientSummary } from '../api';
import { useSession } from '../App';
import PatientPicker from '../components/PatientPicker';

export const CONTEXTS: [string, string][] = [['Consultation', 'Consultation'], ['PreAnaesthetic', 'Pre-anaesthetic assessment'], ['PreAdmission', 'Nurse pre-admission call'], ['TriageCall', 'Triage phone call'],
  ['PostOpReview', 'Post-op review / follow-up call'], ['Discharge', 'Discharge'], ['FollowUpCall', 'Follow-up call']];
export const STATUS: Record<string, string> = { Recording: 'Recording', Recorded: 'Recorded – to transcribe', Transcribed: 'Transcribed – to draft', Drafted: 'Draft – to review and sign', Signed: 'Signed', Discarded: 'Discarded' };

/** BrightPAS Scribe: sessions, templates (governance approval), custom dictionary, accuracy audit and settings. */
export default function Scribe() {
  const { can } = useSession();
  const [params, setParams] = useSearchParams();
  const tab = params.get('tab') ?? (can(Perm.ScribeUse) ? 'sessions' : 'templates');
  const tabs: [string, string][] = [...(can(Perm.ScribeUse) ? [['sessions', 'My sessions'] as [string, string]] : []), ['templates', 'Templates'], ['dictionary', 'Dictionary'], ['accuracy', 'Accuracy'], ...(can(Perm.ScribeManage) ? [['settings', 'Settings'] as [string, string]] : [])];
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Scribe</h1><p className="muted">AI dictation and ambient documentation. Everything the scribe writes is a draft until the clinician checks and signs it.</p></div>
      <section className="card stack">
        <div className="tabs" role="tablist">{tabs.map(([k, t]) => <button key={k} role="tab" aria-selected={tab === k} className={tab === k ? 'on' : ''} onClick={() => setParams({ tab: k })}>{t}</button>)}</div>
        {tab === 'sessions' && <Sessions />}
        {tab === 'templates' && <Templates />}
        {tab === 'dictionary' && <Dictionary />}
        {tab === 'accuracy' && <Accuracy />}
        {tab === 'settings' && <Settings />}
      </section>
    </div>
  );
}

function Sessions() {
  const [params] = useSearchParams();
  const nav = useNavigate();
  const [rows, setRows] = useState<any[] | null>(null);
  const [templates, setTemplates] = useState<any[]>([]);
  const [patient, setPatient] = useState<PatientSummary | null>(null);
  const [context, setContext] = useState(params.get('context') ?? 'Consultation');
  const [template, setTemplate] = useState('');
  const [error, setError] = useState<string | null>(null);
  const bookingId = params.get('bookingId');
  useEffect(() => {
    api<any[]>('/api/scribe/sessions').then(setRows).catch((e) => setError(errText(e)));
    api<any[]>('/api/scribe/templates').then((t) => setTemplates(t.filter((x) => x.status === 'Approved'))).catch(() => undefined);
    const pid = params.get('patientId');
    if (pid) api(`/api/patients/${pid}`).then(setPatient).catch(() => undefined);
  }, []); // eslint-disable-line react-hooks/exhaustive-deps
  const PREFERRED: Record<string, string> = { Consultation: 'CONSULT', PreAnaesthetic: 'PREANAES', PreAdmission: 'PREADMIT', TriageCall: 'TRIAGE', PostOpReview: 'POSTOP', Discharge: 'DISCHARGE', FollowUpCall: 'POSTOP' };
  const fits = templates.filter((t) => t.context === context).sort((a, b) => (a.code === PREFERRED[context] ? -1 : b.code === PREFERRED[context] ? 1 : 0));
  useEffect(() => { if (!fits.some((t) => t.id === template)) setTemplate(fits[0]?.id ?? templates[0]?.id ?? ''); }, [context, templates]); // eslint-disable-line react-hooks/exhaustive-deps
  const start = async () => {
    setError(null);
    try { const r = await api('/api/scribe/sessions', { body: { patientId: patient!.id, bookingId: bookingId || null, context, templateId: template } }); nav(`/scribe/${r.id}`); }
    catch (x) { setError(errText(x)); }
  };
  return (
    <div className="stack">
      <div className="card subtle stack"><h2>Start a scribe session</h2>
        <div className="form-grid"><PatientPicker value={patient} onChange={setPatient} />
          <label>Type<select value={context} onChange={(e) => setContext(e.target.value)}>{CONTEXTS.map(([k, l]) => <option key={k} value={k}>{l}</option>)}</select></label>
          <label>Template<select value={template} onChange={(e) => setTemplate(e.target.value)}>{(fits.length ? fits : templates).map((t) => <option key={t.id} value={t.id}>{t.name}</option>)}</select></label></div>
        {bookingId && <p className="small muted">Linked to the booking – the signed note also goes into the clinical record.</p>}
        <div><button className="btn btn-primary" disabled={!patient || !template} onClick={start}>Start</button></div></div>
      {error && <p className="error" role="alert">{error}</p>}
      {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">No sessions yet.</p> : (
        <table className="results"><thead><tr><th>Started</th><th>Patient</th><th>Type</th><th>Status</th><th>Edited</th></tr></thead>
          <tbody>{rows.map((r) => <tr key={r.id}><td><Link to={`/scribe/${r.id}`}>{nzDateTime(r.createdAt)}</Link></td><td>{r.patient?.name}<div className="small muted">NHI {r.patient?.nhi ?? '—'}</div></td>
            <td>{CONTEXTS.find(([k]) => k === r.context)?.[1]}{r.provider === 'Medow' && <span className="pill">Medow</span>}</td><td>{STATUS[r.status]}</td><td>{r.editPercent != null ? `${r.editPercent}%` : ''}</td></tr>)}</tbody></table>)}
    </div>
  );
}

function Templates() {
  const { can } = useSession();
  const [rows, setRows] = useState<any[]>([]);
  const [edit, setEdit] = useState<any>(null);
  const [error, setError] = useState<string | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const load = () => api<any[]>('/api/scribe/templates').then(setRows).catch((e) => setError(errText(e)));
  useEffect(() => { load(); }, []);
  const act = async (fn: () => Promise<any>, ok: string) => { setError(null); setMsg(null); try { await fn(); setMsg(ok); load(); return true; } catch (x) { setError(errText(x)); return false; } };
  return (
    <div className="stack">
      <p className="hint">Templates decide the sections of the draft. Any change needs approval by clinical governance (not the author) before clinicians can use it.</p>
      {error && <p className="error" role="alert">{error}</p>}{msg && <p className="ok-text" role="status">{msg}</p>}
      <table className="results"><thead><tr><th>Template</th><th>Type</th><th>Version</th><th>Status</th><th></th></tr></thead>
        <tbody>{rows.map((t) => <tr key={t.id}><td>{t.name}<div className="small muted">{t.code}</div></td><td>{CONTEXTS.find(([k]) => k === t.context)?.[1]}</td><td>{t.version}</td>
          <td>{t.status === 'Approved' ? <span className="pill green">Approved</span> : <span className="pill amber">{t.status}</span>}<div className="small muted">{t.approvedBy ? `${t.approvedBy}` : `changed by ${t.updatedBy}`}</div></td>
          <td className="row">{can(Perm.ScribeManage) && <button className="btn btn-link" onClick={() => setEdit({ ...t })}>Edit</button>}
            {can(Perm.ScribeApprove) && t.status !== 'Approved' && <button className="btn btn-link" onClick={() => act(() => api(`/api/scribe/templates/${t.id}/approve`, { body: { approve: true } }), `${t.name} approved.`)}>Approve</button>}
            {can(Perm.ScribeApprove) && t.status === 'Approved' && <button className="btn btn-link danger" onClick={() => act(() => api(`/api/scribe/templates/${t.id}/approve`, { body: { approve: false } }), `${t.name} retired.`)}>Retire</button>}</td></tr>)}</tbody></table>
      {can(Perm.ScribeManage) && !edit && <div><button className="btn btn-light" onClick={() => setEdit({ code: '', name: '', context: 'Consultation', sections: '' })}>New template</button></div>}
      {edit && <div className="card subtle stack"><div className="form-grid">
        <label>Code<input value={edit.code} onChange={(e) => setEdit({ ...edit, code: e.target.value })} /></label>
        <label>Name<input value={edit.name} onChange={(e) => setEdit({ ...edit, name: e.target.value })} /></label>
        <label>Type<select value={edit.context} onChange={(e) => setEdit({ ...edit, context: e.target.value })}>{CONTEXTS.map(([k, l]) => <option key={k} value={k}>{l}</option>)}</select></label></div>
        <label>Sections – one per line, "Heading: what to put here"<textarea rows={8} value={edit.sections} onChange={(e) => setEdit({ ...edit, sections: e.target.value })} /></label>
        <div className="row"><button className="btn btn-primary" onClick={async () => { if (await act(() => api(edit.id ? `/api/scribe/templates/${edit.id}` : '/api/scribe/templates', { method: edit.id ? 'PUT' : 'POST', body: edit }), 'Saved – awaiting approval.')) setEdit(null); }}>Save</button>
          <button className="btn btn-light" onClick={() => setEdit(null)}>Cancel</button></div></div>}
    </div>
  );
}

function Dictionary() {
  const { can } = useSession();
  const [d, setD] = useState<any>(null);
  const [f, setF] = useState<any>({ term: '', soundsLike: '', category: 'Clinician' });
  const [filter, setFilter] = useState('');
  const [error, setError] = useState<string | null>(null);
  const load = () => api('/api/scribe/dictionary').then(setD).catch((e) => setError(errText(e)));
  useEffect(() => { load(); }, []);
  const save = async () => { setError(null); try { await api(f.id ? `/api/scribe/dictionary/${f.id}` : '/api/scribe/dictionary', { method: f.id ? 'PUT' : 'POST', body: f }); setF({ term: '', soundsLike: '', category: f.category }); load(); } catch (x) { setError(errText(x)); } };
  if (!d) return error ? <p className="error">{error}</p> : <p>Loading…</p>;
  const rows = d.terms.filter((t: any) => !filter || (t.term + ' ' + (t.soundsLike ?? '') + ' ' + t.category).toLowerCase().includes(filter.toLowerCase()));
  return (
    <div className="stack">
      <p className="hint">Names and words the speech engine must spell correctly – clinicians, staff, practices, suppliers, medicines, procedures, places and te reo Māori kupu. "Sounds like" lists what the engine tends to write instead (comma-separated).</p>
      {error && <p className="error" role="alert">{error}</p>}
      {can(Perm.ScribeManage) && <div className="card subtle form-grid">
        <label>Term<input value={f.term} onChange={(e) => setF({ ...f, term: e.target.value })} /></label>
        <label>Sounds like<input value={f.soundsLike ?? ''} onChange={(e) => setF({ ...f, soundsLike: e.target.value })} /></label>
        <label>Category<select value={f.category} onChange={(e) => setF({ ...f, category: e.target.value })}>{d.categories.map((c: string) => <option key={c}>{c}</option>)}</select></label>
        <div className="row"><button className="btn btn-primary" onClick={save}>{f.id ? 'Save' : 'Add term'}</button>{f.id && <button className="btn btn-light" onClick={() => setF({ term: '', soundsLike: '', category: f.category })}>Cancel</button>}</div></div>}
      <label>Find<input value={filter} onChange={(e) => setFilter(e.target.value)} /></label>
      <table className="results compact"><thead><tr><th>Term</th><th>Sounds like</th><th>Category</th><th></th></tr></thead>
        <tbody>{rows.map((t: any) => <tr key={t.id}><td>{t.term}</td><td className="small">{t.soundsLike}</td><td>{t.category}</td>
          <td>{can(Perm.ScribeManage) && <span className="row"><button className="btn btn-link" onClick={() => setF({ ...t, soundsLike: t.soundsLike ?? '' })}>Edit</button>
            <button className="btn btn-link danger" onClick={async () => { await api(`/api/scribe/dictionary/${t.id}`, { method: 'DELETE' }).catch((x) => setError(errText(x))); load(); }}>Remove</button></span>}</td></tr>)}</tbody></table>
    </div>
  );
}

function Accuracy() {
  const [to, setTo] = useState(todayIso());
  const [from, setFrom] = useState(addDays(todayIso(), -90));
  const [rows, setRows] = useState<any[] | null>(null);
  useEffect(() => { setRows(null); api<any[]>(`/api/scribe/accuracy?from=${from}&to=${to}`).then(setRows).catch(() => setRows([])); }, [from, to]);
  return (
    <div className="stack">
      <p className="hint">How much clinicians changed the AI draft before signing (word-level). High numbers point to template, dictionary or engine problems – review with the accuracy audit and report errors as incidents.</p>
      <div className="row"><label>From<input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></label><label>To<input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></label></div>
      {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">No signed sessions in this range.</p> : (
        <table className="results"><thead><tr><th>Clinician</th><th>Speech / drafting engine</th><th>Signed notes</th><th>Average edited</th><th>Most edited</th></tr></thead>
          <tbody>{rows.map((r, i) => <tr key={i}><td>{r.clinician}</td><td className="small">{r.engine}</td><td>{r.sessions}</td><td>{r.meanEditPercent}%</td><td>{r.maxEditPercent}%</td></tr>)}</tbody></table>)}
    </div>
  );
}

function Settings() {
  const [d, setD] = useState<any>(null);
  const [f, setF] = useState<any>(null);
  const [token, setToken] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const load = () => api('/api/scribe/settings').then((x) => { setD(x); setF({ ...x.settings, speechKey: '', draftKey: '' }); }).catch((e) => setError(errText(e)));
  useEffect(() => { load(); }, []);
  if (!f) return error ? <p className="error">{error}</p> : <p>Loading…</p>;
  const v = (k: string) => f[k] ?? '';
  const save = async () => { setError(null); setMsg(null); try { await api('/api/scribe/settings', { method: 'PUT', body: f }); setMsg('Settings saved.'); load(); } catch (x) { setError(errText(x)); } };
  const cloud = f.speech === 'Cloud' || f.draft === 'CloudClaude';
  return (
    <div className="stack">
      {error && <p className="error" role="alert">{error}</p>}{msg && <p className="ok-text" role="status">{msg}</p>}
      <p className={d.audio.ok ? 'small ok-text' : 'small error-text'}>Audio folder (encrypted): {d.audio.detail}</p>
      <label>Scribe used at this facility<select value={f.provider} onChange={(e) => setF({ ...f, provider: e.target.value })}>
        <option value="BrightPas">BrightPAS Scribe (default)</option><option value="Medow">Medow Health (external)</option><option value="Off">Off</option></select></label>
      <h2>Speech engine (speech to text)</h2>
      <div className="form-grid">
        <label>Where<select value={f.speech} onChange={(e) => setF({ ...f, speech: e.target.value })}><option value="Off">Off</option><option value="OnSite">On site (PAS-SPEECH server)</option><option value="Cloud">Cloud service (audio leaves TDS)</option></select></label>
        {f.speech !== 'Off' && <><label>Address (OpenAI-compatible transcription API)<input value={v('speechUrl')} onChange={(e) => setF({ ...f, speechUrl: e.target.value })} placeholder="http://pas-speech:8000" /></label>
          <label>Model<input value={v('speechModel')} onChange={(e) => setF({ ...f, speechModel: e.target.value })} placeholder="whisper-1 / large-v3" /></label>
          <label>Key {d.settings.speechKeySet && '(set – leave blank to keep)'}<input type="password" value={f.speechKey} onChange={(e) => setF({ ...f, speechKey: e.target.value })} autoComplete="off" /></label></>}
      </div>
      <h2>Drafting engine (transcript to note)</h2>
      <div className="form-grid">
        <label>How<select value={f.draft} onChange={(e) => setF({ ...f, draft: e.target.value })}><option value="TemplateOnly">Template only – no AI (transcript under the headings)</option>
          <option value="OnSite">On-site language model (option A)</option><option value="CloudClaude">Claude API – de-identified transcript (option B)</option></select></label>
        {f.draft !== 'TemplateOnly' && <><label>Address<input value={v('draftUrl')} onChange={(e) => setF({ ...f, draftUrl: e.target.value })} placeholder={f.draft === 'CloudClaude' ? 'https://api.anthropic.com' : 'http://pas-speech:8001'} /></label>
          <label>Model<input value={v('draftModel')} onChange={(e) => setF({ ...f, draftModel: e.target.value })} /></label>
          <label>Key {d.settings.draftKeySet && '(set – leave blank to keep)'}<input type="password" value={f.draftKey} onChange={(e) => setF({ ...f, draftKey: e.target.value })} autoComplete="off" /></label></>}
      </div>
      {cloud && <label>Privacy impact assessment addendum / supplier assessment reference (required for cloud)<input value={v('cloudPrivacyReference')} onChange={(e) => setF({ ...f, cloudPrivacyReference: e.target.value })} /></label>}
      {cloud && d.settings.cloudApprovedBy && <p className="small muted">Cloud processing recorded by {d.settings.cloudApprovedBy}, {nzDateTime(d.settings.cloudApprovedAt)}. Patient names, NHI, dates of birth, phone numbers and addresses are replaced before drafting and put back inside BrightPAS.</p>}
      <h2>Medow Health (optional external scribe)</h2>
      <div className="form-grid">
        <label>Privacy impact assessment addendum<input value={v('medowPiaReference')} onChange={(e) => setF({ ...f, medowPiaReference: e.target.value })} /></label>
        <label>HISF supplier assessment<input value={v('medowSupplierAssessment')} onChange={(e) => setF({ ...f, medowSupplierAssessment: e.target.value })} /></label>
        <label>Data processing agreement<input value={v('medowAgreementReference')} onChange={(e) => setF({ ...f, medowAgreementReference: e.target.value })} /></label>
        <label>Medow launch address<input value={v('medowLaunchUrl')} onChange={(e) => setF({ ...f, medowLaunchUrl: e.target.value })} placeholder="https://…" /></label>
      </div>
      <div className="row"><button className="btn btn-light btn-small" onClick={async () => { try { setToken((await api('/api/scribe/settings/token', { body: {} })).token); } catch (x) { setError(errText(x)); } }}>New token for returning drafts</button>
        {d.settings.inboundTokenSet && !token && <span className="small muted">A token is set.</span>}
        {token && <span className="small">Give this to the external scribe now (shown once): <code>{token}</code> – it posts drafts to /api/scribe/inbound.</span>}</div>
      <h2>Audio</h2>
      <label>Keep audio after signing (days, 0 = delete when signed)<input type="number" value={f.audioRetentionDays} onChange={(e) => setF({ ...f, audioRetentionDays: Number(e.target.value) })} /></label>
      <div><button className="btn btn-primary" onClick={save}>Save settings</button></div>
    </div>
  );
}
