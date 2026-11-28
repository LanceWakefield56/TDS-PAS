import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useNavigate, useParams, useSearchParams } from 'react-router';
import { api, ApiError, errText, formatDate, nzDateTime, PatientSummary, Perm } from '../api';
import { useSession } from '../App';
import Modal from '../components/Modal';
import FileImport from '../components/FileImport';
import PatientPicker from '../components/PatientPicker';
import ExportButton from '../components/ExportButton';

/** Imaging (PACS) (0.37.0, ADR 0050): X-rays and scans with the patient and episode, upload and watched folder, matching, requests and a viewer. */
const errs = (e: unknown) => (e instanceof ApiError && e.body?.errors ? e.body.errors as string[] : [errText(e)]);
const MODALITY: Record<string, string> = { CR: 'X-ray (CR)', DX: 'X-ray (DX)', CT: 'CT', MR: 'MRI', US: 'Ultrasound', XA: 'Angiography', RF: 'Fluoroscopy', MG: 'Mammography', NM: 'Nuclear medicine', PT: 'PET', OT: 'Other' };
const MATCH: Record<string, string> = { Accession: 'request number', PatientIdAndBirthDate: 'NHI/MRN and date of birth', Person: 'filed by staff' };
const SOURCE: Record<string, string> = { Upload: 'Uploaded', Folder: 'Watched folder', Network: 'Network', Demo: 'Demonstration' };

function Errors({ list }: { list: string[] }) {
  return list.length > 0 ? <div className="error" role="alert"><ul>{list.map((e) => <li key={e}>{e}</li>)}</ul></div> : null;
}

export default function Imaging() {
  const [params, setParams] = useSearchParams();
  const { can } = useSession();
  const [unmatched, setUnmatched] = useState<number | null>(null);
  const tab = params.get('tab') ?? 'studies';
  useEffect(() => { api<any>('/api/imaging/meta').then((m) => setUnmatched(m.unmatched)).catch(() => setUnmatched(null)); }, [tab]);
  const see = can(Perm.ImagingView) || can(Perm.ImagingImport);
  const tabs: [string, string][] = [];
  if (see) tabs.push(['studies', 'Studies'], ['unmatched', `To match${unmatched ? ` (${unmatched})` : ''}`], ['requests', 'Requests']);
  if (can(Perm.ImagingImport)) tabs.push(['upload', 'Upload']);
  if (can(Perm.ImagingManage)) tabs.push(['settings', 'Settings']);
  const current = tabs.some(([k]) => k === tab) ? tab : tabs[0]?.[0];
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Imaging</h1><p className="muted">X-rays and scans with the patient's record. Images come from uploads (files, a ZIP or a CD) and the watched network folder, and are filed to the patient by the request number or by NHI and date of birth. For reference beside the patient's care – the radiologist's report is the diagnostic record.</p></div>
      <div className="tabs" role="tablist">{tabs.map(([k, t]) => <button key={k} role="tab" aria-selected={current === k} className={current === k ? 'on' : ''} onClick={() => setParams({ tab: k })}>{t}</button>)}</div>
      {(current === 'studies' || current === 'unmatched') && <StudyList view={current === 'unmatched' ? 'unmatched' : 'all'} />}
      {current === 'requests' && <Requests />}
      {current === 'upload' && <Upload />}
      {current === 'settings' && <Settings />}
    </div>
  );
}

