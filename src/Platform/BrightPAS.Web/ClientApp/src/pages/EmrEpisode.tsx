import { useEffect, useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router';
import { api, errText, formatDate, nzDateTime, Perm, todayIso } from '../api';
import { useSession } from '../App';

const STAGE: Record<string, string> = { PreAdmission: 'Pre-admission', Admitted: 'Admitted', InTheatre: 'In theatre', Recovery: 'Recovery', ReadyForDischarge: 'Ready for discharge', Discharged: 'Discharged', Transferred: 'Transferred to hospital' };
const RISK: Record<string, string> = { Low: 'pill green', Moderate: 'pill', High: 'pill bad' };
const localNow = () => { const d = new Date(); d.setMinutes(d.getMinutes() - d.getTimezoneOffset()); return d.toISOString().slice(0, 16); };
const iso = (v: string) => v ? new Date(v).toISOString() : null;
const toLocal = (v: string | null) => { if (!v) return ''; const d = new Date(v); d.setMinutes(d.getMinutes() - d.getTimezoneOffset()); return d.toISOString().slice(0, 16); };

type Ctx = { r: any; reload: () => Promise<void>; act: (fn: () => Promise<any>, ok?: string | ((x: any) => string)) => Promise<any>; write: boolean };

/** The clinical record of one theatre/procedure booking. */
export default function EmrEpisode() {
  const { bookingId } = useParams();
  const { can } = useSession();
  const [params, setParams] = useSearchParams();
  const tab = params.get('tab') ?? 'admission';
  const [r, setR] = useState<any>(null);
  const [error, setError] = useState<string | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const reload = async () => { setR(await api(`/api/emr/bookings/${bookingId}`)); };
  useEffect(() => { reload().catch((e) => setError(errText(e))); }, [bookingId]); // eslint-disable-line react-hooks/exhaustive-deps
  const act = async (fn: () => Promise<any>, ok?: string | ((x: any) => string)) => {
    setError(null); setMsg(null);
    try { const x = await fn(); if (ok) setMsg(typeof ok === 'function' ? ok(x) : ok); await reload(); return x ?? true; } catch (x: any) { setError(x?.body?.errors?.join(' ') ?? errText(x)); return null; }
  };
  if (!r) return error ? <div className="card"><p className="error">{error}</p></div> : <p>Loading…</p>;
  const h = r.header;
  const closed = r.episode && ['Discharged', 'Transferred'].includes(r.episode.stage);
  const write = can(Perm.EmrWrite) && !!r.episode && !closed;
  const ctx: Ctx = { r, reload, act, write };
  const tabs: [string, string][] = [['admission', 'Admission & risk'], ['checklist', 'Safety checklist'], ['theatre', 'Theatre record'], ['counts', 'Counts & specimens'], ['obs', 'Observations'], ['meds', 'Medicines & fluids'], ['notes', 'Notes'], ['discharge', 'Discharge']];
  return (
    <div className="stack-lg">
      <div className="patient-banner"><div><h1>{h.name}</h1><p>NHI {h.nhi ?? '—'} · {h.mrn} · born {formatDate(h.dateOfBirth)} · {h.sex}</p>
        <p>{h.procedure}{h.laterality !== 'NotApplicable' ? ` (${h.laterality.toLowerCase()})` : ''} · {formatDate(h.date)} · {h.surgeon}{h.anaesthetist ? ` · ${h.anaesthetist}` : ''} · {h.anaesthetic}</p></div>
        <div className="row"><Link className="btn btn-light btn-small" to="/emr">Today's patients</Link><Link className="btn btn-light btn-small" to={`/patients/${h.patientId}`}>Patient record</Link></div></div>
      {h.alerts.map((a: any, i: number) => <p key={i} className={a.severity === 'Critical' ? 'alert-critical' : 'alert-warning'}><strong>{a.severity === 'Critical' ? 'Critical alert' : 'Alert'}:</strong> {a.type} – {a.description}</p>)}
      {error && <p className="error" role="alert">{error}</p>}{msg && <p className="ok-text" role="status">{msg}</p>}
      {!r.episode ? <section className="card"><p className="muted">No clinical record has been started for this booking.</p></section> : <section className="card stack">
        <div className="row-between"><p><strong>{STAGE[r.episode.stage]}</strong>{closed && ` – ${nzDateTime(r.episode.dischargedAt)} by ${r.episode.dischargedBy}`}</p>
          {write && r.episode.stage !== 'PreAdmission' && <div className="row">{['Admitted', 'InTheatre', 'Recovery', 'ReadyForDischarge'].filter((s) => s !== r.episode.stage).map((s) =>
            <button key={s} className="btn btn-light btn-small" onClick={() => act(() => api(`/api/emr/episodes/${r.episode.id}/stage`, { body: { stage: s } }), `Now: ${STAGE[s]}.`)}>{STAGE[s]}</button>)}</div>}</div>
        <div className="tabs" role="tablist">{tabs.map(([k, t]) => <button key={k} role="tab" aria-selected={tab === k} className={tab === k ? 'on' : ''} onClick={() => setParams({ tab: k })}>{t}</button>)}</div>
        {tab === 'admission' && <Admission {...ctx} />}
        {tab === 'checklist' && <Checklist {...ctx} />}
        {tab === 'theatre' && <Theatre {...ctx} />}
        {tab === 'counts' && <Counts {...ctx} />}
        {tab === 'obs' && <Obs {...ctx} />}
        {tab === 'meds' && <Meds {...ctx} />}
        {tab === 'notes' && <Notes {...ctx} />}
        {tab === 'discharge' && <Discharge {...ctx} />}
      </section>}
    </div>
  );
}

const SCREENS: Record<string, { title: string; fields: [string, string, string[]?][] }> = {
  NursingAdmission: { title: 'Nursing admission', fields: [['id_band', 'Identity band on and checked', ['yes', 'no']], ['allergies_checked', 'Allergies (or "none known")'], ['fasting_from', 'Nil by mouth since (time)'], ['escort_name', 'Escort home (name)'], ['escort_phone', 'Escort phone'], ['medicines_today', 'Medicines taken today'], ['valuables', 'Valuables / dentures / implants noted']] },
  Vte: { title: 'VTE risk (Caprini-based)', fields: [['age', 'Age'], ['major_surgery', 'Surgery over 45 minutes', ['no', 'yes']], ['bmi_over_25', 'BMI over 25', ['no', 'yes']], ['previous_vte', 'Previous DVT/PE', ['no', 'yes']], ['cancer', 'Active cancer', ['no', 'yes']], ['oestrogen', 'Oral contraceptive / HRT', ['no', 'yes']], ['immobile', 'Reduced mobility', ['no', 'yes']], ['thrombophilia', 'Known clotting disorder', ['no', 'yes']]] },
  Falls: { title: 'Falls risk', fields: [['fallen_last_year', 'Fallen in the last year', ['no', 'yes']], ['mobility_aid', 'Uses a walking aid', ['no', 'yes']], ['sedation', 'Sedation/anaesthetic today', ['yes', 'no']], ['confused', 'Confused or agitated', ['no', 'yes']], ['toileting', 'Needs help to toilet', ['no', 'yes']], ['vision', 'Poor eyesight', ['no', 'yes']], ['age', 'Age']] },
  PressureInjury: { title: 'Pressure injury (Braden)', fields: [['sensory', 'Sensory perception (1–4)', ['4', '3', '2', '1']], ['moisture', 'Moisture (1–4)', ['4', '3', '2', '1']], ['activity', 'Activity (1–4)', ['4', '3', '2', '1']], ['mobility', 'Mobility (1–4)', ['4', '3', '2', '1']], ['nutrition', 'Nutrition (1–4)', ['4', '3', '2', '1']], ['friction', 'Friction and shear (1–3)', ['3', '2', '1']]] },
  Frailty: { title: 'Clinical Frailty Scale', fields: [['cfs', 'Level 1 (very fit) to 9 (terminally ill)', ['1', '2', '3', '4', '5', '6', '7', '8', '9']]] },
  Delirium: { title: 'Delirium (4AT)', fields: [['alertness', 'Alertness (0 normal, 4 altered)', ['0', '4']], ['amt4', 'AMT4 (0 none wrong, 1 one wrong, 2 two or more)', ['0', '1', '2']], ['attention', 'Attention – months backwards (0, 1, 2)', ['0', '1', '2']], ['acute_change', 'Acute change or fluctuating course (0 or 4)', ['0', '4']]] },
};

function Admission({ r, act, write }: Ctx) {
  const [kind, setKind] = useState('NursingAdmission');
  const [ans, setAns] = useState<Record<string, string>>({});
  const [score, setScore] = useState<any>(null);
  const [sedation, setSedation] = useState(true);
  const screen = SCREENS[kind];
  useEffect(() => { const init: Record<string, string> = {}; screen.fields.forEach(([k, , opts]) => { if (opts) init[k] = opts[0]; }); setAns(init); setScore(null); }, [kind]); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => { if (kind !== 'NursingAdmission') api('/api/emr/assessments/score', { body: { kind, answers: ans, actions: null } }).then(setScore).catch(() => undefined); }, [ans, kind]);
  const done = (k: string) => r.assessments.find((a: any) => a.kind === k);
  return (
    <div className="stack">
      <table className="results compact"><thead><tr><th>Screen</th><th>Result</th><th>Actions</th><th>By</th></tr></thead>
        <tbody>{Object.entries(SCREENS).map(([k, s]) => { const a = done(k); return <tr key={k}><td>{s.title}</td><td>{a ? (k === 'NursingAdmission' ? 'Done' : <><span className={RISK[a.risk]}>{a.risk}</span> ({a.score})</>) : <span className="muted">Not done</span>}</td>
          <td className="small">{a?.actions ?? ''}</td><td className="small">{a ? `${a.recordedBy}, ${nzDateTime(a.recordedAt)}` : ''}</td></tr>; })}</tbody></table>
      {write && <div className="card subtle stack">
        <label>Screen<select value={kind} onChange={(e) => setKind(e.target.value)}>{Object.entries(SCREENS).map(([k, s]) => <option key={k} value={k}>{s.title}</option>)}</select></label>
        <div className="form-grid">{screen.fields.map(([k, label, opts]) => <label key={k}>{label}{opts ? <select value={ans[k] ?? ''} onChange={(e) => setAns({ ...ans, [k]: e.target.value })}>{opts.map((o) => <option key={o}>{o}</option>)}</select>
          : <input value={ans[k] ?? ''} onChange={(e) => setAns({ ...ans, [k]: e.target.value })} />}</label>)}</div>
        {score && <p>Score <strong>{score.score}</strong> – <span className={RISK[score.risk]}>{score.risk}</span> {score.advice}</p>}
        <div><button className="btn btn-primary" onClick={() => act(() => api(`/api/emr/episodes/${r.episode.id}/assessments`, { body: { kind, answers: ans, actions: null } }), `${screen.title} saved.`)}>Save {screen.title.toLowerCase()}</button></div>
      </div>}
      {write && r.episode.stage === 'PreAdmission' && <div className="card subtle row">
        <label className="check"><input type="checkbox" checked={sedation} onChange={(e) => setSedation(e.target.checked)} /> Having sedation or anaesthetic (escort and overnight carer needed)</label>
        <button className="btn btn-primary" onClick={() => act(() => api(`/api/emr/episodes/${r.episode.id}/admit`, { body: { sedationOrAnaesthesia: sedation } }), 'Patient admitted.')}>Admit patient</button></div>}
    </div>
  );
}

