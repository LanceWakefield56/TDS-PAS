import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useParams, useSearchParams } from 'react-router';
import { api, errText, formatDate, nzDateTime, Perm } from '../api';
import { useSession } from '../App';
import ExportButton from '../components/ExportButton';
import Annotator from '../components/Annotator';
import DictateButton from '../components/DictateButton';
import { KIND, STATUS } from './Endoscopy';

type Item = { code: string; label: string };
type Ctx = { r: any; t: any; reload: () => Promise<void>; act: (fn: () => Promise<any>, ok?: string | ((x: any) => string)) => Promise<any>; write: boolean };
const upper = (k: string) => k === 'Gastroscopy' || k === 'Bidirectional';
const lower = (k: string) => k !== 'Gastroscopy';
const colon = (k: string) => k === 'Colonoscopy' || k === 'Bidirectional';
const localNow = () => { const d = new Date(); d.setMinutes(d.getMinutes() - d.getTimezoneOffset()); return d.toISOString().slice(0, 16); };
const toLocal = (v: string | null) => { if (!v) return ''; const d = new Date(v); d.setMinutes(d.getMinutes() - d.getTimezoneOffset()); return d.toISOString().slice(0, 16); };
const iso = (v: string) => v ? new Date(v).toISOString() : null;
const yn = (v: boolean | null | undefined) => v == null ? '' : v ? 'yes' : 'no';
const fromYn = (v: string) => v === '' ? null : v === 'yes';
const num = (v: any) => v === '' || v == null ? null : Number(v);
const lbl = (list: Item[], code: string | null | undefined) => list.find((x) => x.code === code)?.label ?? code ?? '';
const sites = (t: any, kind: string): Item[] => [...(upper(kind) ? t.upperSites : []), ...(lower(kind) ? t.lowerSites : [])];

/** One endoscopy procedure: details, live capture from the tower, procedure record, findings, polyps, therapy, pictures, report and histology. */
export default function EndoscopyProcedure() {
  const { bookingId } = useParams();
  const { can, mod } = useSession();
  const [params, setParams] = useSearchParams();
  const tab = params.get('tab') ?? 'details';
  const [r, setR] = useState<any>(null);
  const [t, setT] = useState<any>(null);
  const [error, setError] = useState<string | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const reload = useCallback(async () => { setR(await api(`/api/endoscopy/bookings/${bookingId}`)); }, [bookingId]);
  useEffect(() => { reload().catch((e) => setError(errText(e))); api('/api/endoscopy/terminology').then(setT).catch((e) => setError(errText(e))); }, [reload]);
  const act = async (fn: () => Promise<any>, ok?: string | ((x: any) => string)) => {
    setError(null); setMsg(null);
    try { const x = await fn(); const w: string[] = x?.warnings ?? []; const okText = ok ? (typeof ok === 'function' ? ok(x) : ok) : null; if (okText || w.length) setMsg([okText, ...w].filter(Boolean).join(' ')); await reload(); return x ?? true; }
    catch (x: any) { setError(x?.body?.errors?.join(' ') ?? errText(x)); return null; }
  };
  if (!r || !t) return error ? <div className="card"><p className="error">{error}</p></div> : <p>Loading…</p>;
  const h = r.header;
  const p = r.procedure;
  const signed = p?.status === 'Signed';
  const write = can(Perm.EndoWrite) && !!p && !signed;
  const ctx: Ctx = { r, t, reload, act, write };
  const tabs: [string, string][] = p ? [['details', 'Details'], ['capture', 'Capture'], ['procedure', 'Procedure'], ['findings', `Findings (${r.findings.length})`], ['polyps', `Polyps (${r.polyps.length})`],
    ['therapy', 'Tattoos & biopsies'], ['images', `Pictures & video (${r.media.length})`], ['report', 'Report'], ['histology', 'Histology']] : [];
  return (
    <div className="stack-lg">
      <div className="patient-banner"><div><h1>{h.name}</h1><p>NHI {h.nhi ?? '—'} · {h.mrn} · born {formatDate(h.dateOfBirth)} · {h.sex}</p>
        <p>{p ? KIND[p.kind] : h.procedure} · {formatDate(h.date)} · {h.location} · {p?.endoscopistName ?? h.surgeon}{p ? ` · accession ${p.accessionNumber}` : ''}</p></div>
        <div className="row"><Link className="btn btn-light btn-small" to="/endoscopy">Today's list</Link>
          {mod('emr') && can(Perm.EmrView) && <Link className="btn btn-light btn-small" to={`/emr/${h.bookingId}`}>Clinical record</Link>}
          <Link className="btn btn-light btn-small" to={`/patients/${h.patientId}`}>Patient record</Link></div></div>
      {h.alerts.map((a: any, i: number) => <p key={i} className={a.severity === 'Critical' ? 'alert-critical' : 'alert-warning'}><strong>{a.severity === 'Critical' ? 'Critical alert' : 'Alert'}:</strong> {a.type} – {a.description}</p>)}
      {error && <p className="error" role="alert">{error}</p>}{msg && <p className="ok-text" role="status">{msg}</p>}
      {!p ? <section className="card"><p className="muted">No endoscopy record has been started for this booking.</p></section> : <section className="card stack">
        <p><span className={signed ? 'pill green' : p.status === 'Completed' ? 'pill amber' : 'pill'}>{STATUS[p.status]}</span>{signed && ` by ${p.signedBy}, ${nzDateTime(p.signedAt)}`}</p>
        <div className="tabs" role="tablist">{tabs.map(([k, l]) => <button key={k} role="tab" aria-selected={tab === k} className={tab === k ? 'on' : ''} onClick={() => setParams({ tab: k })}>{l}</button>)}</div>
        {tab === 'details' && <Details {...ctx} />}
        {tab === 'capture' && <Capture {...ctx} />}
        {tab === 'procedure' && <ProcedureTab {...ctx} />}
        {tab === 'findings' && <Findings {...ctx} />}
        {tab === 'polyps' && <Polyps {...ctx} />}
        {tab === 'therapy' && <Therapy {...ctx} />}
        {tab === 'images' && <Images {...ctx} />}
        {tab === 'report' && <Report {...ctx} />}
        {tab === 'histology' && <Histology {...ctx} />}
      </section>}
    </div>
  );
}

