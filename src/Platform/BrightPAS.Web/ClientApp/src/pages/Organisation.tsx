import { useEffect, useState } from 'react';
import { useSearchParams } from 'react-router';
import { api, errText } from '../api';
import Modal from '../components/Modal';

interface Loc { id: string; facilityId: string; code: string; name: string; type: string; isActive: boolean; sortOrder: number }
interface Org {
  organisationName: string; hpiOrganisationId: string | null; siteName: string; hpiFacilityId: string | null; addressLine1: string | null; addressLine2: string | null;
  suburb: string | null; city: string | null; postcode: string | null; phone: string | null; email: string | null;
  facilities: { id: string; name: string; type: string; locations: Loc[] }[]; locationTypes: string[];
}
interface Clin { id: string; name: string; type: string; specialty: string | null; hpiCpn: string | null; phone: string | null; email: string | null; practiceName: string | null; userName: string | null; isActive: boolean }
interface Proc { id: string; code: string; name: string; service: string; defaultMinutes: number; defaultAnaesthetic: string; sideRequired: boolean; feeCode: string | null; isActive: boolean }
const typeLabel: Record<string, string> = { Theatre: 'Theatre', ProcedureRoom: 'Procedure room', PacuBay: 'Recovery (PACU)', Stage2Recovery: 'Stage 2 recovery', ConsultRoom: 'Consult room', CssdZone: 'CSSD', Office: 'Office' };
const CTYPES = ['Surgeon', 'Anaesthetist', 'Endoscopist', 'Physician', 'Other'];
const ANAES = ['General', 'Sedation', 'Regional', 'Local', 'None'];

function Errs({ list }: { list: string[] }) { return list.length ? <div className="error" role="alert">{list.map((e) => <div key={e}>{e}</div>)}</div> : null; }
const errsOf = (x: any) => x?.body?.errors ?? [errText(x)];

/** Organisation set-up: clinic details (letterhead), theatres and rooms, clinician register, procedure catalogue. */
export default function Organisation() {
  const [params, setParams] = useSearchParams();
  const tab = params.get('tab') ?? 'details';
  const tabs: [string, string][] = [['details', 'Clinic details'], ['rooms', 'Theatres & rooms'], ['clinicians', 'Clinicians'], ['procedures', 'Procedures']];
  return (
    <div className="stack-lg">
      <div className="page-head"><h1>Organisation set-up</h1><p className="muted">The details BrightPAS uses everywhere: letterhead, rooms you can book, the clinicians and procedures you pick from.</p></div>
      <section className="card">
        <div className="tabs" role="tablist">{tabs.map(([k, t]) => <button key={k} role="tab" aria-selected={tab === k} className={tab === k ? 'on' : ''} onClick={() => setParams({ tab: k })}>{t}</button>)}</div>
        {tab === 'details' && <Details />}
        {tab === 'rooms' && <Rooms />}
        {tab === 'clinicians' && <Clinicians />}
        {tab === 'procedures' && <Procedures />}
      </section>
    </div>
  );
}