function Checklist({ r, act, write }: Ctx) {
  const phases = ['SignIn', 'TimeOut', 'SignOut'];
  const label: Record<string, string> = { SignIn: 'Sign In (before anaesthesia)', TimeOut: 'Time Out (before skin incision)', SignOut: 'Sign Out (before the patient leaves theatre)' };
  const next = phases.find((p) => !r.checks.some((c: any) => c.phase === p));
  const [ticks, setTicks] = useState<string[]>([]);
  const [team, setTeam] = useState<Record<string, string>>({});
  useEffect(() => { setTicks([]); }, [next]);
  return (
    <div className="stack">
      {phases.map((p) => { const c = r.checks.find((x: any) => x.phase === p); return <div key={p} className="row-between"><strong>{label[p]}</strong>
        {c ? <span><span className="pill green">Done</span> {nzDateTime(c.completedAt)} · {c.team.map((t: any) => `${t.role}: ${t.name}`).join(', ')}</span> : <span className="pill">Not done</span>}</div>; })}
      {r.countProblems.length > 0 && next === 'SignOut' && <p className="error-text">Counts: {r.countProblems.join(' ')}</p>}
      {write && next && <div className="card subtle stack">
        <h2>{label[next]}</h2>
        {r.checklist[next].items.map((i: any) => <label key={i.key} className="check"><input type="checkbox" checked={ticks.includes(i.key)} onChange={(e) => setTicks(e.target.checked ? [...ticks, i.key] : ticks.filter((x) => x !== i.key))} /> {i.text}</label>)}
        <div className="form-grid">{r.checklist[next].roles.map((role: string) => <label key={role}>{role}<input value={team[role] ?? ''} onChange={(e) => setTeam({ ...team, [role]: e.target.value })} placeholder="Name" /></label>)}</div>
        <div><button className="btn btn-primary" onClick={() => act(() => api(`/api/emr/episodes/${r.episode.id}/checks`, { body: { phase: next, confirmed: ticks, team, notes: null } }), `${label[next]} complete.`)}>Complete {next === 'SignIn' ? 'Sign In' : next === 'TimeOut' ? 'Time Out' : 'Sign Out'}</button></div>
      </div>}
    </div>
  );
}

