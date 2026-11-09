import { useEffect, useState } from 'react';
import { api, errText, Perm } from '../api';
import { useSession } from '../App';

export const SEVEN_ZIP = 'https://www.7-zip.org/download.html';

/** Administrator control: how exports and uploads protect patient data, Zebra printers, and PA ↔ specialist portal links. */
export default function SecuritySettings() {
  const { can, mod } = useSession();
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Security, exports &amp; printers</h1><p className="muted">These settings change straight away and every change is recorded in the audit log.</p></div>
      {can(Perm.UsersManage) && <Exports />}
      {can(Perm.OrganisationManage) && <Printers />}
      {can(Perm.UsersManage) && mod('portal') && <PortalSettings />}
      {can(Perm.UsersManage) && <DocumentText />}
      {can(Perm.UsersManage) && mod('portal') && <Delegates />}
    </div>
  );
}

function Exports() {
  const [s, setS] = useState<any>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => { api('/api/admin/security-settings').then(setS).catch((e) => setError(errText(e))); }, []);
  const save = async () => { setError(null); setMsg(null); try { await api('/api/admin/security-settings', { method: 'PUT', body: s }); setMsg('Saved.'); } catch (x: any) { setError(x?.body?.errors?.join(' ') ?? errText(x)); } };
  if (!s) return <section className="card">{error ? <p className="error">{error}</p> : <p>Loading…</p>}</section>;
  return (
    <section className="card stack">
      <h2>Exports and uploads</h2>
      <label className="check"><input type="radio" checked={s.exportProtection === 'Required'} onChange={() => setS({ ...s, exportProtection: 'Required' })} /> Every export must be encrypted and password-protected (recommended)</label>
      <label className="check"><input type="radio" checked={s.exportProtection === 'Optional'} onChange={() => setS({ ...s, exportProtection: 'Optional' })} /> Staff may choose (unprotected exports are flagged in the audit log)</label>
      <div className="row">
        <label>Minimum export password length<input type="number" className="narrow" min={8} max={64} value={s.minExportPasswordLength} onChange={(e) => setS({ ...s, minExportPasswordLength: Number(e.target.value) })} /></label>
        <label>Largest upload (MB)<input type="number" className="narrow" min={1} max={25} value={s.maxUploadMegabytes} onChange={(e) => setS({ ...s, maxUploadMegabytes: Number(e.target.value) })} /></label>
        <label>Patient portal links last (days)<input type="number" className="narrow" min={1} max={90} value={s.portalLinkDays} onChange={(e) => setS({ ...s, portalLinkDays: Number(e.target.value) })} /></label>
      </div>
      <p className="hint">Protected exports are AES-256 encrypted ZIP files. Applies to waitlist, NBRS, payroll and transfer-pack exports.</p>
      <div className="notice">
        <strong>Install 7-Zip on every workstation that opens exports.</strong> Windows' built-in ZIP cannot open AES-256 files.
        <div className="row"><a className="btn btn-light" href={SEVEN_ZIP} target="_blank" rel="noopener noreferrer">Download 7-Zip (free, official site)</a></div>
        <span className="small">Choose "64-bit Windows x64". IT can deploy the .msi silently: <code>msiexec /i 7z-x64.msi /qn</code>. Recipients outside TDS (e.g. a hospital) also need 7-Zip, WinZip or WinRAR.</span>
      </div>
      {error && <p className="error">{error}</p>}{msg && <p className="ok-text">{msg}</p>}
      <button className="btn btn-primary" onClick={save}>Save</button>
    </section>
  );
}

