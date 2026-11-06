import { useState } from 'react';
import { api, errText, formatDate, PatientSummary } from '../api';

/** Search and pick a patient (NHI, record number or name). */
export default function PatientPicker({ value, onChange, label = 'Patient' }: { value: PatientSummary | null; onChange: (p: PatientSummary | null) => void; label?: string }) {
  const [q, setQ] = useState('');
  const [results, setResults] = useState<PatientSummary[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const search = async () => {
    if (!q.trim()) return;
    setError(null);
    try { setResults(await api<PatientSummary[]>(`/api/patients?q=${encodeURIComponent(q.trim())}`)); } catch (e) { setError(errText(e)); }
  };
  if (value) return (
    <div className="chosen"><span><strong>{value.familyName.toUpperCase()}, {value.givenNames}</strong> · {value.nhi ?? 'no NHI'} · {value.mrn} · born {formatDate(value.dateOfBirth)}</span>
      <button type="button" className="btn btn-link" onClick={() => { onChange(null); setResults(null); }}>Change</button></div>
  );
  return (
    <div>
      <div className="row">
        <label className="grow">{label} – NHI, record number or name
          <input value={q} onChange={(e) => setQ(e.target.value)} onKeyDown={(e) => { if (e.key === 'Enter') { e.preventDefault(); search(); } }} placeholder="e.g. ZSC21TN or Smith, John" /></label>
        <button type="button" className="btn btn-light" onClick={search}>Find</button>
      </div>
      {error && <p className="error">{error}</p>}
      {results && (results.length === 0 ? <p className="muted">No patients found.</p> : (
        <ul className="picker-results">{results.map((p) => (
          <li key={p.id}><button type="button" onClick={() => onChange(p)}><strong>{p.familyName.toUpperCase()}, {p.givenNames}</strong> · {p.nhi ?? 'no NHI'} · {p.mrn} · {formatDate(p.dateOfBirth)}</button></li>
        ))}</ul>
      ))}
    </div>
  );
}