function Theatre({ r, act, write }: Ctx) {
  const i = r.intraop ?? {};
  const [f, setF] = useState<any>({ ...i, inTheatreAt: toLocal(i.inTheatreAt), anaestheticStartAt: toLocal(i.anaestheticStartAt), knifeToSkinAt: toLocal(i.knifeToSkinAt), closureAt: toLocal(i.closureAt),
    outOfTheatreAt: toLocal(i.outOfTheatreAt), tourniquetOnAt: toLocal(i.tourniquetOnAt), tourniquetOffAt: toLocal(i.tourniquetOffAt), smokeEvacuation: !!i.smokeEvacuation });
  const t = (k: string, l: string) => <label>{l}<span className="row"><input type="datetime-local" value={f[k] ?? ''} disabled={!write} onChange={(e) => setF({ ...f, [k]: e.target.value })} />{write && <button type="button" className="btn btn-link" onClick={() => setF({ ...f, [k]: localNow() })}>Now</button>}</span></label>;
  const x = (k: string, l: string, ph?: string) => <label>{l}<input value={f[k] ?? ''} disabled={!write} placeholder={ph} onChange={(e) => setF({ ...f, [k]: e.target.value })} /></label>;
  const save = () => act(() => api(`/api/emr/episodes/${r.episode.id}/intraop`, { method: 'PUT', body: {
    ...f, inTheatreAt: iso(f.inTheatreAt), anaestheticStartAt: iso(f.anaestheticStartAt), knifeToSkinAt: iso(f.knifeToSkinAt), closureAt: iso(f.closureAt), outOfTheatreAt: iso(f.outOfTheatreAt),
    tourniquetOnAt: iso(f.tourniquetOnAt), tourniquetOffAt: iso(f.tourniquetOffAt), tourniquetPressure: f.tourniquetPressure ? Number(f.tourniquetPressure) : null, woundClass: f.woundClass || null,
    padSiteSkinIntactAfter: f.padSiteSkinIntactAfter === '' || f.padSiteSkinIntactAfter == null ? null : f.padSiteSkinIntactAfter === true || f.padSiteSkinIntactAfter === 'true' } }),
    (res) => res?.warnings?.length ? 'Saved. ' + res.warnings.join(' ') : 'Theatre record saved.');
  return (
    <div className="stack">
      <div className="form-grid">{t('inTheatreAt', 'Into theatre')}{t('anaestheticStartAt', 'Anaesthetic start')}{t('knifeToSkinAt', 'Knife to skin')}{t('closureAt', 'Closure')}{t('outOfTheatreAt', 'Out of theatre')}</div>
      <div className="form-grid">{x('position', 'Position', 'e.g. Supine, arms out')}{x('positioningAids', 'Positioning aids / pressure care')}{x('skinPrep', 'Skin prep', 'e.g. Chlorhexidine 2% in alcohol')}
        {x('diathermyUnit', 'Diathermy unit', 'Asset number')}{x('diathermyPadSite', 'Diathermy pad site')}
        <label>Skin under pad intact after<select value={f.padSiteSkinIntactAfter == null ? '' : String(f.padSiteSkinIntactAfter)} disabled={!write} onChange={(e) => setF({ ...f, padSiteSkinIntactAfter: e.target.value })}><option value="">—</option><option value="true">Yes</option><option value="false">No – reported</option></select></label>
        {x('tourniquetSite', 'Tourniquet site')}{x('tourniquetPressure', 'Tourniquet pressure (mmHg)')}{t('tourniquetOnAt', 'Tourniquet on')}{t('tourniquetOffAt', 'Tourniquet off')}
        <label>Wound class<select value={f.woundClass ?? ''} disabled={!write} onChange={(e) => setF({ ...f, woundClass: e.target.value })}><option value="">—</option>{[['Clean', 'Clean'], ['CleanContaminated', 'Clean-contaminated'], ['Contaminated', 'Contaminated'], ['Dirty', 'Dirty / infected']].map(([k, l]) => <option key={k} value={k}>{l}</option>)}</select></label>
        {x('drains', 'Drains')}{x('dressings', 'Dressings')}
        <label className="check"><input type="checkbox" checked={f.smokeEvacuation} disabled={!write} onChange={(e) => setF({ ...f, smokeEvacuation: e.target.checked })} /> Surgical smoke evacuation used</label></div>
      <label>Notes<textarea rows={3} value={f.notes ?? ''} disabled={!write} onChange={(e) => setF({ ...f, notes: e.target.value })} /></label>
      {write && <div><button className="btn btn-primary" onClick={save}>Save theatre record</button></div>}
      {i.updatedBy && <p className="small muted">Last saved by {i.updatedBy}, {nzDateTime(i.updatedAt)}</p>}
    </div>
  );
}