// ------------------------------------------------------------------ details
function Details({ r, t, act, write }: Ctx) {
  const p = r.procedure;
  const [clin, setClin] = useState<any[]>([]);
  const [f, setF] = useState<any>({ ...p, indications: r.indications, endoscopistId: p.endoscopistId ?? '', upperScopeId: p.upperScopeId ?? '', lowerScopeId: p.lowerScopeId ?? '', towerId: p.towerId ?? '' });
  useEffect(() => { api<any[]>('/api/organisation/clinicians').then((c) => setClin(c.filter((x) => x.isActive !== false))).catch(() => undefined); }, []);
  const ind: Item[] = [...(upper(f.kind) ? t.upperIndications : []), ...(lower(f.kind) ? t.lowerIndications : [])];
  const scope = (which: 'upper' | 'lower') => (r.scopes ?? []).filter((s: any) => s[which]);
  const save = () => {
    const doc = clin.find((c) => c.id === f.endoscopistId);
    return act(() => api(`/api/endoscopy/procedures/${p.id}/details`, { method: 'PUT', body: { ...f, endoscopistId: f.endoscopistId || null, endoscopistName: doc?.name ?? f.endoscopistName,
      upperScopeId: f.upperScopeId || null, lowerScopeId: f.lowerScopeId || null, towerId: f.towerId || null, indications: f.indications.filter((c: string) => ind.some((i) => i.code === c)) } }), 'Details saved.');
  };
  const chk = (k: string, l: string) => <label className="check"><input type="checkbox" checked={!!f[k]} disabled={!write} onChange={(e) => setF({ ...f, [k]: e.target.checked })} /> {l}</label>;
  return (
    <div className="stack">
      <div className="form-grid">
        <label>Procedure<select value={f.kind} disabled={!write} onChange={(e) => setF({ ...f, kind: e.target.value })}>{Object.entries(KIND).map(([k, l]) => <option key={k} value={k}>{l}</option>)}</select></label>
        <label>Endoscopist<select value={f.endoscopistId} disabled={!write} onChange={(e) => setF({ ...f, endoscopistId: e.target.value })}><option value="">{f.endoscopistName ?? '—'}</option>{clin.map((c) => <option key={c.id} value={c.id}>{c.name}</option>)}</select></label>
        <label>Nurses / assistants<input value={f.assistants ?? ''} disabled={!write} onChange={(e) => setF({ ...f, assistants: e.target.value })} /></label>
        {r.scopes && upper(f.kind) && <label>Gastroscope<select value={f.upperScopeId} disabled={!write} onChange={(e) => setF({ ...f, upperScopeId: e.target.value })}><option value="">—</option>
          {scope('upper').map((s: any) => <option key={s.id} value={s.id}>{s.assetNumber} {s.model} – {s.state === 'Ready' ? 'ready' : s.stateText}</option>)}</select></label>}
        {r.scopes && lower(f.kind) && <label>Colonoscope<select value={f.lowerScopeId} disabled={!write} onChange={(e) => setF({ ...f, lowerScopeId: e.target.value })}><option value="">—</option>
          {scope('lower').map((s: any) => <option key={s.id} value={s.id}>{s.assetNumber} {s.model} – {s.state === 'Ready' ? 'ready' : s.stateText}</option>)}</select></label>}
        {r.towers && <label>Tower<select value={f.towerId} disabled={!write} onChange={(e) => setF({ ...f, towerId: e.target.value })}><option value="">—</option>{r.towers.map((x: any) => <option key={x.id} value={x.id}>{x.assetNumber} {x.model}</option>)}</select></label>}
        <label>Sedation<select value={f.sedation} disabled={!write} onChange={(e) => setF({ ...f, sedation: e.target.value })}>{[['None', 'None'], ['ThroatSpray', 'Throat spray only'], ['Conscious', 'Conscious sedation'], ['Propofol', 'Propofol (anaesthetist)'], ['GeneralAnaesthetic', 'General anaesthetic']].map(([k, l]) => <option key={k} value={k}>{l}</option>)}</select></label>
        <label>Sedation notes<input value={f.sedationNotes ?? ''} disabled={!write} onChange={(e) => setF({ ...f, sedationNotes: e.target.value })} /></label>
      </div>
      {r.sedation && <p className="small">Medicines from the clinical record: {r.sedation.length === 0 ? 'none recorded yet' : r.sedation.map((m: any) => `${m.medicine} ${m.dose} ${m.route}`).join(', ')}.</p>}
      <fieldset><legend>Indication</legend><div className="check-grid">{ind.map((i) => <label key={i.code} className="check"><input type="checkbox" disabled={!write} checked={f.indications.includes(i.code)}
        onChange={(e) => setF({ ...f, indications: e.target.checked ? [...f.indications, i.code] : f.indications.filter((x: string) => x !== i.code) })} /> {i.label}</label>)}</div>
        <label>Other indication<input value={f.otherIndication ?? ''} disabled={!write} onChange={(e) => setF({ ...f, otherIndication: e.target.value })} /></label></fieldset>
      <div className="form-grid">
        {lower(f.kind) && chk('nbspScreening', 'National Bowel Screening Programme (NBSP)')}
        {lower(f.kind) && f.nbspScreening && <label>NBSP participant number<input value={f.nbspNumber ?? ''} disabled={!write} onChange={(e) => setF({ ...f, nbspNumber: e.target.value })} /></label>}
        {chk('consentConfirmed', 'Consent confirmed')}
        {lower(f.kind) && <>{chk('co2', 'CO₂ insufflation')}{chk('waterAssisted', 'Water-assisted')}{chk('scopeGuide', 'ScopeGuide used')}{chk('positionChange', 'Position change')}{chk('abdominalPressure', 'Abdominal pressure')}</>}
      </div>
      {write && <div><button className="btn btn-primary" onClick={save}>Save details</button></div>}
    </div>
  );
}