function Details() {
  const [f, setF] = useState<Org | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const [saved, setSaved] = useState(false);
  useEffect(() => { api<Org>('/api/admin/organisation').then(setF).catch((e) => setErrors(errsOf(e))); }, []);
  if (!f) return errors.length ? <Errs list={errors} /> : <p>Loading…</p>;
  const set = (k: keyof Org) => (e: React.ChangeEvent<HTMLInputElement>) => { setSaved(false); setF({ ...f, [k]: e.target.value }); };
  const save = async (e: React.FormEvent) => {
    e.preventDefault(); setErrors([]); setSaved(false);
    try { await api('/api/admin/organisation', { method: 'PUT', body: f }); setSaved(true); } catch (x) { setErrors(errsOf(x)); }
  };
  const v = (x: string | null) => x ?? '';
  return (
    <form className="stack" onSubmit={save}>
      <p className="hint">Printed on letters and used in messages to patients.</p>
      <div className="form-grid">
        <label>Organisation name<input value={f.organisationName} onChange={set('organisationName')} required /></label>
        <label>HPI organisation number<input value={v(f.hpiOrganisationId)} onChange={set('hpiOrganisationId')} placeholder="G12345-A" /></label>
        <label>Site name<input value={f.siteName} onChange={set('siteName')} required /></label>
        <label>HPI facility number<input value={v(f.hpiFacilityId)} onChange={set('hpiFacilityId')} placeholder="F12345-A" /></label>
        <label>Street address<input value={v(f.addressLine1)} onChange={set('addressLine1')} /></label>
        <label>Address line 2<input value={v(f.addressLine2)} onChange={set('addressLine2')} /></label>
        <label>Suburb<input value={v(f.suburb)} onChange={set('suburb')} /></label>
        <label>Town / city<input value={v(f.city)} onChange={set('city')} /></label>
        <label>Postcode<input value={v(f.postcode)} onChange={set('postcode')} inputMode="numeric" /></label>
        <label>Phone<input value={v(f.phone)} onChange={set('phone')} /></label>
        <label>Email<input type="email" value={v(f.email)} onChange={set('email')} /></label>
      </div>
      <Errs list={errors} />
      {saved && <p className="notice" role="status">Saved.</p>}
      <div><button className="btn btn-primary">Save clinic details</button></div>
    </form>
  );
}

function Rooms() {
  const [org, setOrg] = useState<Org | null>(null);
  const [edit, setEdit] = useState<Partial<Loc> | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const load = () => api<Org>('/api/admin/organisation').then(setOrg).catch((e) => setErrors(errsOf(e)));
  useEffect(() => { load(); }, []);
  if (!org) return errors.length ? <Errs list={errors} /> : <p>Loading…</p>;
  const save = async () => {
    setErrors([]);
    try { await api('/api/admin/organisation/locations', { body: { ...edit, sortOrder: Number(edit!.sortOrder ?? 0) } }); setEdit(null); load(); } catch (x) { setErrors(errsOf(x)); }
  };
  return (
    <div className="stack">
      <p className="hint">Only <strong>active theatres and procedure rooms</strong> can have theatre lists. Switch Theatre 2 on here when it opens.</p>
      {org.facilities.map((f) => (
        <div key={f.id} className="stack-sm">
          <h3>{f.name}</h3>
          <table className="results compact">
            <thead><tr><th>Code</th><th>Name</th><th>Type</th><th>Status</th><th></th></tr></thead>
            <tbody>{f.locations.map((l) => (
              <tr key={l.id}><td data-label="Code">{l.code}</td><td data-label="Name"><strong>{l.name}</strong></td><td data-label="Type">{typeLabel[l.type] ?? l.type}</td>
                <td data-label="Status"><span className={`pill ${l.isActive ? 'green' : ''}`}>{l.isActive ? 'Active' : 'Not in use'}</span></td>
                <td><button className="btn btn-link" onClick={() => setEdit(l)}>Edit</button></td></tr>
            ))}</tbody>
          </table>
          <div><button className="btn btn-light btn-small" onClick={() => setEdit({ facilityId: f.id, code: '', name: '', type: 'ProcedureRoom', isActive: true, sortOrder: 100 })}>Add a room to {f.name}</button></div>
        </div>
      ))}
      {edit && (
        <Modal title={<>{edit.id ? `Edit ${edit.name}` : 'New room'}</>} onClose={() => { setEdit(null); setErrors([]); }}>
          <div className="form-grid">
            <label>Code<input value={edit.code ?? ''} onChange={(e) => setEdit({ ...edit, code: e.target.value })} placeholder="e.g. TH2" /></label>
            <label>Name<input value={edit.name ?? ''} onChange={(e) => setEdit({ ...edit, name: e.target.value })} /></label>
            <label>Type<select value={edit.type} onChange={(e) => setEdit({ ...edit, type: e.target.value })}>{org.locationTypes.map((t) => <option key={t} value={t}>{typeLabel[t] ?? t}</option>)}</select></label>
            <label>Area<select value={edit.facilityId} onChange={(e) => setEdit({ ...edit, facilityId: e.target.value })}>{org.facilities.map((f) => <option key={f.id} value={f.id}>{f.name}</option>)}</select></label>
            <label>Order in lists<input type="number" value={edit.sortOrder ?? 0} onChange={(e) => setEdit({ ...edit, sortOrder: Number(e.target.value) })} /></label>
            <label className="check"><input type="checkbox" checked={!!edit.isActive} onChange={(e) => setEdit({ ...edit, isActive: e.target.checked })} /> Active (in use)</label>
          </div>
          <Errs list={errors} />
          <div className="row"><button className="btn btn-primary" onClick={save}>Save</button><button className="btn btn-light" onClick={() => { setEdit(null); setErrors([]); }}>Cancel</button></div>
        </Modal>
      )}
    </div>
  );
}