function Counts({ r, act, write }: Ctx) {
  const [c, setC] = useState({ stage: 'Initial', item: 'Swabs', expected: '', counted: '', checkedBy: '' });
  const [sp, setSp] = useState({ site: '', test: 'Histology', container: '' });
  const [res, setRes] = useState<Record<string, string>>({});
  return (
    <div className="stack">
      <h2>Surgical counts</h2>
      {r.countProblems.length > 0 && <p className="error-text">{r.countProblems.join(' ')}</p>}
      <table className="results compact"><thead><tr><th>Stage</th><th>Item</th><th>Counted / expected</th><th>By / checked</th><th></th></tr></thead>
        <tbody>{r.counts.map((x: any) => <tr key={x.id}><td>{x.stage}</td><td>{x.item}</td><td>{x.counted === x.expected ? `${x.counted} / ${x.expected}` : <span className="error-text">{x.counted} / {x.expected}</span>}</td>
          <td className="small">{x.countedBy} / {x.checkedBy} · {nzDateTime(x.at)}</td>
          <td>{x.counted !== x.expected && (x.resolved ? <span className="small">Resolved: {x.resolutionNote}</span> : write && <span className="row"><input aria-label="How it was resolved" value={res[x.id] ?? ''} onChange={(e) => setRes({ ...res, [x.id]: e.target.value })} placeholder="How it was resolved" />
            <button className="btn btn-link" onClick={() => act(() => api(`/api/emr/counts/${x.id}/resolve`, { body: { note: res[x.id] ?? '' } }), 'Recorded.')}>Resolve</button></span>)}</td></tr>)}</tbody></table>
      {write && <div className="card subtle form-grid">
        <label>Stage<select value={c.stage} onChange={(e) => setC({ ...c, stage: e.target.value })}>{['Initial', 'Additional', 'Closing', 'Final'].map((s) => <option key={s}>{s}</option>)}</select></label>
        <label>Item<select value={c.item} onChange={(e) => setC({ ...c, item: e.target.value })}>{['Swabs', 'Sharps', 'Instruments', 'Other'].map((s) => <option key={s}>{s}</option>)}</select></label>
        <label>Expected<input type="number" value={c.expected} onChange={(e) => setC({ ...c, expected: e.target.value })} /></label>
        <label>Counted<input type="number" value={c.counted} onChange={(e) => setC({ ...c, counted: e.target.value })} /></label>
        <label>Second person<input value={c.checkedBy} onChange={(e) => setC({ ...c, checkedBy: e.target.value })} /></label>
        <div><button className="btn btn-primary" onClick={() => act(() => api(`/api/emr/episodes/${r.episode.id}/counts`, { body: { ...c, expected: Number(c.expected), counted: Number(c.counted) } }), (x) => x?.warning ?? 'Count recorded.')}>Record count</button></div></div>}
      <h2>Specimens</h2>
      <table className="results compact"><thead><tr><th>#</th><th>Specimen</th><th>Status</th><th></th></tr></thead>
        <tbody>{r.specimens.map((s: any) => <tr key={s.id}><td>{s.number}</td><td>{s.site} – {s.test}{s.container && ` (${s.container})`}<div className="small muted">{s.collectedBy}, {nzDateTime(s.collectedAt)}</div></td>
          <td>{s.status === 'ReceivedByLab' ? 'Received by lab' : s.status}{s.courier && <div className="small muted">{s.courier}</div>}</td>
          <td>{s.status === 'Collected' && write && <button className="btn btn-link" onClick={() => { const courier = window.prompt('Courier or who took it'); if (courier) act(() => api(`/api/emr/specimens/${s.id}/step`, { body: { to: 'Sent', courier } }), 'Sent.'); }}>Sent</button>}
            {s.status === 'Sent' && write && <button className="btn btn-link" onClick={() => act(() => api(`/api/emr/specimens/${s.id}/step`, { body: { to: 'ReceivedByLab', courier: null } }), 'Received by the laboratory.')}>Lab received</button>}</td></tr>)}</tbody></table>
      {write && <div className="card subtle form-grid">
        <label>Site<input value={sp.site} onChange={(e) => setSp({ ...sp, site: e.target.value })} placeholder="e.g. Gallbladder" /></label>
        <label>Test<input value={sp.test} onChange={(e) => setSp({ ...sp, test: e.target.value })} /></label>
        <label>Container<input value={sp.container} onChange={(e) => setSp({ ...sp, container: e.target.value })} placeholder="e.g. Formalin pot" /></label>
        <div><button className="btn btn-primary" onClick={() => act(() => api(`/api/emr/episodes/${r.episode.id}/specimens`, { body: { ...sp, container: sp.container || null } }), (x) => `Specimen ${x.number} recorded – label it with the patient's name and NHI.`)}>Record specimen</button></div></div>}
    </div>
  );
}

