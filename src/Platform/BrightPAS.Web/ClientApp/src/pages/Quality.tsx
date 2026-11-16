import { useCallback, useEffect, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { api, errText, formatDate, nzDateTime, Perm, todayIso } from '../api';
import { useSession } from '../App';
import ExportButton from '../components/ExportButton';

const CATEGORIES: [string, string][] = [['Fall', 'Fall'], ['Medication', 'Medication'], ['ClinicalCare', 'Clinical care'], ['SurgicalProcedural', 'Surgical / procedural'], ['Anaesthetic', 'Anaesthetic'],
  ['Infection', 'Infection'], ['PressureInjury', 'Pressure injury'], ['EquipmentDevice', 'Equipment / device'], ['InformationPrivacy', 'Information / privacy'], ['StaffInjury', 'Staff injury'], ['Security', 'Security'], ['Other', 'Other']];
const HARM: [string, string][] = [['None', 'No harm (near miss)'], ['Minor', 'Minor'], ['Moderate', 'Moderate'], ['Severe', 'Severe'], ['Death', 'Death']];
const AGENCIES: [string, string][] = [['Hqsc', 'HQSC'], ['AccTreatmentInjury', 'ACC treatment injury'], ['Medsafe', 'Medsafe (device/medicine)'], ['Hdc', 'Health and Disability Commissioner'],
  ['HealthNz', 'Health NZ contract manager'], ['WorkSafe', 'WorkSafe (notifiable staff event)'], ['PrivacyCommissioner', 'Privacy Commissioner'], ['Coroner', 'Coroner']];
const label = (list: [string, string][], k: string) => list.find(([x]) => x === k)?.[1] ?? k;
const spaced = (s: string) => s.replace(/(?<=[a-z])(?=[A-Z])/g, ' ');
const rate = (v: number | null | undefined, scale: number) => v == null ? '—' : scale === 1 ? String(v) : `${Number(v).toFixed(1)}${scale === 100 ? '%' : ' /1,000'}`;
const localNow = () => { const d = new Date(); d.setMinutes(d.getMinutes() - d.getTimezoneOffset()); return d.toISOString().slice(0, 16); };

function Msg({ error, ok }: { error: string | null; ok: string | null }) {
  return <>{error && <p className="error" role="alert">{error}</p>}{ok && <p className="ok-text" role="status">{ok}</p>}</>;
}

// ------------------------------------------------------------------ report an incident (any staff member, under 2 minutes)
export function ReportIncident() {
  const { can } = useSession();
  const [params] = useSearchParams();
  const [look, setLook] = useState<any>(null);
  const [f, setF] = useState({ occurredAt: localNow(), location: '', category: '', harm: 'None', description: '', immediateAction: '', staffInvolved: '', equipmentId: '', anonymous: false });
  const [patient, setPatient] = useState<{ id: string; name: string } | null>(null);
  const [q, setQ] = useState(''); const [hits, setHits] = useState<any[]>([]);
  const [done, setDone] = useState<any>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => { api('/api/quality/lookups').then(setLook).catch(() => undefined); }, []);
  useEffect(() => { const pid = params.get('patientId'); if (pid && can(Perm.PatientView)) api(`/api/patients/${pid}`).then((p) => setPatient({ id: p.id, name: `${p.familyName}, ${p.givenNames}` })).catch(() => undefined); }, [params]); // eslint-disable-line react-hooks/exhaustive-deps
  const search = async () => { if (q.trim().length > 1) setHits(await api<any[]>(`/api/patients?q=${encodeURIComponent(q)}`).catch(() => [])); };
  const submit = async () => {
    setError(null);
    try {
      setDone(await api('/api/quality/incidents', { body: { ...f, occurredAt: new Date(f.occurredAt).toISOString(), patientId: patient?.id ?? null, bookingId: params.get('bookingId'), equipmentId: f.equipmentId || null, category: f.category || 'Other' } }));
    } catch (e) { setError(errText(e)); }
  };
  if (done) return (
    <div className="card stack">
      <h1>Incident reported</h1>
      <p className="ok-text" role="status">Thank you. Your report is <strong>{done.number}</strong>{f.anonymous ? ' (anonymous – your name is not kept)' : ''}. The quality manager will review it.</p>
      {done.escalated && <p className="warn-box">Because serious harm was reported, the General Manager and Clinical Governance have been alerted.</p>}
      <div className="row"><Link className="btn btn-light" to="/">Back to my work</Link><button className="btn btn-light" onClick={() => { setDone(null); setF({ ...f, description: '', immediateAction: '' }); }}>Report another</button></div>
    </div>
  );
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Report an incident</h1><p className="muted">Anything that harmed, or could have harmed, a patient, visitor or staff member – including near misses. It takes about two minutes. Reporting is about learning, not blame.</p></div>
      <section className="card stack">
        <div className="form-grid">
          <label>When did it happen<input type="datetime-local" value={f.occurredAt} max={localNow()} onChange={(e) => setF({ ...f, occurredAt: e.target.value })} /></label>
          <label>Where<input list="q-locations" value={f.location} onChange={(e) => setF({ ...f, location: e.target.value })} placeholder="e.g. Theatre 1, recovery, reception" />
            <datalist id="q-locations">{(look?.locations ?? []).map((l: string) => <option key={l} value={l} />)}</datalist></label>
          <label>What kind of incident<select value={f.category} onChange={(e) => setF({ ...f, category: e.target.value })}><option value="">Choose…</option>{CATEGORIES.map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
          <label>Harm (as far as you know)<select value={f.harm} onChange={(e) => setF({ ...f, harm: e.target.value })}>{HARM.map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
        </div>
        <label>What happened<textarea rows={4} value={f.description} onChange={(e) => setF({ ...f, description: e.target.value })} placeholder="Facts only: what happened, in what order. Avoid names of other staff here – use the field below." /></label>
        <label>What was done straight away (optional)<textarea rows={2} value={f.immediateAction} onChange={(e) => setF({ ...f, immediateAction: e.target.value })} /></label>
        <div className="form-grid">
          {can(Perm.PatientView) && <div className="stack-sm"><span className="small"><strong>Patient involved (optional)</strong></span>
            {patient ? <p>{patient.name} <button className="btn btn-link" onClick={() => setPatient(null)}>Remove</button></p> : <>
              <div className="row"><input aria-label="Find the patient" value={q} onChange={(e) => setQ(e.target.value)} placeholder="Name, NHI or MRN" /><button className="btn btn-light" onClick={search}>Find</button></div>
              {hits.slice(0, 6).map((h) => <button key={h.id} className="btn btn-link" onClick={() => { setPatient({ id: h.id, name: `${h.familyName}, ${h.givenNames}` }); setHits([]); }}>{h.familyName}, {h.givenNames} ({h.nhi ?? h.mrn})</button>)}</>}</div>}
          <label>Staff involved (optional)<input value={f.staffInvolved} onChange={(e) => setF({ ...f, staffInvolved: e.target.value })} placeholder="Names or roles" /></label>
          {(look?.equipment ?? []).length > 0 && <label>Equipment involved (optional)<select value={f.equipmentId} onChange={(e) => setF({ ...f, equipmentId: e.target.value })}><option value="">None</option>{look.equipment.map((e: any) => <option key={e.id} value={e.id}>{e.label}</option>)}</select></label>}
        </div>
        <label className="check"><input type="checkbox" checked={f.anonymous} onChange={(e) => setF({ ...f, anonymous: e.target.checked })} /> Report anonymously (your name is not kept anywhere; you will not be able to see this report under "my reports")</label>
        <Msg error={error} ok={null} />
        <div><button className="btn btn-primary" onClick={submit}>Send report</button></div>
      </section>
    </div>
  );
}

// ------------------------------------------------------------------ every staff member: policies to read, my reports, learning
export function QualityMine() {
  const [d, setD] = useState<any>(null);
  const [pol, setPol] = useState<any[] | null>(null);
  const [error, setError] = useState<string | null>(null); const [ok, setOk] = useState<string | null>(null);
  const load = () => { api('/api/quality/my').then(setD).catch((e) => setError(errText(e))); api<any[]>('/api/quality/policies').then(setPol).catch(() => undefined); };
  useEffect(load, []);
  const ack = async (id: string, code: string) => { setError(null); setOk(null); try { await api(`/api/quality/policies/${id}/acknowledge`, { body: {} }); setOk(`${code} acknowledged.`); load(); } catch (e) { setError(errText(e)); } };
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Policies &amp; my reports</h1><p className="muted">Policies you need to read, incidents you have reported and what TDS has learnt.</p></div>
      <Msg error={error} ok={ok} />
      <section className="card stack"><h2>Policies to read and acknowledge</h2>
        {!d ? <p>Loading…</p> : d.toRead.length === 0 ? <p className="muted">Nothing waiting – thank you.</p> : (
          <table className="results"><thead><tr><th>Document</th><th>Version</th><th><span className="sr-only">Actions</span></th></tr></thead>
            <tbody>{d.toRead.map((p: any) => <tr key={p.id}><td><strong>{p.code}</strong> {p.title}{p.link && <> · <a href={p.link} target="_blank" rel="noopener noreferrer">Open</a></>}</td><td>{p.version}</td>
              <td><button className="btn btn-light btn-small" onClick={() => ack(p.id, p.code)}>I have read this</button></td></tr>)}</tbody></table>)}
      </section>
      <section className="card stack"><h2>All policies</h2>
        {pol && <table className="results compact"><thead><tr><th>Code</th><th>Title</th><th>Version</th><th>Review by</th><th>Read</th></tr></thead>
          <tbody>{pol.map((p) => <tr key={p.id}><td>{p.code}</td><td>{p.link ? <a href={p.link} target="_blank" rel="noopener noreferrer">{p.title}</a> : p.title}</td><td>{p.version}</td><td>{formatDate(p.reviewBy)}</td>
            <td>{p.requiresAcknowledgement ? (p.acknowledged ? <span className="badge ok">Acknowledged</span> : <span className="badge warn">To read</span>) : '—'}</td></tr>)}</tbody></table>}
      </section>
      <section className="card stack"><h2>Incidents I have reported</h2>
        {d && (d.incidents.length === 0 ? <p className="muted">None (anonymous reports are not listed).</p> : (
          <table className="results compact"><thead><tr><th>Number</th><th>Reported</th><th>Kind</th><th>Status</th></tr></thead>
            <tbody>{d.incidents.map((i: any) => <tr key={i.id}><td>{i.number}</td><td>{nzDateTime(i.reportedAt)}</td><td>{spaced(i.category)}</td><td>{i.status}</td></tr>)}</tbody></table>))}
        <div><Link className="btn btn-primary" to="/quality/report">Report an incident</Link></div>
      </section>
      {d?.learning?.length > 0 && <section className="card stack"><h2>What we have learnt</h2>
        {d.learning.map((l: any) => <p key={l.number}><strong>{l.number}</strong> ({spaced(l.category)}): {l.learningSummary}</p>)}</section>}
    </div>
  );
}