export function StudyTable({ rows, showPatient = true }: { rows: any[]; showPatient?: boolean }) {
  return (
    <div className="table-scroll" tabIndex={0} role="region" aria-label="Imaging studies">
      <table className="results">
        <thead><tr><th>Picture</th><th>Date</th>{showPatient && <th>Patient</th>}<th>Study</th><th>Type</th><th>Images</th><th>Episode</th><th>Filed</th></tr></thead>
        <tbody>{rows.map((s) => (
          <tr key={s.id}>
            <td>{s.firstImageId ? <Link to={`/imaging/studies/${s.id}`}><img className="pacs-thumb" src={`/api/imaging/images/${s.firstImageId}/render?size=96`} alt={`Study ${s.description ?? ''}`} /></Link> : <span className="muted small">No preview</span>}</td>
            <td>{s.studyDate ? formatDate(s.studyDate) : '—'}</td>
            {showPatient && <td>{s.patientId ? <Link to={`/patients/${s.patientId}`}>{s.patient}</Link> : <span className="small">In the images: <strong>{s.filePatientName ?? '—'}</strong>, ID {s.filePatientId ?? '—'}{s.fileBirthDate ? `, born ${formatDate(s.fileBirthDate)}` : ''}</span>}</td>}
            <td><Link to={`/imaging/studies/${s.id}`}>{s.description ?? 'Study'}</Link>{s.accessionNumber && <div className="small muted">{s.accessionNumber}</div>}{s.hasReport && <div><span className="pill green">Report</span></div>}</td>
            <td>{s.modalities.split(',').map((m: string) => MODALITY[m] ?? m).join(', ')}{s.bodyPart && <div className="small muted">{s.bodyPart}</div>}</td>
            <td>{s.imageCount}</td>
            <td className="small">{s.episode ?? '—'}</td>
            <td>{s.status === 'Unmatched' ? <span className="pill amber">To match</span> : <span className="small">{MATCH[s.matchedBy] ?? s.matchedBy}</span>}<div className="small muted">{SOURCE[s.source] ?? s.source} {nzDateTime(s.receivedAt)}</div></td>
          </tr>
        ))}</tbody>
      </table>
    </div>
  );
}