function Obs({ r, act, write }: Ctx) {
  const blank = { respiratoryRate: '', spO2: '', onOxygen: false, oxygenLitres: '', systolicBp: '', diastolicBp: '', heartRate: '', temperatureC: '', consciousness: 'A', pain: '', sedation: '' };
  const [o, setO] = useState<any>(blank);
  const n = (v: any) => v === '' || v == null ? null : Number(v);
  return (
    <div className="stack">
      {write && <div className="card subtle stack"><div className="form-grid">
        <label>Resp rate<input type="number" value={o.respiratoryRate} onChange={(e) => setO({ ...o, respiratoryRate: e.target.value })} /></label>
        <label>SpO2 %<input type="number" value={o.spO2} onChange={(e) => setO({ ...o, spO2: e.target.value })} /></label>
        <label className="check"><input type="checkbox" checked={o.onOxygen} onChange={(e) => setO({ ...o, onOxygen: e.target.checked })} /> On oxygen</label>
        {o.onOxygen && <label>Oxygen L/min<input type="number" value={o.oxygenLitres} onChange={(e) => setO({ ...o, oxygenLitres: e.target.value })} /></label>}
        <label>Systolic BP<input type="number" value={o.systolicBp} onChange={(e) => setO({ ...o, systolicBp: e.target.value })} /></label>
        <label>Diastolic BP<input type="number" value={o.diastolicBp} onChange={(e) => setO({ ...o, diastolicBp: e.target.value })} /></label>
        <label>Heart rate<input type="number" value={o.heartRate} onChange={(e) => setO({ ...o, heartRate: e.target.value })} /></label>
        <label>Temperature °C<input type="number" step="0.1" value={o.temperatureC} onChange={(e) => setO({ ...o, temperatureC: e.target.value })} /></label>
        <label>Consciousness<select value={o.consciousness} onChange={(e) => setO({ ...o, consciousness: e.target.value })}>{[['A', 'Alert'], ['C', 'New confusion'], ['V', 'Responds to voice'], ['P', 'Responds to pain'], ['U', 'Unresponsive']].map(([k, l]) => <option key={k} value={k}>{l}</option>)}</select></label>
        <label>Pain 0–10<input type="number" value={o.pain} onChange={(e) => setO({ ...o, pain: e.target.value })} /></label></div>
        <div><button className="btn btn-primary" onClick={async () => { const x = await act(() => api(`/api/emr/episodes/${r.episode.id}/observations`, { body: { ...o, at: null, respiratoryRate: n(o.respiratoryRate), spO2: n(o.spO2), oxygenLitres: n(o.oxygenLitres), systolicBp: n(o.systolicBp), diastolicBp: n(o.diastolicBp), heartRate: n(o.heartRate), temperatureC: n(o.temperatureC), pain: n(o.pain), sedation: n(o.sedation) } }),
          (x) => x.ews == null ? 'Saved (incomplete set – no early warning score).' : `Early warning score ${x.ews}.${x.escalation ? ' ' + x.escalation : ''}`); if (x) setO(blank); }}>Save observations</button></div></div>}
      {r.observations.length === 0 ? <p className="muted">No observations yet.</p> : (
        <div className="table-scroll" tabIndex={0} role="region" aria-label="Observations (scrolls sideways)"><table className="results compact"><thead><tr><th>Time</th><th>RR</th><th>SpO2</th><th>O2</th><th>BP</th><th>HR</th><th>Temp</th><th>LOC</th><th>Pain</th><th>EWS</th><th>Source</th></tr></thead>
          <tbody>{r.observations.map((x: any) => <tr key={x.id}><td>{nzDateTime(x.at)}</td><td>{x.respiratoryRate ?? ''}</td><td>{x.spO2 ?? ''}</td><td>{x.onOxygen ? `${x.oxygenLitres ?? ''} L` : ''}</td>
            <td>{x.systolicBp ?? ''}{x.diastolicBp ? `/${x.diastolicBp}` : ''}</td><td>{x.heartRate ?? ''}</td><td>{x.temperatureC ?? ''}</td><td>{x.consciousness ?? ''}</td><td>{x.pain ?? ''}</td>
            <td>{x.ews != null && <span className={x.ews >= 6 ? 'pill bad' : x.ews >= 1 ? 'pill' : 'pill green'}>{x.ews}</span>}{x.escalation && <div className="small error-text">{x.escalation}</div>}</td>
            <td className="small">{x.source === 'Device' ? 'Monitor' : x.recordedBy}</td></tr>)}</tbody></table></div>)}
      <p className="hint">Early warning score uses NEWS-style bands; confirm against the HQSC NZ EWS chart and TDS escalation policy before clinical use.</p>
    </div>
  );
}