// ------------------------------------------------------------------ quality manager / governance
export function Quality() {
  const { can } = useSession();
  const manage = can(Perm.QualityManage);
  const [params, setParams] = useSearchParams();
  const tab = params.get('tab') ?? 'dashboard';
  const tabs: [string, string][] = [['dashboard', 'Dashboard'], ['incidents', 'Incidents'], ['complaints', 'Complaints'], ['compliments', 'Compliments'], ['risks', 'Risks'], ['hazards', 'Hazards'],
    ['actions', 'Corrective actions'], ['policies', 'Policies'], ['indicators', 'Indicators'], ...(manage ? [['settings', 'Settings'] as [string, string]] : [])];
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Quality</h1><p className="muted">Incidents, complaints, risks, actions, policies and quality indicators (NZS 8134, HQSC).</p></div>
      <div className="tabs" role="tablist">{tabs.map(([k, t]) => <button key={k} role="tab" aria-selected={tab === k} className={tab === k ? 'on' : ''} onClick={() => setParams({ tab: k })}>{t}</button>)}</div>
      {tab === 'dashboard' && <Dashboard />}
      {tab === 'incidents' && <Incidents />}
      {tab === 'complaints' && <Complaints manage={manage} />}
      {tab === 'compliments' && <Compliments manage={manage} />}
      {tab === 'risks' && <Risks hazards={false} manage={manage} />}
      {tab === 'hazards' && <Risks hazards manage={manage} />}
      {tab === 'actions' && <Actions manage={manage} />}
      {tab === 'policies' && <Policies manage={manage} />}
      {tab === 'indicators' && <Indicators manage={manage} />}
      {tab === 'settings' && manage && <Settings />}
    </div>
  );
}

function Dashboard() {
  const [d, setD] = useState<any>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => { api('/api/quality/summary').then(setD).catch((e) => setError(errText(e))); }, []);
  if (error) return <p className="error">{error}</p>;
  if (!d) return <p>Loading…</p>;
  const tile = (n: number, t: string, to: string, bad = false) => <Link to={to} className={`tile ${bad && n > 0 ? 'tile-bad' : ''}`}><strong>{n}</strong><span>{t}</span></Link>;
  return (
    <div className="stack">
      <div className="tiles">
        {tile(d.incidentsUntriaged, 'incidents to triage', '/quality?tab=incidents', true)}
        {tile(d.sac12Open, 'open SAC 1–2 / escalated', '/quality?tab=incidents', true)}
        {tile(d.incidentsOpen, 'open incidents', '/quality?tab=incidents')}
        {tile(d.complaintsOverdue, 'complaints overdue (Right 10)', '/quality?tab=complaints', true)}
        {tile(d.complaintsOpen, 'open complaints', '/quality?tab=complaints')}
        {tile(d.actionsOverdue, 'actions overdue', '/quality?tab=actions', true)}
        {tile(d.actionsToVerify, 'actions to sign off', '/quality?tab=actions')}
        {tile(d.risksHigh, 'high / extreme risks', '/quality?tab=risks')}
        {tile(d.risksReviewDue + d.policiesReviewDue, 'risks / policies past review', '/quality?tab=policies', true)}
      </div>
      <section className="card stack"><h2>Indicators</h2>
        <table className="results"><thead><tr><th>Indicator</th><th>Target</th><th>Latest month</th><th>Status</th></tr></thead>
          <tbody>{d.indicators.map((i: any) => <tr key={i.id}><td><Link to={`/quality?tab=indicators&id=${i.id}`}>{i.name}</Link>{i.signal && <span className="badge warn"> SPC signal</span>}</td><td>{rate(i.target, i.scale)}</td><td>{rate(i.latest, i.scale)}</td>
            <td><span className={`rag rag-${i.rag}`}>{i.rag === 'none' ? 'No target' : i.rag === 'green' ? 'On target' : i.rag === 'amber' ? 'Watch' : 'Off target'}</span></td></tr>)}</tbody></table>
        <div><ExportButton label="Clinical Governance pack (PDF)" formats={['pdf']} path="/api/quality/pack" body={{ month: todayIso().slice(0, 7) }} /></div>
      </section>
    </div>
  );
}