function Printers() {
  const [list, setList] = useState<any[] | null>(null);
  const [e, setE] = useState<any>(null);
  const [error, setError] = useState<string | null>(null);
  const [media, setMedia] = useState<any[]>([]);
  const load = () => api<any>('/api/admin/printers').then((r) => { setList(r.printers); setMedia(r.dymoMedia); }).catch((x) => setError(errText(x)));
  useEffect(() => { load(); }, []);
  const save = async () => { setError(null); try { await api('/api/admin/printers', { body: e }); setE(null); load(); } catch (x: any) { setError(x?.body?.errors?.join(' ') ?? errText(x)); } };
  const blank = (kind: string, language = 'Zpl') => ({ name: '', kind, language, mediaCode: language === 'Dymo' ? '99012' : null, host: '', port: 9100, dpi: 300, widthMm: kind === 'Wristband' ? 25 : 50, lengthMm: kind === 'Wristband' ? 280 : 25, isDefault: true, isActive: true });
  return (
    <section className="card stack">
      <h2>Label and wristband printers</h2>
      {!list ? <p>Loading…</p> : list.length === 0 ? <p className="muted">No printers yet.</p> : (
        <table className="results compact"><tbody>{list.map((p) => <tr key={p.id}><td>{p.name}<div className="small muted">{p.kind}{p.isDefault ? ' · default' : ''}{p.isActive ? '' : ' · off'}</div></td><td>{p.language === 'Dymo' ? 'DYMO (this computer)' : `Zebra ${p.host}:${p.port}`}</td><td>{p.language === 'Dymo' ? `DYMO ${p.mediaCode}` : `${p.dpi} dpi`} · {p.widthMm}×{p.lengthMm} mm</td>
          <td><button className="btn btn-link" onClick={() => setE(p)}>Edit</button></td></tr>)}</tbody></table>)}
      {!e && <div className="row"><button className="btn btn-light" onClick={() => setE(blank('Wristband'))}>Add wristband printer</button><button className="btn btn-light" onClick={() => setE(blank('Label'))}>Add Zebra label printer</button><button className="btn btn-light" onClick={() => setE(blank('Label', 'Dymo'))}>Add DYMO label printer</button></div>}
      {e && <div className="card subtle stack"><div className="form-grid">
        <label>Name<input value={e.name} onChange={(x) => setE({ ...e, name: x.target.value })} placeholder="e.g. Admissions wristbands" /></label>
        <label>Type<select value={e.kind} onChange={(x) => setE({ ...e, kind: x.target.value })}><option>Wristband</option><option>Label</option></select></label>
        <label>Printer type<select value={e.language} onChange={(x) => setE({ ...e, language: x.target.value, kind: x.target.value === 'Dymo' ? 'Label' : e.kind, mediaCode: x.target.value === 'Dymo' ? (e.mediaCode ?? '99012') : null })}><option value="Zpl">Zebra (network, ZPL)</option><option value="Dymo">DYMO LabelWriter (USB on this computer)</option></select></label>
        {e.language === 'Dymo' ? <label>Label stock<select value={e.mediaCode ?? ''} onChange={(x) => setE({ ...e, mediaCode: x.target.value })}>{media.map((m) => <option key={m.code} value={m.code}>{m.name}</option>)}</select></label> : <>
        <label>IP address or name<input value={e.host} onChange={(x) => setE({ ...e, host: x.target.value })} placeholder="e.g. 192.168.1.60" /></label>
        <label>Port<input type="number" value={e.port} onChange={(x) => setE({ ...e, port: Number(x.target.value) })} /></label>
        <label>Resolution<select value={e.dpi} onChange={(x) => setE({ ...e, dpi: Number(x.target.value) })}>{[203, 300, 600].map((d) => <option key={d} value={d}>{d} dpi</option>)}</select></label>
        <label>Width (mm)<input type="number" value={e.widthMm} onChange={(x) => setE({ ...e, widthMm: Number(x.target.value) })} /></label>
        <label>Length (mm)<input type="number" value={e.lengthMm} onChange={(x) => setE({ ...e, lengthMm: Number(x.target.value) })} /></label>
        </>}
        <label className="check"><input type="checkbox" checked={e.isDefault} onChange={(x) => setE({ ...e, isDefault: x.target.checked })} /> Default for this type</label>
        <label className="check"><input type="checkbox" checked={e.isActive} onChange={(x) => setE({ ...e, isActive: x.target.checked })} /> In use</label></div>
        <div className="row"><button className="btn btn-primary" onClick={save}>Save</button><button className="btn btn-light" onClick={() => setE(null)}>Cancel</button></div></div>}
      <p className="hint">Zebra: BrightPAS prints straight to the printer over the network (port 9100); wristbands e.g. Zebra HC100 / ZD510-HC. DYMO: install the DYMO driver on each workstation; labels open in the browser sized to the stock – choose the DYMO printer, margins None, scale 100 %. DYMO LabelWriters cannot print wristbands.</p>
      {error && <p className="error">{error}</p>}
    </section>
  );
}

function PortalSettings() {
  const [s, setS] = useState<any>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => { api('/api/portal/settings').then(setS).catch((e) => setError(errText(e))); }, []);
  const toggle = async (on: boolean) => {
    setError(null); setMsg(null); setS({ ...s, autoSendOnBooking: on });
    try { await api('/api/portal/settings', { method: 'PUT', body: { autoSendOnBooking: on } }); setMsg('Saved.'); } catch (x: any) { setS({ ...s, autoSendOnBooking: !on }); setError(x?.body?.errors?.join(' ') ?? errText(x)); }
  };
  return (
    <section className="card stack">
      <h2>Patient portal links</h2>
      {!s ? (error ? <p className="error">{error}</p> : <p>Loading…</p>) : <>
        <label className="check"><input type="checkbox" checked={s.autoSendOnBooking} disabled={!s.remindersLicensed} onChange={(e) => toggle(e.target.checked)} /> Send a portal link by SMS automatically when a procedure is booked</label>
        <p className="hint">{s.remindersLicensed ? 'Sent only if the patient has a mobile number and no active link. The portal shows their procedure, the pre-admission questionnaire and any issued estimate to accept. Quiet hours apply.' : 'Needs the Reminders module (SMS gateway).'}</p>
      </>}
      {msg && <p className="ok-text">{msg}</p>}
      {error && s && <p className="error" role="alert">{error}</p>}
    </section>
  );
}

