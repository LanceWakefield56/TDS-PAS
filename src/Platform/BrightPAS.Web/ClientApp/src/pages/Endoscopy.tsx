import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { addDays, api, errText, formatDate, nzDateTime, Perm, todayIso } from '../api';
import { useSession } from '../App';

export const KIND: Record<string, string> = { Gastroscopy: 'Gastroscopy', Colonoscopy: 'Colonoscopy', FlexibleSigmoidoscopy: 'Flexible sigmoidoscopy', Bidirectional: 'Gastroscopy + colonoscopy' };
export const STATUS: Record<string, string> = { NotStarted: 'Not started', Planned: 'Planned', InProgress: 'In progress', Completed: 'Completed – to sign', Signed: 'Signed' };
const statusPill = (s: string) => s === 'Signed' ? 'pill green' : s === 'Completed' ? 'pill amber' : 'pill';

/** Endoscopy: today's list, pictures from the tower that need a patient, quality indicators and settings. */
export default function Endoscopy() {
  const { can } = useSession();
  const [params, setParams] = useSearchParams();
  const tab = params.get('tab') ?? 'today';
  const tabs: [string, string][] = [['today', "Today's list"], ...(can(Perm.EndoWrite) ? [['unmatched', 'Pictures to match'] as [string, string]] : []), ['quality', 'Quality indicators'],
    ...(can(Perm.EndoManage) ? [['settings', 'Settings'] as [string, string]] : [])];
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Endoscopy</h1><p className="muted">Gastroscopy and colonoscopy reporting, pictures and video from the tower, patient reports and quality indicators.</p></div>
      <section className="card stack">
        <div className="tabs" role="tablist">{tabs.map(([k, t]) => <button key={k} role="tab" aria-selected={tab === k} className={tab === k ? 'on' : ''} onClick={() => setParams({ tab: k })}>{t}</button>)}</div>
        {tab === 'today' && <Today />}
        {tab === 'unmatched' && <Unmatched />}
        {tab === 'quality' && <Quality />}
        {tab === 'settings' && <Settings />}
      </section>
    </div>
  );
}

function Today() {
  const [date, setDate] = useState(todayIso());
  const [rows, setRows] = useState<any[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => { setRows(null); api<any[]>(`/api/endoscopy/today?date=${date}`).then(setRows).catch((e) => setError(errText(e))); }, [date]);
  return (
    <div className="stack">
      <div className="row"><label>Day<input type="date" value={date} onChange={(e) => setDate(e.target.value)} /></label>
        <button className="btn btn-light btn-small" onClick={() => setDate(addDays(date, -1))}>← Previous</button><button className="btn btn-light btn-small" onClick={() => setDate(addDays(date, 1))}>Next →</button></div>
      {error && <p className="error" role="alert">{error}</p>}
      {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">No endoscopy booked on {formatDate(date)}.</p> : (
        <table className="results"><thead><tr><th>Time</th><th>Patient</th><th>Procedure</th><th>Report</th><th>Media</th></tr></thead>
          <tbody>{rows.map((r) => <tr key={r.bookingId}>
            <td>{r.start?.slice(0, 5)}<div className="small muted">{r.locationName}</div></td>
            <td><Link to={`/endoscopy/${r.bookingId}`}>{r.patientName}</Link><div className="small muted">NHI {r.nhi ?? '—'} · {r.mrn}</div></td>
            <td>{r.kind ? KIND[r.kind] : r.procedure}<div className="small muted">{r.endoscopist}{r.accession ? ` · ${r.accession}` : ''}</div></td>
            <td><span className={statusPill(r.status)}>{STATUS[r.status] ?? r.status}</span></td>
            <td className="small">{r.pictures} picture{r.pictures === 1 ? '' : 's'}{r.videos > 0 ? ` · ${r.videos} video${r.videos === 1 ? '' : 's'}` : ''}</td></tr>)}</tbody></table>)}
    </div>
  );
}

