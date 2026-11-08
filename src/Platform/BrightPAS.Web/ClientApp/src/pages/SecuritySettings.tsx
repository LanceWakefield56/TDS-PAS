import { useEffect, useState } from 'react';
import { api, errText, Perm } from '../api';
import { useSession } from '../App';

/** Administrator control: how exports and uploads protect patient data, Zebra printers, and PA ↔ specialist portal links. */
export default function SecuritySettings() {
  const { can, mod } = useSession();
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Security, exports &amp; printers</h1><p className="muted">These settings change straight away and every change is recorded in the audit log.</p></div>
      {can(Perm.UsersManage) && <Exports />}
      {can(Perm.OrganisationManage) && <Printers />}
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
      <p className="hint">Protected exports are AES-256 encrypted ZIP files (open with 7-Zip, WinZip or WinRAR). Applies to waitlist, NBRS, payroll and transfer-pack exports.</p>
      {error && <p className="error">{error}</p>}{msg && <p className="ok-text">{msg}</p>}
      <button className="btn btn-primary" onClick={save}>Save</button>
    </section>
  );
}

function Printers() {
  const [list, setList] = useState<any[] | null>(null);
  const [e, setE] = useState<any>(null);
  const [error, setError] = useState<string | null>(null);
  const load = () => api<any[]>('/api/admin/printers').then(setList).catch((x) => setError(errText(x)));
  useEffect(() => { load(); }, []);
  const save = async () => { setError(null); try { await api('/api/admin/printers', { body: e }); setE(null); load(); } catch (x: any) { setError(x?.body?.errors?.join(' ') ?? errText(x)); } };
  const blank = (kind: string) => ({ name: '', kind, host: '', port: 9100, dpi: 300, widthMm: kind === 'Wristband' ? 25 : 50, lengthMm: kind === 'Wristband' ? 280 : 25, isDefault: true, isActive: true });
  return (
    <section className="card stack">
      <h2>Zebra printers (wristbands and labels)</h2>
      {!list ? <p>Loading…</p> : list.length === 0 ? <p className="muted">No printers yet.</p> : (
        <table className="results compact"><tbody>{list.map((p) => <tr key={p.id}><td>{p.name}<div className="small muted">{p.kind}{p.isDefault ? ' · default' : ''}{p.isActive ? '' : ' · off'}</div></td><td>{p.host}:{p.port}</td><td>{p.dpi} dpi · {p.widthMm}×{p.lengthMm} mm</td>
          <td><button className="btn btn-link" onClick={() => setE(p)}>Edit</button></td></tr>)}</tbody></table>)}
      {!e && <div className="row"><button className="btn btn-light" onClick={() => setE(blank('Wristband'))}>Add wristband printer</button><button className="btn btn-light" onClick={() => setE(blank('Label'))}>Add label printer</button></div>}
      {e && <div className="card subtle stack"><div className="form-grid">
        <label>Name<input value={e.name} onChange={(x) => setE({ ...e, name: x.target.value })} placeholder="e.g. Admissions wristbands" /></label>
        <label>Type<select value={e.kind} onChange={(x) => setE({ ...e, kind: x.target.value })}><option>Wristband</option><option>Label</option></select></label>
        <label>IP address or name<input value={e.host} onChange={(x) => setE({ ...e, host: x.target.value })} placeholder="e.g. 192.168.1.60" /></label>
        <label>Port<input type="number" value={e.port} onChange={(x) => setE({ ...e, port: Number(x.target.value) })} /></label>
        <label>Resolution<select value={e.dpi} onChange={(x) => setE({ ...e, dpi: Number(x.target.value) })}>{[203, 300, 600].map((d) => <option key={d} value={d}>{d} dpi</option>)}</select></label>
        <label>Width (mm)<input type="number" value={e.widthMm} onChange={(x) => setE({ ...e, widthMm: Number(x.target.value) })} /></label>
        <label>Length (mm)<input type="number" value={e.lengthMm} onChange={(x) => setE({ ...e, lengthMm: Number(x.target.value) })} /></label>
        <label className="check"><input type="checkbox" checked={e.isDefault} onChange={(x) => setE({ ...e, isDefault: x.target.checked })} /> Default for this type</label>
        <label className="check"><input type="checkbox" checked={e.isActive} onChange={(x) => setE({ ...e, isActive: x.target.checked })} /> In use</label></div>
        <div className="row"><button className="btn btn-primary" onClick={save}>Save</button><button className="btn btn-light" onClick={() => setE(null)}>Cancel</button></div></div>}
      <p className="hint">BrightPAS prints straight to the printer over the network (raw port 9100). Wristbands: e.g. Zebra HC100 / ZD510-HC.</p>
      {error && <p className="error">{error}</p>}
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
