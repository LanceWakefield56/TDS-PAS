import { useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { api, ApiError, ETHNICITY_QUICK, formatDate } from '../api';

interface Dup { id: string; mrn: string; nhi: string | null; familyName: string; givenNames: string; dateOfBirth: string }

const empty = {
  familyName: '', givenNames: '', preferredName: '', dateOfBirth: '', sex: 'Unknown', gender: '', nhi: '',
  mobilePhone: '', homePhone: '', email: '', addressLine1: '', addressLine2: '', suburb: '', city: '', postcode: '',
};

export default function PatientRegister() {
  const navigate = useNavigate();
  const [f, setF] = useState(empty);
  const [ethnicities, setEthnicities] = useState<string[]>([]);
  const [otherCode, setOtherCode] = useState('');
  const [errors, setErrors] = useState<string[]>([]);
  const [dups, setDups] = useState<Dup[]>([]);
  const [busy, setBusy] = useState(false);
  const set = (k: keyof typeof empty) => (e: React.ChangeEvent<HTMLInputElement | HTMLSelectElement>) => setF({ ...f, [k]: e.target.value });

  const toggleEthnicity = (code: string) =>
    setEthnicities((cur) => cur.includes(code) ? cur.filter((c) => c !== code) : cur.length < 6 ? [...cur, code] : cur);

  const submit = async (confirmedNotDuplicate = false) => {
    setBusy(true); setErrors([]);
    const clean = Object.fromEntries(Object.entries(f).map(([k, v]) => [k, v.trim() === '' ? null : v.trim()]));
    try {
      const p = await api<{ id: string }>('/api/patients', { body: { ...clean, ethnicityCodes: ethnicities, confirmedNotDuplicate } });
      navigate(`/patients/${p.id}`, { replace: true });
    } catch (e) {
      if (e instanceof ApiError && e.status === 400) { setErrors(e.body?.errors ?? []); setDups(e.body?.possibleDuplicates ?? []); }
      else setErrors(['The patient could not be saved. Please try again.']);
      window.scrollTo({ top: 0, behavior: 'smooth' });
    } finally { setBusy(false); }
  };

  return (
    <form className="stack-lg" onSubmit={(e) => { e.preventDefault(); submit(false); }}>
      <div className="page-head"><h1>Register a patient</h1><p className="muted">Fields marked * are required.</p></div>

      {errors.length > 0 && (
        <div className="card alert-box" role="alert">
          <ul>{errors.map((m) => <li key={m}>{m}</li>)}</ul>
          {dups.length > 0 && (
            <>
              <table className="results compact">
                <thead><tr><th>Possible match</th><th>NHI</th><th>Record no.</th><th>Date of birth</th><th></th></tr></thead>
                <tbody>{dups.map((d) => (
                  <tr key={d.id}><td><strong>{d.familyName.toUpperCase()}</strong>, {d.givenNames}</td><td>{d.nhi ?? '—'}</td><td>{d.mrn}</td>
                    <td>{formatDate(d.dateOfBirth)}</td><td><Link to={`/patients/${d.id}`}>Open record</Link></td></tr>))}
                </tbody>
              </table>
              {!dups.some((d) => d.nhi && d.nhi === f.nhi.trim().toUpperCase()) && (
                <button type="button" className="btn btn-warn" onClick={() => submit(true)} disabled={busy}>I have checked – this is a different person, register anyway</button>
              )}
            </>
          )}
        </div>
      )}

      <section className="card form-grid">
        <h2 className="span-all">Identity</h2>
        <label>Family name *<input value={f.familyName} onChange={set('familyName')} required autoFocus /></label>
        <label>Given name(s) *<input value={f.givenNames} onChange={set('givenNames')} required /></label>
        <label>Preferred name<input value={f.preferredName} onChange={set('preferredName')} /></label>
        <label>Date of birth *<input type="date" value={f.dateOfBirth} onChange={set('dateOfBirth')} required /></label>
        <label>Sex *
          <select value={f.sex} onChange={set('sex')}>
            <option value="Unknown">Not stated</option><option value="Female">Female</option><option value="Male">Male</option><option value="Indeterminate">Indeterminate</option>
          </select>
        </label>
        <label>Gender (as the patient describes it)<input value={f.gender} onChange={set('gender')} /></label>
        <label>NHI number<input value={f.nhi} onChange={set('nhi')} maxLength={7} placeholder="e.g. ZSC21TN" className="upper" /></label>
      </section>

      <section className="card">
        <h2>Ethnicity <span className="muted small">(up to 6, in the order the patient gives)</span></h2>
        <div className="chips">
          {ETHNICITY_QUICK.map(([code, label]) => (
            <button type="button" key={code} className={`chip ${ethnicities.includes(code) ? 'on' : ''}`} aria-pressed={ethnicities.includes(code)} onClick={() => toggleEthnicity(code)}>{label}</button>
          ))}
        </div>
        <div className="row">
          <label>Other code (HISO 10001, 5 digits)<input value={otherCode} onChange={(e) => setOtherCode(e.target.value.replace(/\D/g, ''))} maxLength={5} /></label>
          <button type="button" className="btn btn-light" disabled={otherCode.length !== 5} onClick={() => { toggleEthnicity(otherCode); setOtherCode(''); }}>Add</button>
        </div>
        {ethnicities.length > 0 && <p>Selected (in order): {ethnicities.map((c) => ETHNICITY_QUICK.find(([k]) => k === c)?.[1] ?? c).join(', ')}</p>}
      </section>

      <section className="card form-grid">
        <h2 className="span-all">Contact</h2>
        <label>Mobile phone<input type="tel" value={f.mobilePhone} onChange={set('mobilePhone')} /></label>
        <label>Home phone<input type="tel" value={f.homePhone} onChange={set('homePhone')} /></label>
        <label>Email<input type="email" value={f.email} onChange={set('email')} /></label>
        <label className="span-all">Address<input value={f.addressLine1} onChange={set('addressLine1')} placeholder="Street address" /></label>
        <label className="span-all"><span className="sr-only">Address line 2</span><input value={f.addressLine2} onChange={set('addressLine2')} placeholder="Address line 2" /></label>
        <label>Suburb<input value={f.suburb} onChange={set('suburb')} /></label>
        <label>Town / city<input value={f.city} onChange={set('city')} /></label>
        <label>Postcode<input value={f.postcode} onChange={set('postcode')} inputMode="numeric" maxLength={4} /></label>
      </section>

      <div className="row sticky-actions">
        <Link className="btn btn-light btn-big" to="/patients">Cancel</Link>
        <button className="btn btn-primary btn-big" disabled={busy}>{busy ? 'Saving…' : 'Register patient'}</button>
      </div>
    </form>
  );
}