// ------------------------------------------------------------------ live capture (capture card = a camera in the browser)
function Capture({ r, act, reload, write }: Ctx) {
  const p = r.procedure;
  const video = useRef<HTMLVideoElement>(null);
  const stream = useRef<MediaStream | null>(null);
  const rec = useRef<{ recorder: MediaRecorder; id: string; seq: number; queue: Promise<void>; started: number } | null>(null);
  const [devices, setDevices] = useState<MediaDeviceInfo[]>([]);
  const [device, setDevice] = useState<string>(() => { try { return localStorage.getItem('endo.camera') ?? ''; } catch { return ''; } });
  const [quality, setQuality] = useState<string>(() => { try { return localStorage.getItem('endo.quality') ?? '1080'; } catch { return '1080'; } });
  const [format, setFormat] = useState('');
  const [live, setLive] = useState(false);
  const [recording, setRecording] = useState(false);
  const [elapsed, setElapsed] = useState(0);
  const [taken, setTaken] = useState(0);
  const [flash, setFlash] = useState(false);
  const [problem, setProblem] = useState<string | null>(null);
  const canCapture = can_(write, p);

  const start = useCallback(async () => {
    setProblem(null);
    try {
      stream.current?.getTracks().forEach((x) => x.stop());
      const size = quality === '2160' ? { width: { ideal: 3840 }, height: { ideal: 2160 } } : quality === '720' ? { width: { ideal: 1280 }, height: { ideal: 720 } } : { width: { ideal: 1920 }, height: { ideal: 1080 } };
      const s = await navigator.mediaDevices.getUserMedia({ video: device ? { deviceId: { exact: device }, ...size, frameRate: { ideal: 30 } } : { ...size, frameRate: { ideal: 30 } }, audio: false });
      const st = s.getVideoTracks()[0]?.getSettings();
      setFormat(st?.width ? `${st.width}×${st.height}${st.frameRate ? ` at ${Math.round(st.frameRate)} fps` : ''}` : '');
      stream.current = s;
      if (video.current) { video.current.srcObject = s; await video.current.play().catch(() => undefined); }
      setLive(true);
      setDevices((await navigator.mediaDevices.enumerateDevices()).filter((d) => d.kind === 'videoinput'));
    } catch (e: any) { setLive(false); setProblem(`The video source could not be opened (${e?.name ?? 'error'}). Check the capture card is plugged in and the browser may use the camera.`); }
  }, [device, quality]);
  useEffect(() => { if (canCapture) start(); return () => { stream.current?.getTracks().forEach((x) => x.stop()); }; }, [start, canCapture]);

  const upload = (blob: Blob) => fetch(`/api/endoscopy/procedures/${p.id}/pictures`, { method: 'POST', credentials: 'same-origin', headers: { 'X-BrightPAS-Request': '1', 'X-Capture': 'station', 'Content-Type': blob.type }, body: blob })
    .then(async (res) => { if (!res.ok) { const j = await res.json().catch(() => null); throw new Error(j?.errors?.join(' ') ?? `HTTP ${res.status}`); } });

  const photo = useCallback(async () => {
    const v = video.current;
    if (!v || !live || v.videoWidth === 0) return;
    const c = document.createElement('canvas'); c.width = v.videoWidth; c.height = v.videoHeight;
    c.getContext('2d')!.drawImage(v, 0, 0);
    setFlash(true); setTimeout(() => setFlash(false), 150);
    const blob = await new Promise<Blob | null>((res) => c.toBlob(res, 'image/jpeg', 0.92));
    if (!blob) return;
    try { await upload(blob); setTaken((n) => n + 1); reload().catch(() => undefined); } catch (e: any) { setProblem('Picture not saved: ' + e.message); }
  }, [live, reload]); // eslint-disable-line react-hooks/exhaustive-deps

  const toggleRecord = useCallback(async () => {
    if (rec.current) {
      const cur = rec.current; rec.current = null;
      await new Promise<void>((res) => { cur.recorder.onstop = () => res(); cur.recorder.stop(); });
      await cur.queue;
      await api(`/api/endoscopy/videos/${cur.id}/finish`, { body: { durationSeconds: (Date.now() - cur.started) / 1000 } }).catch(() => undefined);
      setRecording(false); act(async () => null, 'Video saved.');
      return;
    }
    if (!stream.current) return;
    // MP4 (H.264) first – plays everywhere and suits archiving; WebM (VP9/VP8) where the browser cannot record MP4.
    const type = ['video/mp4;codecs=avc1.640033', 'video/mp4;codecs=avc1', 'video/mp4', 'video/webm;codecs=vp9', 'video/webm;codecs=vp8', 'video/webm'].find((m) => MediaRecorder.isTypeSupported(m)) ?? '';
    const h = stream.current.getVideoTracks()[0]?.getSettings().height ?? 1080;
    const bitrate = h >= 2000 ? 25_000_000 : h >= 1000 ? 8_000_000 : 4_000_000;
    let id: string;
    try { id = (await api(`/api/endoscopy/procedures/${p.id}/videos`, { body: { contentType: type || 'video/webm' } })).id; } catch (e) { setProblem(errText(e)); return; }
    const recorder = new MediaRecorder(stream.current, type ? { mimeType: type, videoBitsPerSecond: bitrate } : { videoBitsPerSecond: bitrate });
    const state = { recorder, id, seq: 0, queue: Promise.resolve(), started: Date.now() };
    recorder.ondataavailable = (e) => {
      if (!e.data || e.data.size === 0) return;
      const seq = state.seq++;
      state.queue = state.queue.then(() => fetch(`/api/endoscopy/videos/${id}/chunks/${seq}`, { method: 'POST', credentials: 'same-origin', headers: { 'X-BrightPAS-Request': '1', 'Content-Type': 'application/octet-stream' }, body: e.data })
        .then((res) => { if (!res.ok) setProblem('Part of the video could not be saved – recording continues.'); }).catch(() => setProblem('Connection lost while saving video.')));
    };
    recorder.start(3000);
    rec.current = state; setRecording(true); setElapsed(0);
  }, [p.id, act]);

  useEffect(() => { if (!recording) return; const i = setInterval(() => setElapsed(Math.round((Date.now() - (rec.current?.started ?? Date.now())) / 1000)), 500); return () => clearInterval(i); }, [recording]);
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const tag = (e.target as HTMLElement)?.tagName;
      if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT' || !live) return;
      if (e.code === 'Space' || e.key === 'F9') { e.preventDefault(); photo(); }
      else if (e.key === 'r' || e.key === 'R' || e.key === 'F10') { e.preventDefault(); toggleRecord(); }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [photo, toggleRecord, live]);
  useEffect(() => () => { if (rec.current) { const cur = rec.current; cur.recorder.stop(); cur.queue.then(() => api(`/api/endoscopy/videos/${cur.id}/finish`, { body: { durationSeconds: (Date.now() - cur.started) / 1000 } })).catch(() => undefined); } }, []);

  const uploadFiles = async (files: FileList | null) => {
    if (!files) return;
    let n = 0;
    for (const f of Array.from(files)) { try { await upload(new Blob([await f.arrayBuffer()], { type: f.type || 'application/octet-stream' })); n++; } catch (e: any) { setProblem(`${f.name}: ${e.message}`); } }
    act(async () => null, `${n} file(s) added.`);
  };

  if (!canCapture) return <p className="muted">{p.status === 'Signed' ? 'The report is signed. Pictures can still be added from the tower; amend the report to capture more here.' : 'You can view pictures and video in the Pictures & video tab.'}</p>;
  const mmss = `${Math.floor(elapsed / 60)}:${String(elapsed % 60).padStart(2, '0')}`;
  return (
    <div className="stack">
      <div className="row">
        <label>Video source<select value={device} onChange={(e) => { setDevice(e.target.value); try { localStorage.setItem('endo.camera', e.target.value); } catch { /* storage off */ } }}>
          <option value="">Default</option>{devices.map((d, i) => <option key={d.deviceId || i} value={d.deviceId}>{d.label || `Camera ${i + 1}`}</option>)}</select></label>
        <label>Quality<select value={quality} onChange={(e) => { setQuality(e.target.value); try { localStorage.setItem('endo.quality', e.target.value); } catch { /* storage off */ } }}>
          <option value="720">720p</option><option value="1080">1080p (Full HD)</option><option value="2160">4K (UHD)</option></select></label>
        <button className="btn btn-light btn-small" onClick={start}>Reconnect</button>{format && <span className="small muted">Receiving {format}</span>}
      </div>
      {problem && <p className="error-text" role="alert">{problem}</p>}
      <div className={`endo-live${flash ? ' flash' : ''}${recording ? ' recording' : ''}`}>
        <video ref={video} muted playsInline aria-label="Live endoscope video" />
        {recording && <span className="endo-rec" aria-live="polite">● REC {mmss}</span>}
      </div>
      <div className="row">
        <button className="btn btn-primary btn-big" disabled={!live} onClick={photo}>Take picture (Space)</button>
        <button className={recording ? 'btn btn-warn btn-big' : 'btn btn-light btn-big'} disabled={!live} onClick={toggleRecord}>{recording ? `Stop recording (R) ${mmss}` : 'Start recording (R)'}</button>
        <span className="small">{taken} picture(s) this session</span>
      </div>
      <p className="hint">A USB foot pedal can be set to send Space (picture) and R (record) – or F9/F10. Pictures taken with the scope's release button on the tower arrive by DICOM when the tower is connected (Endoscopy › Settings).</p>
      <label>Add pictures or video from a file (JPEG, PNG, TIFF, MP4, WebM or DICOM from the tower)<input type="file" accept="image/jpeg,image/png,image/tiff,.tif,.tiff,video/mp4,video/webm,.dcm,application/dicom" multiple onChange={(e) => uploadFiles(e.target.files)} /></label>
      <Strip media={r.media.slice(-8)} />
    </div>
  );
}
const can_ = (write: boolean, p: any) => write && !!p;

function Strip({ media }: { media: any[] }) {
  if (media.length === 0) return null;
  return <div className="endo-strip">{media.map((m) => m.kind === 'Image' ? <img key={m.id} src={m.url} alt={m.caption ?? 'Picture'} /> : <span key={m.id} className="endo-video-chip">▶ {m.durationSeconds ? `${Math.round(m.durationSeconds)} s` : 'video'}</span>)}</div>;
}

// ------------------------------------------------------------------ procedure record
function Timer({ from, to }: { from: string | null; to: string | null }) {
  const [, tick] = useState(0);
  useEffect(() => { if (!from || to) return; const i = setInterval(() => tick((n) => n + 1), 1000); return () => clearInterval(i); }, [from, to]);
  if (!from) return null;
  const s = Math.max(0, Math.round(((to ? new Date(to).getTime() : Date.now()) - new Date(from).getTime()) / 1000));
  return <span className={s >= 360 ? 'pill green' : 'pill amber'}>{Math.floor(s / 60)}:{String(s % 60).padStart(2, '0')}{to ? '' : ' running'}</span>;
}