function Unmatched() {
  const [rows, setRows] = useState<any[] | null>(null);
  const [today, setToday] = useState<any[]>([]);
  const [pick, setPick] = useState<Record<string, string>>({});
  const [error, setError] = useState<string | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const load = () => api<any[]>('/api/endoscopy/unmatched').then(setRows).catch((e) => setError(errText(e)));
  useEffect(() => { load(); api<any[]>(`/api/endoscopy/today?date=${todayIso()}`).then((t) => setToday(t.filter((x) => x.procedureId))).catch(() => undefined); }, []);
  const assign = async (id: string) => {
    setError(null); setMsg(null);
    try { await api(`/api/endoscopy/media/${id}/assign`, { body: { procedureId: pick[id] } }); setMsg('Picture added to the procedure.'); load(); } catch (x) { setError(errText(x)); }
  };
  return (
    <div className="stack">
      <p className="hint">Pictures that arrived from a tower without a worklist entry (e.g. the patient was typed in on the tower). Check the picture and the tower's patient details, then choose the procedure.</p>
      {error && <p className="error" role="alert">{error}</p>}{msg && <p className="ok-text" role="status">{msg}</p>}
      {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">Nothing to match.</p> : (
        <div className="endo-gallery">{rows.map((m) => <figure key={m.id} className="endo-thumb">
          <img src={`/api/endoscopy/media/${m.id}`} alt={`From ${m.capturedBy}`} />
          <figcaption className="small"><strong>{m.dicomPatientName || '(no name)'}</strong> · ID {m.dicomPatientId || '—'}<br />{nzDateTime(m.capturedAt)} · {m.capturedBy}
            <div className="row"><select aria-label="Procedure" value={pick[m.id] ?? ''} onChange={(e) => setPick({ ...pick, [m.id]: e.target.value })}><option value="">Choose today's procedure…</option>
              {today.map((t) => <option key={t.procedureId} value={t.procedureId}>{t.start?.slice(0, 5)} {t.patientName} – {KIND[t.kind] ?? t.procedure}</option>)}</select>
              <button className="btn btn-light btn-small" disabled={!pick[m.id]} onClick={() => assign(m.id)}>Add</button></div></figcaption></figure>)}</div>)}
    </div>
  );
}

const pct = (v: number | null | undefined) => v == null ? '—' : `${v}%`;
const min = (v: number | null | undefined) => v == null ? '—' : `${v} min`;

