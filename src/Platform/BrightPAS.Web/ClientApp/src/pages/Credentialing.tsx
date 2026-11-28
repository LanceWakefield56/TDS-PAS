import { useEffect, useState } from 'react';
import { api, errText, formatDate, Perm, todayIso } from '../api';
import { useSession } from '../App';
import Modal from '../components/Modal';

const STATE: Record<string, string> = { Current: 'pill green', ExpiringSoon: 'pill', Expired: 'pill bad', Missing: 'pill bad', Inactive: 'pill' };
const LABEL: Record<string, string> = { Current: 'Current', ExpiringSoon: 'Due soon', Expired: 'Expired', Missing: 'Missing', Inactive: 'Inactive' };

/** Clinician credentials, scope of practice and expiry alerts. */
export default function Credentialing() {
  const { can } = useSession();
  const manage = can(Perm.CredentialingManage);
  const [status, setStatus] = useState<any[] | null>(null);
  const [settings, setSettings] = useState<any>(null);
  const [sel, setSel] = useState<any>(null);
  const [detail, setDetail] = useState<any>(null);
  const [procs, setProcs] = useState<any[]>([]);
  const [cred, setCred] = useState<any>(null);
  const [priv, setPriv] = useState<any>(null);
  const [error, setError] = useState<string | null>(null);
  const load = () => api<any[]>('/api/credentialing/status').then(setStatus).catch((e) => setError(errText(e)));
  useEffect(() => { load(); api('/api/credentialing/settings').then(setSettings).catch(() => undefined); api<any[]>('/api/organisation/procedures').then(setProcs).catch(() => undefined); }, []);
  const open = async (c: any) => { setSel(c); setCred(null); setPriv(null); setDetail(await api(`/api/credentialing/clinicians/${c.clinicianId}`)); };
  const save = async (path: string, body: any, done: () => void) => {
    setError(null);
    try { await api(path, { body }); done(); load(); if (sel) setDetail(await api(`/api/credentialing/clinicians/${sel.clinicianId}`)); } catch (x: any) { setError(x?.body?.errors?.join(' ') ?? errText(x)); }
  };
  const saveSettings = async () => { setError(null); try { await api('/api/credentialing/settings', { method: 'PUT', body: settings }); load(); } catch (x: any) { setError(x?.body?.errors?.join(' ') ?? errText(x)); } };
  const typeLabel = (id: string) => settings?.types.find((t: any) => t.id === id)?.label ?? id;
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Credentialing</h1><p className="muted">Bookings warn when a clinician's required credentials have expired or the procedure is outside their scope of practice.</p></div>
      {error && <p className="error" role="alert">{error}</p>}
      <section className="card">
        {!status ? <p>Loading…</p> : status.length === 0 ? <p className="muted">Add clinicians in Organisation set-up first.</p> : (
          <table className="results"><thead><tr><th>Clinician</th><th>Status</th><th>Needs attention</th><th>Next expiry</th></tr></thead>
            <tbody>{status.map((c) => <tr key={c.clinicianId} className={sel?.clinicianId === c.clinicianId ? 'selected' : ''} onClick={() => open(c)}>
              <td><button className="btn btn-link">{c.name}</button><div className="small muted">{c.type}</div></td><td><span className={STATE[c.state]}>{LABEL[c.state]}</span></td>
              <td className="small">{c.problems.join('; ') || '—'}</td><td>{c.nextExpiry ? formatDate(c.nextExpiry) : '—'}</td></tr>)}</tbody></table>)}
      </section>
      {sel && detail && (
        <section className="card stack">
          <h2>{sel.name}</h2>
          <h3>Credentials</h3>
          <table className="results compact"><tbody>{detail.credentials.map((c: any) => <tr key={c.id}><td>{typeLabel(c.type)}<div className="small muted">{c.reference} {c.issuer}</div></td>
            <td>{c.expiresOn ? `expires ${formatDate(c.expiresOn)}` : 'no expiry'}</td><td><span className={STATE[c.state]}>{LABEL[c.state]}</span></td><td className="small muted">checked by {c.verifiedBy}</td>
            <td>{manage && <button className="btn btn-link" onClick={() => setCred({ ...c, clinicianId: sel.clinicianId })}>Edit</button>}</td></tr>)}</tbody></table>
          {manage && !cred && <button className="btn btn-light" onClick={() => setCred({ clinicianId: sel.clinicianId, type: 'Apc', reference: '', issuer: '', issuedOn: '', expiresOn: '', notes: '', isActive: true })}>Add credential</button>}
          {cred && <Modal title={cred.id ? 'Edit credential' : 'Add credential'} onClose={() => setCred(null)}><div className="form-grid">
            <label>Type<select value={cred.type} onChange={(e) => setCred({ ...cred, type: e.target.value })}>{settings.types.map((t: any) => <option key={t.id} value={t.id}>{t.label}</option>)}</select></label>
            <label>Number / reference<input value={cred.reference ?? ''} onChange={(e) => setCred({ ...cred, reference: e.target.value })} /></label>
            <label>Issued by<input value={cred.issuer ?? ''} onChange={(e) => setCred({ ...cred, issuer: e.target.value })} placeholder="e.g. MCNZ, MPS" /></label>
            <label>Issued<input type="date" value={cred.issuedOn ?? ''} onChange={(e) => setCred({ ...cred, issuedOn: e.target.value })} /></label>
            <label>Expires<input type="date" value={cred.expiresOn ?? ''} onChange={(e) => setCred({ ...cred, expiresOn: e.target.value })} /></label>
            <label>Notes<input value={cred.notes ?? ''} onChange={(e) => setCred({ ...cred, notes: e.target.value })} /></label>
            <label className="check"><input type="checkbox" checked={cred.isActive} onChange={(e) => setCred({ ...cred, isActive: e.target.checked })} /> Active</label></div>
            <div className="row"><button className="btn btn-primary" onClick={() => save('/api/credentialing/credentials', { ...cred, issuedOn: cred.issuedOn || null, expiresOn: cred.expiresOn || null }, () => setCred(null))}>Save (I have seen the evidence)</button><button className="btn btn-light" onClick={() => setCred(null)}>Cancel</button></div></Modal>}
          <h3>Scope of practice at TDS</h3>
          {detail.privileges.length === 0 ? <p className="muted">None recorded.</p> : <ul className="plain">{detail.privileges.map((p: any) => <li key={p.id}>{p.service}: {p.procedureCode ? procs.find((x) => x.code === p.procedureCode)?.name ?? p.procedureCode : 'all procedures'} · approved {formatDate(p.approvedOn)}{p.reviewDue && ` · review ${formatDate(p.reviewDue)}`}{!p.isActive && ' (inactive)'} {p.conditions}
            {manage && <button className="btn btn-link" onClick={() => setPriv({ ...p, procedureCode: p.procedureCode ?? '', reviewDue: p.reviewDue ?? '', conditions: p.conditions ?? '' })}>Edit</button>}</li>)}</ul>}
          {manage && !priv && <button className="btn btn-light" onClick={() => setPriv({ clinicianId: sel.clinicianId, service: 'Surgical', procedureCode: '', approvedOn: todayIso(), reviewDue: '', conditions: '', isActive: true })}>Add scope</button>}
          {priv && <Modal title={priv.id ? 'Edit scope of practice' : 'Add scope of practice'} onClose={() => setPriv(null)}><div className="form-grid">
            <label>Service<select value={priv.service} onChange={(e) => setPriv({ ...priv, service: e.target.value })}>{settings.services.map((s: string) => <option key={s}>{s}</option>)}</select></label>
            <label>Procedure<select value={priv.procedureCode} onChange={(e) => setPriv({ ...priv, procedureCode: e.target.value })}><option value="">All procedures in the service</option>{procs.filter((p) => p.service === priv.service).map((p) => <option key={p.code} value={p.code}>{p.name}</option>)}</select></label>
            <label>Approved<input type="date" value={priv.approvedOn} onChange={(e) => setPriv({ ...priv, approvedOn: e.target.value })} /></label>
            <label>Review due<input type="date" value={priv.reviewDue} onChange={(e) => setPriv({ ...priv, reviewDue: e.target.value })} /></label>
            <label>Conditions<input value={priv.conditions} onChange={(e) => setPriv({ ...priv, conditions: e.target.value })} /></label>
            <label className="check"><input type="checkbox" checked={priv.isActive} onChange={(e) => setPriv({ ...priv, isActive: e.target.checked })} /> Active</label></div>
            <div className="row"><button className="btn btn-primary" onClick={() => save('/api/credentialing/privileges', { ...priv, procedureCode: priv.procedureCode || null, reviewDue: priv.reviewDue || null }, () => setPriv(null))}>Save</button><button className="btn btn-light" onClick={() => setPriv(null)}>Cancel</button></div></Modal>}
        </section>
      )}
      {manage && settings && (
        <section className="card stack"><h2>Settings</h2>
          <p>Every active clinician must hold:</p>
          <div className="chips">{settings.types.map((t: any) => <button key={t.id} className={`chip ${settings.requiredTypes.includes(t.id) ? 'on' : ''}`} onClick={() => setSettings({ ...settings, requiredTypes: settings.requiredTypes.includes(t.id) ? settings.requiredTypes.filter((x: string) => x !== t.id) : [...settings.requiredTypes, t.id] })}>{t.label}</button>)}</div>
          <div className="row"><label>Warn this many days before expiry<input type="number" className="narrow" value={settings.warningDays} onChange={(e) => setSettings({ ...settings, warningDays: Number(e.target.value) })} /></label>
            <label className="check"><input type="checkbox" checked={settings.checkPrivileges} onChange={(e) => setSettings({ ...settings, checkPrivileges: e.target.checked })} /> Check scope of practice when booking</label></div>
          <button className="btn btn-primary" onClick={saveSettings}>Save settings</button>
        </section>
      )}
    </div>
  );
}