function Incidents() {
  const navigate = useNavigate();
  const [open, setOpen] = useState(true);
  const [rows, setRows] = useState<any[] | null>(null);
  useEffect(() => { api<any[]>(`/api/quality/incidents?open=${open}`).then(setRows).catch(() => setRows([])); }, [open]);
  return (
    <section className="card stack">
      <div className="row"><label className="check"><input type="checkbox" checked={open} onChange={(e) => setOpen(e.target.checked)} /> Open only</label><Link className="btn btn-light" to="/quality/report">Report an incident</Link></div>
      {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">No incidents.</p> : (
        <table className="results"><thead><tr><th>Number</th><th>Happened</th><th>Kind</th><th>Harm / SAC</th><th>Patient</th><th>Status</th></tr></thead>
          <tbody>{rows.map((i) => <tr key={i.id} className="clickable" onClick={() => navigate(`/quality/incidents/${i.id}`)}>
            <td><Link to={`/quality/incidents/${i.id}`}>{i.number}</Link>{i.escalated && <span className="badge warn"> Escalated</span>}</td><td>{nzDateTime(i.occurredAt)}<div className="small muted">{i.location}</div></td>
            <td>{label(CATEGORIES, i.category)}</td><td>{i.harm}{i.sac ? ` · SAC ${i.sac}` : ''}{i.alwaysReport ? ' · ARR' : ''}</td><td className="small">{i.patient ?? '—'}</td><td>{i.status}</td></tr>)}</tbody></table>)}
    </section>
  );
}

export function QualityIncident() {
  const { id } = useParams();
  const { can } = useSession();
  const manage = can(Perm.QualityManage);
  const [d, setD] = useState<any>(null);
  const [t, setT] = useState<any>(null); const [fi, setFi] = useState<any>(null);
  const [disc, setDisc] = useState({ at: localNow(), personTold: '', method: 'In person', notes: '' });
  const [act, setAct] = useState({ title: '', owner: '', dueOn: '' });
  const [refs, setRefs] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null); const [ok, setOk] = useState<string | null>(null);
  const load = useCallback(async () => {
    const x = await api(`/api/quality/incidents/${id}`); setD(x);
    setT({ sac: x.sac ?? 3, harm: x.harm, alwaysReport: x.alwaysReport, investigator: x.investigator ?? '', notify: x.notifications.map((n: any) => n.agency) });
    setFi({ findings: x.findings ?? '', contributingFactors: x.contributingFactors ?? '', learningSummary: x.learningSummary ?? '' });
  }, [id]);
  useEffect(() => { load().catch((e) => setError(errText(e))); }, [load]);
  const run = async (fn: () => Promise<any>, msg: string) => { setError(null); setOk(null); try { await fn(); setOk(msg); await load(); } catch (e) { setError(errText(e)); } };
  if (!d) return error ? <p className="error">{error}</p> : <p>Loading…</p>;
  const closed = d.status === 'Closed';
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Incident {d.number}</h1><p className="muted">{label(CATEGORIES, d.category)} · {d.status}{d.sac ? ` · SAC ${d.sac}` : ''} · reported {nzDateTime(d.reportedAt)} by {d.reportedBy}</p></div>
      {d.escalatedAt && <p className="warn-box">Escalated {nzDateTime(d.escalatedAt)} – alert {d.escalateTo}.</p>}
      <Msg error={error} ok={ok} />
      <section className="card stack"><h2>What happened</h2>
        <dl className="facts"><dt>When</dt><dd>{nzDateTime(d.occurredAt)}</dd><dt>Where</dt><dd>{d.location}</dd><dt>Harm</dt><dd>{label(HARM, d.harm)}</dd>
          {d.patient && <><dt>Patient</dt><dd><Link to={`/patients/${d.patientId}`}>{d.patient}</Link></dd></>}{d.equipment && <><dt>Equipment</dt><dd>{d.equipment}</dd></>}{d.staffInvolved && <><dt>Staff</dt><dd>{d.staffInvolved}</dd></>}</dl>
        <p className="prewrap">{d.description}</p>{d.immediateAction && <p className="prewrap"><strong>Done straight away:</strong> {d.immediateAction}</p>}
      </section>
      {manage && !closed && t && <section className="card stack"><h2>Triage (HQSC Severity Assessment Code)</h2>
        <div className="form-grid">
          <label>SAC rating<select value={t.sac} onChange={(e) => setT({ ...t, sac: Number(e.target.value) })}>{[1, 2, 3, 4].map((n) => <option key={n} value={n}>SAC {n}{n === 1 ? ' – death or severe harm' : n === 2 ? ' – major harm' : n === 3 ? ' – moderate harm' : ' – minor / no harm'}</option>)}</select></label>
          <label>Harm (confirmed)<select value={t.harm} onChange={(e) => setT({ ...t, harm: e.target.value })}>{HARM.map(([k, x]) => <option key={k} value={k}>{x}</option>)}</select></label>
          <label>Investigator<input value={t.investigator} onChange={(e) => setT({ ...t, investigator: e.target.value })} /></label>
        </div>
        <label className="check"><input type="checkbox" checked={t.alwaysReport} onChange={(e) => setT({ ...t, alwaysReport: e.target.checked })} /> On the HQSC Always Report and Review list</label>
        <fieldset className="stack-sm"><legend className="small"><strong>External notifications needed</strong></legend>
          <div className="checklist">{AGENCIES.map(([k, x]) => <label key={k} className="check"><input type="checkbox" checked={t.notify.includes(k)} onChange={(e) => setT({ ...t, notify: e.target.checked ? [...t.notify, k] : t.notify.filter((y: string) => y !== k) })} /> {x}</label>)}</div></fieldset>
        <div><button className="btn btn-primary" onClick={() => run(() => api(`/api/quality/incidents/${id}/triage`, { body: t }), 'Triage saved.')}>Save triage</button></div>
      </section>}
      <section className="card stack"><h2>Review</h2>
        {manage && !closed && fi ? <>
          <label>Findings<textarea rows={4} value={fi.findings} onChange={(e) => setFi({ ...fi, findings: e.target.value })} /></label>
          <label>Contributing factors (e.g. London Protocol)<textarea rows={3} value={fi.contributingFactors} onChange={(e) => setFi({ ...fi, contributingFactors: e.target.value })} /></label>
          <label>Learning summary for staff (no names or identifying details)<textarea rows={2} value={fi.learningSummary} onChange={(e) => setFi({ ...fi, learningSummary: e.target.value })} /></label>
          <div><button className="btn btn-light" onClick={() => run(() => api(`/api/quality/incidents/${id}/findings`, { body: fi }), 'Review saved.')}>Save review</button></div></>
          : <><p className="prewrap">{d.findings ?? 'Not recorded yet.'}</p>{d.learningSummary && <p><strong>Learning:</strong> {d.learningSummary}</p>}</>}
      </section>
      <section className="card stack"><h2>Open disclosure</h2>
        {d.disclosures.length === 0 ? <p className="muted">None recorded.</p> : <ul>{d.disclosures.map((x: any, n: number) => <li key={n}>{nzDateTime(x.at)} – {x.personTold} ({x.method}) by {x.by}{x.notes ? `: ${x.notes}` : ''}</li>)}</ul>}
        {manage && !closed && <div className="form-grid">
          <label>When<input type="datetime-local" value={disc.at} max={localNow()} onChange={(e) => setDisc({ ...disc, at: e.target.value })} /></label>
          <label>Who was told<input value={disc.personTold} onChange={(e) => setDisc({ ...disc, personTold: e.target.value })} placeholder="e.g. patient and daughter" /></label>
          <label>How<select value={disc.method} onChange={(e) => setDisc({ ...disc, method: e.target.value })}>{['In person', 'Phone', 'Letter', 'Video call'].map((m) => <option key={m}>{m}</option>)}</select></label>
          <label>Notes<input value={disc.notes} onChange={(e) => setDisc({ ...disc, notes: e.target.value })} /></label>
          <div><button className="btn btn-light" onClick={() => run(() => api(`/api/quality/incidents/${id}/disclosures`, { body: { ...disc, at: new Date(disc.at).toISOString() } }), 'Disclosure recorded.')}>Record disclosure</button></div></div>}
      </section>
      {d.notifications.length > 0 && <section className="card stack"><h2>External notifications</h2>
        <table className="results compact"><thead><tr><th>Agency</th><th>Notified</th><th><span className="sr-only">Actions</span></th></tr></thead>
          <tbody>{d.notifications.map((n: any) => <tr key={n.agency}><td>{label(AGENCIES, n.agency)}</td><td>{n.notifiedAt ? `${nzDateTime(n.notifiedAt)} by ${n.notifiedBy}${n.reference ? ` (ref ${n.reference})` : ''}` : <span className="badge warn">Outstanding</span>}</td>
            <td>{manage && !closed && !n.notifiedAt && <div className="row"><input aria-label={`Reference from ${label(AGENCIES, n.agency)}`} placeholder="Reference (optional)" value={refs[n.agency] ?? ''} onChange={(e) => setRefs({ ...refs, [n.agency]: e.target.value })} />
              <button className="btn btn-link" onClick={() => run(() => api(`/api/quality/incidents/${id}/notifications/${n.agency}`, { body: { reference: refs[n.agency] ?? null } }), 'Notification recorded.')}>Mark notified</button></div>}</td></tr>)}</tbody></table>
      </section>}
      <section className="card stack"><h2>Corrective actions</h2>
        {d.actions.length === 0 ? <p className="muted">None.</p> : <ul>{d.actions.map((a: any) => <li key={a.id}>{a.number} – {a.title} ({a.owner}, due {formatDate(a.dueOn)}) – {a.status}</li>)}</ul>}
        {manage && !closed && <div className="form-grid">
          <label>Action<input value={act.title} onChange={(e) => setAct({ ...act, title: e.target.value })} /></label>
          <label>Owner<input value={act.owner} onChange={(e) => setAct({ ...act, owner: e.target.value })} /></label>
          <label>Due<input type="date" value={act.dueOn} min={todayIso()} onChange={(e) => setAct({ ...act, dueOn: e.target.value })} /></label>
          <div><button className="btn btn-light" onClick={() => run(() => api('/api/quality/actions', { body: { source: 'Incident', sourceId: id, sourceRef: null, title: act.title, detail: null, owner: act.owner, dueOn: act.dueOn || null } }), 'Action added.')}>Add action</button></div></div>}
      </section>
      {manage && !closed && <div><button className="btn btn-primary" onClick={() => run(() => api(`/api/quality/incidents/${id}/close`, { body: {} }), 'Incident closed.')}>Close incident</button></div>}
      {closed && <p className="ok-text">Closed {nzDateTime(d.closedAt)} by {d.closedBy}.</p>}
    </div>
  );
}