function Quality() {
  const { can } = useSession();
  const [to, setTo] = useState(todayIso());
  const [from, setFrom] = useState(addDays(todayIso(), -365));
  const [d, setD] = useState<any>(null);
  const [error, setError] = useState<string | null>(null);
  const [edit, setEdit] = useState<any>(null);
  const load = () => api(`/api/endoscopy/kpis?from=${from}&to=${to}`).then(setD).catch((e) => setError(errText(e)));
  useEffect(() => { load(); }, [from, to]); // eslint-disable-line react-hooks/exhaustive-deps
  const t = d?.targets;
  const cell = (v: number | null, target: number, higherBetter = true) => <td className={v == null ? '' : (higherBetter ? v >= target : v <= target) ? 'ok-text' : 'error-text'}>{pct(v)}</td>;
  return (
    <div className="stack">
      <div className="row"><label>From<input type="date" value={from} onChange={(e) => setFrom(e.target.value)} /></label><label>To<input type="date" value={to} onChange={(e) => setTo(e.target.value)} /></label></div>
      {error && <p className="error" role="alert">{error}</p>}
      {!d ? <p>Loading…</p> : <>
        <h2>Colonoscopy</h2>
        <div className="table-scroll" tabIndex={0} role="region" aria-label="Colonoscopy indicators (scrolls sideways)"><table className="results compact">
          <thead><tr><th>Endoscopist</th><th>Number</th><th>Caecal intubation (≥{t.caecalIntubation}%)</th><th>Adenoma detection (≥{t.adr}%)</th><th>Serrated detection</th><th>Polyp detection</th>
            <th>Median withdrawal, no polypectomy (≥{t.withdrawalMinutes} min)</th><th>Withdrawal ≥ 6 min</th><th>Polyp retrieval (≥{t.retrieval}%)</th><th>Adequate prep (≥{t.adequatePrep}%)</th>
            <th>Moderate/severe discomfort (≤{t.comfortPoor}%)</th><th>Rectal retroflexion</th><th>Complications</th></tr></thead>
          <tbody>{d.rows.map((r: any) => <tr key={r.endoscopist} className={r.endoscopist === 'All endoscopists' ? 'strong' : ''}><td>{r.endoscopist}</td><td>{r.colonoscopies}</td>
            {cell(r.caecalIntubation, t.caecalIntubation)}{cell(r.adr, t.adr)}<td>{pct(r.ssldr)}</td><td>{pct(r.pdr)}</td>
            <td className={r.medianWithdrawal == null ? '' : r.medianWithdrawal >= t.withdrawalMinutes ? 'ok-text' : 'error-text'}>{min(r.medianWithdrawal)}</td><td>{pct(r.withdrawalOver6)}</td>
            {cell(r.retrieval, t.retrieval)}{cell(r.adequatePrep, t.adequatePrep)}{cell(r.comfortPoor, t.comfortPoor, false)}<td>{pct(r.rectalRetroflexion)}</td><td>{r.lowerComplications}</td></tr>)}</tbody></table></div>
        <h2>Gastroscopy</h2>
        <table className="results compact"><thead><tr><th>Endoscopist</th><th>Number</th><th>D2 reached</th><th>Retroflexion</th><th>Median time (≥{t.upperMinutes} min)</th><th>Time ≥ 7 min</th><th>Photo-documentation</th><th>Complications</th></tr></thead>
          <tbody>{d.rows.map((r: any) => <tr key={r.endoscopist}><td>{r.endoscopist}</td><td>{r.gastroscopies}</td><td>{pct(r.d2)}</td><td>{pct(r.upperRetroflexion)}</td>
            <td className={r.medianUpperMinutes == null ? '' : r.medianUpperMinutes >= t.upperMinutes ? 'ok-text' : 'error-text'}>{min(r.medianUpperMinutes)}</td><td>{pct(r.upperOver7)}</td><td>{pct(r.photoDocumentation)}</td><td>{r.upperComplications}</td></tr>)}</tbody></table>
        <p className="hint">Adenoma detection counts colonoscopies with at least one adenoma on histology – enter histology on each procedure when results arrive. Targets are set by TDS (NZ bowel screening endoscopists may have higher targets).</p>
        {can(Perm.EndoManage) && (!edit ? <div><button className="btn btn-light btn-small" onClick={() => setEdit({ ...t })}>Change targets</button></div> :
          <div className="card subtle form-grid">{[['caecalIntubation', 'Caecal intubation %'], ['adr', 'Adenoma detection %'], ['withdrawalMinutes', 'Withdrawal minutes'], ['retrieval', 'Polyp retrieval %'],
            ['adequatePrep', 'Adequate prep %'], ['comfortPoor', 'Discomfort % (max)'], ['upperMinutes', 'Gastroscopy minutes']].map(([k, l]) =>
            <label key={k}>{l}<input type="number" value={edit[k]} onChange={(e) => setEdit({ ...edit, [k]: Number(e.target.value) })} /></label>)}
            <div className="row"><button className="btn btn-primary" onClick={async () => { try { await api('/api/endoscopy/targets', { method: 'PUT', body: edit }); setEdit(null); load(); } catch (x) { setError(errText(x)); } }}>Save targets</button>
              <button className="btn btn-light" onClick={() => setEdit(null)}>Cancel</button></div></div>)}
      </>}
    </div>
  );
}

