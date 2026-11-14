import { useCallback, useEffect, useRef, useState } from 'react';
import { Link, useParams } from 'react-router';
import { api, errText, formatDate, nzDateTime } from '../api';
import { CONTEXTS, STATUS } from './Scribe';

const SPEAKERS = ['Clinician', 'Patient', 'Support person', 'Interpreter', 'Unknown'];
const KIND: Record<string, string> = { dose: 'Dose', lasa: 'Look-alike medicine', medicine: 'Medicine', side: 'Side', unsure: 'Unsure' };

/** One scribe session: consent, recording, transcription, draft review beside the transcript, safety highlights, sign-off. */
export default function ScribeSession() {
  const { id } = useParams();
  const [d, setD] = useState<any>(null);
  const [error, setError] = useState<string | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [text, setText] = useState('');
  const [checks, setChecks] = useState<any[]>([]);
  const [active, setActive] = useState<number[]>([]);
  const [confirmed, setConfirmed] = useState(false);
  const [paste, setPaste] = useState('');
  const load = useCallback(async () => {
    const x = await api(`/api/scribe/sessions/${id}`);
    setD(x); setText(x.session.signedText ?? x.session.draft ?? ''); setChecks(x.highlights ?? []);
  }, [id]);
  useEffect(() => { load().catch((e) => setError(errText(e))); }, [load]);
  const act = async (label: string, fn: () => Promise<any>, ok?: string | ((x: any) => string)) => {
    setError(null); setMsg(null); setBusy(label);
    try { const x = await fn(); if (ok) setMsg(typeof ok === 'function' ? ok(x) : ok); await load(); return x ?? true; } catch (x) { setError(errText(x)); return null; } finally { setBusy(null); }
  };
  if (!d) return error ? <div className="card"><p className="error">{error}</p></div> : <p>Loading…</p>;
  const s = d.session;
  const open = s.status !== 'Signed' && s.status !== 'Discarded';
  const medow = s.provider === 'Medow';
  const product = medow ? 'Medow Health' : 'BrightPAS Scribe';
  const sections = [...new Set<string>(d.sentences.map((x: any) => x.section))];
  return (
    <div className="stack-lg">
      <div className="patient-banner"><div><h1>{d.patient.name}</h1><p>NHI {d.patient.nhi ?? '—'} · {d.patient.mrn} · born {formatDate(d.patient.dateOfBirth)} · {d.patient.sex}</p>
        <p>{CONTEXTS.find(([k]) => k === s.context)?.[1]} · {d.template.name} (v{s.templateVersion}){d.procedure ? ` · ${d.procedure}` : ''} · {product}</p></div>
        <div className="row"><Link className="btn btn-light btn-small" to="/scribe">Scribe</Link><Link className="btn btn-light btn-small" to={`/patients/${d.patient.id}`}>Patient record</Link></div></div>
      {d.patient.alerts.map((a: any, i: number) => <p key={i} className={a.severity === 'Critical' ? 'alert-critical' : 'alert-warning'}><strong>{a.severity === 'Critical' ? 'Critical alert' : 'Alert'}:</strong> {a.type} – {a.description}</p>)}
      {error && <p className="error" role="alert">{error}</p>}{msg && <p className="ok-text" role="status">{msg}</p>}
      <section className="card stack">
        <p><span className={s.status === 'Signed' ? 'pill green' : s.status === 'Drafted' ? 'pill amber' : 'pill'}>{STATUS[s.status]}</span> {s.clinician}, {nzDateTime(s.createdAt)}
          {s.durationSeconds ? ` · ${Math.round(s.durationSeconds)} s recorded` : ''}{s.audioDeletedAt ? ' · audio deleted' : ''}</p>

        {open && !d.consented && <Consent d={d} product={product} act={act} />}
        {d.consented && open && !medow && s.status === 'Recording' && <Recorder id={s.id} act={act} />}
        {d.consented && open && medow && <div className="card subtle stack"><h2>Medow Health</h2>
          <p className="small">Only this minimum information is shared with Medow (no NHI, address or contact details):</p>
          <pre className="small">{JSON.stringify(d.externalPayload, null, 2)}</pre>
          {d.settings.medowLaunchUrl && <a className="btn btn-light" href={`${d.settings.medowLaunchUrl}${d.settings.medowLaunchUrl.includes('?') ? '&' : '?'}ref=${s.id}`} target="_blank" rel="noreferrer">Open Medow</a>}
          <label>Medow's draft comes back here automatically when connected – or paste it<textarea rows={6} value={paste} onChange={(e) => setPaste(e.target.value)} /></label>
          <div><button className="btn btn-primary" disabled={!paste.trim() || !!busy} onClick={() => act('paste', () => api(`/api/scribe/sessions/${s.id}/paste`, { body: { text: paste } }), 'Draft added – review and sign.')}>Use this draft</button></div></div>}

        {open && !medow && d.consented && s.status !== 'Recording' && <div className="row">
          {(s.status === 'Recorded' || s.status === 'Transcribed' || s.status === 'Drafted') && !s.audioDeletedAt && <button className="btn btn-light" disabled={!!busy}
            onClick={() => act('transcribe', () => api(`/api/scribe/sessions/${s.id}/transcribe`, { body: {} }), (x) => `Transcribed: ${x.segments} part(s) by ${x.engine}.`)}>{busy === 'transcribe' ? 'Transcribing…' : s.status === 'Recorded' ? 'Transcribe' : 'Transcribe again'}</button>}
          {(s.status === 'Transcribed' || s.status === 'Drafted') && <button className="btn btn-primary" disabled={!!busy}
            onClick={() => act('draft', () => api(`/api/scribe/sessions/${s.id}/draft`, { body: {} }), (x) => `Draft written by ${x.engine}${x.deIdentified ? ' (identifiers removed before sending)' : ''} – check it against the transcript.`)}>{busy === 'draft' ? 'Drafting…' : s.status === 'Drafted' ? 'Draft again' : 'Draft the note'}</button>}
          <span className="small muted">Drafting: {d.settings.draft === 'TemplateOnly' ? 'template only (no AI)' : d.settings.draft === 'CloudClaude' ? 'Claude API, de-identified' : 'on-site model'}</span></div>}

        {(d.segments.length > 0 || d.sentences.length > 0) && <div className="scribe-review">
          <div className="stack"><h2>Transcript</h2>
            {d.segments.length === 0 ? <p className="muted">No transcript (external draft).</p> : d.segments.map((g: any) => <div key={g.seq} className={`scribe-seg${active.includes(g.seq) ? ' on' : ''}${g.confidence != null && g.confidence < 0.6 ? ' unsure' : ''}`}>
              <span className="small muted">{g.seq} · {Math.floor(g.start / 60)}:{String(Math.floor(g.start % 60)).padStart(2, '0')}</span>
              {open ? <select aria-label={`Speaker for part ${g.seq}`} value={g.speaker} onChange={(e) => act('speaker', () => api(`/api/scribe/sessions/${s.id}/speaker`, { method: 'PUT', body: { seq: g.seq, speaker: e.target.value } }))}>{SPEAKERS.map((x) => <option key={x}>{x}</option>)}</select>
                : <strong className="small">{g.speaker}</strong>}
              <p>{g.text}</p></div>)}</div>
          <div className="stack"><h2>Draft</h2>
            {sections.map((sec) => <div key={sec}><h3>{sec}</h3>{d.sentences.filter((x: any) => x.section === sec).map((x: any) => <p key={x.seq} className={`scribe-sentence${x.sources ? ' linked' : ''}`}
              onMouseEnter={() => setActive((x.sources ?? '').split(',').filter(Boolean).map(Number))} onFocus={() => setActive((x.sources ?? '').split(',').filter(Boolean).map(Number))} tabIndex={0}>{x.text || <span className="muted">(nothing said)</span>}</p>)}</div>)}</div>
        </div>}

        {(s.status === 'Drafted' || s.status === 'Signed') && <div className="stack">
          <h2>{s.status === 'Signed' ? 'Signed note' : 'Note to sign (edit as needed)'}</h2>
          {checks.length > 0 && <div className="card subtle"><strong>Check before signing:</strong><ul className="plain">{checks.map((h: any, i: number) => <li key={i}><span className="pill amber">{KIND[h.kind] ?? h.kind}</span> “{text.substr(h.start, h.length)}” – {h.message}</li>)}</ul></div>}
          <textarea rows={14} value={text} disabled={!open || !d.mine} onChange={(e) => setText(e.target.value)} aria-label="Note text" />
          {open && d.mine && <>
            <div className="row"><button className="btn btn-light btn-small" onClick={async () => setChecks(await api(`/api/scribe/sessions/${s.id}/check`, { body: { text } }))}>Check again</button></div>
            <label className="check"><input type="checkbox" checked={confirmed} onChange={(e) => setConfirmed(e.target.checked)} /> I have checked this AI-assisted note against the conversation and it is correct.</label>
            <div className="row"><button className="btn btn-primary" disabled={!confirmed || !!busy} onClick={() => act('sign', () => api(`/api/scribe/sessions/${s.id}/sign`, { body: { text } }), (x) => x.message)}>Sign note</button>
              <button className="btn btn-link danger" onClick={() => { const reason = window.prompt('Reason for discarding this session'); if (reason) act('discard', () => api(`/api/scribe/sessions/${s.id}/discard`, { body: { reason } }), 'Discarded – audio deleted.'); }}>Discard</button></div></>}
          {open && !d.mine && <p className="muted">Only {s.clinician} can sign this session.</p>}
          {s.status === 'Signed' && <p className="small muted">Signed by {s.signedBy}, {nzDateTime(s.signedAt)} · AI-assisted ({s.draftEngine}) · {s.editPercent}% edited{s.noteId ? ' · in the clinical record' : ''}{s.documentId ? ' · in the patient\'s documents' : ''}.</p>}
        </div>}
        {open && s.status !== 'Drafted' && <div><button className="btn btn-link danger" onClick={() => { const reason = window.prompt('Reason for discarding this session'); if (reason) act('discard', () => api(`/api/scribe/sessions/${s.id}/discard`, { body: { reason } }), 'Discarded – audio deleted.'); }}>Discard session</button></div>}
        {s.status === 'Discarded' && <p className="muted">Discarded: {s.discardReason}</p>}
      </section>
    </div>
  );
}

