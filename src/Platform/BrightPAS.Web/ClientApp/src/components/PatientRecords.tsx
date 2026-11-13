import { useEffect, useState } from 'react';
import { Link } from 'react-router';
import { api, errText, fileToBase64, nzDateTime, PatientDetail, PatientSummary, Perm } from '../api';
import { useSession } from '../App';
import PatientPicker from './PatientPicker';

const CATEGORIES: [string, string][] = [['Referral', 'Referral'], ['Consent', 'Consent form'], ['Clinical', 'Clinical'], ['Correspondence', 'Correspondence'], ['Identity', 'Identity'],
  ['Insurance', 'Insurance / funding'], ['Results', 'Results'], ['Imaging', 'Imaging'], ['Other', 'Other']];
const errsOf = (x: any): string => x?.body?.errors?.join(' ') ?? errText(x);

/** Base-system panels on the patient record: documents & scanning, wristbands & labels, identity correction, merge, transfer pack, patient portal. */
export default function PatientRecords({ patient, onChanged }: { patient: PatientDetail; onChanged: () => void }) {
  const { can, mod } = useSession();
  return (
    <>
      <Documents patientId={patient.id} />
      <section className="card stack">
        <h2>Wristband, labels &amp; transfer</h2>
        <Labels patientId={patient.id} />
        <p><Link className="btn btn-warn" to={`/patients/${patient.id}/transfer`}>Emergency transfer pack</Link></p>
      </section>
      {mod('portal') && can(Perm.PortalManage) && <PortalLink patientId={patient.id} />}
      {can(Perm.PatientEdit) && <Identity patient={patient} onChanged={onChanged} />}
    </>
  );
}

const textLabel: Record<string, string> = { Pending: 'Reading text…', Done: 'Searchable', NoText: 'No text found', Failed: 'Text not read' };