function Complaints({ manage }: { manage: boolean }) {
  const navigate = useNavigate();
  const [rows, setRows] = useState<any[] | null>(null);
  const [f, setF] = useState<any>(null);
  const [error, setError] = useState<string | null>(null);
  const load = () => api<any[]>('/api/quality/complaints?open=true').then(setRows).catch((e) => setError(errText(e)));
  useEffect(() => { load(); }, []);
  const save = async () => { setError(null); try { const r = await api('/api/quality/complaints', { body: { ...f, patientId: null, incidentId: null } }); navigate(`/quality/complaints/${r.id}`); } catch (e) { setError(errText(e)); } };
  return (
    <section className="card stack">
      <p className="hint">Right 10 of the Code of Rights: acknowledge in writing within 5 working days; within 10 working days of acknowledging, decide whether the complaint is justified or tell the complainant more time is needed and why. Working days exclude weekends, public holidays and 25 December – 15 January.</p>
      {error && <p className="error">{error}</p>}
      {manage && !f && <div><button className="btn btn-primary" onClick={() => setF({ receivedOn: todayIso(), receivedVia: 'Letter', complainant: '', contact: '', summary: '', category: 'Clinical care', owner: '' })}>Record a complaint</button></div>}
      {f && <div className="stack side-panel card"><div className="form-grid">
        <label>Received<input type="date" value={f.receivedOn} max={todayIso()} onChange={(e) => setF({ ...f, receivedOn: e.target.value })} /></label>
        <label>How<select value={f.receivedVia} onChange={(e) => setF({ ...f, receivedVia: e.target.value })}>{['Letter', 'Email', 'Phone', 'In person', 'Portal', 'HDC', 'Advocacy Service'].map((x) => <option key={x}>{x}</option>)}</select></label>
        <label>Complainant<input value={f.complainant} onChange={(e) => setF({ ...f, complainant: e.target.value })} /></label>
        <label>Contact<input value={f.contact} onChange={(e) => setF({ ...f, contact: e.target.value })} /></label>
        <label>Category<select value={f.category} onChange={(e) => setF({ ...f, category: e.target.value })}>{['Clinical care', 'Communication', 'Billing / cost', 'Waiting time', 'Facilities', 'Privacy', 'Staff attitude', 'Other'].map((x) => <option key={x}>{x}</option>)}</select></label>
        <label>Handled by<input value={f.owner} onChange={(e) => setF({ ...f, owner: e.target.value })} /></label></div>
        <label>Summary<textarea rows={3} value={f.summary} onChange={(e) => setF({ ...f, summary: e.target.value })} /></label>
        <div className="row"><button className="btn btn-primary" onClick={save}>Save</button><button className="btn btn-light" onClick={() => setF(null)}>Cancel</button></div></div>}
      {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">No open complaints.</p> : (
        <table className="results"><thead><tr><th>Number</th><th>Received</th><th>Category</th><th>Status</th><th>Next deadline</th></tr></thead>
          <tbody>{rows.map((c) => <tr key={c.id} className="clickable" onClick={() => navigate(`/quality/complaints/${c.id}`)}><td><Link to={`/quality/complaints/${c.id}`}>{c.number}</Link></td><td>{formatDate(c.receivedOn)}</td><td>{c.category}</td><td>{c.status}</td>
            <td>{c.overdue ? <span className="badge warn">{c.overdue}</span> : c.acknowledgedOn ? (c.respondedOn ? 'Responded' : `Decide by ${formatDate(c.decideBy)}`) : `Acknowledge by ${formatDate(c.acknowledgeBy)}`}</td></tr>)}</tbody></table>)}
    </section>
  );
}

