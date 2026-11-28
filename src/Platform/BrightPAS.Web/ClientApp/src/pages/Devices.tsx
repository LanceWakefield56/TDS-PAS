import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { api, errText, nzDateTime, Perm } from '../api';
import { useSession } from '../App';
import FileImport from '../components/FileImport';
import Modal from '../components/Modal';

const fmt = (v: number, code: string) => Number(v).toFixed(code === 'Temp' ? 1 : 0);

/** Devices & SaferSleep: monitors with live status and who is connected, SaferSleep records, settings. */
export default function Devices() {
  const { can } = useSession();
  const manage = can(Perm.DevicesManage);
  const [params, setParams] = useSearchParams();
  const tab = params.get('tab') ?? 'monitors';
  const tabs: [string, string][] = [['monitors', 'Monitors'], ['safersleep', 'SaferSleep'], ...(can(Perm.DevicesUse) ? [['import', 'Import from file'] as [string, string]] : []), ...(manage ? [['settings', 'Settings'] as [string, string]] : [])];
  return (
    <div className="stack-lg">
      <h1>Monitors &amp; SaferSleep</h1>
      <div className="tabs" role="tablist">{tabs.map(([k, t]) => <button key={k} role="tab" aria-selected={tab === k} className={tab === k ? 'on' : ''} onClick={() => setParams({ tab: k })}>{t}</button>)}</div>
      {tab === 'monitors' && <Monitors manage={manage} />}
      {tab === 'safersleep' && <SaferSleep manage={manage} />}
      {tab === 'import' && can(Perm.DevicesUse) && <div className="stack">
        <FileImport url="/api/imports/monitors" title="Monitor readings from a file" accept=".hl7,.txt,.zip,.dat"
          hint="When the monitor network or the CARESCAPE Gateway was down: export the readings from the gateway or central station as HL7 (USB or network share) and choose the file. Readings are filed to whichever patient was connected to that monitor at the time of each reading; readings already held are skipped. They appear in the trend – chart any you need with the nurse's validation as usual." />
        <FileImport url="/api/imports/safersleep" title="SaferSleep anaesthetic records from a file" accept=".hl7,.txt,.zip"
          hint="When the SaferSleep link was down: export the completed record from SaferSleep as HL7 (with its PDF) and choose the file. It is matched and filed like a live record; a record already received is not filed twice." />
      </div>}
      {tab === 'settings' && manage && <Settings />}
    </div>
  );
}