function Consent({ d, product, act }: { d: any; product: string; act: (l: string, fn: () => Promise<any>, ok?: string) => Promise<any> }) {
  const declined = d.consent && !d.consent.given;
  const rec = (given: boolean, standing: boolean) => act('consent', () => api('/api/scribe/consent', { body: { patientId: d.patient.id, sessionId: d.session.id, given, standing } }), given ? 'Consent recorded.' : 'Recorded that the patient declined – the scribe is off for this session.');
  return (
    <div className="card subtle stack">
      <h2>Patient consent</h2>
      <p>Ask: <em>“Is it OK if {product} records and transcribes our conversation to help write your notes? I check everything it writes, and the recording is deleted once the note is signed.”</em></p>
      {declined ? <p className="error-text">The patient declined – recording is off for this session. You can type the note in the usual way.</p> :
        <div className="row"><button className="btn btn-primary" onClick={() => rec(true, false)}>Patient agrees (this visit)</button>
          <button className="btn btn-light" onClick={() => rec(true, true)}>Agrees – for future visits too</button>
          <button className="btn btn-light" onClick={() => rec(false, false)}>Patient declines</button></div>}
    </div>
  );
}

function Recorder({ id, act }: { id: string; act: (l: string, fn: () => Promise<any>, ok?: string) => Promise<any> }) {
  const st = useRef<{ r: MediaRecorder; stream: MediaStream; seq: number; queue: Promise<void>; started: number } | null>(null);
  const [on, setOn] = useState(false);
  const [secs, setSecs] = useState(0);
  const [problem, setProblem] = useState<string | null>(null);
  useEffect(() => { if (!on) return; const i = setInterval(() => setSecs(Math.round((Date.now() - (st.current?.started ?? Date.now())) / 1000)), 500); return () => clearInterval(i); }, [on]);
  useEffect(() => () => { const c = st.current; if (c) { c.r.stop(); c.stream.getTracks().forEach((t) => t.stop()); } }, []);
  const start = async () => {
    setProblem(null);
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true } });
      const type = ['audio/webm;codecs=opus', 'audio/ogg;codecs=opus', 'audio/mp4', 'audio/webm'].find((m) => MediaRecorder.isTypeSupported(m)) ?? '';
      const r = new MediaRecorder(stream, type ? { mimeType: type, audioBitsPerSecond: 64000 } : undefined);
      const state = { r, stream, seq: 0, queue: Promise.resolve(), started: Date.now() };
      r.ondataavailable = (e) => {
        if (!e.data.size) return;
        const seq = state.seq++;
        state.queue = state.queue.then(() => fetch(`/api/scribe/sessions/${id}/audio/${seq}`, { method: 'POST', credentials: 'same-origin', headers: { 'X-BrightPAS-Request': '1', 'Content-Type': r.mimeType || 'audio/webm' }, body: e.data })
          .then(async (res) => { if (!res.ok) { const j = await res.json().catch(() => null); setProblem(j?.errors?.join(' ') ?? 'Part of the recording was not saved.'); } }).catch(() => setProblem('Connection lost while saving audio.')));
      };
      r.start(5000);
      st.current = state; setOn(true); setSecs(0);
    } catch { setProblem('The microphone could not be opened – check it is plugged in and allowed for this site.'); }
  };
  const stop = async () => {
    const c = st.current; if (!c) return;
    st.current = null; setOn(false);
    await new Promise<void>((res) => { c.r.onstop = () => res(); c.r.stop(); });
    c.stream.getTracks().forEach((t) => t.stop());
    await c.queue;
    await act('stop', () => api(`/api/scribe/sessions/${id}/stop`, { body: { seconds: (Date.now() - c.started) / 1000 } }), 'Recording saved – transcribe it next.');
  };
  const mmss = `${Math.floor(secs / 60)}:${String(secs % 60).padStart(2, '0')}`;
  return (
    <div className="card subtle stack">
      <div className="row">{on ? <button className="btn btn-warn btn-big" onClick={stop}>Stop recording {mmss}</button> : <button className="btn btn-primary btn-big" onClick={start}>Start recording</button>}
        {on && <span className="endo-rec-inline" aria-live="polite">● Recording – speak normally; the patient can ask to stop at any time.</span>}</div>
      {problem && <p className="error-text" role="alert">{problem}</p>}
      <p className="hint">Use a headset or desk microphone. Audio is saved encrypted every few seconds and deleted when the note is signed.</p>
    </div>
  );
}