function Settings() {
  const [d, setD] = useState<any>(null);
  const [s, setS] = useState<any>(null);
  const [advice, setAdvice] = useState<any[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const load = async () => { const x = await api('/api/endoscopy/settings'); setD(x); setS({ ...x.settings, mediaPath: x.settings.mediaPath ?? '', dicomCallingAes: x.settings.dicomCallingAes ?? '', videoRetentionDays: x.settings.videoRetentionDays ?? '' }); setAdvice(await api('/api/endoscopy/advice')); };
  useEffect(() => { load().catch((e) => setError(errText(e))); }, []);
  if (!s) return error ? <p className="error">{error}</p> : <p>Loading…</p>;
  const save = async () => {
    setError(null); setMsg(null);
    try { await api('/api/endoscopy/settings', { method: 'PUT', body: { ...s, mediaPath: s.mediaPath || null, dicomPort: Number(s.dicomPort), videoRetentionDays: s.videoRetentionDays === '' ? null : Number(s.videoRetentionDays) } }); setMsg('Settings saved.'); setTimeout(() => load(), 1500); }
    catch (x) { setError(errText(x)); }
  };
  return (
    <div className="stack">
      {error && <p className="error" role="alert">{error}</p>}{msg && <p className="ok-text" role="status">{msg}</p>}
      <h2>Pictures and video</h2>
      <p className={d.media.ok ? 'ok-text' : 'error-text'}>{d.media.ok ? 'Folder OK: ' : 'Problem: '}{d.media.detail}</p>
      <div className="form-grid">
        <label>Media folder (blank = {d.defaultMediaPath})<input value={s.mediaPath} onChange={(e) => setS({ ...s, mediaPath: e.target.value })} placeholder={d.defaultMediaPath} /></label>
        <label>Delete videos after (days, blank = keep)<input type="number" value={s.videoRetentionDays} onChange={(e) => setS({ ...s, videoRetentionDays: e.target.value })} /></label>
      </div>
      <h2>Olympus tower (DICOM)</h2>
      <p className="small">{d.dicom.enabled ? (d.dicom.listening ? <span className="ok-text">Listening on port {d.dicom.port} as {d.dicom.aeTitle}. {d.dicom.images} picture(s), {d.dicom.videos} video(s), {d.dicom.worklist} worklist request(s) since start{d.dicom.lastActivity ? `; last ${nzDateTime(d.dicom.lastActivity)}` : ''}.</span>
        : <span className="error-text">{d.dicom.error ?? 'Not listening.'}</span>) : 'Off.'}</p>
      <div className="form-grid">
        <label className="check"><input type="checkbox" checked={s.dicomEnabled} onChange={(e) => setS({ ...s, dicomEnabled: e.target.checked })} /> Turn on the DICOM node (worklist, pictures/video, procedure start/end)</label>
        <label>Port<input type="number" value={s.dicomPort} onChange={(e) => setS({ ...s, dicomPort: e.target.value })} /></label>
        <label>BrightPAS AE title<input value={s.dicomAeTitle} maxLength={16} onChange={(e) => setS({ ...s, dicomAeTitle: e.target.value })} /></label>
        <label>Tower AE titles allowed (comma-separated)<input value={s.dicomCallingAes} onChange={(e) => setS({ ...s, dicomCallingAes: e.target.value })} placeholder="e.g. CV1500_PR1" /></label>
        <label className="check"><input type="checkbox" checked={s.dicomAnyTower} onChange={(e) => setS({ ...s, dicomAnyTower: e.target.checked })} /> Accept any device on the network (testing only)</label>
      </div>
      <p className="hint">Towers are also accepted by the network address recorded on their asset (Assets › endoscopy tower). On the tower or capture box set: worklist and storage SCP = this server's address, port {s.dicomPort}, AE title {s.dicomAeTitle}; modality ES. Open the port in Windows Firewall for the endoscopy network only.</p>
      <div><button className="btn btn-primary" onClick={save}>Save settings</button></div>
      <h2>Patient report wording</h2>
      {advice.map((a) => <AdviceRow key={a.key} a={a} onSaved={() => setMsg(`"${a.title}" saved.`)} onError={setError} />)}
    </div>
  );
}

function AdviceRow({ a, onSaved, onError }: { a: any; onSaved: () => void; onError: (e: string) => void }) {
  const [t, setT] = useState(a.title);
  const [b, setB] = useState(a.body);
  return (
    <div className="card subtle stack">
      <label>Heading ({a.key})<input value={t} onChange={(e) => setT(e.target.value)} /></label>
      <label>Text<textarea rows={3} value={b} onChange={(e) => setB(e.target.value)} /></label>
      <div><button className="btn btn-light btn-small" onClick={async () => { try { await api('/api/endoscopy/advice', { method: 'PUT', body: { key: a.key, title: t, body: b } }); onSaved(); } catch (x) { onError(errText(x)); } }}>Save</button></div>
    </div>
  );
}