function Clinicians() {
  const [rows, setRows] = useState<Clin[] | null>(null);
  const [edit, setEdit] = useState<Partial<Clin> | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const load = () => api<Clin[]>('/api/admin/organisation/clinicians').then(setRows).catch((e) => setErrors(errsOf(e)));
  useEffect(() => { load(); }, []);
  const save = async () => {
    setErrors([]);
    try { await api('/api/admin/organisation/clinicians', { body: edit }); setEdit(null); load(); } catch (x) { setErrors(errsOf(x)); }
  };
  const set = (k: keyof Clin) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => setEdit({ ...edit, [k]: e.target.value });
  return (
    <div className="stack">
      <div className="row-between"><p className="hint">Surgeons, endoscopists and anaesthetists who work at TDS. They are suggested on theatre lists, referrals and the waitlist.</p>
        {!edit && <button className="btn btn-primary" onClick={() => setEdit({ type: 'Surgeon', isActive: true })}>Add clinician</button>}</div>
      {edit && (
        <Modal title={<>{edit.id ? `Edit ${edit.name}` : 'New clinician'}</>} onClose={() => { setEdit(null); setErrors([]); }}>
          <div className="form-grid">
            <label>Name (as on lists and letters)<input value={edit.name ?? ''} onChange={set('name')} placeholder="Dr Ben Hall" /></label>
            <label>Role<select value={edit.type} onChange={set('type')}>{CTYPES.map((t) => <option key={t}>{t}</option>)}</select></label>
            <label>Specialty<input value={edit.specialty ?? ''} onChange={set('specialty')} placeholder="General surgery" /></label>
            <label>HPI CPN<input value={edit.hpiCpn ?? ''} onChange={set('hpiCpn')} placeholder="12ABCD" /></label>
            <label>Phone<input value={edit.phone ?? ''} onChange={set('phone')} /></label>
            <label>Email<input value={edit.email ?? ''} onChange={set('email')} /></label>
            <label>Their own practice<input value={edit.practiceName ?? ''} onChange={set('practiceName')} /></label>
            <label>BrightPAS username (optional)<input value={edit.userName ?? ''} onChange={set('userName')} /></label>
            <label className="check"><input type="checkbox" checked={!!edit.isActive} onChange={(e) => setEdit({ ...edit, isActive: e.target.checked })} /> Active</label>
          </div>
          <Errs list={errors} />
          <div className="row"><button className="btn btn-primary" onClick={save}>Save</button><button className="btn btn-light" onClick={() => { setEdit(null); setErrors([]); }}>Cancel</button></div>
        </Modal>
      )}
      {rows?.length === 0 && <p className="muted">No clinicians yet.</p>}
      {rows && rows.length > 0 && (
        <table className="results compact">
          <thead><tr><th>Name</th><th>Role</th><th>HPI CPN</th><th>Practice</th><th>Status</th><th></th></tr></thead>
          <tbody>{rows.map((c) => (
            <tr key={c.id} className={c.isActive ? '' : 'muted'}><td data-label="Name"><strong>{c.name}</strong><div className="small muted">{c.specialty}</div></td><td data-label="Role">{c.type}</td>
              <td data-label="HPI CPN">{c.hpiCpn ?? '—'}</td><td data-label="Practice">{c.practiceName ?? '—'}</td>
              <td data-label="Status"><span className={`pill ${c.isActive ? 'green' : ''}`}>{c.isActive ? 'Active' : 'Inactive'}</span></td>
              <td><button className="btn btn-link" onClick={() => setEdit(c)}>Edit</button></td></tr>
          ))}</tbody>
        </table>
      )}
    </div>
  );
}