export function QualityComplaint() {
  const { id } = useParams();
  const { can } = useSession();
  const manage = can(Perm.QualityManage);
  const [d, setD] = useState<any>(null);
  const [s, setS] = useState({ on: todayIso(), text: '', outcome: 'Upheld', hdcReferral: false, advocacyInvolved: false });
  const [error, setError] = useState<string | null>(null); const [ok, setOk] = useState<string | null>(null);
  const load = useCallback(() => api(`/api/quality/complaints/${id}`).then(setD).catch((e) => setError(errText(e))), [id]);
  useEffect(() => { load(); }, [load]);
  const step = async (name: string, msg: string) => { setError(null); setOk(null); try { await api(`/api/quality/complaints/${id}/step`, { body: { step: name, on: s.on, text: s.text || null, outcome: name === 'respond' ? s.outcome : null, hdcReferral: s.hdcReferral, advocacyInvolved: s.advocacyInvolved } }); setOk(msg); setS({ ...s, text: '' }); load(); } catch (e) { setError(errText(e)); } };
  if (!d) return error ? <p className="error">{error}</p> : <p>Loading…</p>;
  const c = d.complaint;
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Complaint {c.number}</h1><p className="muted">{c.category} · {d.status} · from {c.complainant} · received {formatDate(c.receivedOn)} ({c.receivedVia})</p></div>
      {d.overdue && <p className="warn-box">{d.overdue}</p>}
      <Msg error={error} ok={ok} />
      <section className="card stack"><p className="prewrap">{c.summary}</p>
        <dl className="facts"><dt>Acknowledge by</dt><dd>{formatDate(c.acknowledgeBy)}{c.acknowledgedOn ? ` – done ${formatDate(c.acknowledgedOn)}` : ''}</dd>
          <dt>Decide by</dt><dd>{c.decideBy ? formatDate(c.decideBy) : 'after acknowledgement'}{c.extensionNotifiedOn ? ` (extended ${formatDate(c.extensionNotifiedOn)}: ${c.extensionReason})` : ''}</dd>
          {c.respondedOn && <><dt>Responded</dt><dd>{formatDate(c.respondedOn)} – {d.outcome}</dd></>}{d.patient && <><dt>Patient</dt><dd>{d.patient}</dd></>}{d.incident && <><dt>Incident</dt><dd>{d.incident}</dd></>}
          <dt>Handled by</dt><dd>{c.owner}</dd></dl>
        {c.response && <p className="prewrap"><strong>Response:</strong> {c.response}</p>}
      </section>
      {manage && d.status !== 'Closed' && <section className="card stack"><h2>Next step</h2>
        <div className="form-grid"><label>Date<input type="date" value={s.on} max={todayIso()} onChange={(e) => setS({ ...s, on: e.target.value })} /></label>
          {c.acknowledgedOn && !c.respondedOn && <label>Outcome<select value={s.outcome} onChange={(e) => setS({ ...s, outcome: e.target.value })}>{[['Upheld', 'Upheld'], ['PartlyUpheld', 'Partly upheld'], ['NotUpheld', 'Not upheld'], ['Withdrawn', 'Withdrawn']].map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>}</div>
        {c.acknowledgedOn && !c.respondedOn && <label>Reason for more time, or summary of the response<textarea rows={3} value={s.text} onChange={(e) => setS({ ...s, text: e.target.value })} /></label>}
        {c.acknowledgedOn && !c.respondedOn && <div className="row"><label className="check"><input type="checkbox" checked={s.hdcReferral} onChange={(e) => setS({ ...s, hdcReferral: e.target.checked })} /> Referred to / by HDC</label>
          <label className="check"><input type="checkbox" checked={s.advocacyInvolved} onChange={(e) => setS({ ...s, advocacyInvolved: e.target.checked })} /> Advocacy Service involved</label></div>}
        <div className="row">
          {!c.acknowledgedOn && <button className="btn btn-primary" onClick={() => step('acknowledge', 'Acknowledged – the decision is due in 10 working days.')}>Acknowledged in writing</button>}
          {c.acknowledgedOn && !c.respondedOn && <><button className="btn btn-light" onClick={() => step('extend', 'Extension recorded.')}>Told the complainant more time is needed</button>
            <button className="btn btn-primary" onClick={() => step('respond', 'Response recorded.')}>Response sent</button></>}
          {c.respondedOn && <button className="btn btn-primary" onClick={() => step('close', 'Complaint closed.')}>Close complaint</button>}
        </div></section>}
    </div>
  );
}

function Compliments({ manage }: { manage: boolean }) {
  const [rows, setRows] = useState<any[] | null>(null);
  const [f, setF] = useState({ receivedOn: todayIso(), from: '', staffNamed: '', text: '' });
  const [error, setError] = useState<string | null>(null);
  const load = () => api<any[]>('/api/quality/compliments').then(setRows).catch((e) => setError(errText(e)));
  useEffect(() => { load(); }, []);
  return (
    <section className="card stack">
      {error && <p className="error">{error}</p>}
      {manage && <div className="form-grid">
        <label>Received<input type="date" value={f.receivedOn} max={todayIso()} onChange={(e) => setF({ ...f, receivedOn: e.target.value })} /></label>
        <label>From<input value={f.from} onChange={(e) => setF({ ...f, from: e.target.value })} /></label>
        <label>Staff named<input value={f.staffNamed} onChange={(e) => setF({ ...f, staffNamed: e.target.value })} /></label>
        <label>What they said<input value={f.text} onChange={(e) => setF({ ...f, text: e.target.value })} /></label>
        <div><button className="btn btn-light" onClick={async () => { try { await api('/api/quality/compliments', { body: f }); setF({ ...f, from: '', staffNamed: '', text: '' }); load(); } catch (e) { setError(errText(e)); } }}>Add compliment</button></div></div>}
      {rows && <table className="results compact"><thead><tr><th>Received</th><th>From</th><th>Staff</th><th>Compliment</th><th>Shared</th></tr></thead>
        <tbody>{rows.map((c) => <tr key={c.id}><td>{formatDate(c.receivedOn)}</td><td>{c.from}</td><td>{c.staffNamed ?? '—'}</td><td className="small">{c.text}</td>
          <td>{c.sharedAt ? nzDateTime(c.sharedAt) : manage ? <button className="btn btn-link" onClick={async () => { await api(`/api/quality/compliments/${c.id}/share`, { body: {} }); load(); }}>Shared with staff</button> : '—'}</td></tr>)}</tbody></table>}
    </section>
  );
}

const blankRisk = (hazard: boolean) => ({ id: null, isHazard: hazard, title: '', category: hazard ? 'HealthAndSafety' : 'Clinical', description: '', owner: '', likelihood: 3, consequence: 3, controls: '', residualLikelihood: '', residualConsequence: '', location: '', equipmentId: null, reviewBy: todayIso(), status: 'Open' });