function Documents({ patientId }: { patientId: string }) {
  const { can } = useSession();
  const [docs, setDocs] = useState<any[] | null>(null);
  const [cat, setCat] = useState('Referral');
  const [title, setTitle] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [del, setDel] = useState<{ id: string; reason: string } | null>(null);
  const [q, setQ] = useState('');
  const [hits, setHits] = useState<any[] | null>(null);
  const search = async (e: React.FormEvent) => {
    e.preventDefault(); setError(null);
    if (q.trim().length < 2) { setHits(null); return; }
    try { setHits(await api<any[]>(`/api/patients/${patientId}/documents/search?q=${encodeURIComponent(q.trim())}`)); } catch (x) { setError(errsOf(x)); }
  };
  const load = () => api<any[]>(`/api/patients/${patientId}/documents`).then(setDocs).catch(() => setDocs([]));
  useEffect(() => { load(); }, [patientId]); // eslint-disable-line react-hooks/exhaustive-deps
  const upload = async (f: File | undefined, source: 'Upload' | 'Scan') => {
    if (!f) return;
    setError(null); setBusy(true);
    try {
      await api(`/api/patients/${patientId}/documents`, { body: { patientId, bookingId: null, category: cat, title: title || null, fileName: f.name, contentBase64: await fileToBase64(f), source } });
      setTitle(''); await load();
    } catch (x) { setError(errsOf(x)); } finally { setBusy(false); }
  };
  const remove = async () => {
    if (!del) return;
    setError(null);
    try { await api(`/api/patients/documents/${del.id}/delete`, { body: { reason: del.reason } }); setDel(null); await load(); } catch (x) { setError(errsOf(x)); }
  };
  return (
    <section className="card stack">
      <h2>Documents &amp; scans</h2>
      {docs && docs.length > 0 && <form className="row" onSubmit={search} role="search">
        <label>Search inside documents<input value={q} onChange={(e) => { setQ(e.target.value); if (!e.target.value) setHits(null); }} placeholder="e.g. penicillin, consent, ECG" /></label>
        <button className="btn btn-light">Search</button>{hits && <button type="button" className="btn btn-link" onClick={() => { setQ(''); setHits(null); }}>Clear</button>}
      </form>}
      {hits && (hits.length === 0 ? <p className="muted">No documents contain “{q}”. Scans still being read are not searched yet.</p> : (
        <table className="results compact"><tbody>{hits.map((h) => (
          <tr key={h.id}><td><a href={`/api/patients/documents/${h.id}`} target="_blank" rel="noopener noreferrer">{h.title}</a><div className="small muted">{h.category} · {h.matches} match{h.matches === 1 ? '' : 'es'}</div><div className="small">…{h.snippet}…</div></td></tr>))}</tbody></table>))}
      {docs === null ? <p>Loading…</p> : docs.length === 0 ? <p className="muted">No documents attached.</p> : (
        <table className="results compact"><tbody>{docs.map((d) => (
          <tr key={d.id}><td><a href={`/api/patients/documents/${d.id}`} target="_blank" rel="noopener noreferrer">{d.title}</a><div className="small muted">{d.category} · {d.source} · {Math.round(d.sizeBytes / 1024)} KB · <span className={`badge ${d.textStatus === 'Done' ? 'ok' : d.textStatus === 'Failed' ? 'warn' : ''}`}>{textLabel[d.textStatus] ?? d.textStatus}</span></div><div className="small muted">{nzDateTime(d.uploadedAt)} · {d.uploadedBy}</div></td>
            <td>{can(Perm.PatientEdit) && <button className="btn btn-link" onClick={() => setDel({ id: d.id, reason: '' })}>Remove</button>}</td></tr>))}</tbody></table>
      )}
      {del && <div className="row"><label>Reason for removing<input value={del.reason} onChange={(e) => setDel({ ...del, reason: e.target.value })} placeholder="e.g. scanned to the wrong patient" autoFocus /></label>
        <button className="btn btn-warn" onClick={remove}>Remove</button><button className="btn btn-light" onClick={() => setDel(null)}>Cancel</button></div>}
      {can(Perm.PatientEdit) && (
        <div className="stack">
          <div className="row">
            <label>Type<select value={cat} onChange={(e) => setCat(e.target.value)}>{CATEGORIES.map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
            <label>Title (optional)<input value={title} onChange={(e) => setTitle(e.target.value)} placeholder="e.g. Signed consent – lap chole" /></label>
          </div>
          <div className="row">
            <label className="btn btn-light file-btn">Attach a file (PDF, JPEG, PNG, TIFF)<input type="file" className="sr-only" accept="application/pdf,image/jpeg,image/png,image/tiff" disabled={busy} onChange={(e) => { upload(e.target.files?.[0], 'Upload'); e.target.value = ''; }} /></label>
            <label className="btn btn-light file-btn">Scan with camera / scanner<input type="file" className="sr-only" accept="image/*,application/pdf" capture="environment" disabled={busy} onChange={(e) => { upload(e.target.files?.[0], 'Scan'); e.target.value = ''; }} /></label>
          </div>
          <p className="hint">Files are encrypted when stored and every opening is recorded. Scanners: save as PDF to a folder, then attach. Phones/tablets: "Scan" opens the camera.</p>
        </div>
      )}
      {busy && <p>Uploading…</p>}
      {error && <p className="error" role="alert">{error}</p>}
    </section>
  );
}

function Labels({ patientId }: { patientId: string }) {
  const [printers, setPrinters] = useState<any[]>([]);
  const [copies, setCopies] = useState(4);
  const [msg, setMsg] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => { api<any[]>('/api/patients/printers').then(setPrinters).catch(() => undefined); }, []);
  const print = async (kind: 'Wristband' | 'Label', n: number) => {
    setError(null); setMsg(null);
    try { await api(`/api/patients/${patientId}/print`, { body: { kind, copies: n, printerId: null } }); setMsg(kind === 'Wristband' ? 'Wristband sent to the printer.' : `${n} label(s) sent to the printer.`); }
    catch (x) { setError(errsOf(x)); }
  };
  const has = (k: string) => printers.some((p) => p.kind === k && p.isDefault);
  const dymo = printers.find((p) => p.kind === 'Label' && p.isDefault && p.language === 'Dymo');
  return (
    <div className="stack">
      <div className="row">
        <button className="btn btn-light" disabled={!has('Wristband')} onClick={() => print('Wristband', 1)}>Print wristband</button>
        <label>Labels<input type="number" min={1} max={50} value={copies} onChange={(e) => setCopies(Number(e.target.value))} className="narrow" /></label>
        {dymo
          ? <Link className="btn btn-light" to={`/patients/${patientId}/labels?copies=${copies}&printer=${dymo.id}`}>Print labels (DYMO)</Link>
          : <button className="btn btn-light" disabled={!has('Label')} onClick={() => print('Label', copies)}>Print labels</button>}
      </div>
      {printers.length === 0 && <p className="hint">No label or wristband printers are set up yet (Administration → Security &amp; printers).</p>}
      {msg && <p className="ok-text">{msg}</p>}
      {error && <p className="error" role="alert">{error}</p>}
    </div>
  );
}

