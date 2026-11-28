import { useEffect, useState } from 'react';
import { Link, useSearchParams } from 'react-router';
import { api, errText, nzDateTime, Perm } from '../api';
import { useSession } from '../App';
import Modal from '../components/Modal';

interface Tpl { code: string; name: string; kind: string; recipient: string; subject: string; body: string; isActive: boolean; version: number; updatedAt: string | null; updatedBy: string | null }
interface Practice { id: string; name: string; hpiFacilityId: string | null; healthLinkEdi: string | null; addressLine1: string | null; addressLine2: string | null; city: string | null; postcode: string | null; phone: string | null; email: string | null; preferredChannel: string; isActive: boolean }
const KINDS: [string, string][] = [['Appointment', 'Appointment'], ['Admission', 'Admission instructions'], ['GpNotification', 'GP notification'], ['ReferralAcknowledgement', 'Referral received'], ['ReferralDeclined', 'Referral declined'], ['Custom', 'Other']];
const RECIPIENTS: [string, string][] = [['Patient', 'Patient'], ['Gp', 'GP practice'], ['Referrer', 'Referrer'], ['Other', 'Other (typed in)']];
const errsOf = (x: any) => x?.body?.errors ?? [errText(x)];
function Errs({ list }: { list: string[] }) { return list.length ? <div className="error" role="alert">{list.map((e) => <div key={e}>{e}</div>)}</div> : null; }

/** Letter templates, GP practices and the HealthLink connection. */
export default function LetterAdmin() {
  const [params, setParams] = useSearchParams();
  const tab = params.get('tab') ?? 'templates';
  const { can } = useSession();
  return (
    <div className="stack-lg">
      <div className="page-head">{can(Perm.LettersView) && <p className="small"><Link to="/letters">← Letters</Link></p>}<h1>Letter templates &amp; GP practices</h1></div>
      <section className="card">
        <div className="tabs">{[['templates', 'Templates'], ['practices', 'GP practices'], ['healthlink', 'HealthLink']].map(([k, t]) =>
          <button key={k} className={tab === k ? 'on' : ''} onClick={() => setParams({ tab: k })}>{t}</button>)}</div>
        {tab === 'templates' && <Templates />}
        {tab === 'practices' && <Practices />}
        {tab === 'healthlink' && <HealthLink />}
      </section>
    </div>
  );
}