function ProcedureTab({ r, act, write }: Ctx) {
  const p = r.procedure;
  const [f, setF] = useState<any>(() => {
    const x: any = { ...p };
    for (const k of ['upperStartAt', 'upperEndAt', 'insertedAt', 'caecumAt', 'withdrawalStartAt', 'endedAt']) x[k] = toLocal(p[k]);
    for (const k of ['retroflexion', 'upperPhotoDocumentation', 'appendicealOrifice', 'ileocaecalValve', 'terminalIleum', 'caecumPhotographed', 'rectalRetroflexion']) x[k] = yn(p[k]);
    for (const k of ['bbpsRight', 'bbpsTransverse', 'bbpsLeft', 'comfort']) x[k] = p[k] ?? '';
    x.upperExtent = p.upperExtent ?? ''; x.lowerExtent = p.lowerExtent ?? ''; x.incompleteReason = p.incompleteReason ?? '';
    return x;
  });
  const save = (next = f) => act(() => api(`/api/endoscopy/procedures/${p.id}/procedure`, { method: 'PUT', body: {
    upperStartAt: iso(next.upperStartAt), upperEndAt: iso(next.upperEndAt), upperExtent: next.upperExtent || null, retroflexion: fromYn(next.retroflexion), upperPhotoDocumentation: fromYn(next.upperPhotoDocumentation),
    insertedAt: iso(next.insertedAt), caecumAt: iso(next.caecumAt), withdrawalStartAt: iso(next.withdrawalStartAt), endedAt: iso(next.endedAt), lowerExtent: next.lowerExtent || null,
    appendicealOrifice: fromYn(next.appendicealOrifice), ileocaecalValve: fromYn(next.ileocaecalValve), terminalIleum: fromYn(next.terminalIleum), caecumPhotographed: fromYn(next.caecumPhotographed),
    bbpsRight: num(next.bbpsRight), bbpsTransverse: num(next.bbpsTransverse), bbpsLeft: num(next.bbpsLeft), rectalRetroflexion: fromYn(next.rectalRetroflexion), incompleteReason: next.incompleteReason || null, comfort: num(next.comfort) } }), 'Saved.');
  const now = (k: string) => { const next = { ...f, [k]: localNow() }; if (k === 'caecumAt' && !f.lowerExtent) next.lowerExtent = 'Caecum'; setF(next); save(next); };
  const time = (k: string, l: string) => <label>{l}<span className="row"><input type="datetime-local" value={f[k]} disabled={!write} onChange={(e) => setF({ ...f, [k]: e.target.value })} />{write && <button type="button" className="btn btn-light btn-small" onClick={() => now(k)}>Now</button>}</span></label>;
  const ynSel = (k: string, l: string) => <label>{l}<select value={f[k]} disabled={!write} onChange={(e) => setF({ ...f, [k]: e.target.value })}><option value="">—</option><option value="yes">Yes</option><option value="no">No</option></select></label>;
  const bbps = (k: string, l: string) => <label>{l}<select value={f[k]} disabled={!write} onChange={(e) => setF({ ...f, [k]: e.target.value })}><option value="">—</option>{[0, 1, 2, 3].map((n) => <option key={n} value={n}>{n}</option>)}</select></label>;
  const total = [f.bbpsRight, f.bbpsTransverse, f.bbpsLeft].every((v) => v !== '') ? Number(f.bbpsRight) + Number(f.bbpsTransverse) + Number(f.bbpsLeft) : null;
  return (
    <div className="stack">
      {upper(p.kind) && <fieldset><legend>Gastroscopy {p.upperStartAt && <Timer from={p.upperStartAt} to={p.upperEndAt} />}</legend><div className="form-grid">
        {time('upperStartAt', 'Scope in')}{time('upperEndAt', 'Scope out')}
        <label>Extent reached<select value={f.upperExtent} disabled={!write} onChange={(e) => setF({ ...f, upperExtent: e.target.value })}><option value="">—</option>{[['Oesophagus', 'Oesophagus'], ['Stomach', 'Stomach'], ['D1', 'D1'], ['D2', 'D2'], ['Failed', 'Failed intubation']].map(([k, l]) => <option key={k} value={k}>{l}</option>)}</select></label>
        {ynSel('retroflexion', 'Retroflexion (J-manoeuvre)')}{ynSel('upperPhotoDocumentation', 'Photo-documentation of landmarks')}</div></fieldset>}
      {lower(p.kind) && <fieldset><legend>{colon(p.kind) ? 'Colonoscopy' : 'Flexible sigmoidoscopy'} – withdrawal <Timer from={p.withdrawalStartAt ?? p.caecumAt} to={p.endedAt} /></legend><div className="form-grid">
        {time('insertedAt', 'Scope in')}{colon(p.kind) && time('caecumAt', 'Caecum reached')}{time('withdrawalStartAt', 'Withdrawal start (if later)')}{time('endedAt', 'Scope out')}
        <label>Extent reached<select value={f.lowerExtent} disabled={!write} onChange={(e) => setF({ ...f, lowerExtent: e.target.value })}><option value="">—</option>{[['Rectum', 'Rectum'], ['Sigmoid', 'Sigmoid'], ['Descending', 'Descending colon'], ['SplenicFlexure', 'Splenic flexure'], ['Transverse', 'Transverse colon'], ['HepaticFlexure', 'Hepatic flexure'], ['Ascending', 'Ascending colon'], ['Caecum', 'Caecum'], ['TerminalIleum', 'Terminal ileum'], ['Anastomosis', 'Anastomosis']].map(([k, l]) => <option key={k} value={k}>{l}</option>)}</select></label>
        {colon(p.kind) && !['Caecum', 'TerminalIleum', ''].includes(f.lowerExtent) && <label>Why the caecum was not reached<input value={f.incompleteReason} disabled={!write} onChange={(e) => setF({ ...f, incompleteReason: e.target.value })} placeholder="e.g. poor prep, obstruction, discomfort, looping" /></label>}
        {colon(p.kind) && <>{ynSel('appendicealOrifice', 'Appendiceal orifice seen')}{ynSel('ileocaecalValve', 'Ileocaecal valve seen')}{ynSel('terminalIleum', 'Terminal ileum intubated')}{ynSel('caecumPhotographed', 'Caecum photographed')}</>}
        {ynSel('rectalRetroflexion', 'Rectal retroflexion')}
      </div>
        {colon(p.kind) && <div className="form-grid">{bbps('bbpsRight', 'BBPS right colon')}{bbps('bbpsTransverse', 'BBPS transverse')}{bbps('bbpsLeft', 'BBPS left colon')}
          <p>{total == null ? '' : <>BBPS total <strong>{total}</strong> – {total >= 6 && [f.bbpsRight, f.bbpsTransverse, f.bbpsLeft].every((v) => Number(v) >= 2) ? <span className="ok-text">adequate</span> : <span className="error-text">inadequate</span>}</>}</p></div>}
      </fieldset>}
      <label>Comfort (modified Gloucester)<select value={f.comfort} disabled={!write} onChange={(e) => setF({ ...f, comfort: e.target.value })}><option value="">—</option>
        {[[1, 'Comfortable'], [2, 'Minimal – 1–2 episodes of mild discomfort'], [3, 'Mild – more than 2 episodes'], [4, 'Moderate – significant discomfort'], [5, 'Severe – frequent, prolonged']].map(([k, l]) => <option key={k} value={k}>{k} – {l}</option>)}</select></label>
      {write && <div><button className="btn btn-primary" onClick={() => save()}>Save procedure record</button></div>}
    </div>
  );
}