function Procedures() {
  const [rows, setRows] = useState<Proc[] | null>(null);
  const [edit, setEdit] = useState<Partial<Proc> | null>(null);
  const [errors, setErrors] = useState<string[]>([]);
  const [msg, setMsg] = useState<string | null>(null);
  const load = () => api<Proc[]>('/api/admin/organisation/procedures').then(setRows).catch((e) => setErrors(errsOf(e)));
  useEffect(() => { load(); }, []);
  const save = async () => {
    setErrors([]);
    try { await api('/api/admin/organisation/procedures', { body: { ...edit, defaultMinutes: Number(edit!.defaultMinutes) } }); setEdit(null); load(); } catch (x) { setErrors(errsOf(x)); }
  };
  const examples = async () => { const r = await api<{ added: number }>('/api/admin/organisation/procedures/examples', { body: {} }); setMsg(`${r.added} example procedure(s) added. Edit names and times to suit TDS.`); load(); };
  const set = (k: keyof Proc) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => setEdit({ ...edit, [k]: e.target.value });
  return (
    <div className="stack">
      <div className="row-between"><p className="hint">Picked when booking or waitlisting: the usual time and anaesthetic fill in automatically.</p>
        <div className="row">{rows?.length === 0 && <button className="btn btn-light" onClick={examples}>Add example procedures</button>}
          {!edit && <button className="btn btn-primary" onClick={() => setEdit({ service: 'Surgical', defaultMinutes: 30, defaultAnaesthetic: 'General', sideRequired: false, isActive: true })}>Add procedure</button>}</div></div>
      {msg && <p className="notice">{msg}</p>}
      {edit && (
        <Modal title={<>{edit.id ? `Edit ${edit.name}` : 'New procedure'}</>} onClose={() => { setEdit(null); setErrors([]); }}>
          <div className="form-grid">
            <label>Code<input value={edit.code ?? ''} onChange={set('code')} placeholder="LAPCHOLE" /></label>
            <label>Name<input value={edit.name ?? ''} onChange={set('name')} /></label>
            <label>Service<select value={edit.service} onChange={set('service')}><option>Surgical</option><option>Endoscopy</option></select></label>
            <label>Usual minutes<input type="number" min={5} max={720} value={edit.defaultMinutes ?? 30} onChange={set('defaultMinutes')} /></label>
            <label>Usual anaesthetic<select value={edit.defaultAnaesthetic} onChange={set('defaultAnaesthetic')}>{ANAES.map((a) => <option key={a}>{a}</option>)}</select></label>
            <label>Fee code (optional)<input value={edit.feeCode ?? ''} onChange={set('feeCode')} /></label>
            <label className="check"><input type="checkbox" checked={!!edit.sideRequired} onChange={(e) => setEdit({ ...edit, sideRequired: e.target.checked })} /> Side must be chosen (left/right)</label>
            <label className="check"><input type="checkbox" checked={!!edit.isActive} onChange={(e) => setEdit({ ...edit, isActive: e.target.checked })} /> Active</label>
          </div>
          <Errs list={errors} />
          <div className="row"><button className="btn btn-primary" onClick={save}>Save</button><button className="btn btn-light" onClick={() => { setEdit(null); setErrors([]); }}>Cancel</button></div>
        </Modal>
      )}
      {rows && rows.length > 0 && (
        <table className="results compact">
          <thead><tr><th>Code</th><th>Procedure</th><th>Service</th><th>Usual time</th><th>Anaesthetic</th><th></th></tr></thead>
          <tbody>{rows.map((p) => (
            <tr key={p.id} className={p.isActive ? '' : 'muted'}><td data-label="Code">{p.code}</td><td data-label="Procedure"><strong>{p.name}</strong>{p.sideRequired && <span className="badge">Side</span>}</td>
              <td data-label="Service">{p.service}</td><td data-label="Usual time">{p.defaultMinutes} min</td><td data-label="Anaesthetic">{p.defaultAnaesthetic}</td>
              <td><button className="btn btn-link" onClick={() => setEdit(p)}>Edit</button></td></tr>
          ))}</tbody>
        </table>
      )}
    </div>
  );
}
