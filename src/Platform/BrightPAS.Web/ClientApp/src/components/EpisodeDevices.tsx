import { useEffect, useState } from 'react';
import { api, errText, nzDateTime, Perm } from '../api';
import { useSession } from '../App';

const time = (iso: string | null | undefined) => iso ? new Date(iso).toLocaleTimeString('en-NZ', { hour: '2-digit', minute: '2-digit' }) : '';
const fmt = (v: number | null | undefined, d = 0) => v == null ? '–' : Number(v).toFixed(d);

/** Small trend chart (SVG, no inline styles): heart rate, SpO2 and systolic BP over time. */
export function Trend({ points, label }: { points: { at: string; code: string; value: number }[]; label: string }) {
  const series: [string, string][] = [['HR', 'trend-hr'], ['SpO2', 'trend-spo2'], ['NIBP-S', 'trend-bp'], ['RR', 'trend-rr']];
  const pts = points.filter((p) => series.some(([c]) => c === p.code));
  if (pts.length < 2) return <p className="muted small">Not enough readings for a trend yet.</p>;
  const t0 = Math.min(...pts.map((p) => new Date(p.at).getTime())), t1 = Math.max(...pts.map((p) => new Date(p.at).getTime()));
  const W = 600, H = 160, span = Math.max(t1 - t0, 60000);
  const x = (t: string) => ((new Date(t).getTime() - t0) / span) * (W - 40) + 30;
  const y = (v: number) => H - 15 - (Math.min(Math.max(v, 0), 200) / 200) * (H - 30);
  return (
    <figure className="trend">
      <svg viewBox={`0 0 ${W} ${H}`} role="img" aria-label={label}>
        {[50, 100, 150].map((g) => <g key={g}><line x1="30" x2={W - 10} y1={y(g)} y2={y(g)} className="trend-grid" /><text x="2" y={y(g) + 4} className="trend-axis">{g}</text></g>)}
        {series.map(([c, cls]) => {
          const s = pts.filter((p) => p.code === c);
          return s.length > 1 ? <polyline key={c} className={cls} points={s.map((p) => `${x(p.at).toFixed(1)},${y(p.value).toFixed(1)}`).join(' ')} /> : null;
        })}
      </svg>
      <figcaption className="small"><span className="trend-key trend-hr">HR</span> <span className="trend-key trend-spo2">SpO2</span> <span className="trend-key trend-bp">Systolic BP</span> <span className="trend-key trend-rr">RR</span> · {time(new Date(t0).toISOString())}–{time(new Date(t1).toISOString())}</figcaption>
    </figure>
  );
}

function Live({ live }: { live: any[] }) {
  if (!live?.length) return <p className="muted small">No values from the monitor in the last few minutes.</p>;
  return <div className="live-values" aria-live="polite">{live.map((v: any) => <div key={v.code} className="live-value"><span className="small muted">{v.label}</span><strong>{fmt(v.value, v.code === 'Temp' ? 1 : 0)}</strong><span className="small">{v.unit}</span></div>)}</div>;
}

function Pending({ c, done }: { c: any; done: (fn: () => Promise<any>, ok: string | ((x: any) => string)) => Promise<any> }) {
  const [v, setV] = useState({ heartRate: c.heartRate != null, spO2: c.spO2 != null, bloodPressure: c.systolicBp != null, respiratoryRate: c.respiratoryRate != null, temperature: c.temperatureC != null, consciousness: 'A', pain: '', onOxygen: false, oxygenLitres: '', sedation: '' });
  const [reason, setReason] = useState('');
  const n = (x: any) => x === '' || x == null ? null : Number(x);
  const box = (k: keyof typeof v, text: string, has: boolean) => has && <label className="check"><input type="checkbox" checked={v[k] as boolean} onChange={(e) => setV({ ...v, [k]: e.target.checked })} /> {text}</label>;
  return (
    <div className="card subtle stack" aria-label={`Monitor reading at ${time(c.at)}`}>
      <p><strong>{time(c.at)}</strong> · HR {fmt(c.heartRate)} · SpO2 {fmt(c.spO2)}% · BP {fmt(c.systolicBp)}/{fmt(c.diastolicBp)} · RR {fmt(c.respiratoryRate)} · Temp {fmt(c.temperatureC, 1)}{c.etCo2 != null && ` · EtCO2 ${fmt(c.etCo2)}`}</p>
      <div className="row">{box('heartRate', 'Heart rate', c.heartRate != null)}{box('spO2', 'SpO2', c.spO2 != null)}{box('bloodPressure', 'Blood pressure', c.systolicBp != null)}{box('respiratoryRate', 'Resp rate', c.respiratoryRate != null)}{box('temperature', 'Temperature', c.temperatureC != null)}</div>
      <div className="form-grid">
        <label>Consciousness<select value={v.consciousness} onChange={(e) => setV({ ...v, consciousness: e.target.value })}>{[['A', 'Alert'], ['C', 'New confusion'], ['V', 'Responds to voice'], ['P', 'Responds to pain'], ['U', 'Unresponsive']].map(([k, l]) => <option key={k} value={k}>{l}</option>)}</select></label>
        <label>Pain 0–10<input type="number" value={v.pain} onChange={(e) => setV({ ...v, pain: e.target.value })} /></label>
        <label className="check"><input type="checkbox" checked={v.onOxygen} onChange={(e) => setV({ ...v, onOxygen: e.target.checked })} /> On oxygen</label>
        {v.onOxygen && <label>Oxygen L/min<input type="number" value={v.oxygenLitres} onChange={(e) => setV({ ...v, oxygenLitres: e.target.value })} /></label>}
        <label>Sedation score<input type="number" value={v.sedation} onChange={(e) => setV({ ...v, sedation: e.target.value })} /></label>
      </div>
      <div className="row">
        <button className="btn btn-primary btn-small" onClick={() => done(() => api(`/api/devices/captures/${c.id}/validate`, { body: { ...v, pain: n(v.pain), oxygenLitres: n(v.oxygenLitres), sedation: n(v.sedation) } }), 'Charted – the reading is now in Observations.')}>Validate and chart</button>
        <label className="inline">Reason not charted<input value={reason} onChange={(e) => setReason(e.target.value)} placeholder="e.g. artefact – probe off" /></label>
        <button className="btn btn-light btn-small" onClick={() => done(() => api(`/api/devices/captures/${c.id}/reject`, { body: { reason } }), 'Not charted – reason recorded.')}>Don't chart</button>
      </div>
    </div>
  );
}