function Meds({ r, act, write }: Ctx) {
  const [m, setM] = useState({ medicine: '', dose: '', route: 'IV', givenAt: localNow(), barcode: '', controlledDrugId: '', witnessedBy: '', notes: '' });
  const [fl, setFl] = useState({ isInput: true, kind: 'IV', millilitres: '' });
  const [bl, setBl] = useState({ unitNumber: '', product: 'Red cells', receivedAt: localNow(), coldChainOk: false, checkedBy1: '', checkedBy2: '' });
  return (
    <div className="stack">
      <h2>Medicines given</h2>
      {write && <div className="card subtle form-grid">
        <label>Scan barcode (optional)<input value={m.barcode} onChange={(e) => setM({ ...m, barcode: e.target.value })} autoComplete="off" /></label>
        <label>Medicine<input value={m.medicine} onChange={(e) => setM({ ...m, medicine: e.target.value })} /></label>
        <label>Dose<input value={m.dose} onChange={(e) => setM({ ...m, dose: e.target.value })} placeholder="e.g. 1 g" /></label>
        <label>Route<select value={m.route} onChange={(e) => setM({ ...m, route: e.target.value })}>{['IV', 'IM', 'SC', 'Oral', 'SL', 'PR', 'Inhaled', 'Topical'].map((x) => <option key={x}>{x}</option>)}</select></label>
        <label>Given at<input type="datetime-local" value={m.givenAt} onChange={(e) => setM({ ...m, givenAt: e.target.value })} /></label>
        <label>Controlled drug?<select value={m.controlledDrugId} onChange={(e) => setM({ ...m, controlledDrugId: e.target.value })}><option value="">No</option>{r.controlledDrugs.map((d: any) => <option key={d.id} value={d.id}>{d.label} (balance {d.balance})</option>)}</select></label>
        {m.controlledDrugId && <label>Witness<input value={m.witnessedBy} onChange={(e) => setM({ ...m, witnessedBy: e.target.value })} /></label>}
        <div><button className="btn btn-primary" onClick={async () => { if (await act(() => api(`/api/emr/episodes/${r.episode.id}/medications`, { body: { ...m, givenAt: iso(m.givenAt), barcode: m.barcode || null, controlledDrugId: m.controlledDrugId || null, witnessedBy: m.witnessedBy || null, notes: m.notes || null } }), 'Recorded.')) setM({ ...m, medicine: '', dose: '', barcode: '', controlledDrugId: '', witnessedBy: '' }); }}>Record given</button></div></div>}
      {r.medications.length > 0 && <table className="results compact"><thead><tr><th>Time</th><th>Medicine</th><th>Dose / route</th><th>By</th></tr></thead>
        <tbody>{r.medications.map((x: any) => <tr key={x.id}><td>{nzDateTime(x.givenAt)}</td><td>{x.medicine}{x.controlled && <span className="pill">CD</span>}</td><td>{x.dose} {x.route}</td><td className="small">{x.givenBy}{x.witnessedBy && ` / ${x.witnessedBy}`}</td></tr>)}</tbody></table>}
      <h2>Fluid balance</h2>
      <p>In {r.fluids.totalIn} ml · out {r.fluids.totalOut} ml · balance {r.fluids.totalIn - r.fluids.totalOut} ml</p>
      {write && <div className="card subtle row">
        <label>In / out<select value={fl.isInput ? 'in' : 'out'} onChange={(e) => setFl({ ...fl, isInput: e.target.value === 'in', kind: e.target.value === 'in' ? 'IV' : 'Urine' })}><option value="in">In</option><option value="out">Out</option></select></label>
        <label>What<select value={fl.kind} onChange={(e) => setFl({ ...fl, kind: e.target.value })}>{(fl.isInput ? ['IV', 'Oral', 'Blood'] : ['Urine', 'Drain', 'BloodLoss', 'Vomit', 'Other']).map((x) => <option key={x}>{x}</option>)}</select></label>
        <label>ml<input type="number" value={fl.millilitres} onChange={(e) => setFl({ ...fl, millilitres: e.target.value })} /></label>
        <button className="btn btn-light" onClick={async () => { if (await act(() => api(`/api/emr/episodes/${r.episode.id}/fluids`, { body: { ...fl, at: null, millilitres: Number(fl.millilitres) } }), 'Recorded.')) setFl({ ...fl, millilitres: '' }); }}>Add</button></div>}
      <h2>Blood products</h2>
      {r.blood.length > 0 && <ul className="plain">{r.blood.map((b: any) => <li key={b.id}>{b.unitNumber} {b.product} – checked by {b.checkedBy1} and {b.checkedBy2}{b.reaction && <strong className="error-text"> REACTION: {b.reactionDetails}</strong>}{b.returned && ' (returned unused)'}</li>)}</ul>}
      {write && <div className="card subtle form-grid">
        <label>Unit number<input value={bl.unitNumber} onChange={(e) => setBl({ ...bl, unitNumber: e.target.value })} /></label>
        <label>Product<input value={bl.product} onChange={(e) => setBl({ ...bl, product: e.target.value })} /></label>
        <label>Received<input type="datetime-local" value={bl.receivedAt} onChange={(e) => setBl({ ...bl, receivedAt: e.target.value })} /></label>
        <label>Checked by (1)<input value={bl.checkedBy1} onChange={(e) => setBl({ ...bl, checkedBy1: e.target.value })} /></label>
        <label>Checked by (2)<input value={bl.checkedBy2} onChange={(e) => setBl({ ...bl, checkedBy2: e.target.value })} /></label>
        <label className="check"><input type="checkbox" checked={bl.coldChainOk} onChange={(e) => setBl({ ...bl, coldChainOk: e.target.checked })} /> Cold chain confirmed (chilly bin, times within limits)</label>
        <div><button className="btn btn-primary" onClick={() => act(() => api(`/api/emr/episodes/${r.episode.id}/blood`, { body: { ...bl, receivedAt: iso(bl.receivedAt) } }), 'Unit recorded.')}>Record unit</button></div></div>}
    </div>
  );
}