// ------------------------------------------------------------------ findings
function Findings({ r, t, act, write }: Ctx) {
  const p = r.procedure;
  const all = sites(t, p.kind);
  const blank = { id: null, site: all[0]?.code ?? '', kind: 'Normal', grade: '', sizeMm: '', description: '' };
  const [f, setF] = useState<any>(blank);
  const isUpper = t.upperSites.some((s: Item) => s.code === f.site);
  const kinds = t.findings.filter((k: any) => isUpper ? k.upper : k.lower);
  const kind = t.findings.find((k: any) => k.code === f.kind);
  const save = async () => { if (await act(() => api(f.id ? `/api/endoscopy/procedures/${p.id}/findings/${f.id}` : `/api/endoscopy/procedures/${p.id}/findings`, { method: f.id ? 'PUT' : 'POST', body: { ...f, grade: f.grade || null, sizeMm: num(f.sizeMm), description: f.description || null } }), 'Finding saved.')) setF(blank); };
  return (
    <div className="stack">
      {r.findings.length === 0 ? <p className="muted">No findings recorded. Record "Normal" for a normal examination.</p> : (
        <table className="results compact"><thead><tr><th>#</th><th>Site</th><th>Finding</th><th>Detail</th><th></th></tr></thead>
          <tbody>{r.findings.map((x: any) => <tr key={x.id}><td>{x.number}</td><td>{lbl(all, x.site)}</td><td>{t.findings.find((k: any) => k.code === x.kind)?.label ?? x.kind}{x.grade && ` (${x.grade})`}</td>
            <td className="small">{x.sizeMm ? `${x.sizeMm} mm ` : ''}{x.description}</td>
            <td>{write && <span className="row"><button className="btn btn-link" onClick={() => setF({ ...x, grade: x.grade ?? '', sizeMm: x.sizeMm ?? '', description: x.description ?? '' })}>Edit</button>
              <button className="btn btn-link danger" onClick={() => act(() => api(`/api/endoscopy/items/findings/${x.id}`, { method: 'DELETE' }), 'Removed.')}>Remove</button></span>}</td></tr>)}</tbody></table>)}
      {write && <div className="card subtle stack"><h2>{f.id ? `Change finding ${f.number}` : 'Add a finding'}</h2><div className="form-grid">
        <label>Site<select value={f.site} onChange={(e) => setF({ ...f, site: e.target.value })}>{all.map((s) => <option key={s.code} value={s.code}>{s.label}</option>)}</select></label>
        <label>Finding<select value={f.kind} onChange={(e) => setF({ ...f, kind: e.target.value, grade: '' })}>{kinds.map((k: any) => <option key={k.code} value={k.code}>{k.label}</option>)}</select></label>
        {kind?.grades.length > 0 && <label>{kind.gradeLabel}<select value={f.grade} onChange={(e) => setF({ ...f, grade: e.target.value })}><option value="">—</option>{kind.grades.map((g: string) => <option key={g}>{g}</option>)}</select></label>}
        <label>Size (mm)<input type="number" value={f.sizeMm} onChange={(e) => setF({ ...f, sizeMm: e.target.value })} /></label>
        <label>Description<input value={f.description} onChange={(e) => setF({ ...f, description: e.target.value })} /></label></div>
        <div className="row"><button className="btn btn-primary" onClick={save}>{f.id ? 'Save change' : 'Add finding'}</button>{f.id && <button className="btn btn-light" onClick={() => setF(blank)}>Cancel</button>}</div></div>}
    </div>
  );
}

// ------------------------------------------------------------------ polyps
function Polyps({ r, t, act, write }: Ctx) {
  const p = r.procedure;
  const all = sites(t, p.kind);
  const nextPot = `P${r.polyps.length + 1}`;
  const blank = { id: null, site: t.lowerSites.find((s: Item) => all.some((a) => a.code === s.code))?.code ?? all[0]?.code, distanceCm: '', sizeMm: '', paris: 'Is', nice: '', jnet: '', technique: 'ColdSnare', lifted: null, resection: 'EnBloc', retrieved: 'Yes', pot: nextPot, complete: 'Yes', clips: 0, tattooed: false, notes: '' };
  const [f, setF] = useState<any>(blank);
  const set = (k: string, v: any) => { const n = { ...f, [k]: v }; if (k === 'technique' && v === 'NotRemoved') { n.resection = 'NotRemoved'; n.retrieved = 'NotApplicable'; n.pot = ''; } if (k === 'technique' && v !== 'NotRemoved' && f.technique === 'NotRemoved') { n.resection = 'EnBloc'; n.retrieved = 'Yes'; n.pot = nextPot; } setF(n); };
  const save = async () => { if (await act(() => api(f.id ? `/api/endoscopy/procedures/${p.id}/polyps/${f.id}` : `/api/endoscopy/procedures/${p.id}/polyps`, { method: f.id ? 'PUT' : 'POST',
    body: { ...f, distanceCm: num(f.distanceCm), sizeMm: Number(f.sizeMm || 0), nice: f.nice || null, jnet: f.jnet || null, pot: f.pot || null, clips: Number(f.clips || 0), notes: f.notes || null } }), 'Polyp saved.')) setF({ ...blank, pot: `P${r.polyps.length + 2}` }); };
  const sel = (k: string, l: string, opts: (string | Item)[], allowBlank = false) => <label>{l}<select value={f[k] ?? ''} onChange={(e) => set(k, e.target.value)}>{allowBlank && <option value="">—</option>}
    {opts.map((o) => typeof o === 'string' ? <option key={o} value={o}>{o.replace(/([a-z])([A-Z])/g, '$1 $2')}</option> : <option key={o.code} value={o.code}>{o.label}</option>)}</select></label>;
  return (
    <div className="stack">
      {r.polyps.length === 0 ? <p className="muted">No polyps recorded.</p> : (
        <div className="table-scroll" tabIndex={0} role="region" aria-label="Polyps (scrolls sideways)"><table className="results compact"><thead><tr><th>#</th><th>Site</th><th>cm</th><th>mm</th><th>Paris</th><th>NICE</th><th>Removal</th><th>Retrieved</th><th>Pot</th><th>Complete</th><th>Histology</th><th></th></tr></thead>
          <tbody>{r.polyps.map((x: any) => <tr key={x.id}><td>{x.number}</td><td>{lbl(all, x.site)}</td><td>{x.distanceCm ?? ''}</td><td>{x.sizeMm}</td><td>{x.paris}</td><td>{x.nice ?? ''}</td>
            <td className="small">{lbl(t.techniques, x.technique)}{x.resection === 'Piecemeal' ? ', piecemeal' : ''}{x.clips ? `, ${x.clips} clip(s)` : ''}{x.tattooed ? ', tattooed' : ''}</td><td>{x.retrieved}</td><td>{x.pot}</td><td>{x.complete}</td>
            <td className="small">{x.histology ? lbl(t.histologies, x.histology) : <span className="muted">awaited</span>}</td>
            <td>{write && <span className="row"><button className="btn btn-link" onClick={() => setF({ ...x, distanceCm: x.distanceCm ?? '', nice: x.nice ?? '', jnet: x.jnet ?? '', pot: x.pot ?? '', notes: x.notes ?? '' })}>Edit</button>
              <button className="btn btn-link danger" onClick={() => act(() => api(`/api/endoscopy/items/polyps/${x.id}`, { method: 'DELETE' }), 'Removed.')}>Remove</button></span>}</td></tr>)}</tbody></table></div>)}
      {write && <div className="card subtle stack"><h2>{f.id ? `Change polyp ${f.number}` : 'Add a polyp'}</h2><div className="form-grid">
        {sel('site', 'Site', all)}
        <label>Distance from anus (cm)<input type="number" value={f.distanceCm} onChange={(e) => set('distanceCm', e.target.value)} /></label>
        <label>Size (mm)<input type="number" value={f.sizeMm} onChange={(e) => set('sizeMm', e.target.value)} /></label>
        {sel('paris', 'Paris classification', t.paris)}{sel('nice', 'NICE type', t.nice, true)}{sel('jnet', 'JNET type', t.jnet, true)}
        {sel('technique', 'Removal technique', t.techniques)}
        {f.technique === 'Emr' && <label>Lifted with injection<select value={yn(f.lifted)} onChange={(e) => set('lifted', fromYn(e.target.value))}><option value="">—</option><option value="yes">Yes</option><option value="no">No (non-lifting sign)</option></select></label>}
        {sel('resection', 'Resection', t.resections)}{sel('retrieved', 'Retrieved', t.retrievals)}
        <label>Specimen pot<input value={f.pot} onChange={(e) => set('pot', e.target.value)} /></label>
        {sel('complete', 'Complete resection', t.completeness)}
        <label>Clips<input type="number" value={f.clips} onChange={(e) => set('clips', e.target.value)} /></label>
        <label className="check"><input type="checkbox" checked={f.tattooed} onChange={(e) => set('tattooed', e.target.checked)} /> Tattooed (add the tattoo on the next tab)</label>
        <label>Notes<input value={f.notes} onChange={(e) => set('notes', e.target.value)} /></label></div>
        <div className="row"><button className="btn btn-primary" onClick={save}>{f.id ? 'Save change' : 'Add polyp'}</button>{f.id && <button className="btn btn-light" onClick={() => setF(blank)}>Cancel</button>}</div></div>}
    </div>
  );
}