function Risks({ hazards, manage }: { hazards: boolean; manage: boolean }) {
  const [rows, setRows] = useState<any[] | null>(null);
  const [e, setE] = useState<any>(null);
  const [error, setError] = useState<string | null>(null);
  const load = () => api<any[]>(`/api/quality/risks?hazards=${hazards}`).then(setRows).catch((x) => setError(errText(x)));
  useEffect(() => { load(); setE(null); }, [hazards]); // eslint-disable-line react-hooks/exhaustive-deps
  const save = async () => {
    setError(null);
    const body = { ...e, residualLikelihood: e.residualLikelihood === '' ? null : Number(e.residualLikelihood), residualConsequence: e.residualConsequence === '' ? null : Number(e.residualConsequence), likelihood: Number(e.likelihood), consequence: Number(e.consequence) };
    try { await api(e.id ? `/api/quality/risks/${e.id}` : '/api/quality/risks', { method: e.id ? 'PUT' : 'POST', body }); setE(null); load(); } catch (x) { setError(errText(x)); }
  };
  const n5 = [1, 2, 3, 4, 5];
  return (
    <section className="card stack">
      <p className="hint">{hazards ? 'Hazard register (Health and Safety at Work Act 2015): hazards to staff, patients and visitors, linked to locations and equipment.' : 'Risk register: likelihood × consequence (1–5 each). 1–4 low, 5–9 moderate, 10–16 high, 20–25 extreme.'}</p>
      {error && <p className="error">{error}</p>}
      {manage && !e && <div><button className="btn btn-primary" onClick={() => setE(blankRisk(hazards))}>{hazards ? 'Add hazard' : 'Add risk'}</button></div>}
      {e && <div className="card side-panel stack"><div className="form-grid">
        <label>Title<input value={e.title} onChange={(x) => setE({ ...e, title: x.target.value })} /></label>
        <label>Category<select value={e.category} onChange={(x) => setE({ ...e, category: x.target.value })}>{['Clinical', 'Operational', 'Financial', 'InformationTechnology', 'People', 'Compliance', 'HealthAndSafety'].map((c) => <option key={c} value={c}>{spaced(c)}</option>)}</select></label>
        <label>Owner<input value={e.owner} onChange={(x) => setE({ ...e, owner: x.target.value })} /></label>
        <label>Likelihood<select value={e.likelihood} onChange={(x) => setE({ ...e, likelihood: x.target.value })}>{n5.map((n) => <option key={n}>{n}</option>)}</select></label>
        <label>Consequence<select value={e.consequence} onChange={(x) => setE({ ...e, consequence: x.target.value })}>{n5.map((n) => <option key={n}>{n}</option>)}</select></label>
        <label>Residual likelihood (after controls)<select value={e.residualLikelihood ?? ''} onChange={(x) => setE({ ...e, residualLikelihood: x.target.value })}><option value="">—</option>{n5.map((n) => <option key={n}>{n}</option>)}</select></label>
        <label>Residual consequence<select value={e.residualConsequence ?? ''} onChange={(x) => setE({ ...e, residualConsequence: x.target.value })}><option value="">—</option>{n5.map((n) => <option key={n}>{n}</option>)}</select></label>
        {hazards && <label>Location<input value={e.location ?? ''} onChange={(x) => setE({ ...e, location: x.target.value })} /></label>}
        <label>Review by<input type="date" value={e.reviewBy} onChange={(x) => setE({ ...e, reviewBy: x.target.value })} /></label>
        <label>Status<select value={e.status} onChange={(x) => setE({ ...e, status: x.target.value })}>{['Open', 'Monitoring', 'Closed'].map((s) => <option key={s}>{s}</option>)}</select></label></div>
        <label>Description<textarea rows={2} value={e.description} onChange={(x) => setE({ ...e, description: x.target.value })} /></label>
        <label>Controls<textarea rows={2} value={e.controls ?? ''} onChange={(x) => setE({ ...e, controls: x.target.value })} /></label>
        <div className="row"><button className="btn btn-primary" onClick={save}>Save</button><button className="btn btn-light" onClick={() => setE(null)}>Cancel</button></div></div>}
      {rows && (rows.length === 0 ? <p className="muted">None recorded.</p> : <table className="results"><thead><tr><th>Number</th><th>{hazards ? 'Hazard' : 'Risk'}</th><th>Owner</th><th>Inherent</th><th>Residual</th><th>Review by</th><th>Status</th></tr></thead>
        <tbody>{rows.map((r) => <tr key={r.id} className={manage ? 'clickable' : ''} onClick={() => manage && setE({ ...r, residualLikelihood: r.residualLikelihood ?? '', residualConsequence: r.residualConsequence ?? '' })}>
          <td>{r.number}</td><td><strong>{r.title}</strong><div className="small muted">{spaced(r.category)}{r.location ? ` · ${r.location}` : ''}</div></td><td>{r.owner}</td>
          <td><span className={`band band-${r.band.toLowerCase()}`}>{r.score} {r.band}</span></td><td>{r.residualBand ? <span className={`band band-${r.residualBand.toLowerCase()}`}>{r.residualBand}</span> : '—'}</td><td>{formatDate(r.reviewBy)}</td><td>{r.status}</td></tr>)}</tbody></table>)}
    </section>
  );
}

function Actions({ manage }: { manage: boolean }) {
  const [open, setOpen] = useState(true);
  const [rows, setRows] = useState<any[] | null>(null);
  const [f, setF] = useState({ source: 'InternalAudit', sourceRef: '', title: '', owner: '', dueOn: '' });
  const [ev, setEv] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null); const [ok, setOk] = useState<string | null>(null);
  const load = () => api<any[]>(`/api/quality/actions?open=${open}`).then(setRows).catch((e) => setError(errText(e)));
  useEffect(() => { load(); }, [open]); // eslint-disable-line react-hooks/exhaustive-deps
  const run = async (fn: () => Promise<any>, m: string) => { setError(null); setOk(null); try { await fn(); setOk(m); load(); } catch (e) { setError(errText(e)); } };
  return (
    <section className="card stack">
      <p className="hint">Corrective action register (DAA and internal audits, incidents, complaints, risks): each action has an owner and due date; it is signed off by someone other than the person who did it.</p>
      <Msg error={error} ok={ok} />
      {manage && <div className="form-grid">
        <label>From<select value={f.source} onChange={(e) => setF({ ...f, source: e.target.value })}>{[['InternalAudit', 'Internal audit'], ['DaaAudit', 'DAA audit'], ['Feedback', 'Feedback'], ['Other', 'Other']].map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
        <label>Reference<input value={f.sourceRef} onChange={(e) => setF({ ...f, sourceRef: e.target.value })} placeholder="e.g. DAA 2026 CAR 3" /></label>
        <label>Action<input value={f.title} onChange={(e) => setF({ ...f, title: e.target.value })} /></label>
        <label>Owner<input value={f.owner} onChange={(e) => setF({ ...f, owner: e.target.value })} /></label>
        <label>Due<input type="date" value={f.dueOn} min={todayIso()} onChange={(e) => setF({ ...f, dueOn: e.target.value })} /></label>
        <div><button className="btn btn-light" onClick={() => run(() => api('/api/quality/actions', { body: { ...f, sourceId: null, detail: null, dueOn: f.dueOn || null } }), 'Action added.')}>Add action</button></div></div>}
      <label className="check"><input type="checkbox" checked={open} onChange={(e) => setOpen(e.target.checked)} /> Not yet signed off only</label>
      {rows && (rows.length === 0 ? <p className="muted">None.</p> : <table className="results"><thead><tr><th>Number</th><th>Action</th><th>Owner</th><th>Due</th><th>Status</th><th><span className="sr-only">Actions</span></th></tr></thead>
        <tbody>{rows.map((a) => <tr key={a.id}><td>{a.number}<div className="small muted">{spaced(a.source)} {a.sourceRef ?? ''}</div></td><td>{a.title}{a.evidence && <div className="small muted">Evidence: {a.evidence}</div>}</td><td>{a.owner}</td>
          <td>{formatDate(a.dueOn)}{a.overdue && <span className="badge warn"> Overdue</span>}</td><td>{a.status}{a.verifiedBy ? ` (${a.verifiedBy})` : ''}</td>
          <td>{manage && a.status === 'Open' && <div className="row"><input aria-label={`Evidence for ${a.number}`} value={ev[a.id] ?? ''} onChange={(e) => setEv({ ...ev, [a.id]: e.target.value })} placeholder="Evidence" />
            <button className="btn btn-link" onClick={() => run(() => api(`/api/quality/actions/${a.id}/done`, { body: { evidence: ev[a.id] ?? '' } }), `${a.number} done.`)}>Done</button></div>}
            {manage && a.status === 'Done' && <button className="btn btn-link" onClick={() => run(() => api(`/api/quality/actions/${a.id}/verify`, { body: {} }), `${a.number} signed off.`)}>Sign off</button>}</td></tr>)}</tbody></table>)}
    </section>
  );
}