function Notes({ r, act, write }: Ctx) {
  const { can: has } = useSession();
  const [n, setN] = useState({ kind: 'Nursing', text: '' });
  const fill = (id: string) => { const t = r.templates.find((x: any) => x.id === id); if (t) setN({ kind: t.kind, text: t.body.replaceAll('{procedure}', r.header.procedure).replaceAll('{surgeon}', r.header.surgeon ?? '').replaceAll('{anaesthetist}', r.header.anaesthetist ?? '').replaceAll('{date}', formatDate(r.header.date)).replaceAll('{clinic_phone}', 'the clinic') }); };
  const canWrite = has(Perm.EmrWrite) && !!r.episode;
  return (
    <div className="stack">
      {canWrite && <div className="card subtle stack">
        <div className="row"><label>Kind<select value={n.kind} onChange={(e) => setN({ ...n, kind: e.target.value })}>{[['Nursing', 'Nursing note'], ['Medical', 'Medical note'], ['OperationNote', 'Operation note'], ['Anaesthetic', 'Anaesthetic note'], ['Discharge', 'Discharge summary'], ['PatientInstructions', 'Patient instructions']].map(([k, l]) => <option key={k} value={k}>{l}</option>)}</select></label>
          <label>Start from a template<select value="" onChange={(e) => fill(e.target.value)}><option value="">—</option>{r.templates.map((t: any) => <option key={t.id} value={t.id}>{t.name}</option>)}</select></label></div>
        <label>Note<textarea rows={8} value={n.text} onChange={(e) => setN({ ...n, text: e.target.value })} /></label>
        <div className="row"><button className="btn btn-primary" onClick={async () => { if (await act(() => api(`/api/emr/episodes/${r.episode.id}/notes`, { body: { ...n, sign: true, amendsNoteId: null } }), 'Note signed.')) setN({ ...n, text: '' }); }}>Sign note</button>
          <button className="btn btn-light" onClick={async () => { if (await act(() => api(`/api/emr/episodes/${r.episode.id}/notes`, { body: { ...n, sign: false, amendsNoteId: null } }), 'Draft saved.')) setN({ ...n, text: '' }); }}>Save as draft</button></div></div>}
      {!write && r.episode && ['Discharged', 'Transferred'].includes(r.episode.stage) && <p className="hint">The episode is closed; late notes can still be added.</p>}
      {r.notes.length === 0 ? <p className="muted">No notes.</p> : r.notes.map((x: any) => <div key={x.id} className="card subtle"><p className="small muted">{x.kind} · {x.author} · {nzDateTime(x.at)} {x.signed ? '· signed' : '· DRAFT'}{x.amendsNoteId && ' · amendment'}</p><p className="prewrap">{x.text}</p></div>)}
    </div>
  );
}