/** eMR tab: bedside monitor connection, readings waiting for validation, trend, and the SaferSleep anaesthetic record. */
export default function EpisodeDevices({ episodeId, reloadEpisode }: { episodeId: string; reloadEpisode: () => Promise<void> }) {
  const { can } = useSession();
  const [d, setD] = useState<any>(null);
  const [monitor, setMonitor] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const load = async () => setD(await api(`/api/devices/episodes/${episodeId}`));
  useEffect(() => {
    load().catch((e) => setError(errText(e)));
    const t = setInterval(() => { load().catch(() => undefined); }, 10000);
    return () => clearInterval(t);
  }, [episodeId]); // eslint-disable-line react-hooks/exhaustive-deps
  const act = async (fn: () => Promise<any>, ok: string | ((x: any) => string)) => {
    setError(null); setMsg(null);
    try { const x = await fn(); setMsg(typeof ok === 'function' ? ok(x) : ok); await load(); await reloadEpisode(); return x ?? true; } catch (x: any) { setError(x?.body?.errors?.join(' ') ?? errText(x)); return null; }
  };
  if (!d) return error ? <p className="error">{error}</p> : <p>Loading…</p>;
  const use = can(Perm.DevicesUse);
  const chosen = d.monitors.find((m: any) => m.id === monitor);
  return (
    <div className="stack">
      {error && <p className="error" role="alert">{error}</p>}{msg && <p className="ok-text" role="status">{msg}</p>}
      <h2>Bedside monitor</h2>
      {d.connected ? (
        <div className="card subtle stack">
          <div className="row-between"><p><strong>Connected: {d.connected.label}</strong> <span className={d.connected.online ? 'pill green' : 'pill bad'}>{d.connected.online ? 'Online' : 'Offline'}</span><br />
            <span className="small muted">Since {nzDateTime(d.connected.startedAt)} by {d.connected.startedBy}{d.connected.method !== 'Manual' ? ` (${d.connected.method})` : ''}</span></p>
            {use && <button className="btn btn-light btn-small" onClick={() => act(() => api(`/api/devices/associations/${d.connected.associationId}/disconnect`, { method: 'POST' }), 'Monitor disconnected.')}>Disconnect monitor</button>}</div>
          <Live live={d.connected.live} />
          <p className="hint">Readings are offered to the chart every {d.stage === 'InTheatre' ? d.intervals.theatre : d.stage === 'Recovery' ? d.intervals.recovery : d.intervals.ward} minutes. Nothing goes into the observation chart until a nurse validates it.</p>
        </div>
      ) : d.open ? (use ? (
        <div className="card subtle stack">
          <label>Monitor<select aria-label="Bedside monitor" value={monitor} onChange={(e) => setMonitor(e.target.value)}><option value="">Choose the monitor at the bedside…</option>
            {d.monitors.map((m: any) => <option key={m.id} value={m.id}>{m.label}{m.online ? '' : ' – offline'}</option>)}</select></label>
          {chosen && <MonitorPreview id={chosen.id} />}
          <div><button className="btn btn-primary" disabled={!monitor} onClick={() => act(() => api(`/api/devices/episodes/${episodeId}/connect`, { body: { monitorId: monitor } }), 'Monitor connected to this patient.')}>Connect monitor</button></div>
          <p className="hint">Check the values shown match the monitor at the bedside before connecting.</p>
        </div>) : <p className="muted">No monitor connected.</p>) : <p className="muted">Monitors can be connected while the patient is admitted.</p>}

      <h2>Readings waiting for validation ({d.pending.length})</h2>
      {d.pending.length === 0 ? <p className="muted">None waiting.</p> : use ? d.pending.map((c: any) => <Pending key={c.id} c={c} done={act} />) : <p>{d.pending.length} reading(s) waiting for a nurse.</p>}

      <h2>Trend</h2>
      <Trend points={d.trend} label="Monitor trend for this episode" />
      {d.decided.length > 0 && <details><summary>Validated and not-charted readings ({d.decided.length})</summary>
        <table className="results compact"><thead><tr><th>Time</th><th>Values</th><th>Outcome</th><th>By</th></tr></thead><tbody>
          {d.decided.map((c: any) => <tr key={c.id}><td>{time(c.at)}</td><td className="small">HR {fmt(c.heartRate)} · SpO2 {fmt(c.spO2)} · BP {fmt(c.systolicBp)}/{fmt(c.diastolicBp)} · RR {fmt(c.respiratoryRate)}</td>
            <td>{c.status === 'Validated' ? <span className="pill green">Charted</span> : <span className="pill">Not charted</span>}{c.rejectReason && <div className="small">{c.rejectReason}</div>}</td><td className="small">{c.decidedBy}</td></tr>)}</tbody></table></details>}

      <h2>Anaesthetic record (SaferSleep)</h2>
      {!d.record ? <p className="muted">No anaesthetic record received yet.</p> : (
        <div className="card subtle stack">
          <p><strong>Case {d.record.caseNumber}</strong>{d.record.isCorrection && <span className="pill">Corrected</span>} · {d.record.anaesthetist ?? 'Anaesthetist not named'} · {time(d.record.startAt)}–{time(d.record.endAt)}
            {d.record.versions > 1 && <span className="small muted"> · version {d.record.versions}</span>}</p>
          <p className="small">{[d.record.asa && `ASA ${d.record.asa}`, d.record.technique, d.record.airway].filter(Boolean).join(' · ')}</p>
          {d.record.drugs.length > 0 && <table className="results compact"><caption>Drugs</caption><thead><tr><th>Time</th><th>Drug</th><th>Dose</th><th>Route</th></tr></thead>
            <tbody>{d.record.drugs.map((x: any, i: number) => <tr key={i}><td>{time(x.at)}</td><td>{x.name}</td><td>{x.rate ?? `${x.dose ?? ''} ${x.unit ?? ''}`}</td><td>{x.route}</td></tr>)}</tbody></table>}
          {d.record.events.length > 0 && <ul className="plain small">{d.record.events.map((e: any, i: number) => <li key={i}>{time(e.at)} {e.text}</li>)}</ul>}
          <Trend points={d.record.vitals} label="Anaesthetic vital signs from SaferSleep" />
          {d.record.comments && <p className="small">{d.record.comments}</p>}
          {d.record.documentId && <p><a href={`/api/patients/documents/${d.record.documentId}`} target="_blank" rel="noreferrer">Open the anaesthetic record (PDF)</a></p>}
        </div>)}
      {d.saferSleep.enabled && <div className="row-between"><p className="small muted">Sent to SaferSleep: {d.saferSleep.sends.length === 0 ? 'not yet' : d.saferSleep.sends.map((s: any) => `${s.event} ${time(s.at)}${s.error ? ' (failed: ' + s.error + ')' : ''}`).join(', ')}</p>
        {use && d.open && <button className="btn btn-light btn-small" onClick={() => act(() => api(`/api/devices/episodes/${episodeId}/safersleep`, { method: 'POST' }), (x) => x.message)}>Send to SaferSleep</button>}</div>}
    </div>
  );
}

function MonitorPreview({ id }: { id: string }) {
  const [o, setO] = useState<any>(null);
  useEffect(() => {
    let live = true;
    const get = () => api('/api/devices/overview').then((x) => { if (live) setO(x.monitors.find((m: any) => m.id === id)); }).catch(() => undefined);
    get(); const t = setInterval(get, 5000);
    return () => { live = false; clearInterval(t); };
  }, [id]);
  if (!o) return null;
  return <div className="stack-sm"><p className="small">{o.make} {o.model}{o.location ? ` · ${o.location}` : ''}{o.lastNhi ? ` · monitor patient NHI ${o.lastNhi}` : ''}</p><Live live={o.live} /></div>;
}