function StudyList({ view }: { view: string }) {
  const [rows, setRows] = useState<any[] | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  useEffect(() => { setRows(null); api<any[]>(`/api/imaging/studies?view=${view}`).then(setRows).catch((e) => setErrors(errs(e))); }, [view]);
  return (
    <section className="card stack">
      {view === 'unmatched' && <p className="muted">These images could not be filed by themselves (no BrightPAS request number, or the NHI/MRN and date of birth did not match one patient). Open each, check the name and date of birth in the images, and file it to the right patient.</p>}
      <Errors list={errors} />
      {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">Nothing here.</p> : <StudyTable rows={rows} />}
    </section>
  );
}

function Upload() {
  return (
    <section className="stack">
      <FileImport url="/api/imaging/import" title="Upload images" folder
        hint="Choose DICOM files, a ZIP of them, or the whole folder of a CD or USB stick from a radiology provider. Each picture is filed to the patient by the request number or NHI/MRN and date of birth; anything else waits in To match. The same picture is only kept once." />
    </section>
  );
}

function Settings() {
  const [s, setS] = useState<any>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const load = () => api<any>('/api/imaging/settings').then(setS).catch((e) => setErrors(errs(e)));
  useEffect(() => { void load(); }, []);
  const save = async (e: React.FormEvent) => {
    e.preventDefault(); setErrors([]); setMsg(null);
    try { await api('/api/imaging/settings', { method: 'PUT', body: { storagePath: s.storagePath, importFolder: s.importFolder, autoFile: s.autoFile } }); setMsg('Saved.'); await load(); }
    catch (x) { setErrors(errs(x)); }
  };
  if (!s) return <section className="card"><Errors list={errors} />{errors.length === 0 && <p>Loading…</p>}</section>;
  const f = s.folder;
  return (
    <form className="card stack" onSubmit={save}>
      <h2>Settings</h2>
      <label>Imaging folder (where the image files are kept)<input value={s.storagePath ?? ''} onChange={(e) => setS({ ...s, storagePath: e.target.value })} /></label>
      <p className="hint">Leave as set by Setup unless the server's IT support has given a bigger disk. Back this folder up with the database. Changing it does not move images already stored.</p>
      <label>Watched network folder (optional)<input value={s.importFolder ?? ''} placeholder="\\xray-pc\export or D:\Imaging-in" onChange={(e) => setS({ ...s, importFolder: e.target.value })} /></label>
      <p className="hint">The X-ray or scanning machine, or a radiology provider, saves DICOM files here. BrightPAS takes them in every minute and moves each to processed\ or failed\. The BrightPAS service account needs Modify rights on the share.</p>
      {f?.folder && <p className="small">Last checked {f.lastRun ? nzDateTime(f.lastRun) : 'not yet'}{f.lastImport && ` · last image ${nzDateTime(f.lastImport)}`} · {f.stored} stored, {f.failed} failed since the website started.</p>}
      {f?.problem && <p className="warn-box">{f.problem}</p>}
      <label className="check"><input type="checkbox" checked={s.autoFile} onChange={(e) => setS({ ...s, autoFile: e.target.checked })} /> File images by themselves when the NHI or MRN and the date of birth in the images match one patient</label>
      <p className="hint">Images with a BrightPAS request number are always filed to that request's patient. Untick to have staff check every other study.</p>
      <p className="hint">Connecting a GE (or other) X-ray machine or PACS directly over the network is the next step once the device is chosen; the watched folder and upload stay as the fallback.</p>
      {msg && <p className="notice" role="status">{msg}</p>}
      <Errors list={errors} />
      <div className="row"><button className="btn btn-primary">Save settings</button></div>
    </form>
  );
}

// ---------------------------------------------------------------- requests
function Requests() {
  const { can } = useSession();
  const [rows, setRows] = useState<any[] | null>(null);
  const [all, setAll] = useState(false);
  const [adding, setAdding] = useState(false);
  const [errors, setErrors] = useState<string[]>([]);
  const load = useCallback(() => api<any[]>(`/api/imaging/requests?view=${all ? 'all' : 'open'}`).then(setRows).catch((e) => setErrors(errs(e))), [all]);
  useEffect(() => { void load(); }, [load]);
  const cancel = async (id: string) => { setErrors([]); try { await api(`/api/imaging/requests/${id}/cancel`, { body: {} }); await load(); } catch (e) { setErrors(errs(e)); } };
  return (
    <section className="card stack">
      <div className="row-between"><h2>Imaging requests</h2>{can(Perm.ImagingImport) && <button className="btn btn-primary btn-small" onClick={() => setAdding(true)}>New request</button>}</div>
      <p className="muted">A request gives a number (X…) to write on the radiology form or type at the X-ray machine. Images that carry it are filed to the patient and episode by themselves.</p>
      <label className="check"><input type="checkbox" checked={all} onChange={(e) => setAll(e.target.checked)} /> Show done and cancelled requests</label>
      <Errors list={errors} />
      {!rows ? <p>Loading…</p> : rows.length === 0 ? <p className="muted">No requests waiting for images.</p> : <RequestTable rows={rows} onCancel={can(Perm.ImagingImport) ? cancel : undefined} />}
      {adding && <RequestForm onClose={() => setAdding(false)} onSaved={() => { setAdding(false); void load(); }} />}
    </section>
  );
}

function RequestTable({ rows, onCancel, showPatient = true }: { rows: any[]; onCancel?: (id: string) => void; showPatient?: boolean }) {
  return (
    <div className="table-scroll" tabIndex={0} role="region" aria-label="Imaging requests">
      <table className="results">
        <thead><tr><th>Number</th>{showPatient && <th>Patient</th>}<th>Imaging</th><th>Episode</th><th>Wanted</th><th>Requested</th><th>Status</th></tr></thead>
        <tbody>{rows.map((r) => (
          <tr key={r.id}>
            <td><strong>{r.accessionNumber}</strong></td>
            {showPatient && <td><Link to={`/patients/${r.patientId}`}>{r.patient}</Link><div className="small muted">{r.mrn}</div></td>}
            <td>{MODALITY[r.modality] ?? r.modality}: {r.examination}{r.clinicalNotes && <div className="small muted">{r.clinicalNotes}</div>}</td>
            <td className="small">{r.episode ?? '—'}</td>
            <td>{r.wantedOn ? formatDate(r.wantedOn) : '—'}</td>
            <td className="small">{nzDateTime(r.requestedAt)} by {r.requestedBy}</td>
            <td>{r.status === 'Done' && r.studyId ? <Link className="pill green" to={`/imaging/studies/${r.studyId}`}>Images in</Link> : r.status === 'Cancelled' ? <span className="pill">Cancelled</span> : <span className="pill amber">Waiting</span>}
              {r.status === 'Requested' && onCancel && <div><button className="btn btn-link" onClick={() => onCancel(r.id)}>Cancel request</button></div>}</td>
          </tr>
        ))}</tbody>
      </table>
    </div>
  );
}

function RequestForm({ patient, onClose, onSaved }: { patient?: PatientSummary | { id: string }; onClose: () => void; onSaved: () => void }) {
  const [who, setWho] = useState<PatientSummary | null>(null);
  const patientId = patient?.id ?? who?.id ?? null;
  const [episodes, setEpisodes] = useState<any[]>([]);
  const [f, setF] = useState({ bookingId: '', modality: 'DX', examination: '', bodyPart: '', clinicalNotes: '', wantedOn: '' });
  const [errors, setErrors] = useState<string[]>([]);
  const [done, setDone] = useState<string | null>(null);
  useEffect(() => { setEpisodes([]); if (patientId) api<any[]>(`/api/imaging/patients/${patientId}/episodes`).then(setEpisodes).catch(() => setEpisodes([])); }, [patientId]);
  const save = async (e: React.FormEvent) => {
    e.preventDefault(); setErrors([]);
    if (!patientId) { setErrors(['Choose the patient.']); return; }
    try {
      const r = await api<any>('/api/imaging/requests', { body: { patientId, bookingId: f.bookingId || null, modality: f.modality, examination: f.examination, bodyPart: f.bodyPart || null, clinicalNotes: f.clinicalNotes || null, wantedOn: f.wantedOn || null } });
      setDone(r.accessionNumber);
    } catch (x) { setErrors(errs(x)); }
  };
  return (
    <Modal title="New imaging request" onClose={done ? onSaved : onClose}>
      {done ? <>
        <p className="notice" role="status">Request number <strong>{done}</strong>. Write it on the radiology form or give it to the X-ray staff – images with this number are filed to the patient by themselves.</p>
        <div className="row"><button className="btn btn-primary" onClick={onSaved}>Close</button></div>
      </> : <form className="stack" onSubmit={save}>
        {!patient && <PatientPicker value={who} onChange={setWho} />}
        <div className="form-grid">
          <label>Type<select value={f.modality} onChange={(e) => setF({ ...f, modality: e.target.value })}>{Object.entries(MODALITY).map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
          <label>Imaging wanted<input value={f.examination} placeholder="e.g. Left knee AP and lateral" onChange={(e) => setF({ ...f, examination: e.target.value })} /></label>
          <label>Body part<input value={f.bodyPart} placeholder="e.g. KNEE" onChange={(e) => setF({ ...f, bodyPart: e.target.value })} /></label>
          <label>Wanted by<input type="date" value={f.wantedOn} onChange={(e) => setF({ ...f, wantedOn: e.target.value })} /></label>
          <label>Episode<select value={f.bookingId} onChange={(e) => setF({ ...f, bookingId: e.target.value })}><option value="">Not for a booking</option>{episodes.map((x) => <option key={x.bookingId} value={x.bookingId}>{formatDate(x.date)} {x.procedure}</option>)}</select></label>
        </div>
        <label>Clinical notes<textarea rows={3} value={f.clinicalNotes} onChange={(e) => setF({ ...f, clinicalNotes: e.target.value })} /></label>
        <Errors list={errors} />
        <div className="row"><button className="btn btn-primary">Make request</button><button type="button" className="btn btn-light" onClick={onClose}>Cancel</button></div>
      </form>}
    </Modal>
  );
}

// ---------------------------------------------------------------- one study
export function ImagingStudy() {
  const { id } = useParams();
  const { can } = useSession();
  const navigate = useNavigate();
  const [d, setD] = useState<any>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const [filing, setFiling] = useState(false);
  const [deleting, setDeleting] = useState(false);
  const [report, setReport] = useState<string | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const load = useCallback(() => api<any>(`/api/imaging/studies/${id}`).then(setD).catch((e) => setErrors(errs(e))), [id]);
  useEffect(() => { void load(); }, [load]);
  const act = async (fn: () => Promise<unknown>, done: string) => { setErrors([]); setMsg(null); try { await fn(); setMsg(done); await load(); } catch (e) { setErrors(errs(e)); } };
  if (!d) return <div className="stack-lg"><Errors list={errors} />{errors.length === 0 && <p>Loading…</p>}</div>;
  const s = d.study;
  return (
    <div className="stack-lg">
      <div className="page-head">
        <p><Link to="/imaging">← Imaging</Link></p>
        <h1>{s.description ?? 'Imaging study'}</h1>
        <p className="muted">{s.studyDate ? formatDate(s.studyDate) : 'Date not given'} · {s.modalities.split(',').map((m: string) => MODALITY[m] ?? m).join(', ')}{s.bodyPart ? ` · ${s.bodyPart}` : ''} · {s.imageCount} image{s.imageCount === 1 ? '' : 's'}{s.accessionNumber ? ` · ${s.accessionNumber}` : ''}</p>
      </div>
      {msg && <p className="notice" role="status">{msg}</p>}
      <Errors list={errors} />
      <div className="grid-2">
        <section className="card stack">
          <h2>Patient</h2>
          {s.patientId ? <p><Link to={`/patients/${s.patientId}`}><strong>{s.patient}</strong></Link> {s.mrn}{d.patientBirthDate && ` · born ${formatDate(d.patientBirthDate)}`}</p> : <p><span className="pill amber">To match</span> Not filed to a patient yet.</p>}
          <dl className="facts">
            <dt>In the images</dt><dd>{s.filePatientName ?? '—'}, ID {s.filePatientId ?? '—'}{s.fileBirthDate ? `, born ${formatDate(s.fileBirthDate)}` : ''}{d.fileSex ? `, ${d.fileSex}` : ''}</dd>
            <dt>Episode</dt><dd>{s.episode ?? '—'}</dd>
            <dt>Filed</dt><dd>{s.status === 'Filed' ? `${MATCH[s.matchedBy] ?? s.matchedBy}${d.filedAt ? `, ${nzDateTime(d.filedAt)}` : ''}${d.filedBy ? ` (${d.filedBy})` : ''}` : '—'}</dd>
            <dt>Received</dt><dd>{SOURCE[s.source] ?? s.source}, {nzDateTime(s.receivedAt)} ({s.receivedBy})</dd>
            {d.institution && <><dt>From</dt><dd>{d.institution}</dd></>}
            {d.referringPhysician && <><dt>Referrer</dt><dd>{d.referringPhysician}</dd></>}
            {d.request && <><dt>Request</dt><dd>{d.request.accessionNumber}: {d.request.examination} ({d.request.requestedBy})</dd></>}
          </dl>
          {d.birthDateMismatch && <p className="warn-box">The date of birth in the images is not this patient's. Check this is the right patient.</p>}
          {can(Perm.ImagingImport) && <div className="row">
            <button className="btn btn-light btn-small" onClick={() => setFiling(true)}>{s.patientId ? 'File to another patient or episode' : 'File to a patient'}</button>
          </div>}
        </section>
        <section className="card stack">
          <div className="row-between"><h2>Report</h2>{can(Perm.ImagingView) && report === null && <button className="btn btn-light btn-small" onClick={() => setReport(d.report ?? '')}>{d.report ? 'Edit' : 'Add report or note'}</button>}</div>
          {d.report ? <p className="pre-wrap">{d.report}</p> : <p className="muted">No report yet. Paste the radiologist's report here when it arrives, or add a clinical note.</p>}
          {d.reportBy && <p className="small muted">{d.reportBy}, {nzDateTime(d.reportAt)}</p>}
          <div className="row">
            <ExportButton label="Export images (DICOM)" path={`/api/imaging/studies/${s.id}/export`} body={{}} />
            {can(Perm.ImagingManage) && <button className="btn btn-warn btn-small" onClick={() => setDeleting(true)}>Delete study</button>}
          </div>
        </section>
      </div>
      <Viewer series={d.series} />
      {report !== null && (
        <Modal title="Report or note" onClose={() => setReport(null)}>
          <label>Report<textarea rows={10} value={report} onChange={(e) => setReport(e.target.value)} /></label>
          <div className="row"><button className="btn btn-primary" onClick={() => void act(async () => { await api(`/api/imaging/studies/${s.id}/report`, { method: 'PUT', body: { text: report } }); setReport(null); }, 'Report saved.')}>Save</button><button className="btn btn-light" onClick={() => setReport(null)}>Cancel</button></div>
        </Modal>
      )}
      {filing && <FileForm study={d} onClose={() => setFiling(false)} onDone={() => { setFiling(false); setMsg('Filed.'); void load(); }} />}
      {deleting && <DeleteForm studyId={s.id} onClose={() => setDeleting(false)} onDone={() => navigate('/imaging')} />}
    </div>
  );
}

function FileForm({ study, onClose, onDone }: { study: any; onClose: () => void; onDone: () => void }) {
  const s = study.study;
  const [who, setWho] = useState<PatientSummary | null>(null);
  const [episodes, setEpisodes] = useState<any[]>([]);
  const [bookingId, setBookingId] = useState('');
  const [confirm, setConfirm] = useState(false);
  const [errors, setErrors] = useState<string[]>([]);
  const patientId = who?.id ?? s.patientId;
  useEffect(() => { setEpisodes([]); setBookingId(who ? '' : s.bookingId ?? ''); if (patientId) api<any[]>(`/api/imaging/patients/${patientId}/episodes`).then(setEpisodes).catch(() => setEpisodes([])); }, [patientId]); // eslint-disable-line react-hooks/exhaustive-deps
  const mismatch = who && s.fileBirthDate && who.dateOfBirth !== s.fileBirthDate;
  const save = async (e: React.FormEvent) => {
    e.preventDefault(); setErrors([]);
    if (!patientId) { setErrors(['Choose the patient.']); return; }
    try {
      if (who || !s.patientId) await api(`/api/imaging/studies/${s.id}/file`, { body: { patientId, bookingId: bookingId || null, confirmMismatch: confirm } });
      else await api(`/api/imaging/studies/${s.id}/episode`, { method: 'PUT', body: { bookingId: bookingId || null } });
      onDone();
    } catch (x) { setErrors(errs(x)); }
  };
  return (
    <Modal title="File the images" onClose={onClose}>
      <form className="stack" onSubmit={save}>
        <p>In the images: <strong>{s.filePatientName ?? '—'}</strong>, ID {s.filePatientId ?? '—'}{s.fileBirthDate ? `, born ${formatDate(s.fileBirthDate)}` : ''}</p>
        <PatientPicker value={who} onChange={setWho} label={s.patientId ? 'Move to another patient (leave empty to keep this patient)' : 'Patient'} />
        {mismatch && <p className="warn-box">This patient's date of birth ({formatDate(who!.dateOfBirth)}) is not the one in the images.</p>}
        {mismatch && <label className="check"><input type="checkbox" checked={confirm} onChange={(e) => setConfirm(e.target.checked)} /> I have checked these are this patient's images</label>}
        <label>Episode<select value={bookingId} onChange={(e) => setBookingId(e.target.value)}><option value="">Not linked to a booking</option>{episodes.map((x) => <option key={x.bookingId} value={x.bookingId}>{formatDate(x.date)} {x.procedure}</option>)}</select></label>
        <Errors list={errors} />
        <div className="row"><button className="btn btn-primary">File</button><button type="button" className="btn btn-light" onClick={onClose}>Cancel</button></div>
      </form>
    </Modal>
  );
}

function DeleteForm({ studyId, onClose, onDone }: { studyId: string; onClose: () => void; onDone: () => void }) {
  const [reason, setReason] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const go = async (e: React.FormEvent) => {
    e.preventDefault(); setErrors([]);
    try { await api(`/api/imaging/studies/${studyId}?reason=${encodeURIComponent(reason)}`, { method: 'DELETE' }); onDone(); } catch (x) { setErrors(errs(x)); }
  };
  return (
    <Modal title="Delete study" onClose={onClose}>
      <form className="stack" onSubmit={go}>
        <p>The image files are deleted. BrightPAS keeps a note of the study with who deleted it and why, and will not take the same study in again.</p>
        <label>Why<input value={reason} onChange={(e) => setReason(e.target.value)} /></label>
        <Errors list={errors} />
        <div className="row"><button className="btn btn-warn">Delete</button><button type="button" className="btn btn-light" onClick={onClose}>Cancel</button></div>
      </form>
    </Modal>
  );
}

// ---------------------------------------------------------------- viewer
const PRESETS: [string, number | null, number | null][] = [['From the image', null, null], ['Soft tissue (CT)', 40, 400], ['Lung (CT)', -600, 1500], ['Bone (CT)', 500, 2000], ['Brain (CT)', 40, 80]];

/** Browser viewer: series and images, scroll through a stack (wheel or arrow keys), window/level, invert, zoom and pan. */
function Viewer({ series }: { series: any[] }) {
  const viewable = series.flatMap((s: any) => s.images.filter((i: any) => i.viewable).map((i: any) => ({ ...i, series: s })));
  const [seriesId, setSeriesId] = useState<string | null>(viewable[0]?.series.id ?? null);
  const images = viewable.filter((i: any) => i.series.id === seriesId);
  const [index, setIndex] = useState(0);
  const [frame, setFrame] = useState(0);
  const [win, setWin] = useState<{ centre: number | null; width: number | null; adjustable: boolean }>({ centre: null, width: null, adjustable: false });
  const [invert, setInvert] = useState(false);
  const [zoom, setZoom] = useState(1);
  const [pan, setPan] = useState({ x: 0, y: 0 });
  const [loading, setLoading] = useState(false);
  const canvas = useRef<HTMLCanvasElement>(null);
  const img = useRef<HTMLImageElement | null>(null);
  const drag = useRef<{ x: number; y: number } | null>(null);
  const current = images[Math.min(index, images.length - 1)];

  useEffect(() => { setIndex(0); setFrame(0); }, [seriesId]);
  useEffect(() => {
    setFrame(0);
    if (!current) return;
    api<any>(`/api/imaging/images/${current.id}/window`).then((w) => setWin({ centre: w.centre, width: w.width, adjustable: w.adjustable })).catch(() => setWin({ centre: null, width: null, adjustable: false }));
  }, [current?.id]); // eslint-disable-line react-hooks/exhaustive-deps

  const draw = useCallback(() => {
    const c = canvas.current; const i = img.current;
    if (!c || !i) return;
    const ctx = c.getContext('2d'); if (!ctx) return;
    c.width = c.clientWidth || 800; c.height = c.clientHeight || 600;
    ctx.fillStyle = '#000'; ctx.fillRect(0, 0, c.width, c.height);
    const fit = Math.min(c.width / i.naturalWidth, c.height / i.naturalHeight) * zoom;
    const w = i.naturalWidth * fit, h = i.naturalHeight * fit;
    ctx.imageSmoothingEnabled = true;
    ctx.drawImage(i, (c.width - w) / 2 + pan.x, (c.height - h) / 2 + pan.y, w, h);
  }, [zoom, pan]);

  useEffect(() => {
    if (!current) return;
    const q = new URLSearchParams({ frame: String(frame), size: '1600', invert: String(invert) });
    if (win.adjustable && win.centre !== null && win.width !== null) { q.set('centre', String(win.centre)); q.set('width', String(win.width)); }
    const t = setTimeout(() => {
      setLoading(true);
      const i = new Image();
      i.onload = () => { img.current = i; setLoading(false); draw(); };
      i.onerror = () => setLoading(false);
      i.src = `/api/imaging/images/${current.id}/render?${q}`;
    }, 120);
    return () => clearTimeout(t);
  }, [current?.id, frame, invert, win, draw]); // eslint-disable-line react-hooks/exhaustive-deps
  useEffect(() => { draw(); }, [draw]);
  useEffect(() => { const r = () => draw(); window.addEventListener('resize', r); return () => window.removeEventListener('resize', r); }, [draw]);

  if (viewable.length === 0) return <section className="card"><h2>Images</h2><p className="muted">BrightPAS cannot draw these images (for example a compression it has no decoder for, or a report object). Export them and open them in a DICOM viewer.</p></section>;
  const step = (n: number) => {
    if (current?.frames > 1) { setFrame((f) => Math.max(0, Math.min(current.frames - 1, f + n))); return; }
    setIndex((x) => Math.max(0, Math.min(images.length - 1, x + n)));
  };
  const adjust = (dc: number, dw: number) => win.centre !== null && win.width !== null && setWin({ ...win, centre: Math.round(win.centre + dc * win.width), width: Math.max(1, Math.round(win.width * dw)) });
  return (
    <section className="card stack">
      <div className="row-between"><h2>Images</h2><span className="small muted">For reference – not for primary diagnosis.</span></div>
      {series.length > 1 && <div className="row" role="group" aria-label="Series">{series.filter((s: any) => s.images.some((i: any) => i.viewable)).map((s: any) =>
        <button key={s.id} className={`btn btn-small ${s.id === seriesId ? 'btn-primary' : 'btn-light'}`} onClick={() => setSeriesId(s.id)}>{s.description ?? s.modality} ({s.imageCount})</button>)}</div>}
      <div className="row pacs-tools" role="toolbar" aria-label="Viewer">
        <button className="btn btn-light btn-small" onClick={() => step(-1)} disabled={(current?.frames > 1 ? frame : index) === 0}>‹ Previous</button>
        <span className="small">{current?.frames > 1 ? `Frame ${frame + 1} of ${current.frames}` : `Image ${index + 1} of ${images.length}`}</span>
        <button className="btn btn-light btn-small" onClick={() => step(1)} disabled={current?.frames > 1 ? frame >= current.frames - 1 : index >= images.length - 1}>Next ›</button>
        <button className="btn btn-light btn-small" onClick={() => setZoom((z) => Math.min(8, z * 1.25))}>Zoom in</button>
        <button className="btn btn-light btn-small" onClick={() => setZoom((z) => Math.max(0.25, z / 1.25))}>Zoom out</button>
        <button className="btn btn-light btn-small" onClick={() => { setZoom(1); setPan({ x: 0, y: 0 }); }}>Fit</button>
        <label className="check"><input type="checkbox" checked={invert} onChange={(e) => setInvert(e.target.checked)} /> Invert</label>
      </div>
      {win.adjustable && <div className="row pacs-tools" role="group" aria-label="Brightness and contrast">
        <select aria-label="Window preset" onChange={(e) => { const p = PRESETS[Number(e.target.value)]; if (p[1] === null) { api<any>(`/api/imaging/images/${current.id}/window`).then((w) => setWin({ centre: w.centre, width: w.width, adjustable: w.adjustable })).catch(() => undefined); } else setWin({ ...win, centre: p[1], width: p[2] }); }}>
          {PRESETS.map((p, n) => <option key={p[0]} value={n}>{p[0]}</option>)}
        </select>
        <button className="btn btn-light btn-small" onClick={() => adjust(-0.1, 1)}>Brighter</button>
        <button className="btn btn-light btn-small" onClick={() => adjust(0.1, 1)}>Darker</button>
        <button className="btn btn-light btn-small" onClick={() => adjust(0, 0.8)}>More contrast</button>
        <button className="btn btn-light btn-small" onClick={() => adjust(0, 1.25)}>Less contrast</button>
        <span className="small muted">Level {win.centre} · width {win.width}</span>
      </div>}
      <canvas ref={canvas} className="pacs-canvas" tabIndex={0} aria-label="Image (arrow keys or wheel to move through images, drag to move)"
        onWheel={(e) => { e.preventDefault(); step(e.deltaY > 0 ? 1 : -1); }}
        onKeyDown={(e) => { if (e.key === 'ArrowDown' || e.key === 'ArrowRight') { e.preventDefault(); step(1); } if (e.key === 'ArrowUp' || e.key === 'ArrowLeft') { e.preventDefault(); step(-1); } }}
        onPointerDown={(e) => { drag.current = { x: e.clientX - pan.x, y: e.clientY - pan.y }; (e.target as HTMLElement).setPointerCapture(e.pointerId); }}
        onPointerMove={(e) => { if (drag.current) setPan({ x: e.clientX - drag.current.x, y: e.clientY - drag.current.y }); }}
        onPointerUp={() => { drag.current = null; }} />
      {loading && <p className="small muted" role="status">Loading image…</p>}
      {images.length > 1 && images.length <= 60 && <div className="pacs-strip" tabIndex={0} role="region" aria-label="Images in this series">
        {images.map((i: any, n: number) => <button key={i.id} className={`pacs-strip-item${n === index ? ' on' : ''}`} onClick={() => setIndex(n)} aria-label={`Image ${n + 1}`}><img src={`/api/imaging/images/${i.id}/render?size=96`} alt="" /></button>)}
      </div>}
    </section>
  );
}

// ---------------------------------------------------------------- patient record panel
export function PatientImaging({ patientId }: { patientId: string }) {
  const { can } = useSession();
  const [rows, setRows] = useState<any[] | null>(null);
  const [reqs, setReqs] = useState<any[]>([]);
  const [adding, setAdding] = useState(false);
  const load = useCallback(() => {
    api<any[]>(`/api/imaging/studies?patientId=${patientId}`).then(setRows).catch(() => setRows([]));
    api<any[]>(`/api/imaging/requests?patientId=${patientId}`).then(setReqs).catch(() => setReqs([]));
  }, [patientId]);
  useEffect(() => { load(); }, [load]);
  if (!rows) return null;
  return (
    <section className="card span-all">
      <div className="row-between"><h2>Imaging</h2>{can(Perm.ImagingImport) && <button className="btn btn-light btn-small" onClick={() => setAdding(true)}>Request imaging</button>}</div>
      {rows.length === 0 ? <p className="muted">No X-rays or scans.</p> : <StudyTable rows={rows} showPatient={false} />}
      {reqs.length > 0 && <><h3>Waiting for images</h3><RequestTable rows={reqs} showPatient={false} /></>}
      {adding && <RequestForm patient={{ id: patientId }} onClose={() => setAdding(false)} onSaved={() => { setAdding(false); load(); }} />}
    </section>
  );
}
