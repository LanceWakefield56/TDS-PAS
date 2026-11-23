import { useEffect, useState } from 'react';
import { Link, useParams } from 'react-router';
import { age, api, ApiError, ethnicityName, formatDate, PatientDetail, Perm } from '../api';
import { useSession } from '../App';
import PatientModules from '../components/PatientModules';
import JourneyPanel from '../components/Journey';
import PatientRecords from '../components/PatientRecords';

export default function PatientView() {
  const { id } = useParams();
  const { can, mod } = useSession();
  const [editContact, setEditContact] = useState<Record<string, string> | null>(null);
  const [practices, setPractices] = useState<{ name: string; hpiFacilityId: string | null; isActive: boolean }[]>([]);
  useEffect(() => { if (mod('letters') && can(Perm.LettersView)) api<any[]>('/api/letters/practices').then(setPractices).catch(() => undefined); }, [mod, can]);
  const [p, setP] = useState<PatientDetail | null>(null);
  const [notFound, setNotFound] = useState(false);
  const [showAlertForm, setShowAlertForm] = useState(false);
  const [alert, setAlert] = useState({ type: 'Allergy', severity: 'Warning', description: '' });
  const [error, setError] = useState<string | null>(null);

  const load = async () => {
    try { setP(await api<PatientDetail>(`/api/patients/${id}`)); }
    catch (e) { if (e instanceof ApiError && e.status === 404) setNotFound(true); }
  };
  useEffect(() => { load(); }, [id]); // eslint-disable-line react-hooks/exhaustive-deps

  const addAlert = async (e: React.FormEvent) => {
    e.preventDefault(); setError(null);
    try { await api(`/api/patients/${id}/alerts`, { body: alert }); setAlert({ ...alert, description: '' }); setShowAlertForm(false); await load(); }
    catch { setError('The alert could not be saved.'); }
  };

  const startEdit = () => {
    if (!p) return;
    const v = (x: string | null | undefined) => x ?? '';
    setError(null);
    setEditContact({ preferredName: v(p.preferredName), mobilePhone: v(p.mobilePhone), homePhone: v(p.homePhone), email: v(p.email), addressLine1: v(p.addressLine1),
      addressLine2: v(p.addressLine2), suburb: v(p.suburb), city: v(p.city), postcode: v(p.postcode), gpPracticeHpiFacilityId: v(p.gpPracticeHpiFacilityId) });
  };
  const saveContact = async () => {
    setError(null);
    try { await api(`/api/patients/${id}/contact`, { method: 'PUT', body: editContact }); setEditContact(null); await load(); }
    catch (x: any) { setError(x?.body?.errors?.join(' ') ?? 'The details could not be saved.'); }
  };

  if (notFound) return <div className="card"><h1>Patient not found</h1><Link to="/patients">Back to search</Link></div>;
  if (!p) return <p>Loading…</p>;
  const critical = p.alerts.filter((a) => a.severity === 'Critical');
  const address = [p.addressLine1, p.addressLine2, p.suburb, p.city, p.postcode].filter(Boolean).join(', ');

  return (
    <div className="stack-lg">
      <section className="banner">
        <div>
          <h1>{p.familyName.toUpperCase()}, {p.givenNames}{p.preferredName && <span className="light"> ({p.preferredName})</span>}</h1>
          <div className="banner-facts">
            <span><b>NHI</b> {p.nhi ?? 'not recorded'}</span>
            <span><b>Record</b> {p.mrn}</span>
            <span><b>Born</b> {formatDate(p.dateOfBirth)} ({age(p.dateOfBirth)} years)</span>
            <span><b>Sex</b> {p.sex}</span>
            {p.isTestPatient && <span className="badge test">TEST PATIENT</span>}
          </div>
        </div>
      </section>

      {critical.length > 0 && (
        <div className="alert-critical" role="alert">
          <strong>Critical alert{critical.length > 1 ? 's' : ''}:</strong> {critical.map((a) => `${a.type} – ${a.description}`).join(' · ')}
        </div>
      )}

      <JourneyPanel patientId={p.id} />

      <div className="grid">
        <section className="card">
          <div className="row-between"><h2>Alerts</h2>
            {can(Perm.PatientAlerts) && !showAlertForm && <button className="btn btn-light" onClick={() => setShowAlertForm(true)}>Add alert</button>}
          </div>
          {p.alerts.length === 0 && <p className="muted">No alerts recorded.</p>}
          <ul className="plain">
            {p.alerts.map((a) => (
              <li key={a.id} className={`alert-item sev-${a.severity.toLowerCase()}`}>
                <span className="badge">{a.severity}</span> <strong>{a.type}</strong> – {a.description}
                <div className="muted small">Recorded by {a.recordedBy}, {new Date(a.recordedAt).toLocaleString('en-NZ')}</div>
              </li>
            ))}
          </ul>
          {showAlertForm && (
            <form className="stack" onSubmit={addAlert}>
              <div className="row">
                <label>Type<select value={alert.type} onChange={(e) => setAlert({ ...alert, type: e.target.value })}>
                  {['Allergy', 'Infection', 'Clinical', 'Safety', 'Administrative'].map((t) => <option key={t}>{t}</option>)}</select></label>
                <label>Severity<select value={alert.severity} onChange={(e) => setAlert({ ...alert, severity: e.target.value })}>
                  {['Information', 'Warning', 'Critical'].map((t) => <option key={t}>{t}</option>)}</select></label>
              </div>
              <label>Description<input value={alert.description} onChange={(e) => setAlert({ ...alert, description: e.target.value })} required autoFocus placeholder="e.g. Penicillin – anaphylaxis" /></label>
              {error && <p className="error">{error}</p>}
              <div className="row"><button type="button" className="btn btn-light" onClick={() => setShowAlertForm(false)}>Cancel</button><button className="btn btn-primary">Save alert</button></div>
            </form>
          )}
        </section>
        {editContact && (
          <section className="card span-all stack">
            <h2>Edit contact and GP details</h2>
            <div className="form-grid">
              {([['preferredName', 'Preferred name'], ['mobilePhone', 'Mobile'], ['homePhone', 'Home phone'], ['email', 'Email'], ['addressLine1', 'Street address'],
                ['addressLine2', 'Address line 2'], ['suburb', 'Suburb'], ['city', 'Town / city'], ['postcode', 'Postcode']] as [string, string][]).map(([k, t]) =>
                <label key={k}>{t}<input value={editContact[k]} onChange={(e) => setEditContact({ ...editContact, [k]: e.target.value })} /></label>)}
              <label>GP practice
                {practices.length > 0
                  ? <select value={editContact.gpPracticeHpiFacilityId} onChange={(e) => setEditContact({ ...editContact, gpPracticeHpiFacilityId: e.target.value })}>
                      <option value="">Not recorded</option>{practices.filter((x) => x.hpiFacilityId && (x.isActive || x.hpiFacilityId === editContact.gpPracticeHpiFacilityId)).map((x) => <option key={x.hpiFacilityId!} value={x.hpiFacilityId!}>{x.name} ({x.hpiFacilityId})</option>)}</select>
                  : <input value={editContact.gpPracticeHpiFacilityId} placeholder="HPI facility number, e.g. F12345-A" onChange={(e) => setEditContact({ ...editContact, gpPracticeHpiFacilityId: e.target.value })} />}
              </label>
            </div>
            <p className="hint">Name, date of birth and NHI are changed with "Correct name, date of birth or NHI" (needs a second person's approval).</p>
            {error && <p className="error" role="alert">{error}</p>}
            <div className="row"><button className="btn btn-primary" onClick={saveContact}>Save</button><button className="btn btn-light" onClick={() => setEditContact(null)}>Cancel</button></div>
          </section>
        )}
        <section className="card">
          <div className="row-between"><h2>Contact</h2>{can(Perm.PatientEdit) && !editContact && <button className="btn btn-light btn-small" onClick={startEdit}>Edit</button>}</div>
          <dl className="facts">
            <dt>Mobile</dt><dd>{p.mobilePhone ?? '—'}</dd>
            <dt>Home</dt><dd>{p.homePhone ?? '—'}</dd>
            <dt>Email</dt><dd>{p.email ?? '—'}</dd>
            <dt>Address</dt><dd>{address || '—'}</dd>
          </dl>
        </section>
        <section className="card">
          <h2>Details</h2>
          <dl className="facts">
            <dt>Gender</dt><dd>{p.gender ?? '—'}</dd>
            <dt>Ethnicity</dt><dd>{p.ethnicities.length ? p.ethnicities.map((c) => ethnicityName(c) ? `${ethnicityName(c)} (${c})` : c).join(', ') : '—'}</dd>
            <dt>GP practice</dt><dd>{practices.find((x) => x.hpiFacilityId === p.gpPracticeHpiFacilityId)?.name ?? p.gpPracticeHpiFacilityId ?? '—'}</dd>
          </dl>
        </section>
        <PatientRecords patient={p} onChanged={load} />
        <PatientModules patientId={p.id} />
      </div>
    </div>
  );
}