// ------------------------------------------------------------------ tattoos, biopsies, therapy
function Therapy({ r, t, act, write }: Ctx) {
  const p = r.procedure;
  const all = sites(t, p.kind);
  const tBlank = { id: null, site: all[0]?.code, distanceCm: '', position: 'Distal', ink: 'Spot (carbon particle suspension)', volumeMl: '', injections: '', polypId: '', reason: '' };
  const iBlank = { id: null, kind: 'Biopsy', site: all[0]?.code, count: '', pot: '', purpose: '', details: '', result: '' };
  const [tt, setTt] = useState<any>(tBlank);
  const [iv, setIv] = useState<any>(iBlank);
  const saveT = async () => { if (await act(() => api(tt.id ? `/api/endoscopy/procedures/${p.id}/tattoos/${tt.id}` : `/api/endoscopy/procedures/${p.id}/tattoos`, { method: tt.id ? 'PUT' : 'POST',
    body: { ...tt, distanceCm: num(tt.distanceCm), volumeMl: num(tt.volumeMl), injections: num(tt.injections), polypId: tt.polypId || null, reason: tt.reason || null } }), 'Tattoo saved.')) setTt(tBlank); };
  const saveI = async () => { if (await act(() => api(iv.id ? `/api/endoscopy/procedures/${p.id}/therapy/${iv.id}` : `/api/endoscopy/procedures/${p.id}/therapy`, { method: iv.id ? 'PUT' : 'POST',
    body: { ...iv, count: num(iv.count), pot: iv.pot || null, purpose: iv.purpose || null, details: iv.details || null, result: iv.result || null } }), 'Saved.')) setIv(iBlank); };
  return (
    <div className="stack">
      {lower(p.kind) && <>
        <h2>Tattoos</h2>
        {r.tattoos.length === 0 ? <p className="muted">None.</p> : <table className="results compact"><thead><tr><th>Site</th><th>cm</th><th>Position</th><th>Ink</th><th>mL / injections</th><th>Polyp</th><th></th></tr></thead>
          <tbody>{r.tattoos.map((x: any) => <tr key={x.id}><td>{lbl(all, x.site)}</td><td>{x.distanceCm ?? ''}</td><td>{x.position}</td><td>{x.ink}</td><td>{x.volumeMl ?? ''} / {x.injections ?? ''}</td>
            <td>{r.polyps.find((q: any) => q.id === x.polypId)?.number ?? ''}</td>
            <td>{write && <span className="row"><button className="btn btn-link" onClick={() => setTt({ ...x, distanceCm: x.distanceCm ?? '', volumeMl: x.volumeMl ?? '', injections: x.injections ?? '', polypId: x.polypId ?? '', reason: x.reason ?? '' })}>Edit</button>
              <button className="btn btn-link danger" onClick={() => act(() => api(`/api/endoscopy/items/tattoos/${x.id}`, { method: 'DELETE' }), 'Removed.')}>Remove</button></span>}</td></tr>)}</tbody></table>}
        {write && <div className="card subtle stack"><div className="form-grid">
          <label>Site<select value={tt.site} onChange={(e) => setTt({ ...tt, site: e.target.value })}>{t.lowerSites.filter((s: Item) => all.some((a) => a.code === s.code)).map((s: Item) => <option key={s.code} value={s.code}>{s.label}</option>)}</select></label>
          <label>Distance from anus (cm)<input type="number" value={tt.distanceCm} onChange={(e) => setTt({ ...tt, distanceCm: e.target.value })} /></label>
          <label>In relation to the lesion<select value={tt.position} onChange={(e) => setTt({ ...tt, position: e.target.value })}>{t.tattooPositions.map((x: string) => <option key={x}>{x}</option>)}</select></label>
          <label>Ink<input value={tt.ink} onChange={(e) => setTt({ ...tt, ink: e.target.value })} /></label>
          <label>Volume (mL)<input type="number" step="0.5" value={tt.volumeMl} onChange={(e) => setTt({ ...tt, volumeMl: e.target.value })} /></label>
          <label>Injections<input type="number" value={tt.injections} onChange={(e) => setTt({ ...tt, injections: e.target.value })} /></label>
          <label>For polyp<select value={tt.polypId} onChange={(e) => setTt({ ...tt, polypId: e.target.value })}><option value="">—</option>{r.polyps.map((q: any) => <option key={q.id} value={q.id}>Polyp {q.number} – {q.sizeMm} mm {lbl(all, q.site)}</option>)}</select></label>
          <label>Reason<input value={tt.reason} onChange={(e) => setTt({ ...tt, reason: e.target.value })} placeholder="e.g. site of EMR for surveillance; suspicious lesion for surgery" /></label></div>
          <div className="row"><button className="btn btn-primary" onClick={saveT}>{tt.id ? 'Save tattoo' : 'Add tattoo'}</button>{tt.id && <button className="btn btn-light" onClick={() => setTt(tBlank)}>Cancel</button>}</div></div>}
      </>}
      <h2>Biopsies and therapy</h2>
      {r.therapy.length === 0 ? <p className="muted">None.</p> : <table className="results compact"><thead><tr><th>What</th><th>Site</th><th>No.</th><th>Pot</th><th>Purpose / details</th><th>Result</th><th></th></tr></thead>
        <tbody>{r.therapy.map((x: any) => <tr key={x.id}><td>{lbl(t.interventions, x.kind)}</td><td>{lbl(all, x.site)}</td><td>{x.count ?? ''}</td><td>{x.pot ?? ''}</td><td className="small">{[x.purpose, x.details].filter(Boolean).join(' – ')}</td><td>{x.result ?? ''}</td>
          <td>{write && <span className="row"><button className="btn btn-link" onClick={() => setIv({ ...x, count: x.count ?? '', pot: x.pot ?? '', purpose: x.purpose ?? '', details: x.details ?? '', result: x.result ?? '' })}>Edit</button>
            <button className="btn btn-link danger" onClick={() => act(() => api(`/api/endoscopy/items/therapy/${x.id}`, { method: 'DELETE' }), 'Removed.')}>Remove</button></span>}</td></tr>)}</tbody></table>}
      {write && <div className="card subtle stack"><div className="form-grid">
        <label>What<select value={iv.kind} onChange={(e) => setIv({ ...iv, kind: e.target.value })}>{t.interventions.map((k: Item) => <option key={k.code} value={k.code}>{k.label}</option>)}</select></label>
        <label>Site<select value={iv.site} onChange={(e) => setIv({ ...iv, site: e.target.value })}>{all.map((s) => <option key={s.code} value={s.code}>{s.label}</option>)}</select></label>
        <label>Number<input type="number" value={iv.count} onChange={(e) => setIv({ ...iv, count: e.target.value })} /></label>
        <label>Specimen pot<input value={iv.pot} onChange={(e) => setIv({ ...iv, pot: e.target.value })} placeholder={`B${r.therapy.filter((x: any) => x.kind === 'Biopsy').length + 1}`} /></label>
        <label>Purpose{iv.kind === 'Biopsy' ? <select value={iv.purpose} onChange={(e) => setIv({ ...iv, purpose: e.target.value })}><option value="">—</option>{t.biopsyPurposes.map((x: string) => <option key={x}>{x}</option>)}</select>
          : <input value={iv.purpose} onChange={(e) => setIv({ ...iv, purpose: e.target.value })} />}</label>
        <label>Details<input value={iv.details} onChange={(e) => setIv({ ...iv, details: e.target.value })} /></label>
        <label>Result (e.g. CLO)<input value={iv.result} onChange={(e) => setIv({ ...iv, result: e.target.value })} /></label></div>
        <div className="row"><button className="btn btn-primary" onClick={saveI}>{iv.id ? 'Save change' : 'Add'}</button>{iv.id && <button className="btn btn-light" onClick={() => setIv(iBlank)}>Cancel</button>}</div></div>}
    </div>
  );
}