function Policies({ manage }: { manage: boolean }) {
  const [rows, setRows] = useState<any[] | null>(null);
  const [e, setE] = useState<any>(null);
  const [error, setError] = useState<string | null>(null);
  const load = () => api<any[]>('/api/quality/policies').then(setRows).catch((x) => setError(errText(x)));
  useEffect(() => { load(); }, []);
  const save = async () => { setError(null); try { await api(e.id ? `/api/quality/policies/${e.id}` : '/api/quality/policies', { method: e.id ? 'PUT' : 'POST', body: { ...e, link: e.link || null } }); setE(null); load(); } catch (x) { setError(errText(x)); } };
  return (
    <section className="card stack">
      <p className="hint">Policy and procedure register. The documents stay in SharePoint; BrightPAS keeps the version, owner, review date and who has read the current version.</p>
      {error && <p className="error">{error}</p>}
      {manage && !e && <div><button className="btn btn-primary" onClick={() => setE({ id: null, code: '', title: '', version: '1.0', owner: '', link: '', reviewBy: todayIso(), requiresAcknowledgement: true, isActive: true })}>Add document</button></div>}
      {e && <div className="card side-panel stack"><div className="form-grid">
        <label>Code<input value={e.code} onChange={(x) => setE({ ...e, code: x.target.value })} placeholder="e.g. TDS-5.9" /></label>
        <label>Title<input value={e.title} onChange={(x) => setE({ ...e, title: x.target.value })} /></label>
        <label>Version<input value={e.version} onChange={(x) => setE({ ...e, version: x.target.value })} /></label>
        <label>Owner<input value={e.owner} onChange={(x) => setE({ ...e, owner: x.target.value })} /></label>
        <label>Link (SharePoint)<input value={e.link ?? ''} onChange={(x) => setE({ ...e, link: x.target.value })} placeholder="https://…" /></label>
        <label>Review by<input type="date" value={e.reviewBy} onChange={(x) => setE({ ...e, reviewBy: x.target.value })} /></label></div>
        <label className="check"><input type="checkbox" checked={e.requiresAcknowledgement} onChange={(x) => setE({ ...e, requiresAcknowledgement: x.target.checked })} /> Staff must read and acknowledge it</label>
        <label className="check"><input type="checkbox" checked={e.isActive} onChange={(x) => setE({ ...e, isActive: x.target.checked })} /> Current</label>
        <div className="row"><button className="btn btn-primary" onClick={save}>Save</button><button className="btn btn-light" onClick={() => setE(null)}>Cancel</button></div></div>}
      {rows && <table className="results"><thead><tr><th>Code</th><th>Title</th><th>Version</th><th>Owner</th><th>Review by</th><th>Read and acknowledge</th></tr></thead>
        <tbody>{rows.map((p) => <tr key={p.id} className={manage ? 'clickable' : ''} onClick={() => manage && setE({ ...p, link: p.link ?? '' })}><td>{p.code}</td><td>{p.title}{!p.isActive && <span className="badge off"> Retired</span>}</td><td>{p.version}</td><td>{p.owner}</td>
          <td>{formatDate(p.reviewBy)}{p.reviewBy < todayIso() && <span className="badge warn"> Due</span>}</td><td>{p.requiresAcknowledgement ? 'Yes' : 'No'}</td></tr>)}</tbody></table>}
    </section>
  );
}

/** SPC chart drawn with SVG attributes only (the Content-Security-Policy forbids inline styles). */
function SpcChart({ s }: { s: any }) {
  const pts = s.points as any[];
  const vals = pts.flatMap((p) => [p.rate, p.ucl, p.lcl]).filter((v) => v != null) as number[];
  if (s.indicator.target != null) vals.push(s.indicator.target);
  if (vals.length === 0) return <p className="muted">No data yet.</p>;
  const W = 640, H = 220, L = 48, B = 28, T = 10;
  const max = Math.max(...vals) * 1.1 || 1, min = 0;
  const x = (i: number) => L + (i * (W - L - 10)) / Math.max(1, pts.length - 1);
  const y = (v: number) => T + (H - T - B) * (1 - (v - min) / (max - min));
  const path = (key: string) => pts.map((p, i) => p[key] == null ? null : `${x(i)},${y(p[key])}`).filter(Boolean).join(' ');
  return (
    <svg viewBox={`0 0 ${W} ${H}`} className="spc" role="img" aria-label={`${s.indicator.name}: ${s.chart}-chart of the last ${pts.length} months`}>
      <line x1={L} y1={T} x2={L} y2={H - B} stroke="#9AA9B4" />
      <line x1={L} y1={H - B} x2={W - 10} y2={H - B} stroke="#9AA9B4" />
      {[0, 0.5, 1].map((f) => <text key={f} x={L - 6} y={y(min + f * (max - min)) + 4} textAnchor="end" fontSize="10" fill="#5B6B77">{(min + f * (max - min)).toFixed(1)}</text>)}
      {pts.map((p, i) => <text key={i} x={x(i)} y={H - 10} textAnchor="middle" fontSize="10" fill="#5B6B77">{new Date(p.month).toLocaleDateString('en-NZ', { month: 'short' })}</text>)}
      <polyline points={path('ucl')} fill="none" stroke="#C0392B" strokeDasharray="4 3" />
      <polyline points={path('lcl')} fill="none" stroke="#C0392B" strokeDasharray="4 3" />
      {s.centre != null && <line x1={L} x2={W - 10} y1={y(s.centre)} y2={y(s.centre)} stroke="#24688A" />}
      {s.indicator.target != null && <line x1={L} x2={W - 10} y1={y(s.indicator.target)} y2={y(s.indicator.target)} stroke="#2E7D32" strokeDasharray="2 2" />}
      <polyline points={path('rate')} fill="none" stroke="#1B2730" strokeWidth="2" />
      {pts.map((p, i) => p.rate == null ? null : <circle key={i} cx={x(i)} cy={y(p.rate)} r={p.outsideLimits || p.shift || p.trend ? 5 : 3.5} fill={p.outsideLimits ? '#C0392B' : p.shift || p.trend ? '#B45309' : '#1B2730'}>
        <title>{`${new Date(p.month).toLocaleDateString('en-NZ', { month: 'long', year: 'numeric' })}: ${rate(p.rate, s.indicator.scale)} (${p.numerator}${p.denominator != null ? ` / ${p.denominator}` : ''})${p.outsideLimits ? ' – outside control limits' : ''}${p.shift ? ' – shift' : ''}${p.trend ? ' – trend' : ''}`}</title></circle>)}
    </svg>
  );
}