function Discharge({ r, act, write }: Ctx) {
  const e = r.episode;
  const [d, setD] = useState({ escortName: e.escortName ?? '', escortPhone: e.escortPhone ?? '', overnightCarer: e.overnightCarer ?? '', escortConfirmed: e.escortConfirmed, escortOverrideReason: '', criteriaMet: false, destination: 'Home', followUpCallOn: todayIso() });
  const [transfer, setTransfer] = useState({ destination: 'Nelson Hospital Emergency Department' });
  if (['Discharged', 'Transferred'].includes(e.stage)) return <p>{e.stage === 'Transferred' ? 'Transferred to' : 'Discharged to'} {e.dischargeDestination} on {nzDateTime(e.dischargedAt)} by {e.dischargedBy}. Escort: {e.escortName ?? '—'}, overnight: {e.overnightCarer ?? '—'}{e.escortOverrideReason && ` – override: ${e.escortOverrideReason}`}.</p>;
  if (!write) return <p className="muted">Not discharged.</p>;
  return (
    <div className="stack">
      <div className="card subtle stack"><h2>Discharge home</h2>
        <label className="check"><input type="checkbox" checked={d.criteriaMet} onChange={(x) => setD({ ...d, criteriaMet: x.target.checked })} /> Discharge criteria met: observations stable, pain and nausea controlled, wound/dressing checked, passed urine if required, eating and drinking, instructions given</label>
        {e.sedationOrAnaesthesia && <><p><strong>Sedation/anaesthetic given:</strong> a responsible adult must take the patient home and someone must stay overnight.</p>
          <div className="form-grid"><label>Escort home<input value={d.escortName} onChange={(x) => setD({ ...d, escortName: x.target.value })} /></label>
            <label>Escort phone<input value={d.escortPhone} onChange={(x) => setD({ ...d, escortPhone: x.target.value })} /></label>
            <label>Overnight carer<input value={d.overnightCarer} onChange={(x) => setD({ ...d, overnightCarer: x.target.value })} /></label>
            <label className="check"><input type="checkbox" checked={d.escortConfirmed} onChange={(x) => setD({ ...d, escortConfirmed: x.target.checked })} /> Escort is here / confirmed by phone</label>
            <label>Or – reason the patient may go without (override)<input value={d.escortOverrideReason} onChange={(x) => setD({ ...d, escortOverrideReason: x.target.value })} /></label></div></>}
        <label>Follow-up phone call<input type="date" value={d.followUpCallOn} onChange={(x) => setD({ ...d, followUpCallOn: x.target.value })} /></label>
        <div><button className="btn btn-primary" onClick={() => act(() => api(`/api/emr/episodes/${e.id}/discharge`, { body: { ...d, escortOverrideReason: d.escortOverrideReason || null, followUpCallOn: d.followUpCallOn || null, transfer: false } }), 'Patient discharged.')}>Discharge patient</button></div></div>
      <div className="card subtle row"><label>Emergency transfer to<input value={transfer.destination} onChange={(x) => setTransfer({ destination: x.target.value })} /></label>
        <Link className="btn btn-light" to={`/patients/${r.header.patientId}/transfer`}>Transfer pack</Link>
        <button className="btn btn-warn" onClick={() => act(() => api(`/api/emr/episodes/${e.id}/discharge`, { body: { escortName: null, escortPhone: null, overnightCarer: null, escortConfirmed: false, escortOverrideReason: null, criteriaMet: false, destination: transfer.destination, followUpCallOn: null, transfer: true } }), 'Recorded as transferred.')}>Record transfer to hospital</button></div>
    </div>
  );
}