function Monitors({ manage }: { manage: boolean }) {
  const [o, setO] = useState<any>(null);
  const [eq, setEq] = useState<any[]>([]);
  const [edit, setEdit] = useState<any>(null);
  const [error, setError] = useState<string | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const load = async () => setO(await api('/api/devices/overview'));
  useEffect(() => {
    load().catch((e) => setError(errText(e)));
    if (manage) api('/api/devices/equipment').then(setEq).catch(() => undefined);
    const t = setInterval(() => { load().catch(() => undefined); }, 10000);
    return () => clearInterval(t);
  }, [manage]); // eslint-disable-line react-hooks/exhaustive-deps
  const save = async () => {
    setError(null); setMsg(null);
    try {
      await api(edit.id ? `/api/devices/monitors/${edit.id}` : '/api/devices/monitors', { method: edit.id ? 'PUT' : 'POST', body: { equipmentId: edit.equipmentId, code: edit.code, isActive: edit.isActive } });
      setMsg('Monitor saved.'); setEdit(null); await load(); setEq(await api('/api/devices/equipment'));
    } catch (x: any) { setError(x?.body?.errors?.join(' ') ?? errText(x)); }
  };
  if (!o) return error ? <p className="error">{error}</p> : <p>Loading…</p>;
  return (
    <div className="stack">
      {error && <p className="error" role="alert">{error}</p>}{msg && <p className="ok-text" role="status">{msg}</p>}
      {o.monitors.length === 0 && <p className="muted">No monitors set up yet.</p>}
      <div className="device-grid">
        {o.monitors.map((m: any) => (
          <section key={m.id} className="card stack-sm" aria-label={`Monitor ${m.assetNumber ?? m.code}`}>
            <div className="row-between"><h2>{m.assetNumber ?? m.code} – {m.name}</h2>
              <span className={!m.isActive ? 'pill' : m.online ? 'pill green' : 'pill bad'}>{!m.isActive ? 'Switched off' : m.online ? 'Online' : 'Offline'}</span></div>
            <p className="small muted">{m.make} {m.model}{m.location ? ` · ${m.location}` : ''} · gateway name {m.code}{m.address ? ` · ${m.address}` : ''}<br />Last message {m.lastSeenAt ? nzDateTime(m.lastSeenAt) : 'never'}</p>
            {m.live.length > 0 ? <div className="live-values">{m.live.map((v: any) => <div key={v.code} className="live-value"><span className="small muted">{v.label}</span><strong>{fmt(v.value, v.code)}</strong><span className="small">{v.unit}</span></div>)}</div>
              : <p className="muted small">No recent values.</p>}
            {m.connected ? <p>Connected: <strong>{m.connected.patient}</strong> (NHI {m.connected.nhi ?? '—'}) since {nzDateTime(m.connected.startedAt)}{m.connected.method !== 'Manual' && ` · ${m.connected.method}`}
              {m.connected.nhiMismatch && <span className="error-text"> – the monitor is sending a different NHI ({m.lastNhi}). Check the patient on the monitor.</span>}</p>
              : <p className="muted small">No patient connected{m.lastNhi ? ` (monitor has NHI ${m.lastNhi})` : ''}.</p>}
            {manage && <div><button className="btn btn-light btn-small" onClick={() => setEdit({ id: m.id, equipmentId: m.equipmentId, code: m.code, isActive: m.isActive })}>Edit</button></div>}
          </section>))}
      </div>
      {manage && (edit ? (
        <Modal title={edit.id ? 'Edit monitor' : 'Add a monitor'} onClose={() => setEdit(null)}>
          <div className="form-grid">
            <label>Equipment<select value={edit.equipmentId} onChange={(e) => setEdit({ ...edit, equipmentId: e.target.value })}><option value="">Choose from the equipment register…</option>
              {eq.filter((x) => !x.used || x.id === edit.equipmentId).map((x) => <option key={x.id} value={x.id}>{x.assetNumber} – {x.name} ({x.make} {x.model})</option>)}</select></label>
            <label>Name the gateway sends<input value={edit.code} onChange={(e) => setEdit({ ...edit, code: e.target.value })} placeholder="e.g. PACU/BAY1 or MON-02" /></label>
            <label className="check"><input type="checkbox" checked={edit.isActive} onChange={(e) => setEdit({ ...edit, isActive: e.target.checked })} /> Switched on</label>
          </div>
          <p className="hint">The GE gateway identifies each monitor by its care unit and bed label (PV1-3) or device id (OBX-18). Enter the same text here.</p>
          <div className="row"><button className="btn btn-primary" disabled={!edit.equipmentId || !edit.code} onClick={save}>Save monitor</button><button className="btn btn-light" onClick={() => setEdit(null)}>Cancel</button></div>
        </Modal>) : <div><button className="btn btn-light" onClick={() => setEdit({ id: null, equipmentId: '', code: '', isActive: true })}>Add a monitor</button></div>)}
      <section className="card stack-sm">
        <h2>Interfaces</h2>
        {o.interfaces.length === 0 ? <p className="muted">No device interfaces in the messaging engine yet.</p> :
          <table className="results compact"><thead><tr><th>Interface</th><th>Direction</th><th>Connection</th><th>Status</th></tr></thead>
            <tbody>{o.interfaces.map((i: any) => <tr key={i.code}><td>{i.name} <span className="small muted">({i.code})</span></td><td>{i.direction}</td><td className="small">{i.transport} {i.host ?? ''}{i.port ? `:${i.port}` : ''}</td><td>{i.isActive ? <span className="pill green">On</span> : <span className="pill">Off</span>}</td></tr>)}</tbody></table>}
        <p className="small"><Link to="/messaging/interfaces">Messaging engine interfaces</Link> · monitors show offline after {o.offlineAfterSeconds} seconds without a message.</p>
      </section>
    </div>
  );
}