function Indicators({ manage }: { manage: boolean }) {
  const [params, setParams] = useSearchParams();
  const sel = params.get('id');
  const [d, setD] = useState<any>(null);
  const [s, setS] = useState<any>(null);
  const [cases, setCases] = useState<{ month: string; rows: any[] } | null>(null);
  const [v, setV] = useState({ month: todayIso().slice(0, 7), numerator: '', denominator: '' });
  const [error, setError] = useState<string | null>(null); const [ok, setOk] = useState<string | null>(null);
  useEffect(() => { api('/api/quality/indicators').then(setD).catch((e) => setError(errText(e))); }, []);
  const loadSeries = useCallback(() => { if (sel) api(`/api/quality/indicators/${sel}/series?months=12`).then(setS).catch((e) => setError(errText(e))); else setS(null); setCases(null); }, [sel]);
  useEffect(loadSeries, [loadSeries]);
  const enter = async () => { setError(null); setOk(null); try { await api(`/api/quality/indicators/${sel}/values`, { body: { month: `${v.month}-01`, numerator: Number(v.numerator), denominator: v.denominator === '' ? null : Number(v.denominator) } }); setOk('Saved.'); loadSeries(); } catch (e) { setError(errText(e)); } };
  if (!d) return error ? <p className="error">{error}</p> : <p>Loading…</p>;
  return (
    <div className="stack">
      <Msg error={error} ok={ok} />
      <section className="card stack"><table className="results compact"><thead><tr><th>Code</th><th>Indicator</th><th>Source</th><th>Target</th><th>Owner</th></tr></thead>
        <tbody>{d.indicators.map((i: any) => <tr key={i.id} className={`clickable ${sel === i.id ? 'selected' : ''}`} onClick={() => setParams({ tab: 'indicators', id: i.id })}><td>{i.code}</td><td><button className="btn btn-link row-toggle" onClick={(e) => { e.stopPropagation(); setParams({ tab: 'indicators', id: i.id }); }}>{i.name}</button></td>
          <td className="small">{i.calculator ? 'Calculated' : 'Entered monthly'}</td><td>{rate(i.target, i.scale)}</td><td>{i.owner}</td></tr>)}</tbody></table></section>
      {s && <section className="card stack">
        <h2>{s.indicator.name}</h2>
        <p className="small">{s.indicator.definition} <strong>Numerator:</strong> {s.indicator.numerator}. <strong>Denominator:</strong> {s.indicator.denominator}. Chart: {s.chart === 'p' ? 'p-chart (proportion)' : s.chart === 'u' ? 'u-chart (rate)' : 'c-chart (count)'}; blue line = 12-month centre, red dashes = 3-sigma limits, green dots = target.</p>
        <p><span className={`rag rag-${s.rag}`}>{s.rag === 'none' ? 'No target' : s.rag === 'green' ? 'On target' : s.rag === 'amber' ? 'Watch' : 'Off target'}</span> Latest: <strong>{rate(s.latest, s.indicator.scale)}</strong> · centre {rate(s.centre, s.indicator.scale)}</p>
        <SpcChart s={s} />
        <table className="results compact"><thead><tr><th>Month</th><th>Numerator</th><th>Denominator</th><th>Rate</th><th>Signal</th></tr></thead>
          <tbody>{s.points.map((p: any) => <tr key={p.month}><td>{s.indicator.calculator && manage ? <button className="btn btn-link row-toggle" onClick={async () => setCases({ month: p.month, rows: await api(`/api/quality/indicators/${sel}/cases?month=${p.month}`) })}>{new Date(p.month).toLocaleDateString('en-NZ', { month: 'short', year: 'numeric' })}</button> : new Date(p.month).toLocaleDateString('en-NZ', { month: 'short', year: 'numeric' })}</td>
            <td>{p.numerator}</td><td>{p.denominator ?? '—'}</td><td>{rate(p.rate, s.indicator.scale)}</td><td>{[p.outsideLimits && 'Outside limits', p.shift && 'Shift', p.trend && 'Trend'].filter(Boolean).join(', ')}</td></tr>)}</tbody></table>
        {s.equity.length > 0 && <><h3>Equity view (12 months, prioritised ethnicity)</h3>
          <table className="results compact"><thead><tr><th>Group</th><th>Numerator</th><th>Denominator</th><th>Rate</th></tr></thead>
            <tbody>{s.equity.map((e: any) => <tr key={e.group}><td>{e.group}</td><td>{e.numerator}</td><td>{e.denominator}</td><td>{rate(e.rate, s.indicator.scale)}</td></tr>)}</tbody></table></>}
        {cases && <><h3>Cases – {new Date(cases.month).toLocaleDateString('en-NZ', { month: 'long', year: 'numeric' })}</h3>
          {cases.rows.length === 0 ? <p className="muted">None.</p> : <table className="results compact"><thead><tr><th>Date</th><th>Case</th><th>Counted in</th><th>Group</th></tr></thead>
            <tbody>{cases.rows.map((c: any, n: number) => <tr key={n}><td>{formatDate(c.on)}</td><td>{c.link ? <Link to={c.link}>{c.label}</Link> : c.label}</td><td>{c.numeratorCount > 0 ? 'Numerator' : 'Denominator only'}</td><td>{c.group}</td></tr>)}</tbody></table>}</>}
        {manage && !s.indicator.calculator && <div className="form-grid">
          <label>Month<input type="month" value={v.month} onChange={(e) => setV({ ...v, month: e.target.value })} /></label>
          <label>Numerator<input type="number" value={v.numerator} onChange={(e) => setV({ ...v, numerator: e.target.value })} /></label>
          <label>Denominator<input type="number" value={v.denominator} onChange={(e) => setV({ ...v, denominator: e.target.value })} /></label>
          <div><button className="btn btn-light" onClick={enter}>Save month</button></div></div>}
      </section>}
    </div>
  );
}

function Settings() {
  const [s, setS] = useState<any>(null);
  const [error, setError] = useState<string | null>(null); const [ok, setOk] = useState<string | null>(null);
  useEffect(() => { api('/api/quality/settings').then((x) => setS({ escalateTo: x.escalateTo, extraNonWorkingDays: x.extraNonWorkingDays ?? '' })).catch((e) => setError(errText(e))); }, []);
  if (!s) return error ? <p className="error">{error}</p> : <p>Loading…</p>;
  return (
    <section className="card stack">
      <Msg error={error} ok={ok} />
      <label>Alert about SAC 1–2 and Always Report and Review events<input value={s.escalateTo} onChange={(e) => setS({ ...s, escalateTo: e.target.value })} /></label>
      <label>Extra non-working days for complaint timeframes (yyyy-mm-dd, comma-separated – e.g. Nelson Anniversary Day, closure days)<textarea rows={2} value={s.extraNonWorkingDays} onChange={(e) => setS({ ...s, extraNonWorkingDays: e.target.value })} /></label>
      <div><button className="btn btn-primary" onClick={async () => { setError(null); setOk(null); try { await api('/api/quality/settings', { method: 'PUT', body: s }); setOk('Settings saved.'); } catch (e) { setError(errText(e)); } }}>Save settings</button></div>
    </section>
  );
}