function Templates() {
  const [data, setData] = useState<{ templates: Tpl[]; placeholders: Record<string, string> } | null>(null);
  const [edit, setEdit] = useState<Partial<Tpl> | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const load = () => api<any>('/api/letters/admin/templates').then(setData).catch((e) => setErrors(errsOf(e)));
  useEffect(() => { load(); }, []);
  if (!data) return errors.length ? <Errs list={errors} /> : <p>Loading…</p>;
  const save = async () => { setErrors([]); try { await api('/api/letters/templates', { method: 'PUT', body: edit }); setEdit(null); load(); } catch (x) { setErrors(errsOf(x)); } };
  const insert = (k: string) => setEdit({ ...edit, body: (edit?.body ?? '') + `{${k}}` });
  if (edit) return (
    <div className="stack">
      <h3>{edit.version ? `Edit: ${edit.name}` : 'New template'}</h3>
      <div className="form-grid">
        <label>Code<input value={edit.code ?? ''} disabled={!!edit.version} onChange={(e) => setEdit({ ...edit, code: e.target.value })} placeholder="e.g. POSTOP" /></label>
        <label>Name<input value={edit.name ?? ''} onChange={(e) => setEdit({ ...edit, name: e.target.value })} /></label>
        <label>Type<select value={edit.kind} onChange={(e) => setEdit({ ...edit, kind: e.target.value })}>{KINDS.map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
        <label>Goes to<select value={edit.recipient} onChange={(e) => setEdit({ ...edit, recipient: e.target.value })}>{RECIPIENTS.map(([k, t]) => <option key={k} value={k}>{t}</option>)}</select></label>
        <label className="check"><input type="checkbox" checked={!!edit.isActive} onChange={(e) => setEdit({ ...edit, isActive: e.target.checked })} /> Active</label>
      </div>
      <label>Subject<input value={edit.subject ?? ''} onChange={(e) => setEdit({ ...edit, subject: e.target.value })} /></label>
      <label>Letter text<textarea className="letter-edit" value={edit.body ?? ''} onChange={(e) => setEdit({ ...edit, body: e.target.value })} /></label>
      <details><summary>Placeholders (click to add to the end of the text)</summary>
        <div className="chips mt">{Object.entries(data.placeholders).map(([k, v]) => <button type="button" key={k} className="chip" title={v} onClick={() => insert(k)}>{`{${k}}`}</button>)}</div></details>
      <Errs list={errors} />
      <div className="row"><button className="btn btn-primary" onClick={save}>Save template</button><button className="btn btn-light" onClick={() => { setEdit(null); setErrors([]); }}>Cancel</button></div>
    </div>
  );
  return (
    <div className="stack">
      <div className="row-between"><p className="hint">Placeholders such as {'{BookingDate}'} are filled in from the patient, booking or referral when a letter is written.</p>
        <button className="btn btn-primary" onClick={() => setEdit({ kind: 'Custom', recipient: 'Patient', isActive: true, subject: '', body: 'Dear {PatientTitleName}\n\n\n\nYours sincerely\n\n{Author}\n{ClinicName}' })}>New template</button></div>
      <table className="results compact">
        <thead><tr><th>Template</th><th>Goes to</th><th>Version</th><th>Status</th><th></th></tr></thead>
        <tbody>{data.templates.map((t) => (
          <tr key={t.code} className={t.isActive ? '' : 'muted'}><td data-label="Template"><strong>{t.name}</strong><div className="small muted">{t.code} · {t.subject}</div></td>
            <td data-label="Goes to">{RECIPIENTS.find(([k]) => k === t.recipient)?.[1]}</td>
            <td data-label="Version">v{t.version}{t.updatedAt && <div className="small muted">{nzDateTime(t.updatedAt)} {t.updatedBy}</div>}</td>
            <td data-label="Status"><span className={`pill ${t.isActive ? 'green' : ''}`}>{t.isActive ? 'Active' : 'Off'}</span></td>
            <td><button className="btn btn-link" onClick={() => setEdit(t)}>Edit</button></td></tr>
        ))}</tbody>
      </table>
    </div>
  );
}

function Practices() {
  const [rows, setRows] = useState<Practice[] | null>(null);
  const [edit, setEdit] = useState<Partial<Practice> | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const load = () => api<Practice[]>('/api/letters/admin/practices').then(setRows).catch((e) => setErrors(errsOf(e)));
  useEffect(() => { load(); }, []);
  const save = async () => { setErrors([]); try { await api('/api/letters/practices', { body: edit }); setEdit(null); load(); } catch (x) { setErrors(errsOf(x)); } };
  const set = (k: keyof Practice) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => setEdit({ ...edit, [k]: e.target.value });
  return (
    <div className="stack">
      <div className="row-between"><p className="hint">A patient is linked to their practice by the practice's HPI facility number (on the patient record).</p>
        {!edit && <button className="btn btn-primary" onClick={() => setEdit({ preferredChannel: 'HealthLink', isActive: true })}>Add practice</button>}</div>
      {edit && (
        <Modal title={edit.id ? `Edit ${edit.name}` : 'New practice'} onClose={() => { setEdit(null); setErrors([]); }}>
          <div className="form-grid">
            <label>Practice name<input value={edit.name ?? ''} onChange={set('name')} /></label>
            <label>HPI facility number<input value={edit.hpiFacilityId ?? ''} onChange={set('hpiFacilityId')} placeholder="F12345-A" /></label>
            <label>HealthLink EDI account<input value={edit.healthLinkEdi ?? ''} onChange={set('healthLinkEdi')} placeholder="e.g. stokemed" /></label>
            <label>Send letters by<select value={edit.preferredChannel} onChange={set('preferredChannel')}><option value="HealthLink">HealthLink</option><option value="Post">Post</option><option value="Email">Email</option></select></label>
            <label>Street address<input value={edit.addressLine1 ?? ''} onChange={set('addressLine1')} /></label>
            <label>Address line 2<input value={edit.addressLine2 ?? ''} onChange={set('addressLine2')} /></label>
            <label>Town / city<input value={edit.city ?? ''} onChange={set('city')} /></label>
            <label>Postcode<input value={edit.postcode ?? ''} onChange={set('postcode')} /></label>
            <label>Phone<input value={edit.phone ?? ''} onChange={set('phone')} /></label>
            <label>Email<input value={edit.email ?? ''} onChange={set('email')} /></label>
            <label className="check"><input type="checkbox" checked={!!edit.isActive} onChange={(e) => setEdit({ ...edit, isActive: e.target.checked })} /> Active</label>
          </div>
          <Errs list={errors} />
          <div className="row"><button className="btn btn-primary" onClick={save}>Save</button><button className="btn btn-light" onClick={() => { setEdit(null); setErrors([]); }}>Cancel</button></div>
        </Modal>
      )}
      {rows?.length === 0 && <p className="muted">No practices yet.</p>}
      {rows && rows.length > 0 && (
        <table className="results compact">
          <thead><tr><th>Practice</th><th>HPI</th><th>HealthLink</th><th>Town</th><th></th></tr></thead>
          <tbody>{rows.map((p) => (
            <tr key={p.id} className={p.isActive ? '' : 'muted'}><td data-label="Practice"><strong>{p.name}</strong></td><td data-label="HPI">{p.hpiFacilityId ?? '—'}</td>
              <td data-label="HealthLink">{p.healthLinkEdi ?? '—'}<div className="small muted">{p.preferredChannel}</div></td><td data-label="Town">{p.city ?? ''}</td>
              <td><button className="btn btn-link" onClick={() => setEdit(p)}>Edit</button></td></tr>
          ))}</tbody>
        </table>
      )}
    </div>
  );
}

function HealthLink() {
  const [f, setF] = useState<any | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const [saved, setSaved] = useState(false);
  useEffect(() => { api('/api/letters/healthlink').then(setF).catch((e) => setErrors(errsOf(e))); }, []);
  if (!f) return errors.length ? <Errs list={errors} /> : <p>Loading…</p>;
  const save = async () => { setErrors([]); setSaved(false); try { await api('/api/letters/healthlink', { method: 'PUT', body: f }); setSaved(true); } catch (x) { setErrors(errsOf(x)); } };
  return (
    <div className="stack">
      <p className="hint">HealthLink delivers letters to GP practice systems. Its messaging client runs on the server and sends any file BrightPAS puts in the outbox folder, then puts acknowledgements in the inbox folder.</p>
      <div className="form-grid">
        <label>Mode<select value={f.mode} onChange={(e) => setF({ ...f, mode: e.target.value })}>
          <option value="TestOutbox">Test mode – record only, nothing sent</option><option value="FolderDrop">Folder – HealthLink client on this server</option><option value="Off">Off – print letters to GPs</option></select></label>
        <label>TDS HealthLink EDI account<input value={f.senderEdi ?? ''} onChange={(e) => setF({ ...f, senderEdi: e.target.value })} /></label>
        <label>Outbox folder (on the server)<input value={f.outboxFolder ?? ''} onChange={(e) => setF({ ...f, outboxFolder: e.target.value })} placeholder="C:\HealthLink\Out" /></label>
        <label>Inbox folder for acknowledgements<input value={f.inboxFolder ?? ''} onChange={(e) => setF({ ...f, inboxFolder: e.target.value })} placeholder="C:\HealthLink\In" /></label>
      </div>
      {f.mode === 'FolderDrop' && <p className="warn-box">The BrightPAS website account (IIS APPPOOL\BrightPAS) needs write access to the outbox folder and modify access to the inbox folder. Confirm the message profile with HealthLink before sending real letters.</p>}
      {f.updatedAt && <p className="hint">Last changed {nzDateTime(f.updatedAt)} by {f.updatedBy}.</p>}
      <Errs list={errors} />
      {saved && <p className="notice" role="status">Saved.</p>}
      <div><button className="btn btn-primary" onClick={save}>Save</button></div>
    </div>
  );
}