// ------------------------------------------------------------------ pictures and video
function Images({ r, t, act, write }: Ctx) {
  const { can } = useSession();
  const p = r.procedure;
  const all = sites(t, p.kind);
  const canEdit = can(Perm.EndoWrite);
  if (r.media.length === 0) return <p className="muted">No pictures or video yet. Use the Capture tab, or connect the tower (Endoscopy › Settings).</p>;
  return (
    <div className="stack">
      <p className="hint">Tick pictures for the clinical report and the patient report, add the site and a caption, and link a picture to its polyp or finding. Videos stay in BrightPAS (they are not printed).</p>
      <div className="endo-gallery">{r.media.map((m: any) => <MediaCard key={m.id} m={m} all={all} r={r} t={t} act={act} canEdit={canEdit} canDelete={write || can(Perm.EndoManage)} />)}</div>
    </div>
  );
}

function MediaCard({ m, all, r, act, canEdit, canDelete }: { m: any; all: Item[]; r: any; t: any; act: Ctx['act']; canEdit: boolean; canDelete: boolean }) {
  const [f, setF] = useState({ site: m.site ?? '', caption: m.caption ?? '', inReport: m.inReport, inPatientReport: m.inPatientReport, findingId: m.findingId ?? '', polypId: m.polypId ?? '', sort: m.sort });
  const [drawing, setDrawing] = useState(false);
  const saveDrawing = async (png: Blob) => {
    const res = await fetch(`/api/endoscopy/media/${m.id}/annotate`, { method: 'POST', credentials: 'same-origin', headers: { 'X-BrightPAS-Request': '1', 'Content-Type': 'image/png' }, body: png });
    if (!res.ok) { const j = await res.json().catch(() => null); throw new Error(j?.errors?.join(' ') ?? `HTTP ${res.status}`); }
    setDrawing(false); await act(async () => null, 'Annotated copy saved – it replaces the original in the reports.');
  };
  const save = (n = f) => act(() => api(`/api/endoscopy/media/${m.id}`, { method: 'PUT', body: { ...n, site: n.site || null, caption: n.caption || null, findingId: n.findingId || null, polypId: n.polypId || null } }), 'Saved.');
  return (
    <figure className="endo-thumb">
      {m.kind === 'Image' ? <a href={m.url} target="_blank" rel="noreferrer"><img src={m.url} alt={m.caption ?? `Picture ${m.sort}`} /></a> : <video src={m.url} controls preload="metadata" aria-label={m.caption ?? 'Video'} />}
      <figcaption className="small stack">
        {m.annotatedFromId && <span className="pill blue">Annotated copy</span>}
        {m.kind === 'Video' && (m.archivedAt ? <span className="pill green">Archived {nzDateTime(m.archivedAt)}</span>
          : canEdit && <button type="button" className="btn btn-link" onClick={() => act(() => api(`/api/endoscopy/media/${m.id}/archive`, { body: { folder: null } }), 'Encrypted copy written to the archive folder.')}>Archive (encrypted copy)</button>)}{m.original && <a href={m.original}>Original {m.originalType === 'image/tiff' ? 'TIFF' : 'file'}</a>}
        <span className="muted">{nzDateTime(m.capturedAt)} · {m.source === 'Dicom' ? 'tower' : m.source === 'CaptureStation' ? 'capture' : 'upload'}{m.durationSeconds ? ` · ${Math.round(m.durationSeconds)} s` : ''}{m.bytes > 1048576 ? ` · ${Math.round(m.bytes / 1048576)} MB` : ''}</span>
        {canEdit ? <>
          <select aria-label="Site" value={f.site} onChange={(e) => setF({ ...f, site: e.target.value })}><option value="">Site…</option>{all.map((s) => <option key={s.code} value={s.code}>{s.label}</option>)}</select>
          <input aria-label="Caption" value={f.caption} placeholder="Caption" onChange={(e) => setF({ ...f, caption: e.target.value })} />
          {r.polyps.length > 0 && <select aria-label="Polyp" value={f.polypId} onChange={(e) => setF({ ...f, polypId: e.target.value })}><option value="">Polyp…</option>{r.polyps.map((q: any) => <option key={q.id} value={q.id}>Polyp {q.number}</option>)}</select>}
          {r.findings.length > 0 && <select aria-label="Finding" value={f.findingId} onChange={(e) => setF({ ...f, findingId: e.target.value })}><option value="">Finding…</option>{r.findings.map((q: any) => <option key={q.id} value={q.id}>Finding {q.number}</option>)}</select>}
          {m.kind === 'Image' && <><label className="check"><input type="checkbox" checked={f.inReport} onChange={(e) => { const n = { ...f, inReport: e.target.checked }; setF(n); save(n); }} /> Clinical report</label>
            <label className="check"><input type="checkbox" checked={f.inPatientReport} onChange={(e) => { const n = { ...f, inPatientReport: e.target.checked }; setF(n); save(n); }} /> Patient report</label></>}
          <span className="row"><button className="btn btn-light btn-small" onClick={() => save()}>Save</button>
            {m.kind === 'Image' && <button className="btn btn-light btn-small" onClick={() => setDrawing(true)}>Draw on picture</button>}
            {canDelete && <button className="btn btn-link danger" onClick={() => { const reason = window.prompt('Reason for deleting (e.g. wrong patient, blurred)'); if (reason) act(() => api(`/api/endoscopy/media/${m.id}?reason=${encodeURIComponent(reason)}`, { method: 'DELETE' }), 'Deleted.'); }}>Delete</button>}</span>
        </> : <span>{[m.site && all.find((s) => s.code === m.site)?.label, m.caption].filter(Boolean).join(' – ')}</span>}
      </figcaption>
      {drawing && <Annotator src={m.url} onSave={saveDrawing} onCancel={() => setDrawing(false)} />}
    </figure>
  );
}