function SaferSleep({ manage }: { manage: boolean }) {
  const [rows, setRows] = useState<any[] | null>(null);
  const [only, setOnly] = useState(false);
  const [filing, setFiling] = useState<any>(null);
  const [cands, setCands] = useState<any[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const load = async () => setRows(await api(`/api/devices/records${only ? '?unmatched=true' : ''}`));
  useEffect(() => { load().catch((e) => setError(errText(e))); }, [only]); // eslint-disable-line react-hooks/exhaustive-deps
  const open = async (r: any) => { setFiling(r); setCands(await api(`/api/devices/records/${r.id}/candidates`)); };
  const file = async (episodeId: string) => {
    setError(null); setMsg(null);
    try { await api(`/api/devices/records/${filing.id}/file`, { body: { episodeId } }); setMsg(`Case ${filing.caseNumber} filed to the episode.`); setFiling(null); await load(); }
    catch (x: any) { setError(x?.body?.errors?.join(' ') ?? errText(x)); }
  };
  if (!rows) return error ? <p className="error">{error}</p> : <p>Loading…</p>;
  return (
    <div className="stack">
      {error && <p className="error" role="alert">{error}</p>}{msg && <p className="ok-text" role="status">{msg}</p>}
      <label className="check"><input type="checkbox" checked={only} onChange={(e) => setOnly(e.target.checked)} /> Only records not matched to an episode</label>
      {rows.length === 0 ? <p className="muted">No anaesthetic records{only ? ' waiting to be filed' : ' received yet'}.</p> : (
        <table className="results"><thead><tr><th>Received</th><th>Case</th><th>Patient</th><th>Anaesthetist</th><th>Drugs</th><th>Filed</th></tr></thead>
          <tbody>{rows.map((r) => <tr key={r.id}><td>{nzDateTime(r.receivedAt)}</td><td>{r.caseNumber}{r.isCorrection && <span className="pill">Corrected</span>}</td><td>{r.patient ?? '—'} <span className="small muted">{r.nhi}</span></td>
            <td>{r.anaesthetist ?? ''}</td><td>{r.drugs}</td>
            <td>{r.episodeId ? <span className="pill green">Filed</span> : manage ? <button className="btn btn-light btn-small" onClick={() => open(r)}>File to an episode</button> : <span className="pill bad">Not matched</span>}</td></tr>)}</tbody></table>)}
      {filing && <section className="card stack" aria-label="File the anaesthetic record">
        <h2>File case {filing.caseNumber}</h2>
        <p className="small">Choose the episode this record belongs to{filing.nhi ? ` (patients with NHI ${filing.nhi})` : ''}.</p>
        {cands.length === 0 ? <p className="muted">No admitted episodes found for this patient.</p> :
          <ul className="plain">{cands.map((c) => <li key={c.episodeId} className="row-between"><span>{c.patient} · NHI {c.nhi ?? '—'} · admitted {nzDateTime(c.admittedAt)} · {c.stage}</span>
            <button className="btn btn-primary btn-small" onClick={() => file(c.episodeId)}>File here</button></li>)}</ul>}
        <div><button className="btn btn-light" onClick={() => setFiling(null)}>Cancel</button></div>
      </section>}
    </div>
  );
}

function Settings() {
  const [s, setS] = useState<any>(null);
  const [error, setError] = useState<string | null>(null);
  const [msg, setMsg] = useState<string | null>(null);
  useEffect(() => { api('/api/devices/settings').then(setS).catch((e) => setError(errText(e))); }, []);
  if (!s) return error ? <p className="error">{error}</p> : <p>Loading…</p>;
  const num = (k: string, label: string) => <label>{label}<input type="number" value={s[k]} onChange={(e) => setS({ ...s, [k]: Number(e.target.value) })} /></label>;
  const save = async () => {
    setError(null); setMsg(null);
    try { await api('/api/devices/settings', { method: 'PUT', body: s }); setMsg('Settings saved.'); } catch (x: any) { setError(x?.body?.errors?.join(' ') ?? errText(x)); }
  };
  return (
    <section className="card stack">
      {error && <p className="error" role="alert">{error}</p>}{msg && <p className="ok-text" role="status">{msg}</p>}
      <h2>Bedside monitors</h2>
      <div className="form-grid">
        {num('theatreChartMinutes', 'Offer a reading in theatre every (minutes)')}{num('recoveryChartMinutes', 'In recovery every (minutes)')}{num('wardChartMinutes', 'On the ward / stage 2 every (minutes)')}
        {num('storeEverySeconds', 'Keep monitor values every (seconds)')}{num('offlineAfterSeconds', 'Show offline after (seconds)')}{num('unassociatedKeepHours', 'Delete values with no patient after (hours)')}
        {num('keepMonitorMessagesHours', 'Delete monitor messages from the message store after (hours)')}
        <label className="check"><input type="checkbox" checked={s.autoConnectByNhi} onChange={(e) => setS({ ...s, autoConnectByNhi: e.target.checked })} /> Connect automatically when the monitor sends the patient's NHI</label>
      </div>
      <h2>SaferSleep</h2>
      <div className="form-grid">
        <label className="check"><input type="checkbox" checked={s.saferSleepEnabled} onChange={(e) => setS({ ...s, saferSleepEnabled: e.target.checked })} /> SaferSleep interface on</label>
        <label>Outbound interface code<input value={s.saferSleepOutCode ?? ''} onChange={(e) => setS({ ...s, saferSleepOutCode: e.target.value })} /></label>
        <label>Receiving application (MSH-5)<input value={s.saferSleepApplication ?? ''} onChange={(e) => setS({ ...s, saferSleepApplication: e.target.value })} /></label>
        <label>Receiving facility (MSH-6)<input value={s.saferSleepFacility ?? ''} onChange={(e) => setS({ ...s, saferSleepFacility: e.target.value })} /></label>
        <label>Send the patient to SaferSleep<select value={s.sendOn} onChange={(e) => setS({ ...s, sendOn: e.target.value })}><option value="Admission">On admission</option><option value="InTheatre">When they go into theatre</option></select></label>
        <label className="check"><input type="checkbox" checked={s.fileDrugsToEmr} onChange={(e) => setS({ ...s, fileDrugsToEmr: e.target.checked })} /> Add anaesthetic drugs to the eMR medicines list</label>
      </div>
      <p className="hint">The SaferSleep message profile (ADT out, anaesthetic record ORU with PDF back) must be confirmed with SaferSleep before go-live. Controlled drugs given in theatre stay on the anaesthetist's register; they are not taken off the BrightPAS register automatically.</p>
      <div><button className="btn btn-primary" onClick={save}>Save settings</button></div>
    </section>
  );
}