const textNames: Record<string, string> = { Pending: 'Waiting to be read', Done: 'Searchable', NoText: 'No text found', Failed: 'Could not be read' };
function DocumentText() {
  const [s, setS] = useState<any>(null);
  const [msg, setMsg] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const load = () => api('/api/admin/document-text').then(setS).catch((e) => setError(errText(e)));
  useEffect(() => { load(); }, []);
  const retry = async () => { setError(null); setMsg(null); try { const r = await api('/api/admin/document-text/retry', { body: {} }); setMsg(`${r?.queued ?? 0} document(s) queued again.`); load(); } catch (x) { setError(errText(x)); } };
  const counts: Record<string, number> = s?.summary?.counts ?? {};
  return (
    <section className="card stack">
      <h2>Text recognition (OCR) on documents</h2>
      <p className="hint">Scanned and uploaded documents are read in the background so staff can search inside them on the patient record. The text is stored encrypted and searched one patient at a time.</p>
      {!s ? (error ? <p className="error">{error}</p> : <p>Loading…</p>) : <>
        {s.engineProblem && <p className="error" role="alert">{s.engineProblem}</p>}
        <p>{Object.keys(textNames).map((k) => `${textNames[k]}: ${counts[k] ?? 0}`).join(' · ')}</p>
        {s.summary?.lastProblem && <p className="small muted">Last problem: {s.summary.lastProblem}</p>}
        {(counts.Failed ?? 0) > 0 && <div><button className="btn btn-light" onClick={retry}>Try failed documents again</button></div>}
      </>}
      {msg && <p className="ok-text">{msg}</p>}
    </section>
  );
}

function Delegates() {
  const [list, setList] = useState<any[] | null>(null);
  const [users, setUsers] = useState<any[]>([]);
  const [clinicians, setClinicians] = useState<any[]>([]);
  const [f, setF] = useState({ userName: '', clinicianId: '' });
  const [error, setError] = useState<string | null>(null);
  const load = () => api<any[]>('/api/portal/delegates').then(setList).catch((x) => setError(errText(x)));
  useEffect(() => { load(); api<any[]>('/api/admin/users').then(setUsers).catch(() => undefined); api<any[]>('/api/organisation/clinicians').then(setClinicians).catch(() => undefined); }, []);
  const add = async () => { setError(null); try { await api('/api/portal/delegates', { body: f }); setF({ userName: '', clinicianId: '' }); load(); } catch (x: any) { setError(x?.body?.errors?.join(' ') ?? errText(x)); } };
  const remove = async (d: any) => { setError(null); try { await api('/api/portal/delegates/remove', { body: { userName: d.userName, clinicianId: d.clinicianId } }); load(); } catch (x) { setError(errText(x)); } };
  return (
    <section className="card stack">
      <h2>Practice assistants (portal)</h2>
      <p className="hint">A PA sees the portal for each specialist they are linked to. Specialists themselves are linked by user name in Organisation set-up › Clinicians.</p>
      {list && list.length > 0 && <ul className="plain">{list.map((d) => <li key={d.id}>{d.userName} → {d.clinician} <button className="btn btn-link" onClick={() => remove(d)}>Remove</button></li>)}</ul>}
      <div className="row">
        <label>PA user<select value={f.userName} onChange={(e) => setF({ ...f, userName: e.target.value })}><option value="">Choose…</option>{users.filter((u) => u.isActive).map((u) => <option key={u.id} value={u.userName}>{u.displayName} ({u.userName})</option>)}</select></label>
        <label>Works for<select value={f.clinicianId} onChange={(e) => setF({ ...f, clinicianId: e.target.value })}><option value="">Choose…</option>{clinicians.map((c: any) => <option key={c.id} value={c.id}>{c.name}</option>)}</select></label>
        <button className="btn btn-light" disabled={!f.userName || !f.clinicianId} onClick={add}>Link</button>
      </div>
      {error && <p className="error">{error}</p>}
    </section>
  );
}