// ------------------------------------------------------------------ report
function Report({ r, t, act, write }: Ctx) {
  const { can, mod } = useSession();
  const p = r.procedure;
  const [f, setF] = useState({ complication: p.complication ?? 'None', complicationDetails: p.complicationDetails ?? '', diagnosis: p.diagnosis ?? '', recommendations: p.recommendations ?? '', followUp: p.followUp ?? '', surveillanceMonths: p.surveillanceMonths ?? '', patientAdvice: p.patientAdvice ?? '' });
  const save = () => act(() => api(`/api/endoscopy/procedures/${p.id}/report`, { method: 'PUT', body: { ...f, complicationDetails: f.complicationDetails || null, diagnosis: f.diagnosis || null, recommendations: f.recommendations || null,
    followUp: f.followUp || null, surveillanceMonths: num(f.surveillanceMonths), patientAdvice: f.patientAdvice || null } }), 'Report saved.');
  const draft = () => {
    const parts = [...r.findings.filter((x: any) => x.kind !== 'Normal').map((x: any) => `${t.findings.find((k: any) => k.code === x.kind)?.label}${x.grade ? ` (${x.grade})` : ''} – ${lbl(sites(t, p.kind), x.site).toLowerCase()}`),
      ...(r.polyps.length ? [`${r.polyps.length} polyp(s), ${r.polyps.map((x: any) => `${x.sizeMm} mm ${lbl(sites(t, p.kind), x.site).toLowerCase()}`).join(', ')}`] : [])];
    setF({ ...f, diagnosis: parts.length ? parts.join('; ') + '.' : 'Normal examination.' });
  };
  return (
    <div className="stack">
      {r.missing.length > 0 && <div className="card subtle"><strong>Still to record before signing:</strong><ul className="plain">{r.missing.map((m: string) => <li key={m}>{m}</li>)}</ul></div>}
      <div className="form-grid">
        <label>Complication<select value={f.complication} disabled={!write} onChange={(e) => setF({ ...f, complication: e.target.value })}>{t.complications.map((c: string) => <option key={c}>{c}</option>)}</select></label>
        {f.complication !== 'None' && <label>What happened and what was done<input value={f.complicationDetails} disabled={!write} onChange={(e) => setF({ ...f, complicationDetails: e.target.value })} /></label>}
      </div>
      <label>Conclusion / diagnosis{write && <DictateButton onText={(t) => setF((x) => ({ ...x, diagnosis: x.diagnosis ? x.diagnosis + (t.startsWith('\n') ? '' : ' ') + t : t }))} />}{write && <button type="button" className="btn btn-link" onClick={draft}>Draft from findings</button>}<textarea rows={3} value={f.diagnosis} disabled={!write} onChange={(e) => setF({ ...f, diagnosis: e.target.value })} /></label>
      <label>Recommendations (for the GP and referrer){write && <DictateButton onText={(t) => setF((x) => ({ ...x, recommendations: x.recommendations ? x.recommendations + (t.startsWith('\n') ? '' : ' ') + t : t }))} />}<textarea rows={3} value={f.recommendations} disabled={!write} onChange={(e) => setF({ ...f, recommendations: e.target.value })} /></label>
      <div className="form-grid">
        <label>Follow-up<input value={f.followUp} disabled={!write} onChange={(e) => setF({ ...f, followUp: e.target.value })} placeholder="e.g. Clinic review with histology in 4 weeks" /></label>
        <label>Next colonoscopy in (months)<input type="number" value={f.surveillanceMonths} disabled={!write} onChange={(e) => setF({ ...f, surveillanceMonths: e.target.value })} /></label>
      </div>
      {r.suggestion && <p className="hint">Suggested (confirm clinically): {r.suggestion}{write && /(\d+) years?/.test(r.suggestion) && <button type="button" className="btn btn-link" onClick={() => setF({ ...f, surveillanceMonths: String(Number(/(\d+) years?/.exec(r.suggestion)![1]) * 12) })}>Use</button>}</p>}
      <label>Advice for the patient (in plain words – printed on the patient report){write && <DictateButton onText={(t) => setF((x) => ({ ...x, patientAdvice: x.patientAdvice ? x.patientAdvice + (t.startsWith('\n') ? '' : ' ') + t : t }))} />}<textarea rows={3} value={f.patientAdvice} disabled={!write} onChange={(e) => setF({ ...f, patientAdvice: e.target.value })} /></label>
      <div className="row">
        {write && <button className="btn btn-primary" onClick={save}>Save report</button>}
        {write && can(Perm.EndoSign) && <button className="btn btn-primary" onClick={async () => { await save(); act(() => api(`/api/endoscopy/procedures/${p.id}/sign`, { body: {} }), 'Report signed.'); }}>Save and sign</button>}
        {p.status === 'Signed' && can(Perm.EndoSign) && <button className="btn btn-light" onClick={() => { const reason = window.prompt('Reason for amending the signed report'); if (reason) act(() => api(`/api/endoscopy/procedures/${p.id}/amend`, { body: { reason } }), 'The report is open for amendment – sign it again when done.'); }}>Amend</button>}
      </div>
      <h2>Reports</h2>
      <div className="row">
        <ExportButton label="Clinical report (PDF)" formats={['pdf']} path={`/api/endoscopy/procedures/${p.id}/report-pdf`} body={{ kind: 'clinical' }} />
        <ExportButton label="Patient report (PDF)" formats={['pdf']} path={`/api/endoscopy/procedures/${p.id}/report-pdf`} body={{ kind: 'patient' }} />
        {p.status === 'Signed' && can(Perm.EndoSign) && <>
          <button className="btn btn-light" onClick={() => act(() => api(`/api/endoscopy/procedures/${p.id}/save-to-record`, { body: { kind: 'clinical' } }), 'Clinical report saved to the patient record (Documents).')}>Save clinical report to patient record</button>
          <button className="btn btn-light" onClick={() => act(() => api(`/api/endoscopy/procedures/${p.id}/save-to-record`, { body: { kind: 'patient' } }), 'Patient report saved to the patient record (Documents).')}>Save patient report to patient record</button></>}
      </div>
      {mod('letters') && <div className="stack"><h2>Send to the GP</h2>
        {r.gpLetter ? <p>Sent as letter <strong>{r.gpLetter.number}</strong> to {r.gpLetter.recipientName} – {r.gpLetter.status}{r.gpLetter.sentAt ? `, ${nzDateTime(r.gpLetter.sentAt)}` : ''}{r.gpLetter.ackText ? ` – HealthLink says: ${r.gpLetter.ackText}` : ''}.</p>
          : p.status === 'Signed' && can(Perm.EndoSign) ? <div><button className="btn btn-primary" onClick={() => act(() => api(`/api/endoscopy/procedures/${p.id}/send-to-gp`, { body: {} }), (x) => x.message)}>Send report to GP by HealthLink</button></div>
          : <p className="muted">Sign the report to send it to the patient's GP by HealthLink (the PDF with pictures goes with a covering letter).</p>}</div>}
      <p className="hint">Pictures ticked "Clinical report" or "Patient report" in the Pictures & video tab are printed in that report. Patient report wording is set in Endoscopy › Settings.</p>
    </div>
  );
}

// ------------------------------------------------------------------ histology
function Histology({ r, t, act }: Ctx) {
  const { can } = useSession();
  const write = can(Perm.EndoWrite);
  const all = sites(t, r.procedure.kind);
  if (r.polyps.length === 0) return <p className="muted">No polyps on this procedure. Biopsy results go to the patient's lab results.</p>;
  return (
    <div className="stack">
      <p className="hint">Enter histology when the results arrive (it can be added after signing). It feeds the adenoma detection rate and the surveillance suggestion.</p>
      <table className="results compact"><thead><tr><th>#</th><th>Polyp</th><th>Histology</th><th>Dysplasia</th><th>Notes</th><th></th></tr></thead>
        <tbody>{r.polyps.map((x: any) => <HistoRow key={x.id} x={x} all={all} t={t} act={act} write={write} />)}</tbody></table>
      {r.suggestion && <p>Surveillance suggestion: <strong>{r.suggestion}</strong></p>}
    </div>
  );
}

function HistoRow({ x, all, t, act, write }: { x: any; all: Item[]; t: any; act: Ctx['act']; write: boolean }) {
  const [f, setF] = useState({ histology: x.histology ?? '', dysplasia: x.dysplasia ?? '', notes: x.histologyNotes ?? '' });
  return (
    <tr><td>{x.number}</td><td className="small">{x.sizeMm} mm {lbl(all, x.site)}, pot {x.pot ?? '—'}</td>
      <td><select aria-label={`Histology polyp ${x.number}`} value={f.histology} disabled={!write} onChange={(e) => setF({ ...f, histology: e.target.value })}><option value="">Awaited</option>{t.histologies.map((h: Item) => <option key={h.code} value={h.code}>{h.label}</option>)}</select></td>
      <td><select aria-label={`Dysplasia polyp ${x.number}`} value={f.dysplasia} disabled={!write} onChange={(e) => setF({ ...f, dysplasia: e.target.value })}><option value="">—</option>{t.dysplasias.map((d: string) => <option key={d}>{d}</option>)}</select></td>
      <td><input aria-label={`Notes polyp ${x.number}`} value={f.notes} disabled={!write} onChange={(e) => setF({ ...f, notes: e.target.value })} /></td>
      <td>{write && <button className="btn btn-light btn-small" onClick={() => act(() => api(`/api/endoscopy/polyps/${x.id}/histology`, { method: 'PUT', body: { histology: f.histology || null, dysplasia: f.dysplasia || null, notes: f.notes || null } }), 'Histology saved.')}>Save</button>}
        {x.histologyBy && <div className="small muted">{x.histologyBy}</div>}</td></tr>
  );
}
