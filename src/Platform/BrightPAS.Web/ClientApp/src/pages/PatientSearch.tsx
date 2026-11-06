import { useState } from 'react';
import { Link, useNavigate } from 'react-router';
import { age, api, formatDate, PatientSummary, Perm } from '../api';
import { useSession } from '../App';

export default function PatientSearch() {
  const { can } = useSession();
  const navigate = useNavigate();
  const [q, setQ] = useState('');
  const [dob, setDob] = useState('');
  const [results, setResults] = useState<PatientSummary[] | null>(null);
  const [busy, setBusy] = useState(false);

  const search = async (e: React.FormEvent) => {
    e.preventDefault(); setBusy(true);
    try {
      const p = new URLSearchParams();
      if (q.trim()) p.set('q', q.trim());
      if (dob) p.set('dob', dob);
      setResults(await api<PatientSummary[]>(`/api/patients?${p}`));
    } finally { setBusy(false); }
  };

  return (
    <div className="stack-lg">
      <div className="page-head row-between">
        <h1>Find a patient</h1>
        {can(Perm.PatientRegister) && <Link className="btn btn-light" to="/patients/new">Register new patient</Link>}
      </div>
      <form className="card search" onSubmit={search} role="search">
        <label className="grow">NHI, record number or name
          <input autoFocus placeholder="e.g. ZSC21TN, TDS0000012 or Smith, John" value={q} onChange={(e) => setQ(e.target.value)} />
        </label>
        <label>Date of birth (optional)<input type="date" value={dob} onChange={(e) => setDob(e.target.value)} /></label>
        <button className="btn btn-primary btn-big" disabled={busy || (!q.trim() && !dob)}>{busy ? 'Searching…' : 'Search'}</button>
      </form>
      {results && (
        <section className="card">
          <h2>{results.length === 0 ? 'No patients found' : `${results.length} patient${results.length === 1 ? '' : 's'} found`}</h2>
          {results.length === 0 && <p>Check the spelling, try fewer letters, or search by NHI.{can(Perm.PatientRegister) && <> If the patient is new, <Link to="/patients/new">register them</Link>.</>}</p>}
          {results.length > 0 && (
            <table className="results">
              <thead><tr><th>Name</th><th>NHI</th><th>Record no.</th><th>Date of birth</th><th>Sex</th></tr></thead>
              <tbody>
                {results.map((p) => (
                  <tr key={p.id} tabIndex={0} onClick={() => navigate(`/patients/${p.id}`)} onKeyDown={(e) => e.key === 'Enter' && navigate(`/patients/${p.id}`)}>
                    <td><strong>{p.familyName.toUpperCase()}</strong>, {p.givenNames}{p.preferredName && <span className="muted"> ({p.preferredName})</span>} {p.isTestPatient && <span className="badge test">TEST</span>}</td>
                    <td>{p.nhi ?? <span className="muted">—</span>}</td>
                    <td>{p.mrn}</td>
                    <td>{formatDate(p.dateOfBirth)} <span className="muted">({age(p.dateOfBirth)}y)</span></td>
                    <td>{p.sex}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>
      )}
    </div>
  );
}