function PortalLink({ patientId }: { patientId: string }) {
  const [status, setStatus] = useState<any>(undefined);
  const [link, setLink] = useState<string | null>(null);
  const [sms, setSms] = useState(true);
  const [email, setEmail] = useState(false);
  const [sent, setSent] = useState<any[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const load = () => api(`/api/portal/patients/${patientId}/link`).then(setStatus).catch(() => setStatus(null));
  useEffect(() => { load(); }, [patientId]); // eslint-disable-line react-hooks/exhaustive-deps
  const create = async () => {
    setError(null);
    try {
      const r = await api(`/api/portal/patients/${patientId}/link`, { body: { sendSms: sms, sendEmail: email } });
      setSent(r.sent ?? null); setLink(r.sent?.length ? null : window.location.origin + r.path); await load();
    } catch (x) { setError(errsOf(x)); }
  };
  const revoke = async () => { setError(null); try { await api(`/api/portal/patients/${patientId}/revoke`, { body: {} }); setLink(null); await load(); } catch (x) { setError(errsOf(x)); } };
  return (
    <section className="card stack">
      <h2>Patient portal</h2>
      {status === undefined ? <p>Loading…</p> : status?.active
        ? <p>Link active until {nzDateTime(status.expiresAt)}{status.lastUsedAt ? `, last used ${nzDateTime(status.lastUsedAt)}` : ', not used yet'}.{status.locked && <strong> Locked after wrong dates of birth.</strong>}</p>
        : <p className="muted">No active link. The patient opens the link and confirms their date of birth to see appointments, send requests and upload documents.</p>}
      {sent && sent.length > 0 && <p className="ok-text">New link queued to the patient: {sent.map((m: any) => `${m.channel} (${m.status}${m.lastError ? ` – ${m.lastError}` : ''})`).join(', ')}. Delivery shows under Reminders.</p>}
      {link && <div className="copy-box"><p><strong>Copy this link to the patient now</strong> (by SMS or email) – it is shown only once:</p><code className="mono-box">{link}</code></div>}
      <div className="row"><label className="check"><input type="checkbox" checked={sms} onChange={(e) => setSms(e.target.checked)} /> Send by SMS</label><label className="check"><input type="checkbox" checked={email} onChange={(e) => setEmail(e.target.checked)} /> Send by email</label></div>
      <p className="hint">Tick neither to show the link here once so you can copy it. Sending needs the Reminders module and a mobile number or email on the patient.</p>
      <div className="row"><button className="btn btn-light" onClick={create}>{status?.active ? 'Send a new link' : 'Create link'}</button>{status?.active && <button className="btn btn-link" onClick={revoke}>Switch off link</button>}</div>
      {error && <p className="error" role="alert">{error}</p>}
    </section>
  );
}

function Identity({ patient, onChanged }: { patient: PatientDetail; onChanged: () => void }) {
  const [mode, setMode] = useState<'none' | 'correct' | 'merge'>('none');
  const [f, setF] = useState({ familyName: '', givenNames: '', dateOfBirth: '', sex: '', nhi: '', clearNhi: false, reason: '', evidence: '' });
  const [other, setOther] = useState<PatientSummary | null>(null);
  const [keep, setKeep] = useState<'this' | 'other'>('this');
  const [reason, setReason] = useState('');
  const [pending, setPending] = useState<any[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  useEffect(() => { api<any[]>(`/api/patients/${patient.id}/identity-corrections`).then(setPending).catch(() => undefined); }, [patient.id, msg]);
  const correct = async (e: React.FormEvent) => {
    e.preventDefault(); setError(null);
    try {
      await api(`/api/patients/${patient.id}/identity-corrections`, { body: { patientId: patient.id, familyName: f.familyName || null, givenNames: f.givenNames || null,
        dateOfBirth: f.dateOfBirth || null, sex: f.sex || null, nhi: f.nhi || null, clearNhi: f.clearNhi, reason: f.reason, evidence: f.evidence || null } });
      setMode('none'); setMsg('Correction sent for approval. A second person (Privacy Officer) must approve it before the record changes.'); onChanged();
    } catch (x) { setError(errsOf(x)); }
  };
  const merge = async (e: React.FormEvent) => {
    e.preventDefault(); setError(null);
    if (!other) { setError('Find the other record first.'); return; }
    const [survivorId, duplicateId] = keep === 'this' ? [patient.id, other.id] : [other.id, patient.id];
    try { await api('/api/patients/merges', { body: { survivorId, duplicateId, reason } }); setMode('none'); setMsg('Merge sent for approval by a second person.'); }
    catch (x) { setError(errsOf(x)); }
  };
  const open = pending.filter((p) => p.status === 'Pending');
  return (
    <section className="card stack">
      <h2>Identity</h2>
      {open.length > 0 && <p className="notice">Correction waiting for approval (requested by {open[0].requestedBy}).</p>}
      {msg && <p className="ok-text">{msg}</p>}
      {mode === 'none' && <div className="row"><button className="btn btn-light" onClick={() => setMode('correct')}>Correct name, date of birth or NHI</button><button className="btn btn-light" onClick={() => setMode('merge')}>Merge duplicate record</button></div>}
      {mode === 'correct' && (
        <form className="stack" onSubmit={correct}>
          <p className="hint">Only fill in what is wrong. Changes need a second person's approval.</p>
          <div className="form-grid">
            <label>Family name<input value={f.familyName} placeholder={patient.familyName} onChange={(e) => setF({ ...f, familyName: e.target.value })} /></label>
            <label>Given names<input value={f.givenNames} placeholder={patient.givenNames} onChange={(e) => setF({ ...f, givenNames: e.target.value })} /></label>
            <label>Date of birth<input type="date" value={f.dateOfBirth} onChange={(e) => setF({ ...f, dateOfBirth: e.target.value })} /></label>
            <label>Sex<select value={f.sex} onChange={(e) => setF({ ...f, sex: e.target.value })}><option value="">(no change)</option>{['Female', 'Male', 'Indeterminate', 'Unknown'].map((x) => <option key={x}>{x}</option>)}</select></label>
            <label>NHI<input value={f.nhi} placeholder={patient.nhi ?? ''} onChange={(e) => setF({ ...f, nhi: e.target.value.toUpperCase() })} maxLength={7} /></label>
            <label className="check"><input type="checkbox" checked={f.clearNhi} onChange={(e) => setF({ ...f, clearNhi: e.target.checked })} /> Remove the NHI (it was wrong)</label>
            <label>Reason<input value={f.reason} required onChange={(e) => setF({ ...f, reason: e.target.value })} placeholder="e.g. Name changed on marriage" /></label>
            <label>Evidence seen<input value={f.evidence} onChange={(e) => setF({ ...f, evidence: e.target.value })} placeholder="e.g. Passport, NHI service" /></label>
          </div>
          {error && <p className="error" role="alert">{error}</p>}
          <div className="row"><button className="btn btn-primary">Send for approval</button><button type="button" className="btn btn-light" onClick={() => setMode('none')}>Cancel</button></div>
        </form>
      )}
      {mode === 'merge' && (
        <form className="stack" onSubmit={merge}>
          <PatientPicker value={other} onChange={setOther} label="The other record for the same person" />
          {other && other.id === patient.id && <p className="error">Choose a different record.</p>}
          {other && <div className="row">
            <label className="check"><input type="radio" checked={keep === 'this'} onChange={() => setKeep('this')} /> Keep this record ({patient.mrn})</label>
            <label className="check"><input type="radio" checked={keep === 'other'} onChange={() => setKeep('other')} /> Keep the other record ({other.mrn})</label>
          </div>}
          <label>How did you confirm they are the same person?<input value={reason} required onChange={(e) => setReason(e.target.value)} /></label>
          <p className="hint">Everything recorded against the duplicate (bookings, letters, results, invoices, documents, alerts) moves to the record you keep, after a second person approves.</p>
          {error && <p className="error" role="alert">{error}</p>}
          <div className="row"><button className="btn btn-primary">Send for approval</button><button type="button" className="btn btn-light" onClick={() => setMode('none')}>Cancel</button></div>
        </form>
      )}
    </section>
  );
}
